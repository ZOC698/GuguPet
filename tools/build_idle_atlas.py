from __future__ import annotations

import math
from bisect import bisect_left, bisect_right
from pathlib import Path

from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
CONCEPTS = ROOT.parent / "work" / "gugu-idle-concepts" / "generated"
THINKING = ROOT.parent / "work" / "gugu-thinking-concepts"
PERSONALITY = ROOT.parent / "work" / "gugu-personality-actions" / "generated"
INTERRUPTED = ROOT.parent / "work" / "gugu-interrupted" / "generated"
CELEBRATIONS = ROOT.parent / "work" / "gugu-completion-celebrations" / "generated"
PASSIVE_DRAG = ROOT.parent / "work" / "gugu-passive-drag" / "generated"
PASSIVE_DRAG_EXPRESSIVE = ROOT.parent / "work" / "gugu-passive-drag" / "expressive" / "generated"
PASSIVE_DRAG_EXPRESSIVE_RIGHT = PASSIVE_DRAG_EXPRESSIVE / "gugu-passive-drag-expressive-right-pair-transparent.png"
MUSIC = ROOT.parent / "work" / "gugu-music-actions" / "generated"
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
    ("passive-drag", "gugu-passive-drag-keyposes-transparent.png"),
    ("passive-drag-expressive", "gugu-passive-drag-expressive-keyposes-v2-transparent.png"),
    ("interrupted", "gugu-interrupted-shrug-keyposes-transparent.png"),
    ("headphones", "gugu-headphones-keyposes-v1-transparent.png"),
    ("drums", "gugu-drums-keyposes-v2-compact-transparent.png"),
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
    "passive-drag": "gugu-passive-drag-keyposes-transparent.png",
    "passive-drag-expressive": "gugu-passive-drag-expressive-keyposes-v2-transparent.png",
    "interrupted": "gugu-interrupted-shrug-keyposes-transparent.png",
    "headphones": "gugu-headphones-keyposes-v1-transparent.png",
    "drums": "gugu-drums-keyposes-v2-compact-transparent.png",
}

KEYPOSE_GRIDS = {
    "passive-drag-expressive": (3, 2),
    "headphones": (4, 1),
    "drums": (4, 1),
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
    # Lift, centered hang, screen-left lag, centered, then screen-right lag.
    "passive-drag": (0, 1, 2, 2, 1, 3, 3, 1),
    # Center arm beats, screen-left trailing pair, screen-right trailing pair.
    "passive-drag-expressive": (0, 1, 2, 3, 4, 5, 0, 1),
    # Startled stop -> open shrug -> resigned hold -> lower both arms.
    "interrupted": (0, 0, 1, 2, 2, 1, 3, 3),
    # A restrained left-center-right listening sway with no whole-body zoom.
    "headphones": (0, 1, 2, 3, 2, 1, 0, 2),
    # Alternate snare and cymbal hits, returning through the stable ready pose.
    "drums": (0, 1, 0, 2, 0, 3, 0, 2),
}


def crop_visible(image: Image.Image) -> Image.Image:
    bbox = image.getbbox()
    if bbox is None:
        raise ValueError("source image has no visible pixels")
    return image.crop(bbox)


def _clamp_channel(value: float) -> int:
    return max(0, min(255, round(value)))


def _smoothstep(edge0: float, edge1: float, value: float) -> float:
    if edge0 == edge1:
        return float(value >= edge1)
    amount = max(0.0, min(1.0, (value - edge0) / (edge1 - edge0)))
    return amount * amount * (3.0 - 2.0 * amount)


def match_passive_drag_palette(image: Image.Image, expressive: bool) -> Image.Image:
    """Cool generated drag poses toward the canonical atlas without changing geometry."""
    result = image.convert("RGBA")
    pixels = result.load()
    for y in range(result.height):
        for x in range(result.width):
            red, green, blue, alpha = pixels[x, y]
            if alpha == 0:
                continue

            high = max(red, green, blue)
            low = min(red, green, blue)
            saturation = 0.0 if high == 0 else (high - low) / high
            dark = (1.0 - _smoothstep(115, 160, high)) * (
                1.0 - _smoothstep(0.34, 0.58, saturation)
            )
            white = _smoothstep(155, 205, low) * (
                1.0 - _smoothstep(0.16, 0.32, saturation)
            )
            orange = (
                _smoothstep(145, 205, red)
                * _smoothstep(45, 105, red - blue)
                * _smoothstep(0.32, 0.58, saturation)
                * (1.0 - _smoothstep(105, 145, blue))
            )
            skin = (
                _smoothstep(165, 215, red)
                * _smoothstep(8, 28, red - green)
                * _smoothstep(2, 20, green - blue)
                * (1.0 - _smoothstep(0.42, 0.62, saturation))
            )
            blue_gray = (
                _smoothstep(0, 18, blue - red)
                * _smoothstep(35, 70, red)
                * (1.0 - _smoothstep(165, 205, high))
                * (1.0 - _smoothstep(0.38, 0.58, saturation))
            )

            if expressive:
                deltas = (
                    dark * 4 - orange * 5 - skin * 3 - blue_gray * 7,
                    dark * 6 + white * 2 - orange * 9 - skin * 7 - blue_gray * 32,
                    dark * 15 + white * 9 + orange * 15 - skin - blue_gray * 32,
                )
            else:
                deltas = (
                    -orange - skin - blue_gray * 4,
                    white * 2 - orange * 3 - skin - blue_gray * 15,
                    dark * 6 + white * 8 + orange * 7 + skin * 7 - blue_gray * 18,
                )

            pixels[x, y] = (
                _clamp_channel(red + deltas[0]),
                _clamp_channel(green + deltas[1]),
                _clamp_channel(blue + deltas[2]),
                alpha,
            )
    return result


def _dark_yellow_values(images: tuple[Image.Image, ...]) -> list[int]:
    values: list[int] = []
    for image in images:
        for red, green, blue, alpha in image.convert("RGBA").getdata():
            if alpha > 220 and max(red, green, blue) < 150:
                # Twice the yellow index: ((R + G) / 2) - B.
                values.append(red + green - 2 * blue)
    return sorted(values)


def cool_residual_drag_yellow(frames: tuple[Image.Image, ...]) -> tuple[Image.Image, ...]:
    """Match warm dark highlights to idle while never adding yellow back."""
    canonical = Image.open(ROOT / "Assets" / "spritesheet.png").convert("RGBA")
    canonical_idle = canonical.crop((0, 0, CELL_W * FRAMES, CELL_H))
    target = _dark_yellow_values((canonical_idle,))
    source = _dark_yellow_values(frames)
    if not source or not target:
        return frames

    lookup: dict[int, float] = {}
    for yellow2 in set(source):
        first = bisect_left(source, yellow2)
        last = bisect_right(source, yellow2) - 1
        percentile = ((first + last) / 2) / max(1, len(source) - 1)
        target_index = round(percentile * (len(target) - 1))
        # Keep the generated art a touch cooler than the warm edge of the idle
        # distribution so the larger drag silhouette does not read as olive.
        target_yellow2 = target[target_index] - 4
        lookup[yellow2] = max(0.0, min(14.0, (yellow2 - target_yellow2) / 2))

    corrected_frames: list[Image.Image] = []
    for image in frames:
        corrected = image.convert("RGBA")
        pixels = corrected.load()
        for y in range(corrected.height):
            for x in range(corrected.width):
                red, green, blue, alpha = pixels[x, y]
                if alpha == 0 or max(red, green, blue) >= 150:
                    continue
                excess = lookup.get(red + green - 2 * blue, 0.0)
                if excess <= 0:
                    continue
                # Preserve approximate luminance while moving yellow into blue.
                pixels[x, y] = (
                    _clamp_channel(red - excess / 3),
                    _clamp_channel(green - excess / 3),
                    _clamp_channel(blue + excess * 2 / 3),
                    alpha,
                )
        corrected_frames.append(corrected)
    return tuple(corrected_frames)


def _directional_yellow_values(images: tuple[Image.Image, ...], family: str) -> list[int]:
    values: list[int] = []
    for image in images:
        for red, green, blue, alpha in image.convert("RGBA").getdata():
            if alpha <= 220:
                continue
            if family == "dark":
                selected = max(red, green, blue) < 150
            elif family == "orange":
                selected = red > 150 and green > 65 and blue < 120 and red - blue > 80
            else:
                raise ValueError(f"unknown palette family: {family}")
            if selected:
                values.append(red + green - 2 * blue)
    return sorted(values)


def cool_direction_to_reference(
    frames: tuple[Image.Image, ...],
    reference: tuple[Image.Image, ...],
    family: str,
) -> tuple[Image.Image, ...]:
    """Remove only excess yellow so one direction matches its approved opposite."""
    source = _directional_yellow_values(frames, family)
    target = _directional_yellow_values(reference, family)
    if not source or not target:
        return frames

    lookup: dict[int, float] = {}
    for yellow2 in set(source):
        first = bisect_left(source, yellow2)
        last = bisect_right(source, yellow2) - 1
        percentile = ((first + last) / 2) / max(1, len(source) - 1)
        target_yellow2 = target[round(percentile * (len(target) - 1))]
        lookup[yellow2] = max(0.0, min(18.0, (yellow2 - target_yellow2) / 2))

    corrected_frames: list[Image.Image] = []
    for image in frames:
        corrected = image.convert("RGBA")
        pixels = corrected.load()
        for y in range(corrected.height):
            for x in range(corrected.width):
                red, green, blue, alpha = pixels[x, y]
                if alpha == 0:
                    continue
                if family == "dark":
                    selected = max(red, green, blue) < 150
                else:
                    selected = red > 150 and green > 65 and blue < 120 and red - blue > 80
                if not selected:
                    continue
                excess = lookup.get(red + green - 2 * blue, 0.0)
                if excess <= 0:
                    continue
                pixels[x, y] = (
                    _clamp_channel(red - excess / 3),
                    _clamp_channel(green - excess / 3),
                    _clamp_channel(blue + excess * 2 / 3),
                    alpha,
                )
        corrected_frames.append(corrected)
    return tuple(corrected_frames)


def match_left_drag_to_right(
    frames: tuple[Image.Image, ...], expressive: bool
) -> tuple[Image.Image, ...]:
    result = list(frames)
    right_indices = (2, 3) if expressive else (2,)
    left_indices = (4, 5) if expressive else (5,)
    reference = tuple(result[index] for index in right_indices)
    left = tuple(result[index] for index in left_indices)
    for family in ("dark", "orange"):
        left = cool_direction_to_reference(left, reference, family)
    for index, frame in zip(left_indices, left):
        result[index] = frame
    return tuple(result)


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


def load_keyposes(path: Path, columns: int = 2, rows: int = 2) -> list[Image.Image]:
    board = path.open("rb")
    with board:
        image = Image.open(board).convert("RGBA")
        image.load()
    poses = []
    for row in range(rows):
        top = round(image.height * row / rows)
        bottom = round(image.height * (row + 1) / rows)
        for column in range(columns):
            left = round(image.width * column / columns)
            right = round(image.width * (column + 1) / columns)
            poses.append(crop_visible(image.crop((left, top, right, bottom))))
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


def hood_tip_x(pose: Image.Image) -> float:
    alpha = pose.getchannel("A")
    samples = []
    for y in range(min(18, pose.height)):
        for x in range(pose.width):
            value = alpha.getpixel((x, y))
            if value >= 96:
                samples.append((x, value))
    if not samples:
        return pose.width / 2
    total_weight = sum(weight for _, weight in samples)
    return sum(x * weight for x, weight in samples) / total_weight


def place_pose(
    pose: Image.Image,
    *,
    anchor_top: bool = False,
    anchor_hood_tip: bool = False,
) -> Image.Image:
    frame = Image.new("RGBA", (CELL_W, CELL_H), (0, 0, 0, 0))
    y = 5 if anchor_top else CELL_H - pose.height - 5
    if anchor_hood_tip:
        x = round(CELL_W / 2 - hood_tip_x(pose))
        x = min(max(5, x), CELL_W - pose.width - 5)
    else:
        x = (CELL_W - pose.width) // 2
    frame.alpha_composite(pose, (x, y))
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
        keypose_root = (
            CELEBRATIONS if row_name.startswith("celebrate-")
            else PASSIVE_DRAG_EXPRESSIVE if row_name == "passive-drag-expressive"
            else PASSIVE_DRAG if row_name == "passive-drag"
            else INTERRUPTED if row_name == "interrupted"
            else MUSIC if row_name in {"headphones", "drums"}
            else PERSONALITY if row_name in {
                "needs-input", "drink", "stretch", "sit-think", "head-pat", "belly-poke",
            }
            else CONCEPTS
        )
        keypose_path = keypose_root / KEYPOSE_FILES.get(row_name, "")
        if row_name in KEYPOSE_FILES and keypose_path.exists():
            grid_columns, grid_rows = KEYPOSE_GRIDS.get(row_name, (2, 2))
            poses = load_keyposes(keypose_path, grid_columns, grid_rows)
            if row_name == "passive-drag-expressive":
                # The six-pose edit preserved the centered and screen-left pairs,
                # while the isolated repair supplies a consistent screen-right pair.
                poses = poses[:4] + load_keyposes(PASSIVE_DRAG_EXPRESSIVE_RIGHT, 2, 1)
            is_passive_drag = row_name.startswith("passive-drag")
            row_frames = []
            for pose_index in KEYPOSE_SEQUENCE[row_name]:
                frame = place_pose(
                    poses[pose_index],
                    anchor_top=is_passive_drag,
                    anchor_hood_tip=is_passive_drag,
                )
                if is_passive_drag:
                    frame = match_passive_drag_palette(
                        frame,
                        expressive=row_name == "passive-drag-expressive",
                    )
                row_frames.append(frame)
            if is_passive_drag:
                matched_frames = cool_residual_drag_yellow(tuple(row_frames))
                row_frames = list(match_left_drag_to_right(
                    matched_frames,
                    expressive=row_name == "passive-drag-expressive",
                ))
            elif row_name in {"headphones", "drums"}:
                # Image generation can introduce a warm olive cast in dark hair
                # and cloth. Match those pixels to the approved idle palette
                # before the row is committed to the atlas.
                row_frames = list(cool_residual_drag_yellow(tuple(row_frames)))
            for column, frame in enumerate(row_frames):
                atlas.alpha_composite(frame, (column * CELL_W, row * CELL_H))
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
