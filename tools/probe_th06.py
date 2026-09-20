# -*- coding: utf-8 -*-
"""
th06(紅魔郷) 循环点提取

th06 不使用 thbgm.dat + thbgm.fmt，而是：
  - wave 文件直接放在 tsa/kouma/bgm/*.wav（原始目录内即明文）
  - 循环点在 紅魔郷MD.DAT 内的 *.pos，格式 <start sample>.4b <end sample>.4b

th06 的 DAT 是 thdat v6 格式，brightmoon 不支持（仅支持 th07+ 的 PBG4 系列），
因此改用 thtk 的 thdat。thdat 无法处理 Unicode 路径，故先复制为 ASCII 名的副本再解包
（源文件只读，不做任何修改）。
"""
import pathlib
import shutil
import struct
import subprocess

BASE = pathlib.Path(r"E:\GitWorkspace\thworks")
THDAT = BASE / "tools" / "thtk" / "thtk-bin-12" / "thdat.exe"
SRC = BASE / "tsa" / "kouma" / "紅魔郷MD.DAT"
# ⚠️ 弃用标记（2026-09-21 用户确认）：目标目录（旧 dependence/04_source/extract/th06）不再随仓库提供
#    ⇒ 本脚本大概率已弃用，保留仅作记录。
DST_DIR = pathlib.Path(__file__).resolve().parents[1] / "docs" / "source" / "extract" / "th06"


def main():
    DST_DIR.mkdir(parents=True, exist_ok=True)
    print("源存在:", SRC.exists(), "|", SRC)

    copy = DST_DIR / "md.dat"
    if not copy.exists():
        shutil.copy2(SRC, copy)          # 只读复制，源文件不动
        print("已复制副本 ->", copy)

    r = subprocess.run([str(THDAT), "-x", "6", str(copy)],
                       cwd=str(DST_DIR), capture_output=True, text=True,
                       encoding="utf-8", errors="replace")
    print("thdat returncode:", r.returncode)
    if r.stdout.strip():
        print("stdout:", r.stdout[:500])
    if r.stderr.strip():
        print("stderr:", r.stderr[:500])

    pos_files = sorted(DST_DIR.glob("*.pos"))
    print("\n.pos 文件数:", len(pos_files))

    print(f"\n{'pos':<12}{'start_sample':>14}{'end_sample':>12}"
          f"{'start_byte':>12}{'end_byte':>11}{'loop_len':>11}")
    print("-" * 74)
    for p in pos_files:
        b = p.read_bytes()
        if len(b) < 8:
            print(f"{p.name:<12}  长度异常 {len(b)}")
            continue
        ss, es = struct.unpack_from("<2I", b, 0)
        # 样本 -> 字节：×4（2ch × 16bit）
        print(f"{p.name:<12}{ss:>14}{es:>12}{ss * 4:>12}{es * 4:>11}{(es - ss) * 4:>11}")

    # 对照: bgm 目录中的 wav
    bgm = sorted((BASE / "tsa" / "kouma" / "bgm").glob("*.wav"))
    print("\nbgm/*.wav 数:", len(bgm))
    for w in bgm[:3]:
        print("  ", w.name, w.stat().st_size, "B")


if __name__ == "__main__":
    main()
