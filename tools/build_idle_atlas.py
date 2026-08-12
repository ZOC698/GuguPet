from __future__ import annotations

import math
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
CONCEPTS = ROOT.parent / "work" / "gugu-idle-concepts" / "generated"
THINKING = ROOT.parent / "work" / "gugu-thinking-concepts"
PERSONALITY = ROOT.parent / "work" / "gugu-personality-actions" / "generated"
CELEBRATIONS = ROOT.parent / "work" / "gugu-completion-celebrations" / "generated"
OUTPUT = ROOT / "Assets" / "idle-actions.png"

CELL_W = 192
CELL_H = 208
FRAMES = 8

ROWS = (
    ("guitar", "gugu-guitar-concept-transparent.png"),
    ("cookie", "gugu-cookie-concept-transparent.png"),
    ("sleep-side", "gugu-sleep-concept-transparent.png"),
    ("sleep-prone", "gugu-sleep-prone-concept-v2-transparent.png"),
    ("sleep-supine", "gugu-sleep-supine-concept-v2-transparent.png"),
    ("thinking-star", "gugu-thinking-star-eyes-transparent.png"),
    ("thinking-spiral", "gugu-thinking-spiral-eyes-transparent.png"),
    ("thinking-chin", "gugu-thinking-chin-rest-transparent.png"),
    ("needs-input", "gugu-needs-input-keyposes-transparent.png"),
    ("drink", "gugu-drink-keyposes-transparent.png"),
    ("stretch", "gugu-stretch-keyposes-transparent.png"),
    ("sit-think", "gugu-sit-think-keyposes-transparent.png"),
    ("head-pat", "gugu-head-pat-keyposes-transparent.png"),
    ("belly-poke", "gugu-belly-poke-keyposes-transparent.png"),
    ("celebrate-cheer", "gugu-celebration-cheer-keyposes-transparent.png"),
    ("celebrate-clap", "gugu-celebration-clap-keyposes-transparent.png"),
    ("celebrate-dance", "gugu-celebration-dance-keyposes-transparent.png"),
)

KEYPOSE_FILES = {
    "guitar": "gugu-guitar-keyposes-transparent.png",
    "cookie": "gugu-cookie-keyposes-transparent.png",
    "needs-input": "gugu-needs-input-keyposes-transparent.png",
    "drink": "gugu-drink-keyposes-transparent.png",
    "stretch": "gugu-stretch-keyposes-transparent.png",
    "sit-think": "gugu-sit-think-keyposes-transparent.png",
    "head-pat": "gugu-head-pat-keyposes-transparent.png",
    "belly-poke": "gugu-belly-poke-keyposes-transparent.png",
    "celebrate-cheer": "gugu-celebration-cheer-keyposes-transparent.png",
    "celebrate-clap": "gugu-celebration-clap-keyposes-transparent.png",
    "celebrate-dance": "gugu-celebration-dance-keyposes-transparent.png",
}

KEYPOSE_SEQUENCE = {
    "guitar": (0, 1, 2, 3, 2, 1, 0, 1),
    "cookie": (0, 1, 2, 3, 2, 1, 0, 0),
    "needs-input": (0, 1, 2, 3, 2, 1, 0, 0),
    "drink": (0, 1, 2, 2, 3, 2, 1, 0),
    "stretch": (0, 1, 2, 2, 3, 1, 0, 0),
    "sit-think": (0, 1, 2, 3, 2, 1, 0, 0),
    "head-pat": (0, 1, 2, 2, 3, 2, 1, 0),
    "belly-poke": (0, 1, 2, 2, 3, 2, 1, 0),
    # Hold the authored extremes instead of transforming the whole character;
    # this keeps Gugu's face, belly volume, and shoulder roots pixel-stable.
    "celebrate-cheer": (0, 1, 2, 3, 2, 1, 0, 1),
    "celebrate-clap": (0, 1, 2, 3, 0, 1, 2, 3),
    "celebrate-dance": (0, 1, 2, 1, 0, 1, 2, 3),
}


def crop_visible(image: Image.Image) -> Image.Image:
    bbox = image.getbbox()
    if bbox is None:
        raise ValueError("source image has no visible pixels")
    return image.crop(bbox)


def fit_source(image: Image.Image, row_name: str) -> Image.Image:
    image = crop_visible(image.convert("RGBA"))
    # Wide sleeping poses need almost all horizontal space; upright actions need
    # slightly more breathing room around hands, props, and feet.
    padding_x = 5 if row_name.startswith("sleep-") else 9
    padding_y = 8
    scale = min(
        (CELL_W - 2 * padding_x) / image.width,
        (CELL_H - 2 * padding_y) / image.height,
    )
    size = (max(1, round(image.width * scale)), max(1, round(image.height * scale)))
    return image.resize(size, Image.Resampling.LANCZOS)


def load_keyposes(path: Path) -> list[Image.Image]:
    board = path.open("rb")
    with board:
        image = Image.open(board).convert("RGBA")
        image.load()
    half_w = image.width // 2
    half_h = image.height // 2
    quadrants = (
        (0, 0, half_w, half_h),
        (half_w, 0, image.width, half_h),
        (0, half_h, half_w, image.height),
        (half_w, half_h, image.width, image.height),
    )
    poses = [crop_visible(image.crop(box)) for box in quadrants]
    max_w = max(pose.width for pose in poses)
    max_h = max(pose.height for pose in poses)
    scale = min((CELL_W - 16) / max_w, (CELL_H - 14) / max_h)
    return [
        pose.resize(
            (max(1, round(pose.width * scale)), max(1, round(pose.height * scale))),
            Image.Resampling.LANCZOS,
        )
        for pose in poses
    ]


def place_pose(pose: Image.Image) -> Image.Image:
    frame = Image.new("RGBA", (CELL_W, CELL_H), (0, 0, 0, 0))
    frame.alpha_composite(pose, ((CELL_W - pose.width) // 2, CELL_H - pose.height - 5))
    return frame


def transformed_frame(source: Image.Image, row_name: str, phase: float) -> Image.Image:
    # Thinking is conveyed by the authored pose and by switching among the
    # three thinking variants. Keep the complete character pixel-locked within
    # each variant: whole-sprite rotation, squash/stretch, and vertical offsets
    # made Gugu visibly wobble and changed her apparent size.
    if row_name.startswith("thinking-"):
        return place_pose(source)

    wave = math.sin(phase)
    wave2 = math.sin(phase * 2)

    if row_name == "guitar":
        scale_x, scale_y = 1.0, 1.0 + 0.008 * wave
        angle = 1.6 * wave
        y_offset = round(-1.5 * wave)
    elif row_name == "cookie":
        # A small chew/squash loop: the cookie remains attached to both flippers,
        # while the whole compact pose compresses and rises by a few pixels.
        scale_x = 1.0 + 0.006 * wave2
        scale_y = 1.0 - 0.018 * max(0.0, wave)
        angle = 0.35 * wave2
        y_offset = round(-2.0 * max(0.0, wave))
    elif row_name == "sleep-supine":
        # The large belly visibly inflates and settles without sliding the feet.
        scale_x = 1.0 + 0.012 * wave
        scale_y = 1.0 + 0.022 * wave
        angle = 0.0
        y_offset = round(-1.0 * max(0.0, wave))
    else:
        scale_x = 1.0 + 0.008 * wave
        scale_y = 1.0 + 0.015 * wave
        angle = 0.25 * wave
        y_offset = round(-1.0 * max(0.0, wave))

    resized = source.resize(
        (max(1, round(source.width * scale_x)), max(1, round(source.height * scale_y))),
        Image.Resampling.LANCZOS,
    )
    if angle:
        resized = resized.rotate(angle, resample=Image.Resampling.BICUBIC, expand=True)

    frame = Image.new("RGBA", (CELL_W, CELL_H), (0, 0, 0, 0))
    x = (CELL_W - resized.width) // 2
    y = CELL_H - resized.height - 5 + y_offset
    frame.alpha_composite(resized, (x, y))
    return frame


def main() -> None:
    atlas = Image.new("RGBA", (CELL_W * FRAMES, CELL_H * len(ROWS)), (0, 0, 0, 0))
    for row, (row_name, filename) in enumerate(ROWS):
        keypose_root = CELEBRATIONS if row_name.startswith("celebrate-") else PERSONALITY if row_name in {
            "needs-input", "drink", "stretch", "sit-think", "head-pat", "belly-poke"
        } else CONCEPTS
        keypose_path = keypose_root / KEYPOSE_FILES.get(row_name, "")
        if row_name in KEYPOSE_FILES and keypose_path.exists():
            poses = load_keyposes(keypose_path)
            for column, pose_index in enumerate(KEYPOSE_SEQUENCE[row_name]):
                atlas.alpha_composite(place_pose(poses[pose_index]), (column * CELL_W, row * CELL_H))
            print(f"Using semantic key poses for {row_name}: {keypose_path}")
            continue

        source_path = (THINKING if row_name.startswith("thinking-") else CONCEPTS) / filename
        if not source_path.exists():
            raise FileNotFoundError(source_path)
        source = fit_source(Image.open(source_path), row_name)
        for column in range(FRAMES):
            phase = (column / FRAMES) * math.tau
            frame = transformed_frame(source, row_name, phase)
            atlas.alpha_composite(frame, (column * CELL_W, row * CELL_H))

    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    atlas.save(OUTPUT, optimize=True)
    print(f"Wrote {OUTPUT} ({atlas.width}x{atlas.height})")


if __name__ == "__main__":
    main()
