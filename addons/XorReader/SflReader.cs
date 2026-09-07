// SflReader.cs — 解析 tasofro .sfl（RIFF "SFPL" 容器）BGM 循环点文件。
//
// 布局（经 th105b/data/bgm/*.sfl 实测 + tracklist.csv 核对）：
//   RIFF("SFPL"
//     "cue " 块：body[0:4]=cue 点数 n；之后每 24 字节一个 cue：
//       第 1 个 cue 的 body[8:12] = dwPosition = 循环起点（采样数，rate=44100）
//     "data" 块：本场景恒为空（size=0），跳过
//     LIST("adtl"
//       "ltxt" 块：body[0:4]=dwIdentifier（对应 cue 序号），
//                  body[4:8]=dwSampleLength = 循环段长（采样数）
//     ))
//   循环终点样本 = 循环起点样本 + 循环段长样本
//   秒值 = 采样数 / 44100
//
// 注意：cue 里另有 dwSampleOffset 字段，与 dwPosition 同值，只取其一，不叠加。

namespace addons.XorReader;

public sealed record SflLoopInfo(long LoopStartSample, long LoopLengthSample, long SampleRate = 44100)
{
    public long LoopEndSample => LoopStartSample + LoopLengthSample;
    public double LoopStartSec => LoopStartSample / (double)SampleRate;
    public double LoopEndSec => LoopEndSample / (double)SampleRate;
    public double LoopBodySec => LoopLengthSample / (double)SampleRate;
}

public static class SflReader
{
    public const uint DefaultRate = 44100;

    public static SflLoopInfo Read(byte[] data)
    {
        using var ms = new MemoryStream(data, writable: false);
        return Read(ms);
    }

    public static SflLoopInfo Read(Stream stream)
    {
        using var br = new BinaryReader(stream);

        // RIFF 头：'RIFF' + u32 总长 + form 'SFPL'
        Span<byte> four = stackalloc byte[4];
        if (stream.Read(four) != 4 || four.SequenceEqual("RIFF"u8) == false)
            throw new InvalidDataException("sfl: 缺少 RIFF 头");
        stream.ReadExactly(four); // riff size（部分文件此值偏小，不校验）
        if (stream.Read(four) != 4 || four.SequenceEqual("SFPL"u8) == false)
            throw new InvalidDataException("sfl: form 不是 SFPL");

        long loopStartSample = -1, loopLengthSample = -1;

        while (stream.Read(four) == 4)
        {
            uint chunkSize = br.ReadUInt32();
            long bodyPos = stream.Position;
            long nextChunk = bodyPos + chunkSize + (chunkSize & 1); // RIFF word 对齐填充

            if (four.SequenceEqual("cue "u8))
            {
                uint cueCount = br.ReadUInt32();
                if (cueCount < 1)
                    throw new InvalidDataException("sfl: cue 点数为 0");
                // 第 1 个 cue（24 字节）：dwIdentifier(4) dwPosition(4) dwFccChunk(4)
                // dwChunkStart(4) dwBlockStart(4) dwSampleOffset(4)
                // dwPosition 与 dwSampleOffset 同值，取 body[8:12] 的 dwPosition 即可
                stream.ReadExactly(four); // dwIdentifier（cue 序号）
                loopStartSample = br.ReadUInt32(); // dwPosition = 循环起点样本数
            }
            else if (four.SequenceEqual("LIST"u8))
            {
                // LIST body 前 4 字节为 adtl，随后是子块序列
                if (stream.Read(four) == 4 && four.SequenceEqual("adtl"u8))
                {
                    long subEnd = bodyPos + 4 + chunkSize - 4;
                    while (stream.Position + 8 <= subEnd)
                    {
                        if (stream.Read(four) != 4)
                            break;
                        uint subSize = br.ReadUInt32();
                        long subBody = stream.Position;
                        if (four.SequenceEqual("ltxt"u8) && subSize >= 8)
                        {
                            stream.ReadExactly(four); // dwIdentifier（对应 cue 序号）
                            loopLengthSample = br.ReadUInt32(); // dwSampleLength = sb[4:8]
                        }
                        stream.Position = subBody + subSize + (subSize & 1);
                    }
                }
            }

            stream.Position = nextChunk;
            if (loopStartSample >= 0 && loopLengthSample >= 0)
                break;
        }

        if (loopStartSample < 0 || loopLengthSample < 0)
            throw new InvalidDataException("sfl: 缺少 cue/ltxt 循环点字段");

        return new SflLoopInfo(loopStartSample, loopLengthSample, DefaultRate);
    }
}
