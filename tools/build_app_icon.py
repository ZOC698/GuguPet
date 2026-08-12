from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageChops, ImageFilter


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Assets" / "gugu-icon-cutout.png"
PNG_OUTPUT = ROOT / "Assets" / "gugu-icon.png"
ICO_OUTPUT = ROOT / "Assets" / "gugu.ico"
PREVIEW_OUTPUT = ROOT.parent / "work" / "gugu-idle-animation-qa" / "gugu-app-icon-preview.png"


def main() -> None:
    source = Image.open(SOURCE).convert("RGBA")

    # Identity-preserving head crop from the canonical front pose. It keeps the
    # penguin hood eyes and beak, Gugu's face, blue hair clip, and collar.
    portrait = source.crop((215, 30, 990, 805))
    portrait.thumbnail((900, 900), Image.Resampling.LANCZOS)

    icon = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0))
    x = (icon.width - portrait.width) // 2
    y = (icon.height - portrait.height) // 2 + 18

    subject_alpha = Image.new("L", icon.size, 0)
    subject_alpha.paste(portrait.getchannel("A"), (x, y))
    outline_alpha = subject_alpha.filter(ImageFilter.MaxFilter(17))
    outline_alpha = ImageChops.subtract(outline_alpha, subject_alpha)
    outline = Image.new("RGBA", icon.size, (232, 248, 255, 0))
    outline.putalpha(outline_alpha)
    icon.alpha_composite(outline)
    icon.alpha_composite(portrait, (x, y))

    if icon.getbbox() is None:
        raise RuntimeError("generated icon is empty")
    if any(icon.getpixel(point)[3] for point in ((0, 0), (1023, 0), (0, 1023), (1023, 1023))):
        raise RuntimeError("icon corners must remain transparent")

    PNG_OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    icon.save(PNG_OUTPUT, optimize=True)
    icon.save(
        ICO_OUTPUT,
        format="ICO",
        sizes=((16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)),
    )

    preview = Image.new("RGBA", (1280, 420), (228, 230, 234, 255))
    for index, size in enumerate((256, 128, 64, 32, 16)):
        sample = icon.resize((size, size), Image.Resampling.LANCZOS)
        panel_x = 28 + index * 245
        preview.alpha_composite(sample, (panel_x + (192 - size) // 2, 54 + (256 - size) // 2))
    PREVIEW_OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    preview.convert("RGB").save(PREVIEW_OUTPUT)

    print(f"Wrote {PNG_OUTPUT}")
    print(f"Wrote {ICO_OUTPUT}")
    print(f"Wrote {PREVIEW_OUTPUT}")


if __name__ == "__main__":
    main()
