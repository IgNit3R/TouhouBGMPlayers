# -*- coding: utf-8 -*-
"""
th06 循环点原点决定性检验（利用 t101 已渲染的参考件做真值比对）

原理（据 release/tsa/kouma/bgm/README.md）：
    rendered = raw 完整播一遍 + 从 pos[0] 起续播 15s + 末尾 8s 淡出
    => rendered[raw_frames : raw_frames + N] 应当逐字节等于 raw[ pos0 : pos0 + N ]
       其中 pos0 是「循环起点在 raw PCM 数据中的帧号」

候选假设：
    A  pos0 = pos[0]             （README t101 的字面读法：pos 即 44100Hz 样本偏移）
    B  pos0 = pos[0] - 11        （我的 -44 修正：44 字节 = 11 帧，样本原点在文件偏移 44）
    C  pos0 = pos[0] + 11
"""
import wave
import struct
import pathlib
import sys

RAW = pathlib.Path(r"E:\GitWorkspace\thworks\release\tsa\kouma\bgm\raw")
REND = pathlib.Path(r"E:\GitWorkspace\thworks\release\tsa\kouma\bgm\rendered")
POS = pathlib.Path(__file__).resolve().parents[1] / "04_source" / "extract" / "th06"

N = 44100 // 2  # 比对 0.5 秒


def read_frames(path, start_frame, count):
    """返回左声道 16bit 样本列表"""
    with wave.open(str(path), "rb") as w:
        w.setpos(start_frame)
        raw = w.readframes(count)
    n = len(raw) // 4
    return list(struct.unpack("<%dh" % (n * 2), raw))[0::2]


def mae(a, b):
    m = min(len(a), len(b))
    if m == 0:
        return float("inf")
    return sum(abs(x - y) for x, y in zip(a[:m], b[:m])) / m


print(f"{'#':>3} {'pos0':>8} {'pos1':>8} {'raw帧数':>9} {'尾奏帧':>8} "
      f"{'A:pos0':>9} {'B:-11':>9} {'C:+11':>9}  判定")
print("-" * 82)

votes = {"A": 0, "B": 0, "C": 0}

for i in range(1, 18):
    name = f"th06_{i:02d}.wav"
    rp, dp = RAW / name, REND / name
    if not rp.exists() or not dp.exists():
        print(f"{i:>3}  缺文件")
        continue

    with wave.open(str(rp), "rb") as w:
        raw_frames = w.getnframes()
    with wave.open(str(dp), "rb") as w:
        rend_frames = w.getnframes()

    pos0, pos1 = struct.unpack("<2I", (POS / f"th06_{i:02d}.pos").read_bytes()[:8])

    # 续播段起点 = 可播放区末尾（实测 rendered 长度 == pos1 + 15*44100，尾奏被裁掉）
    cont = pos1
    if cont + N > rend_frames:
        print(f"{i:>3}  rendered 太短，跳过")
        continue

    ref = read_frames(dp, cont, N)
    cands = {
        "A": pos0,
        "B": pos0 - 11,
        "C": pos0 + 11,
    }
    scores = {}
    for k, p in cands.items():
        if p < 0 or p + N > raw_frames:
            scores[k] = float("inf")
        else:
            scores[k] = mae(ref, read_frames(rp, p, N))

    best = min(scores, key=scores.get)
    votes[best] += 1
    outro = raw_frames - pos1

    print(f"{i:>3} {pos0:>8} {pos1:>8} {raw_frames:>9} {outro:>8} "
          f"{scores['A']:>9.1f} {scores['B']:>9.1f} {scores['C']:>9.1f}  {best}")

print("-" * 82)
print("投票：", votes)

# 附加核验 1：rendered 长度是否 == pos1 + 15s（尾奏被裁掉的硬证据）
print("\nrendered 长度核验（应 = pos1 帧 + 15*44100）：")
bad = 0
for i in range(1, 18):
    name = f"th06_{i:02d}.wav"
    pos0, pos1 = struct.unpack("<2I", (POS / f"th06_{i:02d}.pos").read_bytes()[:8])
    with wave.open(str(REND / name), "rb") as b:
        df = b.getnframes()
    exp = pos1 + 15 * 44100
    if df != exp:
        bad += 1
        print(f"  {name}: rendered={df} 期望={exp} 差={df - exp}")
print(f"  不符 {bad} / 17" if bad else "  17/17 完全吻合 → 可播放区 = [0, pos1)，尾奏不播")
