from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "work" / "gugu-passive-drag" / "passive-drag-keyposes.png"
OUTPUT = ROOT / "work" / "gugu-passive-drag" / "generated" / "gugu-passive-drag-keyposes-transparent.png"
EXPRESSIVE_SOURCE = ROOT / "work" / "gugu-passive-drag" / "expressive" / "gugu-passive-drag-expressive-keyposes-v2.png"
EXPRESSIVE_OUTPUT = ROOT / "work" / "gugu-passive-drag" / "expressive" / "generated" / "gugu-passive-drag-expressive-keyposes-v2-transparent.png"
EXPRESSIVE_RIGHT_SOURCE = ROOT / "work" / "gugu-passive-drag" / "expressive" / "gugu-passive-drag-expressive-right-pair.png"
EXPRESSIVE_RIGHT_OUTPUT = ROOT / "work" / "gugu-passive-drag" / "expressive" / "generated" / "gugu-passive-drag-expressive-right-pair-transparent.png"


def remove_deep_blue_screen(image: Image.Image) -> Image.Image:
    """Remove the generated deep-blue screen without erasing blue-gray eyes."""
    result = image.convert("RGBA")
    pixels = result.load()

    for y in range(result.height):
        for x in range(result.width):
            r, g, b, a = pixels[x, y]
            channel_floor = max(r, g)
            blue_dominance = b - channel_floor

            dominance_key = max(0.0, min(1.0, (blue_dominance - 55) / 115))
            brightness_key = max(0.0, min(1.0, (b - 80) / 120))
            dark_channel_key = max(0.0, min(1.0, (90 - channel_floor) / 70))
            key_amount = dominance_key * brightness_key * dark_channel_key

            if b >= 170 and channel_floor <= 50 and blue_dominance >= 130:
                key_amount = 1.0

            new_alpha = round(a * (1.0 - key_amount))
            if new_alpha == 0:
                pixels[x, y] = (0, 0, 0, 0)
            elif new_alpha < a:
                # Reduce residual blue spill on partially transparent contour pixels.
                corrected_blue = min(b, channel_floor + 24)
                pixels[x, y] = (r, g, corrected_blue, new_alpha)

    return result


def main() -> None:
    sources = (
        (SOURCE, OUTPUT),
        (EXPRESSIVE_SOURCE, EXPRESSIVE_OUTPUT),
        (EXPRESSIVE_RIGHT_SOURCE, EXPRESSIVE_RIGHT_OUTPUT),
    )
    for source, output in sources:
        output.parent.mkdir(parents=True, exist_ok=True)
        image = remove_deep_blue_screen(Image.open(source))
        image.save(output, optimize=True)
        print(f"Wrote {output} ({image.width}x{image.height})")


if __name__ == "__main__":
    main()
