#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
make_musiccmt_resource.py — 音乐室评论内嵌资源生成（v2：按标题重映射到播放器 No）。

输入: docs/musiccmt/musiccmt.json（乐评支线维护；**键 = 乐评支线的「曲目表行序」**）
      + src/Resources/tracks*.json.gz（播放器内嵌曲目索引 = **UI 显示的权威序**）
输出: src/Resources/musiccmt.json.gz（EmbeddedResource，LogicalName = musiccmt.json.gz；
      键 = **播放器的 No**，并保留 title_ja 供自检做对齐校验）

⚠️ **为什么要重映射**：乐评支线的「曲目表行序」与播放器的「musicNo 序」在尾部曲目
（ED/EX/Staffroll）和霊界版条目穿插处**不是同一个序**（实测 569 条按 No 直查有 85 条标题不一致）。
所以这里用「规范化标题」把每条乐评挂到播放器的 No 上 —— 播放器显示什么 No，评论就挂在什么 No。

规范化 = NFKC + 去全部空白（含全角）+ 去「・」；已知用字差异走 ALIAS 表。
对不上的条目会**列出并退出非 0**（禁止静默错配）；霊界版等播放器里不占独立 No 的条目计数跳过
（其评论随基础曲，播放器侧天然正确）。

⚠️ **乐评数据更新后必须重跑本工具并提交**（gz 是提交的生成物）。
"""
import gzip
import json
import os
import sys
import unicodedata

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))   # bgmplayer/
SRC = os.path.join(BASE, "docs", "musiccmt", "musiccmt.json")
OUT = os.path.join(BASE, "src", "Resources", "musiccmt.json.gz")
RES = os.path.join(BASE, "src", "Resources")
INDEXES = ("tracks.json.gz", "tracks.tf.json.gz", "tracks.nc.json.gz")

EXPECT_GAMES = 29

# 已知用字差异（规范化后仍对不上的，把「乐评侧标题」指到「播放器侧标题」）。
ALIAS = {
    # th135：乐评侧写作「ニッ」、播放器侧是「二ッ」
    "佐渡のニッ岩": "佐渡の二ッ岩",
}


def norm(title):
    """NFKC + 去全部空白（含全角）+ 去「・」。两侧同规则，保证可比。"""
    t = unicodedata.normalize("NFKC", title or "")
    t = "".join(t.split())
    return t.replace("・", "")


def fail(msg):
    print(f"[校验失败] {msg}")
    sys.exit(1)


def load_player_games():
    """合并三份播放器索引（与 TrackIndex 的合并顺序一致：后写覆盖）。"""
    games = {}
    for name in INDEXES:
        p = os.path.join(RES, name)
        for g in json.load(gzip.open(p, "rt", encoding="utf-8"))["games"]:
            games[g["id"]] = g
    return games


def build_no_map(game):
    """播放器侧：规范化标题 → No。同标题对应多个不同 No 视为歧义，报错。"""
    nomap = {}
    for t in game["tracks"]:
        no = str(t["n"])
        nt = norm(t.get("t") or "")
        seen = nomap.setdefault(nt, no)
        if seen != no:
            fail(f"{game['id']} 规范化标题「{nt}」同时对应 No {seen} 与 {no}（歧义，拒绝生成）")
    return nomap


def main():
    with open(SRC, "r", encoding="utf-8") as f:
        src = json.load(f)

    if len(src) != EXPECT_GAMES:
        fail(f"顶层键数 {len(src)} != {EXPECT_GAMES}")

    games = load_player_games()

    out = {}            # gid -> {no: {title_ja, comment_ja, comment_zh}}
    unmatched = []      # 对不上播放器的任何 No（禁止静默错配）
    skipped = []        # 播放器里不占独立 No 的条目（霊界版等，评论随基础曲）
    matched = 0
    pairs = 0
    empty_pairs = 0
    with_nl = 0
    longest = ""

    for gid, game in sorted(src.items()):
        pg = games.get(gid)
        if pg is None:
            unmatched.append(f"{gid}: 播放器索引无此作品")
            continue

        nomap = build_no_map(pg)

        for no, entry in (game.get("tracks") or {}).items():
            title = entry.get("title_ja") or ""
            nt = norm(title)
            nt = ALIAS.get(nt, nt)

            target = nomap.get(nt)
            if target is None:
                # 霊界版等：播放器里曲名随主版，这类条目不占独立 No。
                skipped.append(f"{gid}#{no}「{title[:14]}」")
                continue

            ja = (entry.get("comment_ja") or "").replace("\r\n", "\n").replace("\r", "\n")
            zh = (entry.get("comment_zh") or "").replace("\r\n", "\n").replace("\r", "\n")

            if bool(ja.strip()) != bool(zh.strip()):
                fail(f"{gid}#{no}「{title[:14]}」的 comment_ja / comment_zh 只有一边有值（应成对）")

            player_nos = [str(t["n"]) for t in pg["tracks"]]
            player_title = pg["tracks"][player_nos.index(target)]["t"]

            # 🔑 结构与源 json 同形：{gid: {"tracks": {no: {...}}}} —— C# 的 CommentGame.Tracks 按 "tracks" 键取
            g_out = out.setdefault(gid, {})
            g_out.setdefault("tracks", {})[target] = {
                "title_ja": player_title,          # 存播放器侧标题：自检拿它与 TrackIndex 逐条对
                "comment_ja": ja,
                "comment_zh": zh,
            }
            matched += 1

            if ja.strip():
                pairs += 1
                if "\n" in ja:
                    with_nl += 1
                    if len(ja) > len(longest):
                        longest = ja
            else:
                empty_pairs += 1

    # 自查：每条产物的 title_ja 必须与播放器该 No 的标题一致（规范化后）—— 这是 join 的保证
    for gid, g_out in out.items():
        pg_titles = {str(t["n"]): norm(t.get("t") or "") for t in games[gid]["tracks"]}
        for no, e in g_out["tracks"].items():
            if norm(e["title_ja"]) != pg_titles[no]:
                fail(f"{gid}#{no} 产物标题与播放器不一致（重映射实现有 bug）")

    if unmatched:
        print(f"[校验失败] {len(unmatched)} 条乐评对不上播放器的任何 No（禁止静默错配）：")
        for m in unmatched[:20]:
            print("  ", m)
        if len(unmatched) > 20:
            print(f"   …共 {len(unmatched)} 条")
        sys.exit(1)

    # 🔑 out 的结构在插入时就是最终形：{gid: {"tracks": {no: {...}}}} —— C# 的 CommentGame.Tracks 按 "tracks" 键取。
    payload = json.dumps(out, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    with gzip.open(OUT, "wb", compresslevel=9) as f:
        f.write(payload)

    print(f"musiccmt(v2 重映射): {len(out)} 作品 / 挂上播放器 No {matched} 条 / "
          f"有评论 {pairs} 对 / 空对 {empty_pairs} / 含换行 {with_nl} 条（最长 {len(longest)} 字）")
    if skipped:
        head = "、".join(skipped[:6])
        more = f" …共 {len(skipped)} 条" if len(skipped) > 6 else ""
        print(f"跳过（播放器不占独立 No，评论随基础曲）{len(skipped)} 条：{head}{more}")
    print(f"-> {OUT}（{os.path.getsize(OUT):,} B；源 {len(payload):,} B）")


if __name__ == "__main__":
    main()
