"""
封面归一化：把 assets/cover/ 里的原图统一成「长边 512 的 JPEG」，供编进程序集用。

为什么必须归一化（2026-09-21 实测）：
  原图 30 个文件合计 **38 MB**（多为 1000×988 的 PNG，单个 1.3~3.5 MB），
  直接以 Resource 打进程序集会让 exe 从几 MB 涨到 40MB+。
  而可视化面板最大也就 ~300 物理像素 —— 长边 512 已经留了一倍余量。

规则：
  · 长边压到 ≤512，**不放大**（tasofro 那几张本来就是 400~875，保持原样）
  · 一律转 JPEG（q88 + optimize），去掉用不到的 alpha
  · 文件名对齐作 id：`<id>.jpg`；th06nc 的两张碟按「主版=新典 / 副版=原典」改名
      th06nc_NewClassic.jpg → th06nc.jpg
      th06nc_Classic.jpg    → th06nc_alt.jpg

用法：python normalize_covers.py <源目录> <输出目录>
原图**只读**，输出写别处 —— 确认无误再决定要不要替换。
"""
import os
import sys

from PIL import Image

# 长边目标。512 是刻意的：面板最大 ~300 物理像素，留一倍余量。
MAX_EDGE = 512
QUALITY = 88

# 老名字 → 新名字（对齐 <id> / <id>_alt 约定）
RENAME = {
    "th06nc_NewClassic": "th06nc",        # 新典 = 主版
    "th06nc_Classic": "th06nc_alt",       # 原典 = 副版
}


def normalize(src_dir: str, out_dir: str) -> None:
    os.makedirs(out_dir, exist_ok=True)

    files = sorted(f for f in os.listdir(src_dir)
                   if f.lower().endswith((".png", ".jpg", ".jpeg")))

    total_in = total_out = 0
    print(f"{'输出文件':<24}{'原尺寸':>12}{'新尺寸':>12}{'原KB':>9}{'新KB':>8}")
    print("-" * 68)

    for name in files:
        stem = os.path.splitext(name)[0]
        stem = RENAME.get(stem, stem)

        src = os.path.join(src_dir, name)
        dst = os.path.join(out_dir, stem + ".jpg")

        img = Image.open(src)
        w, h = img.size
        before = os.path.getsize(src)

        # 按长边等比缩放；**只缩不放**
        scale = MAX_EDGE / max(w, h)
        if scale < 1:
            img = img.resize((max(1, round(w * scale)), max(1, round(h * scale))),
                             Image.LANCZOS)

        img.convert("RGB").save(dst, "JPEG", quality=QUALITY, optimize=True)

        after = os.path.getsize(dst)
        total_in += before
        total_out += after

        print(f"{stem + '.jpg':<24}{f'{w}x{h}':>12}{f'{img.size[0]}x{img.size[1]}':>12}"
              f"{before // 1024:>9}{after // 1024:>8}")

    print("-" * 68)
    print(f"{len(files)} 个文件：{total_in / 1048576:.1f} MB → **{total_out / 1048576:.2f} MB**"
          f"（降到 {total_out / total_in:.1%}）")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        print(__doc__)
        sys.exit(2)
    normalize(sys.argv[1], sys.argv[2])
