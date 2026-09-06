# -*- coding: utf-8 -*-
"""
生成 tsa 侧全曲目表（权威版 v2）

数据源（全部来自原始文件，不依赖任何既有拆包产物）：
  1. tsa/<dir>/thXX.dat      -> brightmoon 解出 thbgm.fmt / musiccmt.txt（落 dependence/04_source/extract/）
  2. tsa/<dir>/thbgm.dat     -> 音频本体，仅用于校验末轨不越界
  3. tools/BGMforALL/BgmForAll.ini -> 补曲名（灵界版、玩家分数曲、早期作品）
  4. tsa/kouma/bgm/*.wav + 紅魔郷MD.DAT/*.pos -> th06 特例

排序：fmt 物理顺序（= thbgm.dat 内真实布局 = 播放器 seek 顺序）
"""
import re, csv, struct, pathlib, wave, unicodedata

DEP = pathlib.Path(__file__).resolve().parents[1]   # .../bgmplayer/dependence
ROOT = DEP.parents[1]                              # .../thworks（原始数据 tsa/，红线只读）
EXT  = DEP / "04_source" / "extract"               # thbgm.fmt / musiccmt.txt / th06
TSA  = ROOT / "tsa"
INI  = DEP / "04_source" / "BgmForAll.ini"
OUT  = DEP / "_gen"                                # 重生成输出，不覆盖 02_data 定稿

# 作品代号 -> (tsa 目录, 日文标题, 西文标题, 备注)
GAMES = [
    ("th06",  "kouma",  "東方紅魔郷", "The Embodiment of Scarlet Devil", "MIDI 时代，BGM 为独立 wav，循环点在 .pos"),
    ("th07",  "youmu",  "東方妖妖夢", "Perfect Cherry Blossom", ""),
    ("th08",  "eiya",   "東方永夜抄", "Imperishable Night", ""),
    ("th09",  "th09",   "東方花映塚", "Phantasmagoria of Flower View", ""),
    ("th095", "th095",  "東方文花帖", "Shoot the Bullet", ""),
    ("th10",  "th10",   "東方風神録", "Mountain of Faith", ""),
    ("th11",  "th11",   "東方地霊殿", "Subterranean Animism", ""),
    ("th12",  "th12",   "東方星蓮船", "Undefined Fantastic Object", ""),
    ("th125", "th125",  "ダブルスポイラー", "Double Spoiler", ""),
    ("th128", "th128",  "東方三月精", "Fairy Wars", ""),
    ("th13",  "th13",   "東方神霊廟", "Ten Desires", "含 13 首霊界トランス版"),
    ("th14",  "th14",   "東方輝針城", "Double Dealing Character", ""),
    ("th143", "th143",  "弾幕アマノジャク", "Impossible Spell Card", ""),
    ("th15",  "th15",   "東方紺珠伝", "Legacy of Lunatic Kingdom", ""),
    ("th16",  "th16",   "東方天空璋", "Hidden Star in Four Seasons", ""),
    ("th165", "th165",  "秘封ナイトメアダイアリー", "Violet Detector", ""),
    ("th17",  "th17",   "東方鬼形獣", "Wily Beast and Weakest Creature", ""),
    ("th18",  "th18",   "東方虹龍洞", "Unconnected Marketeers", ""),
    ("th185", "th185",  "バレットフィリア達の闇市場", "100th Black Market", ""),
    ("th19",  "th19",   "東方獣王園", "Unfinished Dream of All Living Ghost", ""),
    ("th20",  "th20",   "東方錦上京", "Fossilized Wonders", ""),
]

# 人工覆写曲名（用户 2026-09-04 审定）
# 判据：以官方最终确定的曲名为准。游戏内 musiccmt.txt 的拼写/标点若与
# 商业 CD 收录版不一致，以 CD 为准；musiccmt 无条目的按参考表或用户给定。
TITLE_OVERRIDE = {
    # 该曲收录进商业 CD 时无句点（同作 #1「Eastern Night.」游戏内带句点，保留）
    ("th08", "th08_08.wav"): ("永夜の報い　～ Imperishable Night",
                              "CD 收录版无句点；游戏内 musiccmt 有句点"),
    # musiccmt 无条目，参考表补
    ("th125", "th125_07.wav"): ("はたてアンロック", "musiccmt 无条目，取参考表"),
    # musiccmt 原文 "Faily Wars" 系 ZUN 笔误，CD 作 Fairy Wars
    ("th128", "th128_06.wav"): ("妖精大戦争　～ Fairy Wars",
                                "musiccmt 原文 Faily Wars 为 ZUN 笔误，取 CD 版"),
    # fmt 文件名是 ZUN 的占位命名，用户给定中文场景名
    ("th19", "th19_90.wav"): ("戦闘前会話1",
                              "fmt 原名 Undefined_Object1，用户给定"),
    ("th19", "th19_91.wav"): ("戦闘前会話2",
                              "fmt 原名 Undefined_Object2，用户给定"),
}

# ini 专辑名前缀 -> 作品代号（长 key 优先）
INI_ALBUM = {
    "東方紅魔郷": "th06", "東方妖妖夢": "th07", "東方萃夢想": "th075",
    "東方永夜抄": "th08", "東方花映塚": "th09", "東方文花貼": "th095",
    "東方風神录": "th10", "東方緋想天": "th105", "東方地霊殿": "th11",
    "東方星蓮船": "th12", "東方緋想天則": "th123", "ダブルスポイラー": "th125",
    "東方三月精": "th128", "東方神霊廟": "th13", "東方輝針城": "th14",
    "弹幕天邪鬼": "th143", "東方紺珠伝": "th15", "東方天空璋": "th16",
    "秘封ナイトメアダイアリー": "th165", "東方鬼形獣": "th17",
    "東方虹龍洞": "th18", "バレットフィリア達の闇市場": "th185",
    "東方獣王園": "th19", "東方錦上京": "th20",
}

ZWAV_HEADER = 0x10  # thbgm.dat 头部长度


# ---------------------------------------------------------------- 工具
def norm_key(s):
    """曲名规范化：全角空格/波浪统一"""
    s = unicodedata.normalize("NFKC", s)
    s = s.replace("～", "~").replace("～", "~").replace("\u3000", " ")
    while "  " in s:
        s = s.replace("  ", " ")
    return s.strip()


def fmt_time(sec):
    if sec is None:
        return ""
    m, s = divmod(int(round(sec)), 60)
    return f"{m}:{s:02d}"


# ---------------------------------------------------------------- fmt 解析
def parse_fmt(path, dat_size):
    """thbgm.fmt: 52 字节/条
    name[16] start[4] unk[4] intro[4] total[4] WAVEFORMATEX[18] pad[2]
    """
    b = path.read_bytes()
    n = len(b) // 52
    out = []
    for i in range(n):
        e = b[i * 52:(i + 1) * 52]
        name = e[:16].rstrip(b"\x00").decode("cp932", "replace")
        start, unk, intro, total = struct.unpack("<4I", e[16:32])
        wf = e[32:50]
        tag, ch, rate, avg, align, bits, cbs = struct.unpack("<HHIIHHH", wf)
        # 末轨按 dat 实际大小截断，防止越界
        total_eff = min(total, dat_size - start) if dat_size > start else total
        out.append(dict(idx=i, file=name, start=start, unk=unk, intro=intro,
                        total=total, total_eff=total_eff,
                        ch=ch, rate=rate, bits=bits, fmt_tag=tag))
    return out


# ---------------------------------------------------------------- musiccmt
def stem_of(name):
    """剥离任意扩展名：th09_00.mid / th09_00.wav -> th09_00"""
    return name.rsplit(".", 1)[0] if "." in name else name


def parse_musiccmt(path):
    """@bgm/th13_01\nNo. 2 死霊の夜桜  ->  {'th13_01': (2, '死霊の夜桜')}
    注意：key 的扩展名可能是 .wav / .mid / 无，统一剥离后匹配"""
    if not path.exists():
        return {}
    txt = path.read_bytes().decode("cp932", errors="replace")
    res, cur = {}, None
    for line in txt.splitlines():
        line = line.strip()
        m = re.match(r"^@bgm/(.+)$", line)
        if m:
            cur = stem_of(m.group(1).strip())
            continue
        m = re.match(r"^No\.\s*(\d+)\s+(.+)$", line)
        if m and cur:
            res[cur] = (int(m.group(1)), m.group(2).strip())
            cur = None
    return res


# ---------------------------------------------------------------- ini
def parse_ini():
    txt = INI.read_bytes().decode("gbk", errors="replace")
    albums, cur = {}, None
    for raw in txt.splitlines():
        line = raw.strip()
        if not line or line.startswith(";"):
            continue
        m = re.match(r"^\[(.+?)\]", line)
        if m:
            cur = m.group(1).strip()
            albums[cur] = []
            continue
        if cur is None or not line.upper().startswith("BGM"):
            continue
        payload = line.split("=", 1)[1].strip()
        parts = [p.strip() for p in payload.split(",")]

        def num(x):
            x = x.strip()
            return int(x, 16) if x.lower().startswith("0x") else int(x)

        try:
            if len(parts) >= 6:      # name, file, addr, ilen, laddr, llen
                albums[cur].append((num(parts[-4]), num(parts[-3]),
                                    num(parts[-2]), num(parts[-1]),
                                    ",".join(parts[:-5]).strip(), parts[-5]))
            elif len(parts) == 5:    # name, addr, ilen, laddr, llen
                albums[cur].append((num(parts[-4]), num(parts[-3]),
                                    num(parts[-2]), num(parts[-1]),
                                    ",".join(parts[:-4]).strip(), None))
            elif len(parts) == 4:    # name, ilen, laddr, llen（省略 addr）
                albums[cur].append((None, num(parts[-3]),
                                    num(parts[-2]), num(parts[-1]),
                                    ",".join(parts[:-3]).strip(), None))
        except ValueError:
            pass
    return albums


def build_ini_index(albums):
    """返回 {game: {start: (intro, loop, name, file)}}"""
    by_game = {}
    for album, rows in albums.items():
        g = None
        for key in sorted(INI_ALBUM, key=len, reverse=True):
            if album.startswith(key):
                g = INI_ALBUM[key]
                break
        if g is None:
            continue
        d = by_game.setdefault(g, {})
        for addr, ilen, laddr, llen, name, f in rows:
            if addr is not None:
                d[addr] = (ilen, llen, name, f)
            elif f:
                d.setdefault(("file", f.lower()), (ilen, llen, name, f))
    return by_game


# ---------------------------------------------------------------- th06
def load_th06():
    """th06: 独立 wav + .pos（样本单位，相对数据区）"""
    bgm = TSA / "kouma" / "bgm"
    posdir = EXT / "th06"
    cmt = parse_musiccmt(posdir / "musiccmt.txt")
    rows = []
    wavs = sorted(bgm.glob("th06_*.wav"))
    for w in wavs:
        key = w.stem                       # th06_01
        pfile = posdir / f"{key}.pos"
        if not pfile.exists():
            continue
        with wave.open(str(w), "rb") as wf:
            ch, rate, bits = wf.getnchannels(), wf.getframerate(), wf.getsampwidth() * 8
            nframes = wf.getnframes()
            hdr = wf.getcomptype()  # noqa
        # 真实数据区起点 = 文件大小 - 数据字节数（th06 的 wav 头逐个不同，118~166 B）
        data_bytes = nframes * ch * (bits // 8)
        header = w.stat().st_size - data_bytes
        s0, s1 = struct.unpack("<2I", pfile.read_bytes()[:8])
        # 【2026-09-04 更正】.pos 就是 44100Hz 样本偏移，直接 *4 得「数据区内字节偏移」，
        # 不再减 44。依据 release/tsa/kouma/bgm/README.md（任务 t101）：
        #   .pos = 2 x u32 LE，单位 44100Hz 样本偏移；pos[0]=循环起点，pos[1]=循环终点
        #   已排除 MIDI tick / MIDI 文件字节偏移 / 小节对齐 / 4B 帧对齐四种假设
        #   判定依据③：重渲染后续播尾首样本逐字节等于 wav[pos[0]] 处内容
        # 旧写法 pos*4-44 来自与 BgmForAll.ini 的比对，但 ini 自己也按 44B 头算，
        # 属循环论证；与标准答案 release/tsa/kouma/bgm/tracklist.csv 差整 11 样本。
        intro = s0 * 4
        total = s1 * 4
        no, name = cmt.get(key, (None, None))
        rows.append(dict(idx=len(rows), file=w.name, start=header, unk=0,
                         intro=intro, total=total, total_eff=total,
                         ch=ch, rate=rate, bits=bits, fmt_tag=1,
                         music_no=no, name=name, src="wav"))
    return rows


# ---------------------------------------------------------------- 主流程
def main():
    albums = parse_ini()
    ini_idx = build_ini_index(albums)

    all_rows, stats = [], []
    for code, tdir, ja, en, note in GAMES:
        if code == "th06":
            tracks = load_th06()
            dat_size = None
        else:
            fp = EXT / tdir / "thbgm.fmt"
            if not fp.exists():
                stats.append((code, "缺 fmt", 0))
                continue
            dat = TSA / tdir / "thbgm.dat"
            dat_size = dat.stat().st_size if dat.exists() else None
            tracks = parse_fmt(fp, dat_size or 0)
            cmt = parse_musiccmt(EXT / tdir / "musiccmt.txt")
            for t in tracks:
                stem = stem_of(t["file"])
                no, name = cmt.get(stem, (None, None))
                t["music_no"], t["name"] = no, name
                t["src"] = "fmt"

        # 用 ini 补曲名：先按 start 精确匹配，再按文件名匹配（th06）
        imap = ini_idx.get(code, {})
        for t in tracks:
            hit = imap.get(t["start"]) or imap.get(("file", t["file"].lower()))
            t["ini_name"] = hit[2] if hit else None
            if t["name"] is None and t["ini_name"]:
                t["name"] = t["ini_name"]
                t["name_src"] = "ini"
            elif t["name"]:
                t["name_src"] = "musiccmt"
            else:
                t["name"] = stem_of(t["file"])
                t["name_src"] = "filename"
            # 校验 ini 与 fmt 的 intro/loop 是否一致
            if hit and hit[3] is None:   # 仅按 start 命中的才做数值校验
                t["ini_intro"], t["ini_loop"] = hit[0], hit[1]
                t["xcheck"] = (hit[0] == t["intro"] and
                               hit[1] == t["total_eff"] - t["intro"])
            else:
                t["ini_intro"] = t["ini_loop"] = None
                t["xcheck"] = None

            bps = t["rate"] * t["ch"] * (t["bits"] // 8)
            t["total_sec"] = t["total_eff"] / bps
            t["loop_len"] = t["total_eff"] - t["intro"]
            t["loop_sec"] = t["loop_len"] / bps
            t["intro_sec"] = t["intro"] / bps
            t["loop_start"] = t["start"] + t["intro"]
            t["loop_end"] = t["start"] + t["total_eff"]
            t["game"] = code
            t["ja_title"] = ja
            t["en_title"] = en
            t["note"] = note
            all_rows.append(t)

        named = sum(1 for t in tracks if t["name_src"] != "filename")
        stats.append((code, f"{ja}", len(tracks), named, dat_size))

    # ---- 第二轮：跨作品复用曲（如 th128_08.wav = プレイヤーズスコア）补名
    gmap = {}
    for t in all_rows:
        if t["name_src"] != "filename":
            gmap.setdefault(stem_of(t["file"]), t["name"])
    for t in all_rows:
        if t["name_src"] == "filename":
            g = gmap.get(stem_of(t["file"]))
            if g:
                t["name"] = g
                t["name_src"] = "reuse"

    # ---- 第三轮：人工覆写（用户 2026-09-04 审定）
    # 依据：以官方最终确定的曲名为准。游戏内 musiccmt.txt 的拼写/标点
    # 若与商业 CD 收录版不一致，以 CD 为准。
    for t in all_rows:
        ov = TITLE_OVERRIDE.get((t["game"], t["file"]))
        if ov:
            t["name"], t["title_note"] = ov[0], ov[1]
            t["name_src"] = "override"

    # 重新统计（第二轮之后）
    stats = []
    for code, tdir, ja, en, note in GAMES:
        tr = [t for t in all_rows if t["game"] == code]
        named = sum(1 for t in tr if t["name_src"] != "filename")
        dat = TSA / tdir / "thbgm.dat"
        stats.append((code, ja, len(tr), named,
                      dat.stat().st_size if dat.exists() else None))

    return all_rows, stats, ini_idx


def write_outputs(rows, stats):
    md, csv_rows = [], []

    md.append("# 东方 Project BGM 曲目总表（tsa 侧 21 作）\n")
    md.append("> 数据源：全部从 `tsa/` 原始文件现场解出 —— `thXX.dat` 内的 `thbgm.fmt`"
              "（轨道几何）与 `musiccmt.txt`（曲名），`thbgm.dat`（音频本体，仅校验长度）。\n")
    md.append("> 曲名取日语原名。排序为 **fmt 物理顺序**，即 `thbgm.dat` 内的真实布局，"
              "也是播放器 seek 的顺序。\n")
    md.append("> 循环模型：`intro` 播一遍 → `[循环起点, 循环终点)` 无限循环。\n")

    # 总览
    md.append("\n## 总览\n")
    md.append("| 作品 | 日文标题 | 曲目 | 音频 | 备注 |")
    md.append("|---|---|---:|---:|---|")
    for code, tdir, ja, en, note in GAMES:
        tr = [t for t in rows if t["game"] == code]
        if not tr:
            continue
        tot_sec = sum(t["total_sec"] for t in tr)
        md.append(f"| {code} | {ja} | {len(tr)} | {fmt_time(tot_sec)} | {note} |")
    md.append(f"| **合计** | | **{len(rows)}** | "
              f"**{fmt_time(sum(t['total_sec'] for t in rows))}** | |")

    # 逐作明细
    for code, tdir, ja, en, note in GAMES:
        tr = [t for t in rows if t["game"] == code]
        if not tr:
            continue
        dat = TSA / tdir / "thbgm.dat"
        dsz = dat.stat().st_size if dat.exists() else None
        md.append(f"\n## {code}　{ja}　～ {en}\n")
        if note:
            md.append(f"> {note}\n")
        head = f"> 曲目 {len(tr)}"
        if dsz:
            end = max(t["loop_end"] for t in tr)
            head += f"　·　`thbgm.dat` {dsz:,} B　·　末轨终点 {end:,} B　·　"
            head += f"覆盖 {'完整' if end == dsz else f'差 {dsz - end:,} B'}"
        md.append(head + "\n")
        md.append("| # | 曲名 | 音乐室 | 起始偏移 | 整轨时长 | 循环起点 | 循环终点 |"
                  " 循环段 | 采样率 |")
        md.append("|---:|---|---:|---:|---:|---:|---:|---:|---:|")
        for i, t in enumerate(tr, 1):
            no = t["music_no"] or ""
            md.append(
                f"| {i} | {t['name']} | {no} | `0x{t['start']:08X}` | "
                f"{fmt_time(t['total_sec'])} | `0x{t['loop_start']:08X}` | "
                f"`0x{t['loop_end']:08X}` | {fmt_time(t['loop_sec'])} | "
                f"{t['rate']} |")
            csv_rows.append(dict(
                game=code, index=i, music_no=t["music_no"] or "", title=t["name"],
                file=t["file"], name_src=t["name_src"],
                start=t["start"], intro_bytes=t["intro"], total_bytes=t["total_eff"],
                loop_start=t["loop_start"], loop_end=t["loop_end"],
                loop_bytes=t["loop_len"],
                intro_sec=round(t["intro_sec"], 3),
                total_sec=round(t["total_sec"], 3),
                loop_sec=round(t["loop_sec"], 3),
                rate=t["rate"], ch=t["ch"], bits=t["bits"],
                title_note=t.get("title_note", ""),
            ))
    return "\n".join(md), csv_rows


def verify(rows):
    """连续性 / 覆盖率 / ini 交叉校验"""
    out = []
    out.append("## 验证\n")
    out.append("### 1. 相邻轨连续性（start[k+1] == start[k] + total[k]）\n")
    out.append("| 作品 | 曲目 | 断点 | 说明 |")
    out.append("|---|---:|---:|---|")
    for code, tdir, ja, en, note in GAMES:
        tr = [t for t in rows if t["game"] == code]
        if code == "th06" or not tr:
            continue
        bad = []
        for k in range(len(tr) - 1):
            exp = tr[k]["start"] + tr[k]["total_eff"]
            if exp != tr[k + 1]["start"]:
                bad.append(f"#{k+1}→#{k+2} 期望 0x{exp:X} 实际 0x{tr[k+1]['start']:X}"
                           f" 差 {tr[k+1]['start'] - exp:+,}")
        out.append(f"| {code} | {len(tr)} | {len(bad)} | "
                   f"{'；'.join(bad[:2]) if bad else '全连续'} |")

    out.append("\n### 2. ini 交叉校验（BgmForAll.ini vs thbgm.fmt）\n")
    out.append("| 作品 | 可比对 | 三元组全等 | 仅 intro 差 | 仅 loop 差 | 其他不符 |")
    out.append("|---|---:|---:|---:|---:|---:|")
    teq = tdiff = 0
    for code, tdir, ja, en, note in GAMES:
        tr = [t for t in rows if t["game"] == code if t.get("xcheck") is not None]
        if not tr:
            continue
        eq = idf = ldf = oth = 0
        for t in tr:
            if t["xcheck"]:
                eq += 1
            elif t["ini_intro"] == t["intro"]:
                ldf += 1
            elif t["ini_loop"] == t["loop_len"]:
                idf += 1
            else:
                oth += 1
        teq += eq
        tdiff += len(tr) - eq
        out.append(f"| {code} | {len(tr)} | {eq} | {idf} | {ldf} | {oth} |")
    out.append(f"| **合计** | | **{teq}** | | | **{tdiff}** |")

    out.append("\n### 2b. 谁更可信：ini 自身的偏移连续性\n")
    out.append("同一判据（`start[k+1] == start[k] + intro[k] + loop[k]`）施加于 ini：\n")
    out.append("| 作品 | ini 曲目 | ini 断点 | fmt 断点 | 判定 |")
    out.append("|---|---:|---:|---:|---|")
    _, _, ini_idx = main()
    for code, tdir, ja, en, note in GAMES:
        m = ini_idx.get(code, {})
        r = sorted([(k, v) for k, v in m.items() if isinstance(k, int)])
        if not r:
            continue
        ib = sum(1 for i in range(len(r) - 1)
                 if r[i][0] + r[i][1][0] + r[i][1][1] != r[i + 1][0])
        tr = [t for t in rows if t["game"] == code]
        fb = 0
        if code != "th06":
            fb = sum(1 for k in range(len(tr) - 1)
                     if tr[k]["start"] + tr[k]["total_eff"] != tr[k + 1]["start"])
        verdict = "一致" if (ib == 0 and fb == 0) else ("**fmt 胜出**" if ib > fb else "ini 胜出")
        out.append(f"| {code} | {len(r)} | {ib} | {fb} | {verdict} |")
    out.append("\n> ini 在 th09 / th10 / th11 / th14 上出现断点，而 fmt 全作零断点。"
               "\n> 这些作品的 ini 值经人为微调过 intro/loop 分割点（总长基本不变），"
               "属于「听感优化版」，非游戏本体真值。\n> **本表几何数据一律以 fmt 为准**。\n")

    out.append("\n### 2c. th06 特例：无 thbgm.dat / fmt，散装 wav + `.pos`\n")
    out.append("th06 是本族的例外作：**无 thbgm.dat、无 thbgm.fmt**，BGM 是 `tsa/kouma/bgm/` 下\n"
               "17 个散装 wav，循环点在 `紅魔郷MD.DAT` 的 `th06_XX.pos`。\n")
    out.append("`.pos` = 8 字节 = 2×u32 LE，**单位是 44100Hz 样本偏移**\n"
               "（依据 `release/tsa/kouma/bgm/README.md`，任务 t101）：\n\n"
               "- `pos[0]` = 循环起点（intro 结束）、`pos[1]` = 循环终点，循环段 `[pos[0], pos[1])`\n"
               "- t101 已排除：MIDI tick / MIDI 文件字节偏移 / 小节对齐(88200·176400) / 4B 帧对齐\n"
               "- 判定依据：① 17/17 满足 `0 < pos[0] < pos[1] ≤ wav帧数`；\n"
               "  ② th06_02 开头 0.5s 纯静音(RMS=0) 而 pos[0] 处 RMS=8434；\n"
               "  ③ 重渲染后续播尾首样本逐字节等于 `wav[pos[0]]` 处内容\n")
    out.append("换算式（已与标准答案 `release/tsa/kouma/bgm/tracklist.csv` 逐首复核 17/17 一致）：\n")
    out.append("```\n"
               "header = 文件大小 - nframes*ch*bits/8      # 真实头长，逐个不同 118~166 B\n"
               "intro  = pos[0] * 4                       # 数据区内字节偏移（不减 44）\n"
               "total  = pos[1] * 4\n"
               "loop   = total - intro                    # == (pos[1]-pos[0])*4\n"
               "循环起点(绝对) = header + intro\n"
               "```\n")
    out.append("> **2026-09-04 更正**：旧版用 `pos*4 - 44`，依据是「与 BgmForAll.ini 逐首吻合」。\n"
               "> 那是循环论证——ini 自己也按标准 44B 头计算。与标准答案比对后确认为\n"
               "> **系统性偏早 44 字节 = 11 样本 = 0.25 ms**，已改为 `pos*4`。\n"
               "> 循环段长度不受影响（`loop = (pos[1]-pos[0])*4` 做减法时偏差抵消）。\n")
    out.append("| # | 曲名 | 实际头长 | intro(B) | 循环段(B) |")
    out.append("|---:|---|---:|---:|---:|")
    for i, t in enumerate([x for x in rows if x["game"] == "th06"], 1):
        out.append(f"| {i} | {t['name']} | {t['start']} | "
                   f"{t['intro']:,} | {t['loop_len']:,} |")

    out.append("\n### 3. 曲名来源分布\n")
    out.append("| 来源 | 曲目 |")
    out.append("|---|---:|")
    from collections import Counter
    c = Counter(t["name_src"] for t in rows)
    for k, v in c.most_common():
        out.append(f"| {k} | {v} |")
    return "\n".join(out)


if __name__ == "__main__":
    rows, stats, _ = main()
    md, csv_rows = write_outputs(rows, stats)
    (OUT / "TRACKLIST.md").write_text(md + "\n\n" + verify(rows) + "\n",
                                      encoding="utf-8")
    with open(OUT / "tracklist.csv", "w", newline="", encoding="utf-8-sig") as f:
        w = csv.DictWriter(f, fieldnames=list(csv_rows[0].keys()))
        w.writeheader()
        w.writerows(csv_rows)

    print(f"{'作品':<7}{'曲目':>5}{'有曲名':>7}{'thbgm.dat':>14}")
    print("-" * 40)
    for code, ja, n, named, sz in stats:
        print(f"{code:<7}{n:>5}{named:>7}{(sz or 0):>14,}")
    print("-" * 40)
    print(f"{'合计':<7}{len(rows):>5}")
    print(f"\n已写出: TRACKLIST.md / tracklist.csv  ({len(csv_rows)} 首)")
