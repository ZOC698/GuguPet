from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

from build_idle_atlas import match_passive_drag_palette


ROOT = Path(__file__).resolve().parents[1]
ATLAS = ROOT / "Assets" / "idle-actions.png"
BASE_ATLAS = ROOT / "Assets" / "spritesheet.png"
OUTPUT = ROOT.parent / "work" / "gugu-passive-drag" / "palette-preview"
CELL_W = 192
CELL_H = 208


def row(image: Image.Image, index: int) -> Image.Image:
    return image.crop((0, index * CELL_H, CELL_W * 8, (index + 1) * CELL_H))


def checkerboard(size: tuple[int, int], block: int = 16) -> Image.Image:
    image = Image.new("RGB", size, "#eef0f3")
    draw = ImageDraw.Draw(image)
    for y in range(0, size[1], block):
        for x in range(0, size[0], block):
            if (x // block + y // block) % 2:
                draw.rectangle((x, y, x + block - 1, y + block - 1), fill="#dfe3e8")
    return image


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    atlas = Image.open(ATLAS).convert("RGBA")
    base_atlas = Image.open(BASE_ATLAS).convert("RGBA")
    normal_before = row(atlas, 17)
    expressive_before = row(atlas, 18)
    normal_after = match_passive_drag_palette(normal_before, expressive=False)
    expressive_after = match_passive_drag_palette(expressive_before, expressive=True)
    normal_after.save(OUTPUT / "drag-normal-color-matched.png", optimize=True)
    expressive_after.save(OUTPUT / "drag-expressive-color-matched.png", optimize=True)

    selected = (0, 2, 4, 6)
    entries = (
        ("Original idle palette", row(base_atlas, 0)),
        ("Normal drag - before", normal_before),
        ("Normal drag - matched", normal_after),
        ("Expressive drag - before", expressive_before),
        ("Expressive drag - matched", expressive_after),
    )
    label_h = 32
    sheet = checkerboard((CELL_W * len(selected), (CELL_H + label_h) * len(entries)))
    draw = ImageDraw.Draw(sheet)
    font = ImageFont.load_default(size=16)
    for row_index, (label, strip) in enumerate(entries):
        top = row_index * (CELL_H + label_h)
        draw.rectangle((0, top, sheet.width, top + label_h), fill="#fbf8ef")
        draw.text((10, top + 7), label, fill="#282723", font=font)
        for column, frame_index in enumerate(selected):
            frame = strip.crop((frame_index * CELL_W, 0, (frame_index + 1) * CELL_W, CELL_H))
            sheet.paste(frame, (column * CELL_W, top + label_h), frame)
    sheet.save(OUTPUT / "passive-drag-palette-before-after.png", optimize=True)
    print(OUTPUT / "passive-drag-palette-before-after.png")


if __name__ == "__main__":
    main()
