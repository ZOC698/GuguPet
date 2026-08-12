from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
ATLAS = ROOT / "Assets" / "idle-actions.png"
OUTPUT_DIR = ROOT.parent / "work" / "gugu-idle-animation-qa"
CELL_W, CELL_H = 192, 208
LABELS = (
    "弹吉他", "吃饼干", "侧睡", "趴窝", "仰睡", "星星眼", "线圈眼", "托腮思考",
    "需要输入", "喝水", "伸懒腰",
    "坐着思考", "摸摸头", "护住肚子",
    "胜利欢呼", "开心鼓掌", "企鹅摇摆舞",
)


def get_font(size: int) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    for path in (Path(r"C:\Windows\Fonts\msyh.ttc"), Path(r"C:\Windows\Fonts\simhei.ttf")):
        if path.exists():
            return ImageFont.truetype(str(path), size)
    return ImageFont.load_default()


def main() -> None:
    atlas = Image.open(ATLAS).convert("RGBA")
    panel_w, panel_h, label_h = 224, 244, 38
    font = get_font(22)
    frames: list[Image.Image] = []

    for column in range(8):
        canvas = Image.new("RGBA", (panel_w * len(LABELS), panel_h), (232, 233, 236, 255))
        draw = ImageDraw.Draw(canvas)
        for row, label in enumerate(LABELS):
            sprite = atlas.crop((column * CELL_W, row * CELL_H, (column + 1) * CELL_W, (row + 1) * CELL_H))
            x = row * panel_w + (panel_w - CELL_W) // 2
            canvas.alpha_composite(sprite, (x, label_h))
            bbox = draw.textbbox((0, 0), label, font=font)
            tx = row * panel_w + (panel_w - (bbox[2] - bbox[0])) // 2
            draw.text((tx, 5), label, font=font, fill=(32, 33, 36, 255))
        frames.append(canvas.convert("P", palette=Image.Palette.ADAPTIVE))

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    gif_path = OUTPUT_DIR / "idle-actions-preview.gif"
    frames[0].save(
        gif_path,
        save_all=True,
        append_images=frames[1:],
        duration=(155, 155, 155, 180, 155, 155, 155, 260),
        loop=0,
        disposal=2,
    )
    png_path = OUTPUT_DIR / "idle-actions-contact-sheet.png"
    frames[0].convert("RGB").save(png_path)
    print(f"Wrote {gif_path}")
    print(f"Wrote {png_path}")


if __name__ == "__main__":
    main()
