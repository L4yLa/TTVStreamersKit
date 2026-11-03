"""チャット表示用フォントアセットに焼き込む文字の一覧を作る。

対象: CP932（JIS X 0208 と NEC・IBM の拡張。私用領域を除く）と、記号・ギリシャ文字・キリル文字などの
ブロックのうち、フォントに収録されている文字。

使い方:
    python tools/build_charset.py unity/project/Assets/Fonts/BIZUDPGothic-Regular.ttf unity/project/Assets/Fonts/charset.txt

fontTools が必要（pip install fonttools）。
"""

import sys

from fontTools.ttLib import TTFont

BLOCKS = [
    (0x0020, 0x007E),  # ASCII
    (0x00A0, 0x00FF),  # Latin-1 補助
    (0x0100, 0x017F),  # ラテン文字拡張 A
    (0x02B0, 0x02FF),  # 前進を伴う修飾文字
    (0x0370, 0x03FF),  # ギリシャ文字
    (0x0400, 0x04FF),  # キリル文字
    (0x2000, 0x206F),  # 一般句読点
    (0x2100, 0x214F),  # 文字様記号
    (0x2150, 0x218F),  # 数字の形
    (0x2190, 0x21FF),  # 矢印
    (0x2200, 0x22FF),  # 数学記号
    (0x2300, 0x23FF),  # その他の技術用記号
    (0x2460, 0x24FF),  # 囲み英数字
    (0x2500, 0x257F),  # 罫線
    (0x2580, 0x259F),  # ブロック要素
    (0x25A0, 0x25FF),  # 幾何学模様
    (0x2600, 0x26FF),  # その他の記号
    (0x2700, 0x27BF),  # 装飾記号
    (0x3000, 0x303F),  # CJK の記号と句読点
    (0x3040, 0x309F),  # 平仮名
    (0x30A0, 0x30FF),  # 片仮名
    (0x31F0, 0x31FF),  # 片仮名拡張
    (0x3200, 0x32FF),  # 囲み CJK 文字
    (0x3300, 0x33FF),  # CJK 互換用文字
    (0xFF00, 0xFFEF),  # 半角・全角形
]

LINE_LENGTH = 64


def cp932_chars():
    result = set()
    for b1 in list(range(0x81, 0xA0)) + list(range(0xE0, 0xFD)):
        for b2 in list(range(0x40, 0x7F)) + list(range(0x80, 0xFD)):
            try:
                c = bytes([b1, b2]).decode("cp932")
            except UnicodeDecodeError:
                continue
            if len(c) == 1 and not 0xE000 <= ord(c) <= 0xF8FF:
                result.add(ord(c))
    return result


def main(font_path, out_path):
    cmap = TTFont(font_path).getBestCmap()
    wanted = cp932_chars()
    for lo, hi in BLOCKS:
        wanted.update(range(lo, hi + 1))
    chars = [chr(c) for c in sorted(wanted) if c in cmap]
    lines = ["".join(chars[i:i + LINE_LENGTH]) for i in range(0, len(chars), LINE_LENGTH)]
    with open(out_path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    print(f"{len(chars)} characters -> {out_path}")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        print(__doc__)
        sys.exit(1)
    main(sys.argv[1], sys.argv[2])
