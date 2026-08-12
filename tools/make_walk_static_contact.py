from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT.parent / "work" / "gugu-walk-static" / "generated"
OUTPUT = ROOT.parent / "work" / "gugu-walk-static" / "walk-static-contact-sheet.png"
ITEMS = (
    ("向右 · 近腿前 / 近臂后", "right-near-leg-forward.png"),
    ("向右 · 远腿前 / 近臂前", "right-far-leg-forward.png"),
    ("向左 · 近腿前 / 近臂后", "left-near-leg-forward.png"),
    ("向左 · 远腿前 / 近臂前", "left-far-leg-forward.png"),
)


def font(size: int):
    path = Path(r"C:\Windows\Fonts\msyh.ttc")
    return ImageFont.truetype(str(path), size) if path.exists() else ImageFont.load_default()


def main() -> None:
    cell_w, cell_h, label_h = 560, 590, 52
    sheet = Image.new("RGB", (cell_w * 2, cell_h * 2), (242, 243, 246))
    draw = ImageDraw.Draw(sheet)
    for index, (label, filename) in enumerate(ITEMS):
        image = Image.open(SOURCE / filename).convert("RGB")
        image.thumbnail((cell_w - 20, cell_h - label_h - 10), Image.Resampling.LANCZOS)
        column, row = index % 2, index // 2
        origin_x, origin_y = column * cell_w, row * cell_h
        x = origin_x + (cell_w - image.width) // 2
        y = origin_y + label_h + (cell_h - label_h - image.height) // 2
        sheet.paste(image, (x, y))
        draw.text((origin_x + 18, origin_y + 12), label, font=font(24), fill=(24, 25, 28))
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(OUTPUT, quality=95)
    print(OUTPUT)


if __name__ == "__main__":
    main()
