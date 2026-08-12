from __future__ import annotations

import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
ATLAS = ROOT / "Assets" / "spritesheet.png"
OUTPUT = ROOT.parent / "work" / "gugu-original-walk"
QA = OUTPUT / "qa"
FRAMES = OUTPUT / "frames"
CELL_W, CELL_H = 192, 208
ROWS = {"right": 1, "left": 2}


def font(size: int) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    font_path = Path(r"C:\Windows\Fonts\msyh.ttc")
    return ImageFont.truetype(str(font_path), size) if font_path.exists() else ImageFont.load_default()


def save_gif(frames: list[Image.Image], path: Path) -> None:
    rendered = []
    for frame in frames:
        canvas = Image.new("RGBA", (CELL_W, CELL_H), (242, 243, 246, 255))
        canvas.alpha_composite(frame)
        rendered.append(
            canvas.resize((CELL_W * 2, CELL_H * 2), Image.Resampling.NEAREST)
            .convert("P", palette=Image.Palette.ADAPTIVE)
        )
    rendered[0].save(
        path,
        save_all=True,
        append_images=rendered[1:],
        duration=120,
        loop=0,
        disposal=2,
    )


def main() -> None:
    atlas = Image.open(ATLAS).convert("RGBA")
    if atlas.size != (CELL_W * 8, CELL_H * 11):
        raise ValueError(f"Unexpected atlas size: {atlas.size}")

    QA.mkdir(parents=True, exist_ok=True)
    metrics: dict[str, list[dict[str, object]]] = {}
    rows: dict[str, list[Image.Image]] = {}
    label_h = 36
    contact = Image.new("RGBA", (CELL_W * 8, (CELL_H + label_h) * 2), (242, 243, 246, 255))
    draw = ImageDraw.Draw(contact)

    for output_row, (direction, atlas_row) in enumerate(ROWS.items()):
        frame_dir = FRAMES / direction
        frame_dir.mkdir(parents=True, exist_ok=True)
        exact_row = atlas.crop((0, atlas_row * CELL_H, CELL_W * 8, (atlas_row + 1) * CELL_H))
        exact_row.save(QA / f"original-running-{direction}-row.png", optimize=True)

        frames = []
        row_metrics = []
        top = output_row * (CELL_H + label_h)
        draw.text((7, top + 7), "原版向右" if direction == "right" else "原版向左", font=font(17), fill=(30, 30, 34, 255))
        for column in range(8):
            frame = exact_row.crop((column * CELL_W, 0, (column + 1) * CELL_W, CELL_H))
            frame.save(frame_dir / f"{column:02d}.png", optimize=True)
            frames.append(frame)
            bbox = frame.getbbox()
            row_metrics.append({
                "frame": column + 1,
                "bbox": list(bbox) if bbox else None,
                "visible_width": bbox[2] - bbox[0] if bbox else 0,
                "visible_height": bbox[3] - bbox[1] if bbox else 0,
                "center_x": (bbox[0] + bbox[2]) / 2 if bbox else None,
                "top": bbox[1] if bbox else None,
                "bottom": bbox[3] if bbox else None,
            })
            draw.text((column * CELL_W + 78, top + 8), str(column + 1), font=font(16), fill=(55, 55, 60, 255))
            contact.alpha_composite(frame, (column * CELL_W, top + label_h))
        rows[direction] = frames
        metrics[direction] = row_metrics
        save_gif(frames, QA / f"original-walk-{direction}.gif")

    contact.convert("RGB").save(QA / "original-walk-contact-sheet.png", quality=96)
    (QA / "original-walk-metrics.json").write_text(
        json.dumps(metrics, ensure_ascii=False, indent=2), encoding="utf-8"
    )
    print(QA / "original-walk-contact-sheet.png")
    print(QA / "original-walk-right.gif")
    print(QA / "original-walk-left.gif")


if __name__ == "__main__":
    main()
