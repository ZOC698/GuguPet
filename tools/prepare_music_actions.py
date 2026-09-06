from pathlib import Path

from PIL import Image

from remove_passive_drag_blue import remove_deep_blue_screen


ROOT = Path(__file__).resolve().parents[2]
GENERATED = ROOT / "work" / "gugu-music-actions" / "generated"

SOURCES = (
    (
        GENERATED / "gugu-headphones-keyposes-v1.png",
        GENERATED / "gugu-headphones-keyposes-v1-transparent.png",
    ),
    (
        GENERATED / "gugu-drums-keyposes-v2-compact.png",
        GENERATED / "gugu-drums-keyposes-v2-compact-transparent.png",
    ),
)


def main() -> None:
    for source, output in SOURCES:
        if not source.exists():
            raise FileNotFoundError(source)
        image = remove_deep_blue_screen(Image.open(source))
        output.parent.mkdir(parents=True, exist_ok=True)
        image.save(output, optimize=True)
        print(f"Wrote {output} ({image.width}x{image.height})")


if __name__ == "__main__":
    main()
