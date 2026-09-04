from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
ATLAS = ROOT / "Assets" / "idle-actions.png"
OUTPUT_DIR = ROOT.parent / "work" / "gugu-passive-drag" / "qa"
CELL_W, CELL_H = 192, 208
ROW = 17


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
    sheet = Image.new("RGB", (frame_w * 4, (frame_h + label_h) * 2), (239, 241, 245))
    draw = ImageDraw.Draw(sheet)
    gif_frames: list[Image.Image] = []
    boxes: list[tuple[int, int, int, int]] = []

    for column in range(8):
        frame = atlas.crop((column * CELL_W, ROW * CELL_H, (column + 1) * CELL_W, (ROW + 1) * CELL_H))
        bbox = frame.getbbox()
        if bbox is None:
            raise ValueError(f"blank passive-drag frame {column + 1}")
        boxes.append(bbox)
        preview = checkerboard()
        preview.alpha_composite(frame)
        enlarged = preview.resize((frame_w, frame_h), Image.Resampling.NEAREST)
        gif_frames.append(enlarged.convert("P", palette=Image.Palette.ADAPTIVE))

        x = (column % 4) * frame_w
        y = (column // 4) * (frame_h + label_h)
        sheet.paste(enlarged.convert("RGB"), (x, y + label_h))
        draw.text((x + 12, y + 7), f"Frame {column + 1}", font=font(20), fill=(31, 33, 38))

    sheet_path = OUTPUT_DIR / "passive-drag-contact-sheet.png"
    gif_path = OUTPUT_DIR / "passive-drag-preview.gif"
    sheet.save(sheet_path)
    gif_frames[0].save(
        gif_path,
        save_all=True,
        append_images=gif_frames[1:],
        duration=105,
        loop=0,
        disposal=2,
    )
    print(sheet_path)
    print(gif_path)
    print("bboxes:", boxes)


if __name__ == "__main__":
    main()
