from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image


COLS = 4
ROWS = 2
CELL_SIZE = (360, 480)


def white_fraction(image: Image.Image, axis: str, index: int) -> float:
    pixels = (
        (image.getpixel((index, y)) for y in range(image.height))
        if axis == "x"
        else (image.getpixel((x, index)) for x in range(image.width))
    )
    length = image.height if axis == "x" else image.width
    return sum(1 for pixel in pixels if min(pixel[:3]) >= 245) / length


def separator_band(image: Image.Image, axis: str, expected: int) -> tuple[int, int]:
    length = image.width if axis == "x" else image.height
    start = max(0, expected - 26)
    end = min(length - 1, expected + 26)
    candidates = [
        index for index in range(start, end + 1)
        if white_fraction(image, axis, index) >= 0.98
    ]
    runs: list[list[int]] = []
    for index in candidates:
        if not runs or index != runs[-1][-1] + 1:
            runs.append([index])
        else:
            runs[-1].append(index)
    if not runs:
        raise ValueError(f"No white separator found near {axis}={expected}")
    best = max(runs, key=lambda run: (len(run), -abs((run[0] + run[-1]) / 2 - expected)))
    return best[0], best[-1]


def content_ranges(image: Image.Image, axis: str, count: int) -> list[tuple[int, int]]:
    length = image.width if axis == "x" else image.height
    separators = [separator_band(image, axis, round(length * index / count)) for index in range(count + 1)]
    ranges = [(separators[index][1] + 1, separators[index + 1][0]) for index in range(count)]
    if any(end - start < 100 for start, end in ranges):
        raise ValueError(f"Detected invalid {axis} content ranges: {ranges}")
    return ranges


def trim_white_edge_columns(frame: Image.Image) -> Image.Image:
    left = 0
    right = frame.width
    for _ in range(8):
        fraction = sum(1 for y in range(frame.height) if min(frame.getpixel((left, y))[:3]) >= 245) / frame.height
        if fraction < 0.65:
            break
        left += 1
    for _ in range(8):
        fraction = sum(1 for y in range(frame.height) if min(frame.getpixel((right - 1, y))[:3]) >= 245) / frame.height
        if fraction < 0.65:
            break
        right -= 1
    return frame.crop((left, 0, right, frame.height))


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()

    board = Image.open(args.input).convert("RGB")
    x_ranges = content_ranges(board, "x", COLS)
    y_ranges = content_ranges(board, "y", ROWS)
    atlas = Image.new("RGB", (CELL_SIZE[0] * COLS, CELL_SIZE[1] * ROWS))

    boxes: list[tuple[int, int, int, int]] = []
    for row, (top, bottom) in enumerate(y_ranges):
        for column, (left, right) in enumerate(x_ranges):
            box = (left, top, right, bottom)
            frame = trim_white_edge_columns(board.crop(box)).resize(CELL_SIZE, Image.Resampling.LANCZOS)
            atlas.paste(frame, (column * CELL_SIZE[0], row * CELL_SIZE[1]))
            boxes.append(box)

    args.output.parent.mkdir(parents=True, exist_ok=True)
    atlas.save(args.output, optimize=True)
    print(f"output={args.output.resolve()}")
    print(f"atlas_size={atlas.size[0]}x{atlas.size[1]}")
    print(f"source_boxes={boxes}")


if __name__ == "__main__":
    main()
