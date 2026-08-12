from __future__ import annotations

import argparse
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

from build_walk_repair import CELL_H, CELL_W, remove_blue


ROOT = Path(__file__).resolve().parents[1]
WORK = ROOT.parent / "work"
ORIGINAL = WORK / "gugu-original-walk" / "frames"
GENERATED = WORK / "gugu-walk-16" / "generated"
QA = WORK / "gugu-walk-16" / "qa"
FINAL = WORK / "gugu-walk-16" / "final"


def font(size: int):
    path = Path(r"C:\Windows\Fonts\msyh.ttc")
    return ImageFont.truetype(str(path), size) if path.exists() else ImageFont.load_default()


def load_original(direction: str) -> list[Image.Image]:
    return [Image.open(ORIGINAL / direction / f"{index:02d}.png").convert("RGBA") for index in range(8)]


def split_board(path: Path) -> list[Image.Image]:
    board = remove_blue(Image.open(path))
    if board.width / board.height >= 2.5:
        columns, rows = 8, 1
    else:
        columns, rows = 4, 2
    tile_w, tile_h = board.width // columns, board.height // rows
    poses = []
    for row in range(rows):
        for column in range(columns):
            right = (column + 1) * tile_w if column < columns - 1 else board.width
            bottom = (row + 1) * tile_h if row < rows - 1 else board.height
            tile = board.crop((column * tile_w, row * tile_h, right, bottom))
            bbox = tile.getbbox()
            if bbox is None:
                raise ValueError(f"Missing pose {len(poses) + 1} in {path}")
            poses.append(tile.crop(bbox))
    return poses


def normalize_generated(poses: list[Image.Image]) -> list[Image.Image]:
    heights = [pose.height for pose in poses]
    widths = [pose.width for pose in poses]
    # One scale for the entire generated half-cycle. Never resize each frame
    # independently: that was the source of the earlier visual size pumping.
    scale = min(198 / max(heights), 182 / max(widths))
    frames = []
    for pose in poses:
        resized = pose.resize(
            (max(1, round(pose.width * scale)), max(1, round(pose.height * scale))),
            Image.Resampling.LANCZOS,
        )
        frame = Image.new("RGBA", (CELL_W, CELL_H), (0, 0, 0, 0))
        x = round(96 - resized.width / 2)
        y = 203 - resized.height
        frame.alpha_composite(resized, (x, y))
        frames.append(frame)
    return frames


def lock_original_body(generated: list[Image.Image], templates: list[Image.Image]) -> list[Image.Image]:
    """Keep the exact original upper body and blend only into generated lower motion."""
    if len(generated) != len(templates):
        raise ValueError("Generated and template frame counts differ")
    lower_motion = Image.new("L", (CELL_W, CELL_H), 0)
    lower_pixels = lower_motion.load()
    for y in range(150, CELL_H):
        value = 255 if y >= 174 else round((y - 150) / 24 * 255)
        for x in range(CELL_W):
            lower_pixels[x, y] = value

    locked = []
    for motion, template in zip(generated, templates):
        template = template.convert("RGBA")
        locked.append(Image.composite(motion, template, lower_motion))
    return locked


def save_gif(frames: list[Image.Image], path: Path) -> None:
    rendered = []
    for frame in frames:
        canvas = Image.new("RGBA", (CELL_W, CELL_H), (242, 243, 246, 255))
        canvas.alpha_composite(frame)
        rendered.append(canvas.resize((CELL_W * 2, CELL_H * 2), Image.Resampling.LANCZOS).convert(
            "P", palette=Image.Palette.ADAPTIVE
        ))
    rendered[0].save(path, save_all=True, append_images=rendered[1:], duration=70, loop=0, disposal=2)


def parse_order(value: str) -> list[int]:
    order = [int(item.strip()) - 1 for item in value.split(",")]
    if sorted(order) != list(range(8)):
        raise ValueError("--left-order must contain every original frame 1..8 exactly once")
    return order


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--left-order", default="1,2,3,4,5,6,7,8")
    parser.add_argument("--right-board", default="right-complementary-8.png")
    parser.add_argument("--left-board", default="left-bridges-8.png")
    parser.add_argument("--generated-dir", type=Path, default=GENERATED)
    parser.add_argument("--output-root", type=Path, default=WORK / "gugu-walk-16")
    args = parser.parse_args()
    left_order = parse_order(args.left_order)
    generated_dir = args.generated_dir.resolve()
    qa_dir = (args.output_root / "qa").resolve()
    final_dir = (args.output_root / "final").resolve()

    right_original = load_original("right")
    left_original = load_original("left")
    right_new = lock_original_body(
        normalize_generated(split_board(generated_dir / args.right_board)),
        right_original[4:] + right_original[:4],
    )
    left_templates = [left_original[index] for index in left_order]
    left_new = lock_original_body(
        normalize_generated(split_board(generated_dir / args.left_board)),
        left_templates,
    )

    right = right_original + right_new
    left = []
    for transition, original_index in enumerate(left_order):
        left.extend((left_original[original_index], left_new[transition]))
    sequences = {"right": right, "left": left}

    final_dir.mkdir(parents=True, exist_ok=True)
    qa_dir.mkdir(parents=True, exist_ok=True)
    atlas = Image.new("RGBA", (CELL_W * 16, CELL_H * 2), (0, 0, 0, 0))
    manifest: dict[str, object] = {
        "cell": [CELL_W, CELL_H],
        "columns": 16,
        "rows": 2,
        "left_original_order": [index + 1 for index in left_order],
        "identity_lock": "exact original upper body through y=150, blended into generated lower-body motion by y=174",
        "directions": {},
    }
    for row, (direction, frames) in enumerate(sequences.items()):
        metrics = []
        for column, frame in enumerate(frames):
            atlas.alpha_composite(frame, (column * CELL_W, row * CELL_H))
            bbox = frame.getbbox()
            metrics.append({
                "frame": column + 1,
                "source": (
                    f"original-{column + 1}"
                    if direction == "right" and column < 8
                    else f"generated-{column - 7}"
                    if direction == "right"
                    else f"original-{left_order[column // 2] + 1}"
                    if column % 2 == 0
                    else f"generated-bridge-{column // 2 + 1}"
                ),
                "bbox": list(bbox) if bbox else None,
            })
        manifest["directions"][direction] = metrics
        save_gif(frames, qa_dir / f"walk16-{direction}.gif")
    atlas.save(final_dir / "walk-spritesheet-16.png", optimize=True)

    label_h = 34
    contact = Image.new("RGBA", (CELL_W * 8, (CELL_H + label_h) * 4), (242, 243, 246, 255))
    draw = ImageDraw.Draw(contact)
    display_row = 0
    for direction, frames in sequences.items():
        for half in range(2):
            top = display_row * (CELL_H + label_h)
            label = "向右" if direction == "right" else "向左"
            draw.text((6, top + 6), f"{label} {half * 8 + 1}-{half * 8 + 8}", font=font(16), fill=(25, 25, 28, 255))
            for column, frame in enumerate(frames[half * 8:(half + 1) * 8]):
                frame_no = half * 8 + column + 1
                draw.text((column * CELL_W + 84, top + 7), str(frame_no), font=font(15), fill=(50, 50, 55, 255))
                contact.alpha_composite(frame, (column * CELL_W, top + label_h))
            display_row += 1
    contact.convert("RGB").save(qa_dir / "walk16-contact-sheet.png", quality=96)
    (qa_dir / "walk16-manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    print(qa_dir / "walk16-contact-sheet.png")


if __name__ == "__main__":
    main()
