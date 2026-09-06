# -*- coding: utf-8 -*-
"""
把我生成的 tracklist.csv 与 release/tsa/<dir>/bgm/ 下的参考表逐作比对。

参考表有两代格式：
  旧 tracks.csv      name, abs_start, len,       loop_point,     ...
  新 tracklist.csv   name, abs_start, len_bytes, loop_point_abs, name_jp, ...
列名不统一，按候选名查找。

语义对齐（关键）：
  我的 start        <->  参考 abs_start
  我的 total_bytes  <->  参考 len_bytes
  我的 loop_start   <->  参考 loop_point_abs      （= abs_start + intro）
  我的 intro_bytes  <->  参考 loop_point_abs - abs_start
  我的 title        <->  参考 name_jp / title_jp
th06 参考表是样本单位，单列一档。
"""
import csv
import pathlib
import sys
import unicodedata

REL = pathlib.Path(__file__).resolve().parents[3] / "release" / "tsa"
DEP = pathlib.Path(__file__).resolve().parents[1]   # .../bgmplayer/dependence
MINE = DEP / "02_data" / "tracklist.csv"

GAME_DIR = {
    "th06": "kouma", "th07": "youmu", "th08": "eiya", "th09": "th09",
    "th095": "th095", "th10": "th10", "th11": "th11", "th12": "th12",
    "th125": "th125", "th128": "th128", "th13": "th13", "th14": "th14",
    "th143": "th143", "th15": "th15", "th16": "th16", "th165": "th165",
    "th17": "th17", "th18": "th18", "th185": "th185", "th19": "th19",
    "th20": "th20",
}
ORDER = ["th06", "th07", "th08", "th09", "th095", "th10", "th11", "th12",
         "th125", "th128", "th13", "th14", "th143", "th15", "th16", "th165",
         "th17", "th18", "th185", "th19", "th20"]

CAND = {
    "name": ["name", "file"],
    "start": ["abs_start", "start"],
    "total": ["len_bytes", "len"],
    "loop": ["loop_point_abs", "loop_point"],
    "title": ["name_jp", "title_jp"],
    "rate": ["rate"],
    "ch": ["ch"],
    "bits": ["bits"],
}


def pick(header, key):
    for c in CAND[key]:
        if c in header:
            return c
    return None


def norm_title(s):
    """曲名归一：NFKC + 去所有空白，用来消掉全角/半角空格差异"""
    if s is None:
        return ""
    return "".join(unicodedata.normalize("NFKC", s).split())


def load_ref(d):
    """返回 (rows, using_tracks_csv)；row = {file,start,total,intro,title,rate,ch,bits}"""
    for fn in ("tracklist.csv", "tracks.csv"):
        p = REL / d / "bgm" / fn
        if not p.exists():
            continue
        txt = p.read_bytes().decode("utf-8-sig", errors="replace")
        rd = csv.DictReader(txt.splitlines())
        header = [h.strip() for h in (rd.fieldnames or [])]
        cols = {k: pick(header, k) for k in CAND}
        rows = []
        for r in rd:
            g = lambda k: (r.get(cols[k]) or "").strip() if cols[k] else ""
            try:
                start = int(g("start")) if cols["start"] else None
                total = int(g("total")) if cols["total"] else None
                loop = int(g("loop")) if cols["loop"] else None
            except ValueError:
                continue
            rows.append({
                "file": g("name"),
                "start": start,
                "total": total,
                "intro": (loop - start) if (loop is not None and start is not None) else None,
                "title": g("title"),
                "rate": g("rate"), "ch": g("ch"), "bits": g("bits"),
            })
        return rows, fn
    return [], None


def load_ref_th06():
    """kouma 参考表是样本单位"""
    p = REL / "kouma" / "bgm" / "tracklist.csv"
    txt = p.read_bytes().decode("utf-8-sig", errors="replace")
    rows = []
    for r in csv.DictReader(txt.splitlines()):
        try:
            rows.append({
                "file": (r.get("name") or "").strip() + ".wav",
                "title": (r.get("name_jp") or "").strip(),
                "start_sample": int(r["loop_start_sample"]),
                "end_sample": int(r["loop_end_sample"]),
            })
        except (ValueError, KeyError, TypeError):
            continue
    return rows


def load_mine():
    txt = MINE.read_bytes().decode("utf-8-sig", errors="replace")
    out = {}
    for r in csv.DictReader(txt.splitlines()):
        out.setdefault(r["game"], []).append(r)
    return out


def compare_game(game, mine_rows, ref_rows, src):
    """返回 (status_lines, issues)"""
    issues = []
    n_m, n_r = len(mine_rows), len(ref_rows)
    if n_m != n_r:
        issues.append(f"**曲目数不符**：我的 {n_m} / 参考 {n_r}")

    # 按文件名建立索引（参考表可能缺 name 列 → 退化为按序比对）
    ref_by_file = {r["file"]: r for r in ref_rows if r["file"]}
    use_file = bool(ref_by_file) and n_m == n_r

    for i, m in enumerate(mine_rows):
        r = ref_by_file.get(m["file"]) if use_file else (ref_rows[i] if i < n_r else None)
        if r is None:
            issues.append(f"#{i+1} {m['file']} 参考表中无对应条目")
            continue
        tag = f"#{i+1} {m['file']}"

        if m.get("title") and r.get("title"):
            if norm_title(m["title"]) != norm_title(r["title"]):
                issues.append(f"{tag} 曲名不同：我「{m['title']}」/ 参考「{r['title']}」")
        elif r.get("title") and not m.get("title"):
            issues.append(f"{tag} 我缺曲名，参考为「{r['title']}」")

        for label, mk, rk in (("start", "start", "start"),
                              ("total", "total_bytes", "total"),
                              ("intro", "intro_bytes", "intro"),
                              ("rate", "rate", "rate"),
                              ("ch", "ch", "ch"),
                              ("bits", "bits", "bits")):
            mv, rv = (m.get(mk) or "").strip(), (str(r.get(rk)) if r.get(rk) is not None else "")
            if not mv or not rv:
                continue
            try:
                if int(mv) != int(rv):
                    issues.append(f"{tag} {label} 不同：我 {mv} / 参考 {rv}（差 {int(mv)-int(rv):+,}）")
            except ValueError:
                if mv != rv:
                    issues.append(f"{tag} {label} 不同：我 {mv} / 参考 {rv}")
    return issues


def main():
    only = sys.argv[1] if len(sys.argv) > 1 else None
    mine = load_mine()

    print(f"{'作品':<7}{'目录':<8}{'我的':>5}{'参考':>5}{'参考文件':>16}{'问题':>6}")
    print("-" * 56)
    total_issues = 0
    all_detail = {}
    for g in ORDER:
        d = GAME_DIR[g]
        rows = mine.get(g, [])
        if g == "th06":
            ref = load_ref_th06()
            src = "tracklist.csv(样本)"
            # 特殊比对：intro/total 转样本
            issues = []
            if len(rows) != len(ref):
                issues.append(f"**曲目数不符**：我的 {len(rows)} / 参考 {len(ref)}")
            rb = {r["file"]: r for r in ref}
            for i, m in enumerate(rows):
                r = rb.get(m["file"])
                if not r:
                    issues.append(f"#{i+1} {m['file']} 参考表中无对应条目")
                    continue
                tag = f"#{i+1} {m['file']}"
                if norm_title(m.get("title", "")) != norm_title(r["title"]):
                    issues.append(f"{tag} 曲名不同：我「{m.get('title')}」/ 参考「{r['title']}」")
                # 我的 intro_bytes 是字节（我用了 pos*4-44），参考是样本
                my_start_s = int(m["intro_bytes"]) // 4
                my_end_s = int(m["total_bytes"]) // 4
                if my_start_s != r["start_sample"]:
                    issues.append(f"{tag} 循环起点(样本)：我 {my_start_s} / 参考 {r['start_sample']}"
                                  f"（差 {my_start_s - r['start_sample']:+}）")
                if my_end_s != r["end_sample"]:
                    issues.append(f"{tag} 循环终点(样本)：我 {my_end_s} / 参考 {r['end_sample']}"
                                  f"（差 {my_end_s - r['end_sample']:+}）")
        else:
            ref, src = load_ref(d)
            issues = compare_game(g, rows, ref, src)

        all_detail[g] = (d, len(rows), len(ref), src, issues)
        total_issues += len(issues)
        print(f"{g:<7}{d:<8}{len(rows):>5}{len(ref):>5}{src:>16}{len(issues):>6}")

    print("-" * 56)
    print(f"合计问题条目 {total_issues}")

    if only:
        g = only
        if g not in all_detail:
            print(f"未知作品 {g}")
            return
        d, n, nr, src, issues = all_detail[g]
        print(f"\n===== {g}（{d}）明细：我的 {n} / 参考 {nr}，来自 {src} =====")
        if not issues:
            print("  完全一致")
        for x in issues:
            print("  " + x)
        # 附带两侧前几行供比对
        print("\n  --- 我的前 5 行 ---")
        for r in mine.get(g, [])[:5]:
            print(f"    {r['index']:>3} {r['file']:<14} {r['title']:<24} "
                  f"start={r['start']:>10} intro={r['intro_bytes']:>9} total={r['total_bytes']:>10} "
                  f"{r['rate']}/{r['ch']}/{r['bits']}")
        print("  --- 参考前 5 行 ---")
        if g == "th06":
            for r in load_ref_th06()[:5]:
                print(f"    {r['file']:<14} {r['title']:<24} "
                      f"start_sample={r['start_sample']:>9} end_sample={r['end_sample']:>10}")
        else:
            for r in load_ref(d)[0][:5]:
                print(f"    {str(r['file']):<14} {str(r['title']):<24} "
                      f"start={str(r['start']):>10} intro={str(r['intro']):>9} "
                      f"total={str(r['total']):>10} {r['rate']}/{r['ch']}/{r['bits']}")

    # 落盘完整报告
    out = ["# 曲目表逐作比对（我的 vs release/tsa/<作>/bgm 参考表）", ""]
    out.append(f"合计问题条目：**{total_issues}**")
    out.append("")
    out.append("| 作品 | 目录 | 我的 | 参考 | 参考文件 | 问题数 |")
    out.append("|---:|---|---:|---:|---|---:|")
    for g in ORDER:
        d, n, nr, src, issues = all_detail[g]
        out.append(f"| {g} | {d} | {n} | {nr} | {src} | {len(issues)} |")
    out.append("")
    for g in ORDER:
        d, n, nr, src, issues = all_detail[g]
        out.append(f"## {g}（{d}）　我的 {n} / 参考 {nr}　来源 `{src}`")
        out.append("")
        if not issues:
            out.append("完全一致。")
        else:
            for x in issues:
                out.append(f"- {x}")
        out.append("")
    (MINE.parent / "COMPARE.md").write_text("\n".join(out), encoding="utf-8")
    print(f"\n完整报告已写入 {MINE.parent / 'COMPARE.md'}")


if __name__ == "__main__":
    main()
