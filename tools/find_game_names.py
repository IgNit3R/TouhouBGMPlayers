# -*- coding: utf-8 -*-
"""
在工作区里找「作品名清单」：哪个文本文件同时出现多个东方作品名，
就很可能是我们要找的名单。

用法：
    python tools/find_game_names.py
"""
import pathlib

# __file__ = thworks/bgmplayer/tools/find_game_names.py
# parents: 0=tools 1=bgmplayer 2=thworks
ROOT = pathlib.Path(__file__).resolve().parents[2]

# 只扫这些目录，release/ 和 pushfiles/ 太大且是解包产物
TARGETS = ["bgmplayer", "re_work", "tsa", "tools", "_scan_results", "title", "others"]

NAMES = [
    "東方紅魔郷", "東方妖々夢", "東方永夜抄", "東方花映塚", "東方文花帖",
    "東方風神録", "東方地霊殿", "東方星蓮船", "東方神霊廟", "東方輝針城",
    "東方紺珠伝", "東方天空璋", "東方鬼形獣", "東方虹龍洞", "東方獣王園",
    "東方錦上京", "ダブルスポイラー", "妖精大戦争", "弾幕アマノジャク",
    "秘封ナイトメアダイアリー", "バレットフィリア達の闇市場",
]

SKIP_EXT = {".png", ".jpg", ".jpeg", ".gif", ".bmp", ".exe", ".dll", ".zip",
            ".wav", ".ogg", ".mp3", ".pat", ".anm", ".ecl", ".msg", ".dat",
            ".cv2", ".tga", ".fon", ".ttf", ".mid", ".sfl", ".mt"}

encodings = ("utf-8-sig", "utf-8", "shift_jis", "gbk")

results = []
scanned = 0

for base in TARGETS:
    d = ROOT / base
    if not d.is_dir():
        continue
    for f in d.rglob("*"):
        if not f.is_file():
            continue
        if f.suffix.lower() in SKIP_EXT:
            continue
        try:
            if f.stat().st_size > 3_000_000:
                continue
            raw = f.read_bytes()
        except Exception:
            continue

        scanned += 1
        text = None
        for enc in encodings:
            try:
                text = raw.decode(enc)
                break
            except Exception:
                continue
        if text is None:
            continue

        found = [n for n in NAMES if n in text]
        if len(found) >= 3:
            results.append((len(found), str(f.relative_to(ROOT)), found))

results.sort(reverse=True)

print("扫描文本文件 %d 个" % scanned)
print()
if not results:
    print("没有找到同时含 3 个以上作品名的文件 —— 工作区里没有现成的名单")
else:
    print("命中（按作品名数量排序）：")
    for cnt, path, found in results[:20]:
        print()
        print("  [%d] %s" % (cnt, path))
        print("      %s" % " ".join(found))
