#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
csv_to_ncjson.py — 新典（東方紅魔郷 New Classic / TH06NC）内嵌曲目索引生成。

输入: re_work/reports/projectKouma/nc_tracks_full.csv（UTF-8 BOM，36 行长格式）
      —— 由新典 BGM 分析流水线产出，目前位于工作区 reports 目录，故此处用绝对路径常量。
      variant ∈ {new, original}；new→bgm/（新编曲主版），original→bgm2/（原编曲 Alt）。
输出: src/Resources/tracks.nc.json.gz（EmbeddedResource，LogicalName = tracks.nc.json.gz）。

结构对齐 tracks.tf.json.gz（v2）：
  games[].tracks[] = { n, t, f, r, c, b, lss, les, ds, [alt:{n,t,f,r,c,b,lss,les,ds}] }
  f 为相对游戏根目录 data/ 的子路径（bgm/… 或 bgm2/…），播放器侧拼成 {游戏根目录}/data/{f}。
  lss/les 直接取 CSV 的 *_48000 列（已换算好并钳制到曲长），不再二次换算。
  ds = records × 960（48kHz / 20ms / 立体声单帧样本数）。
  第 16 首为 ZUN 新曲、两版同一文件（md5 相同）→ 省略 alt 字段。
曲名（title_ja）原样透传，不做任何 normalize / trim / 空白合并。
"""
import csv
import gzip
import json
import os

# 输入 CSV（分析流水线产物，位于工作区 reports 目录）——绝对路径。
SRC_CSV = r"E:\GitWorkspace\thworks\re_work\reports\projectKouma\nc_tracks_full.csv"

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))   # bgmplayer/
OUT = os.path.join(BASE, "src", "Resources", "tracks.nc.json.gz")

FRAME = 960   # 48kHz / 20ms 单帧样本数
RATE = 48000

GAME = {
    "id": "th06nc",
    "code": "TH06NC",
    "name": "東方紅魔郷: New Classic　～ the Embodiment of Scarlet Devil.",
    # 副版切换按钮文案（数据驱动）：主版 = 新编曲「新典」，副版 = 原编曲「原典」。
    # 播放器侧按 GameDef.HasVariantLabels 判断是否显示通用的副版切换按钮，
    # 并据 MainLabel / AltLabel 填按钮文字；缺省这两个键的作品不出按钮（如 TH13 走旧交互）。
    "lm": "新典",
    "la": "原典",
    "source": "ncopus",
    "cont": ["data"],
}


def load_rows():
    """读 36 行长格式曲表（UTF-8 BOM）。"""
    with open(SRC_CSV, "r", encoding="utf-8-sig", newline="") as f:
        return list(csv.DictReader(f))


def track_of(row):
    """单行 → 曲目字段（n/t/f/r/c/b/lss/les/ds）。"""
    return {
        "n": int(row["track_no"]),
        "t": row["title_ja"],                                   # 原样透传
        "f": f"{row['dir']}/{row['file']}",                     # 相对 data/ 的子路径
        "r": RATE,
        "c": 2,
        "b": 16,
        "lss": int(row["loop_start_48000"]),                    # 已换算并钳制，直接用
        "les": int(row["loop_end_48000"]),
        "ds": int(row["records"]) * FRAME,
    }


def main():
    rows = load_rows()
    by_no = {}
    for r in rows:
        by_no.setdefault(int(r["track_no"]), {})[r["variant"]] = r

    tracks = []
    alts = 0
    for no in sorted(by_no):
        pair = by_no[no]
        main_row, orig_row = pair["new"], pair["original"]
        t = track_of(main_row)
        # 两版同文件（第 16 首 ZUN 新曲）→ 省略 alt
        if main_row["md5_8"] != orig_row["md5_8"]:
            alt = track_of(orig_row)
            alt["t"] = t["t"]                    # 同上（曲名随主版）
            t["alt"] = alt
            alts += 1
        tracks.append(t)

    doc = {"version": 2, "games": [dict(GAME, tracks=tracks)]}
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    payload = json.dumps(doc, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    with gzip.open(OUT, "wb", compresslevel=9) as f:
        f.write(payload)
    print(f"TH06NC: {len(tracks)} 首（有 alt {alts} 首）  {GAME['name']}")
    print(f"-> {OUT}（{os.path.getsize(OUT):,} B）")


if __name__ == "__main__":
    main()
