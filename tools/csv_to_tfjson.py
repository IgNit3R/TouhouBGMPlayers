#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
csv_to_tfjson.py — 黄昏作（tasofro）内嵌曲目索引生成。

输入: tf_prep/<game>_tracklist_final.csv ×7（th135/th145 读 export/ 下的纯文本导出，
      因为原件被本地编辑通道存成了 xlsx 容器）。
输出: src/Resources/tracks.tf.json.gz（EmbeddedResource，LogicalName = tracks.tf.json.gz）。

循环点以「整数样本」写入（lss/les），优先取游戏自带的权威源（见 tools/loop_delta.py）：
  th075                  : WAV cue + LIST/adtl/ltxt
  th105/123/135/145/155  : data/bgm/*.sfl（pushfiles 下为同名目录 + .sfl.txt）
  th175                  : *.ogg.ini 的 [loop0] repeatstart / repeatend
取不到时才回落到 CSV 的秒值 ×44100 取整。
整曲样本数（ds）优先取资源真实长度（OGG 末页 granule / WAV data 块），同样回落到 CSV 秒值。
"""
import csv
import gzip
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import loop_delta as AUTH  # 权威循环点解析 + 资源定位

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))   # bgmplayer/
PREP = os.path.join(BASE, "docs")
OUT = os.path.join(BASE, "src", "Resources", "tracks.tf.json.gz")    # csproj 相对 src/ 取资源
RATE = 44100

# 编号序（用户确认）。containers = 游戏根目录下的相对路径，按覆盖优先序（后面的包覆盖前面的同名条目）。
GAMES = [
    ("th075", "TH07.5", "東方萃夢想　～ Immaterial and Missing Power.",
     "tfsuica", ["th075bgm.dat"]),
    ("th105", "TH10.5", "東方緋想天　～ Scarlet Weather Rhapsody.",
     "tfogg", ["th105b.dat"]),
    ("th123", "TH12.3", "東方非想天則　～ 超弩級ギニョルの謎を追え",
     "tfogg", ["th123b.dat"]),
    ("th135", "TH13.5", "東方心綺楼　～ Hopeless Masquerade.",
     "tfogg", ["th135.pak", "th135b.pak"]),
    ("th145", "TH14.5", "東方深秘録　～ Urban Legend in Limbo.",
     "tfogg", ["th145.pak"]),
    ("th155", "TH15.5", "東方憑依華　～ Antinomy of Common Flowers.",
     "tfogg", ["th155.pak", "th155b.pak"]),
    ("th175", "TH17.5", "東方剛欲異聞　～ 水没した沈愁地獄",
     "tfogg", ["data.cga", "data.cgb"]),
]


def load_rows(game):
    """读定稿曲表。th135/th145 的原件被本地编辑通道存成了 xlsx 容器（PK 头），用 openpyxl 直接读。"""
    path = os.path.join(PREP, f"{game}_tracklist_final.csv")
    raw = open(path, "rb").read()
    if raw[:2] == b"PK":                   # xlsx 容器（th135/th145）
        import io
        import openpyxl
        wb = openpyxl.load_workbook(io.BytesIO(raw), read_only=True, data_only=True)
        ws = wb.active
        data = []
        for r in ws.iter_rows(values_only=True):
            if r is None or all(v is None for v in r):
                continue
            data.append(["" if v is None else str(v) for v in r])
        wb.close()
        header, body = data[0], data[1:]
        return [dict(zip(header, row)) for row in body]
    import io as _io
    return list(csv.DictReader(_io.StringIO(raw.decode("utf-8-sig"))))


def sec(v):
    v = (v or "").strip()
    return float(v) if v else None


def track_of(row, gid, auth, assets):
    raw_loop = (row.get("loop") or "").strip()
    loop = raw_loop.upper() == "Y" or raw_loop == "循环"
    stem = (row["file"] or "").rsplit(".", 1)[0]

    t = {
        "n": int(row["order"]),
        "t": (row.get("title") or "").strip(),
        "f": (row.get("file") or "").strip(),
        "r": 44100,
        "c": 2,
        "b": 16,
        "comp": (row.get("composer") or "").strip() or None,
        "theme": (row.get("theme") or "").strip() or None,
    }

    # 循环点：优先游戏自带的整数样本，否则按 CSV 秒值换算
    if loop:
        a = auth.get(stem)
        ls_s = le_s = None
        if a:
            ls_s, le_s = a[0], a[1]
        else:
            v = sec(row.get("loop_start_sec"))
            if v is not None:
                ls_s = round(v * RATE)
            v = sec(row.get("loop_end_sec"))
            if v is not None:
                le_s = round(v * RATE)
        if ls_s is not None:
            t["lss"] = ls_s
            if le_s is not None:
                t["les"] = le_s

    # 整曲样本数：优先资源真实长度
    total = AUTH.asset_total_samples(assets.get(row["file"] or ""))
    if total is None:
        v = sec(row.get("duration_sec"))
        if v is not None:
            total = round(v * RATE)
    if total:
        t["ds"] = total

    return {k: v for k, v in t.items() if v is not None}


def main():
    games = []
    total = 0
    for gid, code, name, source, containers in GAMES:
        rows = load_rows(gid)
        auth = AUTH.collect(gid, *reversed(AUTH.SOURCES[gid])) if gid in AUTH.SOURCES else {}
        assets = AUTH.build_asset_index(gid)
        tracks = [track_of(r, gid, auth, assets) for r in rows]
        loops = sum(1 for t in tracks if "lss" in t)
        have_ds = sum(1 for t in tracks if "ds" in t)
        games.append({
            "id": gid, "code": code, "name": name, "source": source,
            "cont": containers, "tracks": tracks,
        })
        total += len(tracks)
        print(f"{gid}: {len(tracks):3d} 首（循环 {loops:3d}，有整曲长度 {have_ds:3d}）  {name}")

    doc = {"version": 2, "games": games}   # v2：循环点改为整数样本
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    payload = json.dumps(doc, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    with gzip.open(OUT, "wb", compresslevel=9) as f:
        f.write(payload)
    print(f"\n合计 {total} 首 -> {OUT}（{os.path.getsize(OUT):,} B）")


if __name__ == "__main__":
    main()
