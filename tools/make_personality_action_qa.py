from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
ATLAS = ROOT / "Assets" / "idle-actions.png"
OUTPUT_DIR = ROOT.parent / "work" / "gugu-personality-actions" / "qa"
CELL_W, CELL_H = 192, 208
ACTIONS = (
    (8, "needs-input", "需要输入"),
    (9, "drink", "喝水"),
    (10, "stretch", "伸懒腰"),
    (11, "sit-think", "坐着思考"),
    (12, "head-pat", "摸摸头"),
    (13, "belly-poke", "护住肚子"),
    (14, "celebrate-cheer", "胜利欢呼"),
    (15, "celebrate-clap", "开心鼓掌"),
    (16, "celebrate-dance", "企鹅摇摆舞"),
)


def font(size: int):
    path = Path(r"C:\Windows\Fonts\msyh.ttc")
    return ImageFont.truetype(str(path), size) if path.exists() else ImageFont.load_default()


def main() -> None:
    atlas = Image.open(ATLAS).convert("RGBA")
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    scale = 0.75
    sprite_w, sprite_h = round(CELL_W * scale), round(CELL_H * scale)
    label_w, label_h = 106, 30
    sheet = Image.new("RGB", (label_w + sprite_w * 8, (sprite_h + label_h) * len(ACTIONS)), (235, 236, 239))
    draw = ImageDraw.Draw(sheet)
    metrics: dict[str, list[tuple[int, int, int, int]]] = {}

    for action_index, (row, slug, label) in enumerate(ACTIONS):
        gif_frames: list[Image.Image] = []
        row_metrics: list[tuple[int, int, int, int]] = []
        y0 = action_index * (sprite_h + label_h)
        draw.text((10, y0 + (sprite_h - 24) // 2), label, font=font(20), fill=(28, 29, 33))
        for column in range(8):
            frame = atlas.crop((column * CELL_W, row * CELL_H, (column + 1) * CELL_W, (row + 1) * CELL_H))
            bbox = frame.getbbox()
            if bbox is None:
                raise ValueError(f"blank frame: {slug}/{column}")
            row_metrics.append(bbox)
            preview = Image.new("RGBA", (CELL_W, CELL_H), (235, 236, 239, 255))
            preview.alpha_composite(frame)
            gif_frames.append(preview.convert("P", palette=Image.Palette.ADAPTIVE))
            thumb = preview.resize((sprite_w, sprite_h), Image.Resampling.LANCZOS).convert("RGB")
            x = label_w + column * sprite_w
            sheet.paste(thumb, (x, y0 + label_h))
            draw.text((x + 5, y0 + 4), str(column + 1), font=font(15), fill=(75, 76, 82))
        metrics[slug] = row_metrics
        gif_frames[0].save(
            OUTPUT_DIR / f"{slug}.gif",
            save_all=True,
            append_images=gif_frames[1:],
            duration=170,
            loop=0,
            disposal=2,
        )

    sheet.save(OUTPUT_DIR / "personality-actions-contact-sheet.png")
    print(OUTPUT_DIR / "personality-actions-contact-sheet.png")
    for slug, boxes in metrics.items():
        widths = [box[2] - box[0] for box in boxes]
        heights = [box[3] - box[1] for box in boxes]
        baselines = [box[3] for box in boxes]
        print(
            f"{slug}: width={min(widths)}..{max(widths)}, "
            f"height={min(heights)}..{max(heights)}, baseline={min(baselines)}..{max(baselines)}"
        )


if __name__ == "__main__":
    main()
