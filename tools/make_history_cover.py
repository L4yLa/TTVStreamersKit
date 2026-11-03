"""履歴のプレイリストのカバー画像（src/TTVStreamersKit/Resources/history-cover.png）を作る。

図形だけで描く（文字やフォントは使わない）。
使い方: python tools/make_history_cover.py
"""
import math
import os

from PIL import Image, ImageDraw

SIZE = 256
SCALE = 4  # 縁を滑らかにするため、大きく描いてから縮める
OUT = os.path.join(os.path.dirname(__file__), "..", "src", "TTVStreamersKit", "Resources", "history-cover.png")


def main():
    s = SIZE * SCALE
    img = Image.new("RGBA", (s, s))
    draw = ImageDraw.Draw(img)

    # 背景: 上から下への青緑のグラデーション
    top, bottom = (30, 150, 170), (15, 45, 90)
    for y in range(s):
        t = y / (s - 1)
        color = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)) + (255,)
        draw.line([(0, y), (s, y)], fill=color)

    cx, cy = s / 2, s / 2
    r = s * 0.30
    width = int(s * 0.055)
    white = (255, 255, 255, 255)

    # 反時計回りの矢印の付いた円（履歴の印）。左上を少し空ける
    gap_start, gap_end = 200, 235
    draw.arc([cx - r, cy - r, cx + r, cy + r], start=gap_end, end=gap_start + 360, fill=white, width=width)

    # 矢じり: 円の切れ目の端に、円の接線方向を向いた三角形を置く
    a = math.radians(gap_end)
    ex, ey = cx + r * math.cos(a), cy + r * math.sin(a)
    tangent = a - math.pi / 2  # 反時計回りの向き
    head = s * 0.11
    tip = (ex + head * 0.9 * math.cos(tangent), ey + head * 0.9 * math.sin(tangent))
    normal = a
    left = (ex + head * 0.55 * math.cos(normal), ey + head * 0.55 * math.sin(normal))
    right = (ex - head * 0.55 * math.cos(normal), ey - head * 0.55 * math.sin(normal))
    draw.polygon([tip, left, right], fill=white)

    # 時計の針
    hand = int(s * 0.045)
    draw.line([(cx, cy), (cx, cy - r * 0.62)], fill=white, width=hand)
    draw.line([(cx, cy), (cx + r * 0.45, cy + r * 0.28)], fill=white, width=hand)
    dot = hand * 0.9
    draw.ellipse([cx - dot, cy - dot, cx + dot, cy + dot], fill=white)

    img = img.resize((SIZE, SIZE), Image.LANCZOS).convert("RGB")
    img.save(OUT, optimize=True)
    print(f"wrote {os.path.abspath(OUT)} ({os.path.getsize(OUT)} bytes)")


if __name__ == "__main__":
    main()
