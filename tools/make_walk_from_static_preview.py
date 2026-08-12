from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

from build_walk_repair import CELL_H, CELL_W, remove_blue


ROOT = Path(__file__).resolve().parents[1]
WORK = ROOT.parent / "work"
APPROVED = WORK / "gugu-walk-static" / "approved"
GENERATED = WORK / "gugu-walk-animation" / "generated"
QA = WORK / "gugu-walk-animation" / "qa"
LABELS = ("近腿着地", "承重", "过腿", "远腿前抬", "远腿着地", "承重", "过腿", "近腿前抬")


def font(size: int):
    path = Path(r"C:\Windows\Fonts\msyh.ttc")
    return ImageFont.truetype(str(path), size) if path.exists() else ImageFont.load_default()


def single_pose(path: Path) -> Image.Image:
    image = remove_blue(Image.open(path))
    bbox = image.getbbox()
    if bbox is None:
        raise ValueError(f"No visible character: {path}")
    return image.crop(bbox)


def split3(path: Path) -> list[Image.Image]:
    board = remove_blue(Image.open(path))
    tile_w = board.width // 3
    poses = []
    for column in range(3):
        tile = board.crop((column * tile_w, 0, (column + 1) * tile_w if column < 2 else board.width, board.height))
        bbox = tile.getbbox()
        if bbox is None:
            raise ValueError(f"Missing intermediate {column + 1}: {path}")
        poses.append(tile.crop(bbox))
    return poses


def hood_metrics(pose: Image.Image) -> tuple[float, float]:
    alpha = pose.getchannel("A")
    rows = []
    for y in range(round(pose.height * .05), round(pose.height * .42)):
        bbox = alpha.crop((0, y, pose.width, y + 1)).getbbox()
        if bbox is not None and bbox[2] - bbox[0] > pose.width * .25:
            rows.append((bbox[2] - bbox[0], (bbox[0] + bbox[2]) / 2))
    if not rows:
        return float(pose.width), pose.width / 2
    rows.sort(key=lambda item: item[0])
    selected = rows[round(len(rows) * .72):]
    span = sum(item[0] for item in selected) / len(selected)
    center = sum(item[1] for item in selected) / len(selected)
    return span, center


def normalize_stable_height(poses: list[Image.Image]) -> list[Image.Image]:
    frames = []
    for pose in poses:
        _, hood_center = hood_metrics(pose)
        # Keep every walking pose at one full-character height.  Scaling by the
        # hood width made small source-shape differences look like breathing or
        # size popping, which is especially distracting in a restrained walk.
        scale = (CELL_H - 9) / pose.height
        if pose.width * scale > CELL_W - 10:
            scale = (CELL_W - 10) / pose.width
        resized = pose.resize(
            (max(1, round(pose.width * scale)), max(1, round(pose.height * scale))),
            Image.Resampling.LANCZOS,
        )
        frame = Image.new("RGBA", (CELL_W, CELL_H), (0, 0, 0, 0))
        x = round(CELL_W / 2 - hood_center * scale)
        min_x = min(0, CELL_W - resized.width)
        max_x = max(0, CELL_W - resized.width)
        x = max(min_x, min(max_x, x))
        frame.alpha_composite(resized, (x, CELL_H - resized.height - 4))
        frames.append(frame)
    return frames


def load_direction(key: str) -> list[Image.Image]:
    near = single_pose(APPROVED / f"{key}-near-leg-forward.png")
    far = single_pose(APPROVED / f"{key}-far-leg-forward.png")
    near_support_name = "right-near-support-stable.png" if key == "right" else "left-near-support.png"
    far_support_name = "right-far-support-stable.png" if key == "right" else "left-far-support.png"
    near_support = single_pose(GENERATED / near_support_name)
    far_support = single_pose(GENERATED / far_support_name)
    first_half = split3(GENERATED / f"{key}-a-to-b-intermediates.png")
    second_half = split3(GENERATED / f"{key}-b-to-a-intermediates.png")
    first_passing = (
        single_pose(GENERATED / "left-passing-a-to-b-stable.png")
        if key == "left" else first_half[1]
    )
    second_passing = (
        single_pose(GENERATED / "left-passing-b-to-a-stable.png")
        if key == "left" else second_half[1]
    )
    return normalize_stable_height([
        near, near_support, first_passing, first_half[2],
        far, far_support, second_passing, second_half[2],
    ])


def save_gif(frames: list[Image.Image], path: Path) -> None:
    output = []
    for frame in frames:
        canvas = Image.new("RGBA", (CELL_W, CELL_H), (242, 243, 246, 255))
        canvas.alpha_composite(frame)
        output.append(canvas.resize((CELL_W * 2, CELL_H * 2), Image.Resampling.LANCZOS).convert("P", palette=Image.Palette.ADAPTIVE))
    output[0].save(path, save_all=True, append_images=output[1:], duration=110, loop=0, disposal=2)


def main() -> None:
    QA.mkdir(parents=True, exist_ok=True)
    frames_by_direction = {key: load_direction(key) for key in ("right", "left")}
    label_h = 34
    contact = Image.new("RGBA", (CELL_W * 8, (CELL_H + label_h) * 2), (242, 243, 246, 255))
    draw = ImageDraw.Draw(contact)
    for row, key in enumerate(("right", "left")):
        top = row * (CELL_H + label_h)
        draw.text((5, top + 5), "向右" if key == "right" else "向左", font=font(17), fill=(25, 25, 28, 255))
        for column, frame in enumerate(frames_by_direction[key]):
            draw.text((column * CELL_W + 53, top + 7), LABELS[column], font=font(14), fill=(45, 45, 48, 255))
            contact.alpha_composite(frame, (column * CELL_W, top + label_h))
        save_gif(frames_by_direction[key], QA / f"walk-{key}-from-static.gif")
    contact.convert("RGB").save(QA / "walk-from-static-contact-sheet.png")
    print(QA / "walk-from-static-contact-sheet.png")


if __name__ == "__main__":
    main()
