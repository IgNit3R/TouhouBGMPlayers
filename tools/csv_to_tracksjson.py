#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
tracklist.csv  ->  tracks.json.gz（播放器内嵌索引）

用法:
    python csv_to_tracksjson.py            # 默认输出到 src/Resources/
    python csv_to_tracksjson.py --dry-run  # 只打印统计，不写文件

输出结构（键名缩写以压体积）:
{
  "version": 1,
  "games": [ {
      "id": "th13", "name": "東方神霊廟　〜 Ten Desires.",
      "code": "TH13", "source": "zwav", "dir": "th13",
      "tracks": [ {
          "n": 2,                     显示序号（音乐室顺序，无编号者补到末尾）
          "t": "死霊の夜桜",            曲名（日语原名）
          "s": 10812944,              数据起点字节偏移（zwav: 相对 dat 开头; wav: 相对文件开头）
          "i": 1522004,               intro 字节数（= 循环起点 - 起点）
          "l": 9586104,               total 字节数（= 循环终点 - 起点）
          "r": 44100, "c": 2, "b": 16,
          "f": "th06_01.wav",         仅 source=wav 时有：音频文件名
          "alt": { ... }              灵界版（仅 th13 部分曲），字段同 s/i/l/r/c/b
      } ]
  } ]
}

规则要点（详见 DESIGN_v3.md §4.2）:
  1. 灵界版判定 = 作品 th13 且 file 以 'b.wav' 结尾 且 去尾 b 后存在同名主版。
     不能简单用 "文件名带 b" 一刀切 —— TH07/TH08/TH09 有 4 首正式曲也以 b.wav 结尾。
  2. 灵界版不单独成行，挂在主版的 alt 字段。
  3. 显示序号按 music_no 升序；music_no 为空的按物理顺序补到该作末尾。
"""
import csv
import gzip
import io
import json
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]         # tools/ 上一级即 .../bgmplayer 仓库根
CSV = ROOT / "docs" / "tracklist.csv"
OUT = ROOT / "src" / "Resources" / "tracks.json.gz"

# 作品顺序与代号（小数作带小数点）
GAME_ORDER = [
    "th06", "th07", "th08", "th09", "th095", "th10", "th11", "th12", "th125",
    "th128", "th13", "th14", "th143", "th15", "th16", "th165", "th17", "th18",
    "th185", "th19", "th20",
]
CODE = {
    "th06": "TH06", "th07": "TH07", "th08": "TH08", "th09": "TH09",
    "th095": "TH09.5", "th10": "TH10", "th11": "TH11", "th12": "TH12",
    "th125": "TH12.5", "th128": "TH12.8", "th13": "TH13", "th14": "TH14",
    "th143": "TH14.3", "th15": "TH15", "th16": "TH16", "th165": "TH16.5",
    "th17": "TH17", "th18": "TH18", "th185": "TH18.5", "th19": "TH19",
    "th20": "TH20",
}
# 目录名提示（仅设置界面的人肉参考，运行时不参与路径推断）
DIR_HINT = {"th06": "kouma", "th07": "youmu", "th08": "eiya"}

# 作品名（完整标题，日文原名）
#
# 来源：各游戏安装目录下自带的「おまけ.txt / omake.txt」首行 —— 随游戏发行的
# 官方文本，写法由 ZUN 定，不存在社区整理常见的错字。
# 提取脚本见同目录 extract_game_names.py，可重新生成核对。
#
# 注意分隔符：官方文本用的是 **U+301C WAVE DASH（〜）**，不是更常见的
# 全角波浪号 U+FF5E（～）。两者长得几乎一样但码点不同，
# 按 U+FF5E 切会切不开（表现为主标题没变短）。
# C# 侧两个码点都认，见 TrackIndex.cs 的 ShortName。
NAME = {
    "th06": "東方紅魔郷　〜 the Embodiment of Scarlet Devil.",
    "th07": "東方妖々夢　〜 Perfect Cherry Blossom.",
    "th08": "東方永夜抄　〜 Imperishable Night",
    "th09": "東方花映塚　〜 Phantasmagoria of Flower View.",
    "th095": "東方文花帖　〜 Shoot the Bullet.",
    "th10": "東方風神録　〜 Mountain of Faith.",
    "th11": "東方地霊殿　〜 Subterranean Animism.",
    "th12": "東方星蓮船　〜 Undefined Fantastic Object",
    "th125": "ダブルスポイラー　〜 東方文花帖",
    "th128": "妖精大戦争　〜 東方三月精",
    "th13": "東方神霊廟　〜 Ten Desires.",
    "th14": "東方輝針城　〜 Double Dealing Character",
    "th143": "弾幕アマノジャク　〜 Impossible Spell Card.",
    "th15": "東方紺珠伝　〜 Legacy of Lunatic Kingdom.",
    "th16": "東方天空璋　〜 Hidden Star in Four Seasons.",
    "th165": "秘封ナイトメアダイアリー　〜 Violet Detector.",
    "th17": "東方鬼形獣　〜 Wily Beast and Weakest Creature.",
    "th18": "東方虹龍洞　〜 Unconnected Marketeers.",
    "th185": "バレットフィリア達の闇市場　〜 100th Black Market.",
    "th19": "東方獣王園　〜 Unfinished Dream of All Living Ghost.",
    "th20": "東方錦上京　〜 Fossilized Wonders.",
}


def num(v):
    """csv 里的数值字段，空串视为 0"""
    v = (v or "").strip()
    return int(float(v)) if v else 0


def is_spirit(r):
    """灵界版：th13 专有的 22050Hz 变体，file 去尾 b 后有同名主版"""
    return r["game"] == "th13" and r["file"].endswith("b.wav")


def track_of(r, with_file):
    d = {
        "s": num(r["start"]),
        "i": num(r["intro_bytes"]),
        "l": num(r["total_bytes"]),
        "r": num(r["rate"]),
        "c": num(r["ch"]),
        "b": num(r["bits"]),
    }
    if with_file:
        d["f"] = r["file"]
    return d


def main():
    dry = "--dry-run" in sys.argv
    rows = list(csv.DictReader(io.open(CSV, encoding="utf-8-sig")))

    unknown = sorted({r["game"] for r in rows} - set(GAME_ORDER))
    if unknown:
        raise SystemExit("csv 中出现未知作品: %s" % unknown)

    games = []
    stat_rows = []
    for gid in GAME_ORDER:
        gr = [r for r in rows if r["game"] == gid]
        if not gr:
            print("!! 警告: %s 在 csv 中没有任何记录" % gid)
            continue

        # 1) 拆出灵界版，并建立 主版file -> 灵界版记录 的配对表
        mains, spirits = [], {}
        for r in gr:
            if is_spirit(r):
                spirits[r["file"][:-5] + ".wav"] = r
            else:
                mains.append(r)

        paired = 0
        orphans = [f for f in spirits if not any(r["file"] == f for r in mains)]
        if orphans:
            print("!! 警告: %s 有 %d 条灵界版找不到主版: %s" % (gid, len(orphans), orphans))

        # 2) 显示序号：music_no 升序；无编号者按物理顺序补到末尾
        numbered = [r for r in mains if num(r["music_no"])]
        blank = [r for r in mains if not num(r["music_no"])]
        numbered.sort(key=lambda r: num(r["music_no"]))
        maxno = max((num(r["music_no"]) for r in numbered), default=0)
        for k, r in enumerate(blank, 1):
            r["_fillno"] = maxno + k
        ordered = numbered + blank

        # 3) 组装
        with_file = (gid == "th06")          # 只有 th06 靠文件名定位音频
        tracks = []
        for r in ordered:
            d = track_of(r, with_file)
            d["n"] = num(r["music_no"]) or r["_fillno"]
            d["t"] = r["title"]
            alt = spirits.get(r["file"])
            if alt is not None:
                d["alt"] = track_of(alt, False)
                paired += 1
            tracks.append({k: d[k] for k in
                           (["n", "t", "f"] if with_file else ["n", "t"]) +
                           ["s", "i", "l", "r", "c", "b", "alt"] if k in d})

        games.append({
            "id": gid,
            "name": NAME[gid],
            "code": CODE[gid],
            "source": "wav" if gid == "th06" else "zwav",
            "dir": DIR_HINT.get(gid, gid),
            "tracks": tracks,
        })
        nalt = sum(1 for t in tracks if "alt" in t)
        stat_rows.append((CODE[gid], len(mains), nalt,
                          len([r for r in numbered if not num(r["music_no"])]),
                          len(blank)))

    doc = {"version": 1, "games": games}

    # ---- 统计 ----
    print("%-8s %6s %6s %6s %6s" % ("作品", "曲目", "灵界版", "无编号", "补号"))
    for code, n, nalt, _, nb in stat_rows:
        print("%-8s %6d %6d %6s %6d" % (code, n, nalt, "-", nb))
    total = sum(r[1] for r in stat_rows)
    totalalt = sum(r[2] for r in stat_rows)
    print("-" * 40)
    print("%-8s %6d %6d" % ("合计", total, totalalt))

    raw = json.dumps(doc, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    gz = gzip.compress(raw, 9)
    print()
    print("原始 JSON : %6d B" % len(raw))
    print("gzip 之后 : %6d B  (%.1f%%)" % (len(gz), len(gz) * 100.0 / len(raw)))

    if dry:
        print("\n[--dry-run] 未写文件")
        return

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_bytes(gz)
    print("\n已写出 -> %s" % OUT)


if __name__ == "__main__":
    main()
