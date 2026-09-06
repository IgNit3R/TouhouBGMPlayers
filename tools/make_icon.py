# 从 assets/icon/bgmplayer-icon-2048.png 生成多尺寸打包的 bgmplayer.ico。
# Windows 各处（资源管理器、任务栏、标题栏、Alt+Tab）按场合取不同尺寸，
# 缺了哪档就会被系统缩放，发虚。256 档由 Pillow 自动写成 PNG 压缩（Vista+）。
#
# 用法：python tools/make_icon.py
# 依赖：Pillow（托管 venv 里已装）

import pathlib
import sys

from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parents[1]      # tools/ 上一级即仓库根
SRC = ROOT / "assets" / "icon" / "bgmplayer-icon-2048.png"
OUT = ROOT / "assets" / "icon" / "bgmplayer.ico"

SIZES = [16, 24, 32, 48, 64, 128, 256]


def main() -> int:
    if not SRC.exists():
        print("找不到源图：%s" % SRC)
        return 1

    img = Image.open(SRC).convert("RGBA")
    img.save(OUT, sizes=[(s, s) for s in SIZES])

    # 回读验证：尺寸档位必须齐全
    chk = Image.open(OUT)
    have = sorted({f"{w}x{h}" for (w, h) in chk.info.get("sizes", set())} | {f"{chk.width}x{chk.height}"})
    print("已生成 %s（%d KB）" % (OUT, OUT.stat().st_size // 1024))
    print("包含尺寸：%s" % ", ".join(have))
    return 0


if __name__ == "__main__":
    sys.exit(main())
