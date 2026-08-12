from __future__ import annotations

import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

from build_walk_repair import CELL_H, CELL_W, remove_blue, normalize


ROOT = Path(__file__).resolve().parents[1]
WORK = ROOT.parent / "work" / "gugu-walk-repair"
GENERATED = WORK / "generated"
QA = WORK / "qa-8phase"
PHASES = ("近腿着地", "承重", "过腿", "近腿后蹬", "远腿着地", "承重", "过腿", "远腿后蹬")


def font(size: int):
    path = Path(r"C:\Windows\Fonts\msyh.ttc")
    return ImageFont.truetype(str(path), size) if path.exists() else ImageFont.load_default()


def split8(path: Path) -> list[Image.Image]:
    board = remove_blue(Image.open(path))
    tile_w, tile_h = board.width // 4, board.height // 2
    poses = []
    for row in range(2):
        for column in range(4):
            tile = board.crop((column * tile_w, row * tile_h, (column + 1) * tile_w, (row + 1) * tile_h))
            bbox = tile.getbbox()
            if bbox is None:
                raise ValueError(f"Missing phase {row * 4 + column + 1}: {path}")
            poses.append(tile.crop(bbox))
    return poses


def belly_offset(frame: Image.Image) -> float | None:
    xs = []
    for y in range(round(CELL_H * .45), round(CELL_H * .9)):
        for x in range(CELL_W):
            r, g, b, a = frame.getpixel((x, y))
            if a > 180 and min(r, g, b) > 160 and max(r, g, b) - min(r, g, b) < 38:
                xs.append(x)
    return round(sum(xs) / len(xs) - CELL_W / 2, 2) if xs else None


def save_gif(frames: list[Image.Image], path: Path) -> None:
    output = []
    for frame in frames:
        canvas = Image.new("RGBA", (CELL_W, CELL_H), (242, 243, 246, 255))
        canvas.alpha_composite(frame)
        output.append(canvas.resize((CELL_W * 2, CELL_H * 2), Image.Resampling.LANCZOS).convert("P", palette=Image.Palette.ADAPTIVE))
    output[0].save(path, save_all=True, append_images=output[1:], duration=105, loop=0, disposal=2)


def main() -> None:
    sources = {
        "right": ("向右", GENERATED / "running-right-8phase-subtle.png"),
        "left": ("向左", GENERATED / "running-left-8phase-subtle.png"),
    }
    QA.mkdir(parents=True, exist_ok=True)
    frames_by_direction = {key: normalize(split8(path)) for key, (_, path) in sources.items()}
    metrics = {key: [belly_offset(frame) for frame in frames] for key, frames in frames_by_direction.items()}
    for key, frames in frames_by_direction.items():
        save_gif(frames, QA / f"walk-{key}-8phase-preview.gif")

    label_h = 34
    contact = Image.new("RGBA", (CELL_W * 8, (CELL_H + label_h) * 2), (242, 243, 246, 255))
    draw = ImageDraw.Draw(contact)
    for row, key in enumerate(("right", "left")):
        top = row * (CELL_H + label_h)
        draw.text((5, top + 5), sources[key][0], font=font(17), fill=(25, 25, 28, 255))
        for column, frame in enumerate(frames_by_direction[key]):
            draw.text((column * CELL_W + 54, top + 7), PHASES[column], font=font(14), fill=(45, 45, 48, 255))
            contact.alpha_composite(frame, (column * CELL_W, top + label_h))
    contact.convert("RGB").save(QA / "walk-8phase-contact-sheet.png")
    (QA / "belly-sway-metrics.json").write_text(json.dumps(metrics, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(metrics, ensure_ascii=False))


if __name__ == "__main__":
    main()
