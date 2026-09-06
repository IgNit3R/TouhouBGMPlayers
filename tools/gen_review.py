# -*- coding: utf-8 -*-
"""
生成逐作核对表 REVIEW.md + REVIEW_TITLES.csv。

REVIEW.md：按作品分节，每首一行，列：
  序号 / 音乐室 / 曲名 / 整轨 / 循环起点 / 循环段 / 采样率 / 备注
  备注仅在「与 release/tsa/<作>/bgm 参考表不一致」时给出参考值，供人工判定。

REVIEW_TITLES.csv：纯曲名对照（便于批量修订）
  game,index,file,music_no,my_title,ref_title,title_same
"""
import csv
import pathlib
import re
import unicodedata

import compare_tracklist as CT

DEP = pathlib.Path(__file__).resolve().parents[1]   # .../bgmplayer/dependence
OUT_MD = DEP / "_gen" / "REVIEW.md"
OUT_CSV = DEP / "_gen" / "REVIEW_TITLES.csv"

# ---- 已裁定规则：命中即在备注里标注为"已裁定"，不再列为待判 ----
# R1 末轨长度：start+total == thbgm.dat 大小（Δ=0）为准。
#    参考表末轨普遍少 16 字节，成因见 release/tsa/_bgm_report.md §3.5
#    （"后期 +16B"误判，t65 已勘误）。裁定：采信我方。
RULES_DOC = [
    "## 已裁定规则",
    "",
    "以下差异**已经判定、不再逐条讨论**：",
    "",
    "| 编号 | 规则 | 依据 |",
    "|---|---|---|",
    "| **R1** | 末轨整轨长度以使 `start + total == thbgm.dat 文件大小`（Δ=0）为准；"
    "参考表末轨普遍少 16 字节（4 帧 / 0.09 ms） | `release/tsa/_bgm_report.md` §3.5："
    "「th07–th20 末轨 start+len == 文件大小 精确成立，Δ=0（全部）」，并点名"
    "「早期 +16B 误判」已被 t65 勘误 |",
    "| **R2** | 曲名一律以游戏本体 `musiccmt.txt` 的 Shift-JIS 原文为准"
    "（含 ZUN 的拼写与标点） | 需求：取日语原名 |",
    "| **R3** | 以**官方最终确定的曲名**为准：游戏内拼写/标点与**商业 CD 收录版**"
    "不一致时，**以 CD 为准** | 用户 2026-09-04 审定：th08 #8 进 CD 后无句点，"
    "同作 #1 未进 CD 故保留游戏内写法 |",
    "| **R4** | 用户在 `REVIEW_TITLES.csv` 中**未改动即视为认可** | "
    "用户 2026-09-04：「我没动的那列基本上就是我已经认定好的」 |",
    "",
]

# R4：用户未改动即认可。th13 灵界版 13 首无 musiccmt 条目、无 CD 可依，
# 我方取 BgmForAll.ini 的「（霊界トランス）」写法，参考表用「 (霊界バージョン)」。
USER_CONFIRMED = {
    ("th13", n) for n in (3, 5, 7, 9, 11, 13, 15, 17, 19, 21, 23, 28, 30)
}

GAME_JA = {
    "th06": "東方紅魔郷", "th07": "東方妖々夢", "th08": "東方永夜抄",
    "th09": "東方花映塚", "th095": "東方文花帖", "th10": "東方風神録",
    "th11": "東方地霊殿", "th12": "東方星蓮船", "th125": "ダブルスポイラー",
    "th128": "妖精大戦争", "th13": "東方神霊廟", "th14": "東方輝針城",
    "th143": "弾幕アマノジャク", "th15": "東方紺珠伝", "th16": "東方天空璋",
    "th165": "秘封ナイトメア", "th17": "東方鬼形獣", "th18": "東方虹龍洞",
    "th185": "バレットフィリア達の闇市場", "th19": "東方獣王園",
    "th20": "東方錦上京",
}


def norm(s):
    return "".join(unicodedata.normalize("NFKC", s or "").split())


def hms(seconds):
    """秒 -> m:ss.mmm"""
    if seconds is None:
        return ""
    neg = seconds < 0
    s = abs(seconds)
    m = int(s // 60)
    rest = s - m * 60
    return f"{'-' if neg else ''}{m}:{rest:06.3f}"


def frames_of(row, byte_field):
    """字节 -> 帧数"""
    try:
        b = int(row[byte_field])
    except (KeyError, TypeError, ValueError):
        return None
    ch = int(row.get("ch") or 2)
    bits = int(row.get("bits") or 16)
    return b // (ch * bits // 8)


def sec_of(row, byte_field):
    f = frames_of(row, byte_field)
    if f is None:
        return None
    return f / int(row.get("rate") or 44100)


def build_ref_index():
    """{game: {file: ref_row}}"""
    idx = {}
    for g in CT.ORDER:
        if g == "th06":
            rows = CT.load_ref_th06()
            idx[g] = {r["file"]: r for r in rows}
        else:
            d = CT.GAME_DIR[g]
            rows, _ = CT.load_ref(d)
            idx[g] = {r["file"]: r for r in rows if r["file"]}
    return idx


def load_cmt(game):
    """{stem: (no, title)} 游戏本体 musiccmt.txt（Shift-JIS）"""
    d = CT.GAME_DIR.get(game)
    if not d:
        return {}
    p = DEP / "04_source" / "extract" / d / "musiccmt.txt"
    if not p.exists():
        return {}
    txt = p.read_bytes().decode("cp932", errors="replace")
    res, cur = {}, None
    for line in txt.splitlines():
        line = line.strip()
        mt = re.match(r"^@bgm/(.+)$", line)
        if mt:
            cur = mt.group(1).strip()
            continue
        mt = re.match(r"^No\.\s*(\d+)\s+(.+)$", line)
        if mt and cur:
            res[cur.rsplit(".", 1)[0] if "." in cur else cur] = (int(mt.group(1)),
                                                                 mt.group(2).strip())
            cur = None
    return res


def compare_one(game, m, r, i, n_total, cmt=None):
    """比对单首，返回 (notes, ref_title, title_same, all_ruled)

    all_ruled=True 表示**本行所有**差异都命中了已裁定规则，不再计入待判。
    （一行可能同时命中多条，例如末轨既改曲名又命中 R1，此时仍算已裁定）
    """
    notes, unruled = [], 0
    if r is None:
        return ["参考表无此条"], "", "", False

    ref_title = r["title"]
    same = "1" if norm(m.get("title")) == norm(ref_title) else "0"
    if not int(same):
        stem = m["file"].rsplit(".", 1)[0]
        orig = (cmt or {}).get(stem)
        # R3：TITLE_OVERRIDE 人工覆写（含 CD 版优先等理由）
        note = (m.get("title_note") or "").strip()
        is_override = m.get("name_src") == "override"
        # R4：用户未改动即认可
        key = (game, int(m["index"]))
        if is_override:
            notes.append(f"曲名≠参考「{ref_title}」"
                         f"（**R3 已裁定**：{note or '人工覆写'}）")
        elif key in USER_CONFIRMED:
            notes.append(f"曲名≠参考「{ref_title}」"
                         f"（**R4 已认可**：我方 `{m.get('title')}`）")
        elif orig and norm(m.get("title")) == norm(orig[1]):
            # R2：我方即 musiccmt 原文，以游戏本体为准
            notes.append(f"曲名≠参考「{ref_title}」"
                         f"（**R2 已裁定**：我方即 musiccmt 原文）")
        else:
            notes.append(f"曲名≠参考「{ref_title}」")
            unruled += 1

    if game == "th06":
        ms, me = int(m["intro_bytes"]) // 4, int(m["total_bytes"]) // 4
        if ms != r["start_sample"]:
            notes.append(f"循环起点≠参考 {r['start_sample']}（差 {ms - r['start_sample']:+} 样本）")
        if me != r["end_sample"]:
            notes.append(f"循环终点≠参考 {r['end_sample']}（差 {me - r['end_sample']:+} 样本）")
        return notes, ref_title, same, bool(notes) and unruled == 0

    for label, mk, rk in (("整轨长度", "total_bytes", "total"),
                          ("循环起点", "intro_bytes", "intro"),
                          ("起始偏移", "start", "start")):
        mv = (m.get(mk) or "").strip()
        rv = r.get(rk)
        if not mv or rv is None:
            continue
        diff = int(mv) - int(rv)
        if diff == 0:
            continue
        # R1：末轨整轨长度比参考多 16 字节 —— 已裁定采信我方
        if label == "整轨长度" and diff == 16 and i == n_total:
            notes.append("末轨长度比参考多 16B（**R1 已裁定**：采信我方）")
        else:
            notes.append(f"{label}≠参考 {rv}（差 {diff:+,}）")
            unruled += 1
    return notes, ref_title, same, bool(notes) and unruled == 0


def main():
    mine = CT.load_mine()
    refidx = build_ref_index()

    md = ["# tsa 侧全曲目核对表", "",
          "按作品分节，每作内按 **`thbgm.dat` 物理顺序**（即文件内真实排布）排列。",
          "时长格式为 `分:秒.毫秒`。「循环起点」是相对本轨开头的偏移；",
          "循环段 = 循环起点 → 轨尾，播放器播法为「intro 播一遍 + 循环段无限重复」。",
          "",
          "**备注列**仅在数值或曲名与 `release/tsa/<作>/bgm/` 参考表不一致时出现，",
          "括号内为参考表的值，供人工判定。",
          ""]
    md += RULES_DOC

    # 目录
    md.append("## 目录")
    md.append("")
    md.append("| 作品 | 曲目数 | 待判 | 已裁定 |")
    md.append("|---:|---:|---:|---:|")
    per_game = {}
    for g in CT.ORDER:
        rows = mine.get(g, [])
        rmap = refidx.get(g, {})
        cmt = load_cmt(g)
        n_issue = n_ruled = 0
        for i, m in enumerate(rows, 1):
            r = rmap.get(m["file"])
            notes, _, _, all_ruled = compare_one(g, m, r, i, len(rows), cmt)
            if not notes:
                continue
            if all_ruled:
                n_ruled += 1
            else:
                n_issue += 1
        per_game[g] = (n_issue, n_ruled)
        md.append(f"| [{g}　{GAME_JA.get(g,'')}](#{g}) | {len(rows)} | {n_issue} | {n_ruled} |")
    md.append("")

    csvrows = []

    for g in CT.ORDER:
        rows = mine.get(g, [])
        rmap = refidx.get(g, {})
        cmt = load_cmt(g)
        ja = GAME_JA.get(g, "")
        md.append(f"## {g}")
        md.append("")
        md.append(f"**{ja}**　{len(rows)} 首")
        md.append("")
        md.append("| # | 音乐室 | 曲名 | 整轨 | 循环起点 | 循环段 | 采样率 | 备注 |")
        md.append("|---:|---:|---|---:|---:|---:|---:|---|")

        for i, m in enumerate(rows, 1):
            r = rmap.get(m["file"])
            notes, ref_title, same, _ = compare_one(g, m, r, i, len(rows), cmt)

            csvrows.append({
                "game": g, "index": i, "file": m["file"],
                "music_no": m.get("music_no", ""),
                "my_title": m.get("title", ""), "ref_title": ref_title,
                "title_same": same,
            })

            total_s = sec_of(m, "total_bytes")
            intro_s = sec_of(m, "intro_bytes")
            loop_s = sec_of(m, "loop_bytes")
            rate = m.get("rate", "")
            note = "；".join(notes) if notes else ""

            md.append(f"| {i} | {m.get('music_no') or '-'} | {m.get('title','')} "
                      f"| {hms(total_s)} | {hms(intro_s)} | {hms(loop_s)} | {rate} | {note} |")

        md.append("")

    OUT_MD.write_text("\n".join(md), encoding="utf-8")
    print(f"REVIEW.md 已写入（{len(md)} 行）")

    with open(OUT_CSV, "w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=["game", "index", "file", "music_no",
                                          "my_title", "ref_title", "title_same"])
        w.writeheader()
        w.writerows(csvrows)
    n_diff = sum(1 for r in csvrows if r["title_same"] == "0")
    print(f"REVIEW_TITLES.csv 已写入：{len(csvrows)} 首，曲名与参考不一致 {n_diff} 首")


if __name__ == "__main__":
    main()
