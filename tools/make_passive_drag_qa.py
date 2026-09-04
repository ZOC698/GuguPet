from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
ATLAS = ROOT / "Assets" / "idle-actions.png"
OUTPUT_DIR = ROOT.parent / "work" / "gugu-passive-drag" / "qa"
CELL_W, CELL_H = 192, 208
ROWS = ((17, "normal"), (18, "expressive"))


def font(size: int):
    path = Path(r"C:\Windows\Fonts\msyh.ttc")
    return ImageFont.truetype(str(path), size) if path.exists() else ImageFont.load_default()


def checkerboard() -> Image.Image:
    image = Image.new("RGBA", (CELL_W, CELL_H), (239, 241, 245, 255))
    draw = ImageDraw.Draw(image)
    tile = 16
    for y in range(0, CELL_H, tile):
        for x in range(0, CELL_W, tile):
            if (x // tile + y // tile) % 2:
                draw.rectangle((x, y, x + tile - 1, y + tile - 1), fill=(222, 225, 231, 255))
    return image


def main() -> None:
    atlas = Image.open(ATLAS).convert("RGBA")
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    scale = 2
    frame_w, frame_h = CELL_W * scale, CELL_H * scale
    label_h = 38
    sheet = Image.new("RGB", (frame_w * 4, (frame_h + label_h) * 4), (239, 241, 245))
    draw = ImageDraw.Draw(sheet)
    metrics: dict[str, list[tuple[int, int, int, int]]] = {}

    for row_index, (row, slug) in enumerate(ROWS):
        gif_frames: list[Image.Image] = []
        boxes: list[tuple[int, int, int, int]] = []
        for column in range(8):
            frame = atlas.crop((column * CELL_W, row * CELL_H, (column + 1) * CELL_W, (row + 1) * CELL_H))
            bbox = frame.getbbox()
            if bbox is None:
                raise ValueError(f"blank {slug} passive-drag frame {column + 1}")
            boxes.append(bbox)
            preview = checkerboard()
            preview.alpha_composite(frame)
            enlarged = preview.resize((frame_w, frame_h), Image.Resampling.NEAREST)
            gif_frames.append(enlarged.convert("P", palette=Image.Palette.ADAPTIVE))

            x = (column % 4) * frame_w
            y = (row_index * 2 + column // 4) * (frame_h + label_h)
            sheet.paste(enlarged.convert("RGB"), (x, y + label_h))
            draw.text((x + 12, y + 7), f"{slug} · Frame {column + 1}", font=font(20), fill=(31, 33, 38))

        metrics[slug] = boxes
        gif_frames[0].save(
            OUTPUT_DIR / f"passive-drag-{slug}-preview.gif",
            save_all=True,
            append_images=gif_frames[1:],
            duration=105,
            loop=0,
            disposal=2,
        )

    sheet_path = OUTPUT_DIR / "passive-drag-contact-sheet-v2.png"
    sheet.save(sheet_path)
    print(sheet_path)
    for slug, boxes in metrics.items():
        print(f"{slug} bboxes:", boxes)


if __name__ == "__main__":
    main()
