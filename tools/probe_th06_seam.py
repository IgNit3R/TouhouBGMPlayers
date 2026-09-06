# -*- coding: utf-8 -*-
"""
th06 循环点接缝判定：pos 样本偏移的两种解读哪个对？

README (release/tsa/kouma/bgm/README.md, t101) 说：
    .pos = 2 x u32 LE，单位是 44100Hz 样本偏移
    pos[0] = 循环起点，pos[1] = 循环终点，循环段 = [pos[0], pos[1])
    => PCM 数据内字节偏移 = pos * 4

我此前在 gen_tracklist_v2.py 里用了 intro = pos[0]*4 - 44
（依据是与 BgmForAll.ini 逐首吻合），那等于假设样本原点在文件偏移 44。
两种解读相差 44 字节 = 11 样本 = 0.25 ms，听不出来，但可以测出来。

判据：接缝连续性。播到 loop_end 跳回 loop_start，
则 left[loop_end + j] 应当与 left[loop_start + j] 高度相似。
比较三种假设的 MAE：
    H_A  offset 0      样本偏移 = pos            （README 的解读）
    H_B  offset -11    样本偏移 = pos - 11       （我的 -44 字节解读）
    H_C  offset -22    样本偏移 = pos - 22       （负对照）
"""
import pathlib
import struct
import wave

BGM = pathlib.Path(__file__).resolve().parents[3] / "tsa" / "kouma" / "bgm"
POS = pathlib.Path(__file__).resolve().parents[1] / "04_source" / "extract" / "th06"
K = 512  # 比较窗口样本数


def load_left(path):
    with wave.open(str(path), "rb") as w:
        n = w.getnframes()
        rate = w.getframerate()
        ch = w.getnchannels()
        sw = w.getsampwidth()
        data_bytes = n * ch * sw
    header = path.stat().st_size - data_bytes
    raw = path.read_bytes()[header:header + data_bytes]
    pcm = struct.unpack("<%dh" % (len(raw) // 2), raw)
    return list(pcm[0::ch]), rate, n, header


def mae_at(left, ls, le, k=K):
    """接缝 MAE：比较 [le, le+k) 与 [ls, ls+k)"""
    if ls < 0 or le + k > len(left) or le < 0:
        return None
    a = left[le:le + k]
    b = left[ls:ls + k]
    return sum(abs(x - y) for x, y in zip(a, b)) / k


def main():
    print(f"{'#':>3} {'率':>6} {'帧数':>9} {'头长':>5} "
          f"{'pos0':>9} {'pos1':>9} "
          f"{'MAE_A(0)':>10} {'MAE_B(-11)':>11} {'MAE_C(-22)':>11} {'随机对照':>9}  判定")
    print("-" * 104)
    win = {"A": 0, "B": 0, "C": 0}
    for i in range(1, 18):
        wav = BGM / f"th06_{i:02d}.wav"
        posf = POS / f"th06_{i:02d}.pos"
        if not (wav.exists() and posf.exists()):
            print(f"{i:>3}  缺文件")
            continue
        left, rate, nframes, header = load_left(wav)
        s0, s1 = struct.unpack("<2I", posf.read_bytes()[:8])

        res = {}
        for tag, off in (("A", 0), ("B", -11), ("C", -22)):
            ls = s0 + off
            le = s1 + off
            res[tag] = mae_at(left, ls, le)

        # 随机对照：把起点挪到轨中段
        ctrl = mae_at(left, nframes // 2, nframes // 2 + (s1 - s0))
        ctrl = ctrl if ctrl is not None else mae_at(left, 0, max(1, (s1 - s0)))

        if all(v is not None for v in res.values()):
            best = min(res, key=lambda t: res[t])
            win[best] += 1
            verdict = f"{best} 最优"
        else:
            verdict = "(窗口越界)"

        print(f"{i:>3} {rate:>6} {nframes:>9} {header:>5} "
              f"{s0:>9} {s1:>9} "
              f"{res['A']:>10.1f} {res['B']:>11.1f} {res['C']:>11.1f} "
              f"{(ctrl or 0):>9.1f}  {verdict}")

    print("-" * 104)
    print(f"17 首中胜出次数： A(offset 0, README 解读) = {win['A']}   "
          f"B(offset -11, 我的 -44B 解读) = {win['B']}   C(负对照) = {win['C']}")


if __name__ == "__main__":
    main()
