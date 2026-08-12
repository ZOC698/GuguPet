from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
WORK = ROOT.parent / "work" / "gugu-walk-repair"
GENERATED = WORK / "generated"
QA = WORK / "qa"
ATLAS = ROOT / "Assets" / "spritesheet.png"
CELL_W, CELL_H = 192, 208
SEQUENCE = (0, 1, 2, 3, 0, 1, 2, 3)
PHASE_LABELS = ("近腿前", "收脚", "远腿前", "收脚", "近腿前", "收脚", "远腿前", "收脚")


def remove_blue(image: Image.Image) -> Image.Image:
    image = image.convert("RGBA")
    pixels = image.load()
    for y in range(image.height):
        for x in range(image.width):
            r, g, b, a = pixels[x, y]
            dominance = b - max(r, g)
            if b > 105 and dominance > 46:
                pixels[x, y] = (r, g, min(b, max(r, g)), 0)
            elif b > 90 and dominance > 18:
                edge_alpha = max(0, min(a, round(a * (46 - dominance) / 28)))
                pixels[x, y] = (r, g, min(b, max(r, g) + 8), edge_alpha)
            elif a:
                pixels[x, y] = (r, g, min(b, max(r, g) + 22), a)
    return image


def split_board(path: Path) -> list[Image.Image]:
    board = remove_blue(Image.open(path))
    half_w, half_h = board.width // 2, board.height // 2
    boxes = (
        (0, 0, half_w, half_h),
        (half_w, 0, board.width, half_h),
        (0, half_h, half_w, board.height),
        (half_w, half_h, board.width, board.height),
    )
    poses = []
    for box in boxes:
        pose = board.crop(box)
        bbox = pose.getbbox()
        if bbox is None:
            raise ValueError(f"No visible pose in {path}: {box}")
        poses.append(pose.crop(bbox))
    return poses


def normalize(poses: list[Image.Image]) -> list[Image.Image]:
    max_w = max(p.width for p in poses)
    max_h = max(p.height for p in poses)
    scale = min((CELL_W - 10) / max_w, (CELL_H - 10) / max_h)
    result = []
    for pose in poses:
        resized = pose.resize(
            (max(1, round(pose.width * scale)), max(1, round(pose.height * scale))),
            Image.Resampling.LANCZOS,
        )
        frame = Image.new("RGBA", (CELL_W, CELL_H), (0, 0, 0, 0))
        frame.alpha_composite(resized, ((CELL_W - resized.width) // 2, CELL_H - resized.height - 4))
        result.append(frame)
    return result


def font(size: int):
    path = Path(r"C:\Windows\Fonts\msyh.ttc")
    return ImageFont.truetype(str(path), size) if path.exists() else ImageFont.load_default()


def main() -> None:
    sources = {
        1: ("向右", GENERATED / "running-right-keyposes.png"),
        2: ("向左", GENERATED / "running-left-keyposes.png"),
    }
    atlas = Image.open(ATLAS).convert("RGBA")
    qa_rows = []
    gif_frames_by_row: dict[int, list[Image.Image]] = {}
    for row, (label, source) in sources.items():
        poses = normalize(split_board(source))
        atlas.paste((0, 0, 0, 0), (0, row * CELL_H, CELL_W * 8, (row + 1) * CELL_H))
        row_preview = Image.new("RGBA", (CELL_W * 8, CELL_H + 32), (242, 243, 246, 255))
        draw = ImageDraw.Draw(row_preview)
        draw.text((6, 4), label, font=font(18), fill=(25, 25, 28, 255))
        for column, pose_index in enumerate(SEQUENCE):
            draw.text((column * CELL_W + 48, 5), PHASE_LABELS[column], font=font(16), fill=(45, 45, 48, 255))
            frame = poses[pose_index]
            atlas.alpha_composite(frame, (column * CELL_W, row * CELL_H), (0, 0, CELL_W, CELL_H))
            row_preview.alpha_composite(frame, (column * CELL_W, 32))
        qa_rows.append(row_preview)

        gif_frames = []
        for pose_index in SEQUENCE[:4]:
            canvas = Image.new("RGBA", (CELL_W, CELL_H), (242, 243, 246, 255))
            canvas.alpha_composite(poses[pose_index])
            gif_frames.append(canvas.convert("P", palette=Image.Palette.ADAPTIVE))
        gif_frames_by_row[row] = gif_frames

    QA.mkdir(parents=True, exist_ok=True)
    candidate_atlas = QA / "candidate-spritesheet.png"
    atlas.save(candidate_atlas, optimize=True)
    contact = Image.new("RGBA", (CELL_W * 8, (CELL_H + 32) * 2), (242, 243, 246, 255))
    for i, row_preview in enumerate(qa_rows):
        contact.alpha_composite(row_preview, (0, i * (CELL_H + 32)))
    contact.convert("RGB").save(QA / "walk-contact-sheet.png")
    for row, filename in ((1, "walk-right-preview.gif"), (2, "walk-left-preview.gif")):
        gif_frames = gif_frames_by_row[row]
        gif_frames[0].save(
            QA / filename,
            save_all=True,
            append_images=gif_frames[1:],
            duration=125,
            loop=0,
            disposal=2,
        )
    print(f"Wrote {candidate_atlas}")
    print(f"Wrote {QA / 'walk-contact-sheet.png'}")


if __name__ == "__main__":
    main()
