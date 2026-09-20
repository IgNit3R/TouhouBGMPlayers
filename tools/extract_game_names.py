# -*- coding: utf-8 -*-
"""
从各游戏目录里自带的「おまけ.txt / omake.txt」提取正式作品名。

这是最权威的来源 —— 文件本身随游戏发行，标题写法是 ZUN 定的，
不存在社区整理常见的错字（東方妖妖夢 / 東方三月精 之类）。

用法：
    python tools/extract_game_names.py
"""
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parents[2]   # tools/x.py -> bgmplayer -> thworks 根（tsa/ 在这里）
TSA = ROOT / "tsa"
STEAM = pathlib.Path("F:/SteamLibrary/steamapps/common")

# 内部代号 -> (tsa 目录名, Steam 目录名)
WORKS = [
    ("th06",  "kouma", "kouma"),
    ("th07",  "youmu", "youmu"),
    ("th08",  "eiya",  "eiya"),
    ("th09",  "th09",  "th09"),
    ("th095", "th095", "th95"),
    ("th10",  "th10",  "th10"),
    ("th11",  "th11",  "th11"),
    ("th12",  "th12",  "th12"),
    ("th125", "th125", "th125"),
    ("th128", "th128", "th128"),
    ("th13",  "th13",  "th13"),
    ("th14",  "th14",  "th14"),
    ("th143", "th143", "th143"),
    ("th15",  "th15",  "th15"),
    ("th16",  "th16",  "th16"),
    ("th165", "th165", "th165"),
    ("th17",  "th17",  "th17"),
    ("th18",  "th18",  "th18"),
    ("th185", "th185", "th185"),
    ("th19",  "th19",  "th19"),
    ("th20",  "th20",  "th20"),
    # 東方紅魔郷 新典（New Classic）：Steam appid 4659620，安装目录 th06nc；
    # 注意 th06c/th06nc 两边的 omake.txt 完全相同，并列写了 Classic 与 New Classic 两条标题，
    # 需按安装变体选行（见下面 pick_title）。
    ("th06nc", "th06nc", "th06nc"),
]

# 优先挑日文版：文件名里带这些标记的说明是汉化/英文化的，跳过
NON_JP = ("汉化", "cn", "zh", "chs", "cht", "eng", "en")


def pick_omake(d: pathlib.Path):
    """在目录里挑一个日文版的 omake/おまけ 文本。"""
    if not d.is_dir():
        return None
    cands = [f for f in d.iterdir()
             if f.is_file()
             and re.search(r"omake|おまけ", f.name, re.I)
             and f.suffix.lower() in (".txt", "")
             and ".old" not in f.name.lower()]

    jp = [f for f in cands if not any(k in f.name.lower() for k in NON_JP)]
    pool = jp or cands
    if not pool:
        return None
    # 同名下 utf8 版本更好读，其次原版
    pool.sort(key=lambda f: ("utf8" not in f.name.lower(), len(f.name)))
    return pool[0]


def read_text(f: pathlib.Path):
    raw = f.read_bytes()
    for enc in ("utf-8-sig", "utf-8", "shift_jis", "cp932", "gbk"):
        try:
            return raw.decode(enc), enc
        except Exception:
            continue
    return None, None


def main():
    print("%-7s %-46s %s" % ("代号", "作品名（おまけ 首行）", "来源"))
    print("-" * 96)

    out = {}
    for gid, tsa_dir, steam_dir in WORKS:
        src = None
        # Steam 安装目录是原封不动的游戏文件，优先用它；
        # 工作区 tsa/ 是解包产物，部分文本被转成了 GBK（th09/th10/th12 等），能用但对不上原始字节
        for base in (STEAM / steam_dir, TSA / tsa_dir):
            f = pick_omake(base)
            if f is None:
                continue
            text, enc = read_text(f)
            if text is None:
                continue
            # 标题一般在前几行：取第一个看起来像标题的非空行
            want_nc = gid.endswith("nc")      # 新典：omake 同时列 Classic/New Classic，按变体选行
            title = None
            fallback = None
            for line in text.splitlines()[:12]:
                s = line.strip().strip("　 ")
                if not s or s.startswith("#"):
                    continue
                # 跳过明显的说明性文字
                if re.match(r"^[-=─*・\s]+$", s):
                    continue
                is_nc = "New Classic" in s
                if want_nc == is_nc:
                    title = s
                    break
                if fallback is None:
                    fallback = s
            title = title or fallback
            if title and len(title) <= 80:
                src = f"{f.name} [{enc}]  ← {base.name}"
                out[gid] = title
                break

        print("  %-6s %-46s %s" % (gid, out.get(gid, "（没找到）"), src or ""))

    print()
    print("=== 可直接贴进转换器的表 ===")
    for gid, _t, _s in WORKS:
        if gid in out:
            print('    "%s": "%s",' % (gid, out[gid]))


if __name__ == "__main__":
    main()
