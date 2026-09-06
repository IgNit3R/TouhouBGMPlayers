# -*- coding: utf-8 -*-
"""
按 release/tsa/_bgm_report.md（W3 规范）逐项校验 bgmplayer 的曲目表。
只读，输出报告到 stdout。
"""
import struct, pathlib, sys

ROOT = pathlib.Path(r"E:\GitWorkspace\thworks")
TSA = ROOT / "tsa"
EXT = pathlib.Path(__file__).resolve().parents[1] / "04_source" / "extract"

# 报告 §3.2 条目数矩阵 + §3.3 zwavid
EXPECT = {
    "th07":  (20, "youmu", 0x00, 0x07), "th08":  (21, "eiya",  0x00, 0x08),
    "th09":  (19, "th09",  0x00, 0x09), "th095": (6,  "th095", 0x50, 0x09),
    "th10":  (18, "th10",  0x00, 0x10), "th11":  (18, "th11",  0x00, 0x11),
    "th12":  (18, "th12",  0x00, 0x12), "th125": (7,  "th125", 0x50, 0x12),
    "th128": (10, "th128", 0x80, 0x12), "th13":  (31, "th13",  0x00, 0x13),
    "th14":  (18, "th14",  0x00, 0x14), "th143": (10, "th143", 0x30, 0x14),
    "th15":  (18, "th15",  0x00, 0x15), "th16":  (18, "th16",  0x00, 0x16),
    "th165": (8,  "th165", 0x50, 0x16), "th17":  (18, "th17",  0x00, 0x17),
    "th18":  (18, "th18",  0x00, 0x18), "th185": (10, "th185", 0x50, 0x18),
    "th19":  (24, "th19",  0x00, 0x19), "th20":  (19, "th20",  0x00, 0x20),
}


def zwav_check(dat_path, exp8, exp9):
    with open(dat_path, "rb") as f:
        h = f.read(16)
    if h[:4] != b"ZWAV":
        return "魔数非 ZWAV"
    ver = struct.unpack("<I", h[4:8])[0]
    if ver != 1:
        return f"version={ver}"
    if h[8] != exp8 or h[9] != exp9:
        return f"zwavid=({h[8]:02X},{h[9]:02X}) 期望({exp8:02X},{exp9:02X})"
    if any(h[10:16]):
        return "0x0A-0x0F 非全零"
    return "OK"


def main():
    print(f"{'作品':<7}{'条目':>5}{'期望':>5}{'fmt零尾':>9}{'ZWAV头':>9}"
          f"{'首轨':>7}{'末轨Δ':>9}{'22050':>7}  备注")
    print("-" * 78)
    allok = True
    for g, (n_exp, tdir, z8, z9) in EXPECT.items():
        fp = EXT / tdir / "thbgm.fmt"
        dp = TSA / tdir / "thbgm.dat"
        if not fp.exists() or not dp.exists():
            print(f"{g:<7}{'缺文件':>5}"); allok = False; continue
        fb = fp.read_bytes()
        n = (len(fb) - 17) // 52
        tail_ok = (len(fb) - 17) % 52 == 0 and all(b == 0 for b in fb[-17:])
        with open(dp, "rb") as f:
            f.seek(0, 2)
            fsize = f.tell()
        zw = zwav_check(dp, z8, z9)

        rows = []
        for i in range(n):
            e = fb[i * 52:(i + 1) * 52]
            st, x, intro, tot = struct.unpack("<4I", e[16:32])
            rate = struct.unpack("<I", e[36:40])[0]
            rows.append((st, intro, tot, rate))
        first_ok = rows[0][0] == 0x10
        delta = rows[-1][0] + rows[-1][2] - fsize
        n22050 = sum(1 for r in rows if r[3] == 22050)
        exp22050 = 13 if g == "th13" else 0
        align = all((r[0] % 4 == 0 and r[1] % 4 == 0 and r[2] % 4 == 0)
                    for r in rows)

        notes = []
        if n != n_exp:
            notes.append(f"条目数不符(期望{n_exp})")
        if not first_ok:
            notes.append(f"首轨start=0x{rows[0][0]:X}")
        if delta != 0:
            notes.append(f"末轨Δ={delta}")
        if n22050 != exp22050:
            notes.append(f"22050Hz轨数{n22050}≠{exp22050}")
        if not align:
            notes.append("非4字节对齐")
        if notes:
            allok = False
        print(f"{g:<7}{n:>5}{n_exp:>5}{'OK' if tail_ok else 'FAIL':>9}"
              f"{zw:>9}{'OK' if first_ok else 'FAIL':>7}{delta:>9}"
              f"{n22050:>7}  {'; '.join(notes) or 'OK'}")
    print("-" * 78)
    print("全部通过" if allok else "存在不符，见备注")


if __name__ == "__main__":
    main()
