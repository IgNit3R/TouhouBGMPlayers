#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
循环点权威比对：把各作"游戏自带"的循环点（整数样本）与内嵌索引的秒值换算对比，
输出 delta 报告（tf_prep/loop_delta.csv）。
权威源：
  th075 : WAV cue 块第一个 cue 的 dwPosition（循环起点），终点 = data 块末尾
  th105/123/135/145/155 : data/bgm/*.sfl（cue 起点 + ltxt 段长）
  th175 : *.ogg.ini 的 [loop0] repeatstart / repeatend
"""
import csv, gzip, json, os, struct

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))          # bgmplayer/
PUSH = r'E:\GitWorkspace\thworks\pushfiles'
RATE = 44100


def parse_sfl_txt(path):
    """pushfiles 里 sfl 已被转成 .txt：直接读整数样本"""
    start = end = None
    for line in open(path, encoding='utf-8', errors='replace'):
        if line.startswith('循环起点:'):
            start = int(line.split(':')[1].strip().split()[0])
        elif line.startswith('循环终点:'):
            end = int(line.split(':')[1].strip().split()[0])
    return None if start is None else (start, end or 0)


def parse_sfl(path):
    """返回 (start_sample, end_sample)；失败返回 None"""
    d = open(path, 'rb').read()
    if d[:4] != b'RIFF':
        return None
    start, length = None, None
    i = 12
    while i + 8 <= len(d):
        cid = d[i:i + 4]
        size = struct.unpack('<I', d[i + 4:i + 8])[0]
        body = d[i + 8:i + 8 + size]
        if cid == b'cue ':
            n = struct.unpack('<I', body[0:4])[0]
            if n > 0:
                start = struct.unpack('<I', body[8:12])[0]   # 第一个 cue 的 dwPosition
        elif cid == b'LIST' and body[:4] == b'adtl':
            j = 4
            while j + 8 <= len(body):
                sc = body[j:j + 4]
                ss = struct.unpack('<I', body[j + 4:j + 8])[0]
                if sc == b'ltxt':
                    length = struct.unpack('<I', body[j + 8 + 4:j + 8 + 8])[0]
                j += 8 + ss + (ss & 1)
        i += 8 + size + (size & 1)
    if start is None:
        return None
    return start, (start + (length or 0))


def parse_wav_cue(path):
    d = open(path, 'rb').read()
    i = d.find(b'cue ')
    if i < 0:
        return None
    n = struct.unpack('<I', d[i + 8:i + 12])[0]
    if n < 1:
        return None
    start = struct.unpack('<I', d[i + 12 + 4:i + 12 + 8])[0]
    di = d.find(b'data')
    total = (len(d) - di - 8) // 4          # 16bit 2ch
    return start, total


def parse_ini(path):
    txt = open(path, encoding='utf-8', errors='replace').read()
    s = e = None
    for line in txt.splitlines():
        line = line.strip()
        if line.startswith('repeatstart='):
            s = int(line.split('=')[1])
        elif line.startswith('repeatend='):
            e = int(line.split('=')[1])
    return None if s is None else (s, e or 0)


def collect(game, bgmdirs, kind):
    """返回 {stem: (start, end)}"""
    out = {}
    for d in bgmdirs:
        if not os.path.isdir(d):
            continue
        for fn in os.listdir(d):
            p = os.path.join(d, fn)
            # ini 形如 flandre1.ogg.ini → 基准名 flandre1（与索引 file 的 stem 对齐）
            stem = fn.split('.')[0] if kind == 'ini' else fn.rsplit('.', 1)[0]
            if os.path.isdir(p):                     # 解包产物是同名目录
                inner = None
                for cand in os.listdir(p):
                    if cand == fn or cand.startswith(fn + '.') or cand == 'data':
                        inner = os.path.join(p, cand); break
                if inner is None:
                    continue
                if kind == 'ini' and (inner.endswith('.ini') or inner.endswith('.txt')):
                    r = parse_ini(inner)
                    if r: out[stem] = r
                    continue
                if kind == 'sfl' and inner.endswith('.txt'):
                    r = parse_sfl_txt(inner)
                    if r: out[stem] = r
                    continue
                p = inner
            try:
                if kind == 'sfl' and (fn.lower().endswith('.sfl') or fn.lower().endswith('.wav')):
                    r = parse_sfl(p)
                elif kind == 'wav' and fn.lower().endswith('.wav'):
                    r = parse_wav_cue(p)
                elif kind == 'ini' and fn.lower().endswith('.ini'):
                    r = parse_ini(p)
                else:
                    continue
            except Exception:
                r = None
            if r:
                out[stem] = r        # 后出现的包覆盖先出现的（b 包优先）
    return out


SOURCES = {
    'th075': ('sfl', [os.path.join(PUSH, r'th075\th075bgm.dat\wave\bgm')]),
    'th105': ('sfl', [os.path.join(PUSH, r'th105\th105a.dat\data\bgm'),
                      os.path.join(PUSH, r'th105\th105b.dat\data\bgm')]),
    'th123': ('sfl', [os.path.join(PUSH, r'th123\th123a.dat\data\bgm'),
                      os.path.join(PUSH, r'th123\th123b.dat\data\bgm')]),
    'th135': ('sfl', [os.path.join(PUSH, r'th135\th135.pak\data\bgm'),
                      os.path.join(PUSH, r'th135\th135b.pak\data\bgm')]),
    'th145': ('sfl', [os.path.join(PUSH, r'th145\th145.pak\data\bgm'),
                      os.path.join(PUSH, r'th145\th145b.pak\data\bgm')]),
    'th155': ('sfl', [os.path.join(PUSH, r'th155\th155.pak\data\bgm'),
                      os.path.join(PUSH, r'th155\th155b.pak\data\bgm')]),
    'th175': ('ini', [os.path.join(PUSH, r'th175\data.cga\data\bgm'),
                      os.path.join(PUSH, r'th175\data.cgb\data\bgm')]),
}


def main():
    idx = json.loads(gzip.open(os.path.join(BASE, 'src', 'Resources', 'tracks.tf.json.gz'), 'rb')
                     .read().decode('utf-8'))
    rows = [('game', 'file', 'title', 'auth_start', 'auth_end', 'idx_start', 'idx_end',
             'd_start(样本)', 'd_end(样本)', 'd_start(ms)', 'd_end(ms)')]
    counts = {}
    big = []
    for g in idx['games']:
        gid = g['id']
        kind, dirs = SOURCES[gid]
        auth = collect(gid, dirs, kind)
        n_match = n_big = 0
        for t in g['tracks']:
            stem = (t['f'] or '').rsplit('.', 1)[0]
            a = auth.get(stem)
            if a is None:
                continue
            a_s, a_e = a
            # 索引自 v2 起直接存整数样本
            i_s = t.get('lss')
            i_e = t.get('les')
            if i_s is None:
                continue
            n_match += 1
            ds = i_s - a_s
            de = (i_e - a_e) if i_e is not None else 0
            rows.append((gid, t['f'], t['t'], a_s, a_e, i_s, i_e,
                         ds, de, round(ds / RATE * 1000, 3), round(de / RATE * 1000, 3)))
            if abs(ds) > 30 or abs(de) > 30:
                n_big += 1
                big.append((gid, t['f'], ds, de))
        counts[gid] = (len(auth), n_match, n_big)

    out = os.path.join(BASE, 'docs', 'loop_delta.csv')
    with open(out, 'w', encoding='utf-8-sig', newline='') as f:
        csv.writer(f).writerows(rows)
    print(f'写出 {out}   比对 {len(rows) - 1} 首\n')
    print(f'{"作":8}{"权威条目":>8}{"已比对":>8}{"偏差>30样本":>12}')
    for gid, (na, nm, nb) in counts.items():
        print(f'{gid:8}{na:>8}{nm:>8}{nb:>12}')
    if big:
        print(f'\n=== 偏差 >30 样本（>{30 / 44.1:.2f} ms）的曲目，共 {len(big)} 首 ===')
        for b in big[:20]:
            print(f'  {b[0]} {b[1]:<18} Δstart={b[2]:>6}  Δend={b[3]:>6}')


if __name__ == '__main__':
    main()

# ---------- 资源定位 / 整曲样本数（供索引生成使用） ----------

def build_asset_index(game):
    """遍历 pushfiles/<game>，建立「文件名 → 实际文件路径」索引（解包产物可能是同名目录）。"""
    out = {}
    root = os.path.join(PUSH, game)
    for cur, dirs, files in os.walk(root):
        for name in list(files) + list(dirs):
            p = os.path.join(cur, name)
            if os.path.isdir(p):
                inner = None
                for cand in os.listdir(p):
                    if cand == name or cand == 'data':
                        inner = os.path.join(p, cand)
                        break
                if inner:
                    out.setdefault(name, inner)
            else:
                out.setdefault(name, p)
    return out


def ogg_last_granule(path):
    """OGG 末页 granule = 流的有效样本总数（权威长度）"""
    d = open(path, 'rb').read()
    i, last = 0, None
    while i + 27 <= len(d) and d[i:i + 4] == b'OggS':
        gran = struct.unpack('<q', d[i + 6:i + 14])[0]
        nseg = d[i + 26]
        segs = list(d[i + 27:i + 27 + nseg])
        last = gran
        i += 27 + nseg + sum(segs)
    return last


def wav_data_samples(path):
    """WAV data 块样本数（16bit 2ch → 4 字节/帧）"""
    d = open(path, 'rb').read()
    i = d.find(b'data')
    if i < 0:
        return None
    size = struct.unpack('<I', d[i + 4:i + 8])[0]
    return size // 4


def asset_total_samples(path):
    if not path:
        return None
    low = path.lower()
    if low.endswith('.ogg'):
        return ogg_last_granule(path)
    if low.endswith('.wav'):
        return wav_data_samples(path)
    return None
