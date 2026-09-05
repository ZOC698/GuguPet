from __future__ import annotations

from collections import deque
from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "work" / "gugu-interrupted" / "generated" / "gugu-interrupted-shrug-keyposes.png"
TRANSPARENT = ROOT / "work" / "gugu-interrupted" / "generated" / "gugu-interrupted-shrug-keyposes-transparent.png"
QA_DIR = ROOT / "work" / "gugu-interrupted" / "qa"


def remove_connected_checkerboard(image: Image.Image) -> Image.Image:
    """Remove only the pale neutral backdrop connected to the canvas edge."""
    rgb = image.convert("RGB")
    width, height = rgb.size
    pixels = rgb.load()
    background = bytearray(width * height)
    queue: deque[tuple[int, int]] = deque()

    def is_backdrop(x: int, y: int) -> bool:
        r, g, b = pixels[x, y]
        return min(r, g, b) >= 225 and max(r, g, b) - min(r, g, b) <= 14

    def visit(x: int, y: int) -> None:
        index = y * width + x
        if background[index] or not is_backdrop(x, y):
            return
        background[index] = 1
        queue.append((x, y))

    for x in range(width):
        visit(x, 0)
        visit(x, height - 1)
    for y in range(height):
        visit(0, y)
        visit(width - 1, y)

    while queue:
        x, y = queue.popleft()
        if x > 0:
            visit(x - 1, y)
        if x + 1 < width:
            visit(x + 1, y)
        if y > 0:
            visit(x, y - 1)
        if y + 1 < height:
            visit(x, y + 1)

    result = rgb.convert("RGBA")
    out = result.load()
    for y in range(height):
        for x in range(width):
            if background[y * width + x]:
                out[x, y] = (0, 0, 0, 0)
    return result


def make_qa(keyposes: Image.Image) -> None:
    QA_DIR.mkdir(parents=True, exist_ok=True)
    cell_w = keyposes.width // 2
    cell_h = keyposes.height // 2
    poses = [
        keyposes.crop((column * cell_w, row * cell_h,
                       (column + 1) * cell_w, (row + 1) * cell_h))
        for row in range(2) for column in range(2)
    ]
    sequence = (0, 0, 1, 2, 2, 1, 3, 3)
    visible = [pose.crop(pose.getbbox()) for pose in poses if pose.getbbox()]
    common_scale = min(326 / max(pose.width for pose in visible),
                       366 / max(pose.height for pose in visible))
    frames = []
    for pose_index in sequence:
        pose = poses[pose_index]
        bbox = pose.getbbox()
        if bbox is None:
            raise ValueError(f"pose {pose_index} has no visible pixels")
        crop = pose.crop(bbox)
        frame = Image.new("RGBA", (360, 400), (244, 242, 236, 255))
        crop = crop.resize((round(crop.width * common_scale), round(crop.height * common_scale)), Image.Resampling.LANCZOS)
        frame.alpha_composite(crop, ((360 - crop.width) // 2, 390 - crop.height))
        frames.append(frame.convert("RGB"))

    frames[0].save(QA_DIR / "gugu-interrupted-shrug.gif", save_all=True,
                   append_images=frames[1:], duration=[130] * 7 + [260], loop=0)
    board = Image.new("RGB", (360 * 4, 400 * 2), (244, 242, 236))
    draw = ImageDraw.Draw(board)
    for index, frame in enumerate(frames):
        x = (index % 4) * 360
        y = (index // 4) * 400
        board.paste(frame, (x, y))
        draw.text((x + 12, y + 10), str(index + 1), fill=(80, 70, 64))
    board.save(QA_DIR / "gugu-interrupted-shrug-contact-sheet.png", optimize=True)


def main() -> None:
    transparent = remove_connected_checkerboard(Image.open(SOURCE))
    TRANSPARENT.parent.mkdir(parents=True, exist_ok=True)
    transparent.save(TRANSPARENT, optimize=True)
    make_qa(transparent)
    print(f"Wrote {TRANSPARENT} ({transparent.width}x{transparent.height})")
    print(f"Wrote QA to {QA_DIR}")


if __name__ == "__main__":
    main()
