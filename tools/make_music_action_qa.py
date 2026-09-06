from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
ATLAS = ROOT / "Assets" / "idle-actions.png"
OUTPUT_DIR = ROOT.parent / "work" / "gugu-music-actions" / "qa"
CELL_W, CELL_H = 192, 208
ROWS = (("headphones", 20), ("drums", 21))


def get_font(size: int) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    for path in (Path(r"C:\Windows\Fonts\msyh.ttc"), Path(r"C:\Windows\Fonts\simhei.ttf")):
        if path.exists():
            return ImageFont.truetype(str(path), size)
    return ImageFont.load_default()


def main() -> None:
    atlas = Image.open(ATLAS).convert("RGBA")
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    scale = 2
    label_h = 44
    contact = Image.new(
        "RGBA",
        (CELL_W * 8 * scale, (CELL_H * scale + label_h) * len(ROWS)),
        (238, 240, 244, 255),
    )
    draw = ImageDraw.Draw(contact)
    font = get_font(24)

    for qa_row, (name, atlas_row) in enumerate(ROWS):
        animation_frames = []
        for column in range(8):
            frame = atlas.crop((
                column * CELL_W,
                atlas_row * CELL_H,
                (column + 1) * CELL_W,
                (atlas_row + 1) * CELL_H,
            ))
            enlarged = frame.resize((CELL_W * scale, CELL_H * scale), Image.Resampling.NEAREST)
            x = column * CELL_W * scale
            y = qa_row * (CELL_H * scale + label_h) + label_h
            contact.alpha_composite(enlarged, (x, y))
            animation_frames.append(enlarged)
        draw.text(
            (12, qa_row * (CELL_H * scale + label_h) + 7),
            name,
            font=font,
            fill=(35, 36, 40, 255),
        )
        gif_frames = []
        for frame in animation_frames:
            canvas = Image.new("RGBA", frame.size, (238, 240, 244, 255))
            canvas.alpha_composite(frame)
            gif_frames.append(canvas.convert("P", palette=Image.Palette.ADAPTIVE))
        gif_frames[0].save(
            OUTPUT_DIR / f"{name}.gif",
            save_all=True,
            append_images=gif_frames[1:],
            duration=140 if name == "drums" else 175,
            loop=0,
            disposal=2,
        )

    contact.convert("RGB").save(OUTPUT_DIR / "music-actions-contact-sheet.png", quality=95)
    print(f"Wrote {OUTPUT_DIR / 'music-actions-contact-sheet.png'}")


if __name__ == "__main__":
    main()
