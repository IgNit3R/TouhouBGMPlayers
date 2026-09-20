# -*- coding: utf-8 -*-
"""
th06 .pos 样本原点判定

.pos 存 <start sample>.4b <end sample>.4b。问题是样本原点在哪里：
  A) 相对 PCM data 起点      -> data 帧偏移 = sample
  B) 相对文件开头(44B 标准头) -> data 帧偏移 = sample - 11   (44/4)
  C) 相对文件开头(实际头)     -> data 帧偏移 = sample - header_bytes/4

th06 的 wav 头实测为 122 字节（非标准 44），故三种假设都需要检验。

判据：正确的循环点处，end 附近波形应与 start 附近波形连续（MAE 最小）。
"""
import pathlib
import struct
import wave

BGM = pathlib.Path(__file__).resolve().parents[2] / "tsa" / "kouma" / "bgm"   # tools/x.py -> bgmplayer -> thworks
# ⚠️ 弃用标记（2026-09-21 用户确认）：*.pos 所在的输入缓存不再随仓库提供 ⇒ 本脚本大概率已弃用。
POS = pathlib.Path(__file__).resolve().parents[1] / "docs" / "source" / "extract" / "th06"
W = 512


def frames_at(w, frame, n=W):
    """读取以 frame 结尾的 n 帧（左声道），越界返回 None"""
    if frame - n < 0:
        return None
    w.setpos(frame - n)
    raw = w.readframes(n)
    if len(raw) < n * 4:
        return None
    # 16bit stereo: n 帧 = 2n 个 int16，交错存放
    return list(struct.unpack("<%dh" % (2 * n), raw))[0::2]   # 取左声道


def mae(a, b):
    return sum(abs(x - y) for x, y in zip(a, b)) / max(1, len(a))


def main():
    totals = {"A(data起点)": 0.0, "B(-11帧/44B头)": 0.0, "C(-实际头)": 0.0}
    wins = {"A(data起点)": 0, "B(-11帧/44B头)": 0, "C(-实际头)": 0}
    count = 0

    for pf in sorted(POS.glob("*.pos")):
        idx = pf.stem.split("_")[-1]
        wav = BGM / f"th06_{idx}.wav"
        if not wav.exists():
            continue
        ss, es = struct.unpack("<2I", pf.read_bytes()[:8])
        w = wave.open(str(wav), "rb")
        nframes = w.getnframes()
        header = wav.stat().st_size - nframes * 4   # 实际头字节数

        cands = {
            "A(data起点)": (ss, es),
            "B(-11帧/44B头)": (ss - 11, es - 11),
            "C(-实际头)": (ss - header // 4, es - header // 4),
        }
        row = {}
        for k, (a, b) in cands.items():
            wa = frames_at(w, a)
            wb = frames_at(w, b)
            if wa is None or wb is None:
                row[k] = None
                continue
            row[k] = mae(wa, wb)

        valid = {k: v for k, v in row.items() if v is not None}
        if not valid:
            w.close()
            continue
        best = min(valid, key=valid.get)
        wins[best] += 1
        for k, v in valid.items():
            totals[k] += v
        count += 1

        if count <= 5:
            print(f"th06_{idx}  header={header}B  frames={nframes}  "
                  f"pos=({ss},{es})")
            for k, v in row.items():
                print(f"    {k:<16} MAE = {v if v is None else round(v,1)}")
        w.close()

    print(f"\n=== 统计（{count} 首）===")
    print(f"{'假设':<18}{'累计MAE':>14}{'平均MAE':>12}{'最优次数':>10}")
    for k in totals:
        avg = totals[k] / max(1, count)
        print(f"{k:<18}{totals[k]:>14.0f}{avg:>12.1f}{wins[k]:>10}")


if __name__ == "__main__":
    main()
