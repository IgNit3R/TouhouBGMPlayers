using System.Buffers.Binary;
using System.IO;
using Concentus;
using NAudio.Wave;

namespace ThbgmPlayer.Audio;

/// <summary>
/// 東方紅魔郷 新典（TH06NC）BGM 的内存音源：裸 Opus 包自定义容器 → Concentus 解码 →
/// 16bit 交错 PCM（48kHz / 2ch / 20ms 帧），整轨驻留内存。
///
/// 容器布局（**不是** Ogg / RIFF，不能用现成容器解析器）：
/// <code>
/// 文件 = 40 字节头 + N × 488 字节定长记录，N = (filesize - 40) / 488（恒为整数）
/// 第 i 条记录 = file[40 + i*488 : 40 + (i+1)*488]
///   [0:4]   大端 u32 = 480  包长，仅作校验
///   [4:8]   随机杂散字节，忽略
///   [8:488] 480 字节裸 Opus 包  ← 只取这 480 字节喂解码器
/// 每包解出固定 960 帧（48kHz / 20ms / 立体声）
/// </code>
///
/// ⚠️ 必须**恰好**喂 480 字节：多喂会让 Opus range coder 从包尾解起，样本数对但内容全错。
/// 容器内没有 OpusHead，故 pre-skip = 0、无 end trimming，采样率只能是 48000。
///
/// 读取语义 / 循环语义与 <see cref="OggMemorySource"/>、<see cref="PcmFileSource"/> 完全一致
/// （DESIGN_v3.md §1）：切曲时一次性解码完，之后 seek / read 都是纯内存操作。
/// 循环点是**整数样本**（索引已换算并钳制），按 4 字节/帧直接换算成字节。
/// </summary>
public sealed class OpusMemorySource : IAudioSource
{
    /// <summary>文件头字节数。</summary>
    private const int HeaderSize = 40;
    /// <summary>单条定长记录字节数。</summary>
    private const int RecordSize = 488;
    /// <summary>记录内 Opus 包起始偏移。</summary>
    private const int PayloadOffset = 8;
    /// <summary>单包字节数（也是记录头 [0:4] 声明的值）。</summary>
    private const int PayloadSize = 480;
    /// <summary>单包解码出的帧数（48kHz / 20ms）。</summary>
    private const int SamplesPerRecord = 960;
    /// <summary>本输出格式下每帧字节数（16bit × 2ch）。</summary>
    private const int BytesPerFrame = 4;

    private const int SampleRate = 48000;
    private const int Channels = 2;

    private byte[] _data = Array.Empty<byte>();
    private long _position;

    /// <param name="opusBytes">完整的自定义容器字节（读自 data\bgm\th06_NN.opus 或 data\bgm2\...）。</param>
    /// <param name="loopStartSample">循环起点样本（48kHz）；null 表示不循环曲。</param>
    /// <param name="loopEndSample">循环终点样本；null 表示循环到曲末（les == ds 时循环到曲末）。</param>
    public OpusMemorySource(byte[] opusBytes, long? loopStartSample, long? loopEndSample)
    {
        ArgumentNullException.ThrowIfNull(opusBytes);
        if (opusBytes.Length < HeaderSize)
            throw new InvalidDataException(
                $"新典 Opus 容器过小（{opusBytes.Length} 字节），缺少 {HeaderSize} 字节文件头。");

        long bodyBytes = opusBytes.Length - HeaderSize;
        long recordCount = bodyBytes / RecordSize;
        if (bodyBytes % RecordSize != 0)
            throw new InvalidDataException(
                $"新典 Opus 容器长度异常：{opusBytes.Length} 字节，(len-{HeaderSize})%{RecordSize} = {bodyBytes % RecordSize}（应为 0）。");
        if (recordCount <= 0)
            throw new InvalidDataException("新典 Opus 容器不含任何记录。");

        Format = new WaveFormat(SampleRate, 16, Channels);

        // 每包恒为 960 帧，总帧数在解码前即可算出，直接按精确长度租缓冲、原位写 PCM。
        long actualBytes = recordCount * SamplesPerRecord * BytesPerFrame;
        if (actualBytes > int.MaxValue)
            throw new InvalidDataException("解码后的 PCM 超出 2GB 上限。");

        byte[] buf = TfMemory.Rent((int)actualBytes);
        try
        {
            DecodeAll(opusBytes, recordCount, buf);
        }
        catch
        {
            TfMemory.Return(buf);   // 解码失败不得把半成品留在池里
            throw;
        }
        _data = buf;

        // 循环点：整数样本 × 4 字节/帧。intro 永不越过实际解码长度；loopEnd 缺省=曲末。
        long intro = loopStartSample is long lss
            ? Math.Clamp(lss * BytesPerFrame, 0, actualBytes)
            : 0;
        long total = loopEndSample is long les
            ? Math.Clamp(les * BytesPerFrame, 0, actualBytes)
            : actualBytes;
        IntroBytes = intro;
        TotalBytes = total;
    }

    public WaveFormat Format { get; }
    public long IntroBytes { get; }
    public long TotalBytes { get; }
    public long PositionBytes => _position;

    /// <summary>定位。纯内存操作，自动夹取范围并对齐到帧边界。</summary>
    public void Seek(long offsetFromTrackStart)
    {
        long p = Math.Clamp(offsetFromTrackStart, 0, TotalBytes);
        int ba = Format.BlockAlign;
        // 绝不停在半帧上，否则左右声道会错位（同 PcmFileSource）
        if (ba > 0) p -= p % ba;
        _position = p;
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        long remain = TotalBytes - _position;
        if (remain <= 0) return 0;

        int n = (int)Math.Min(count, remain);
        if (n > 0) Buffer.BlockCopy(_data, (int)_position, buffer, offset, n);
        _position += n;
        return n;
    }

    public void Dispose()
    {
        if (_data.Length > 0) TfMemory.Return(_data);
        _data = Array.Empty<byte>();
    }

    /// <summary>
    /// 逐记录解码：跳过 40 字节头 → 取 [8:488] 的 480 字节裸包 → Concentus 解 960 帧
    /// → 原地写 16bit LE 交错 PCM。记录头声明不是 480 时立即报错（明确失败优于解出垃圾）。
    /// </summary>
    private static void DecodeAll(byte[] opusBytes, long recordCount, byte[] dest)
    {
        // Concentus 2.x 的推荐入口（new OpusDecoder 已标记过时，会触发构建警告）。
        // 这个包只有纯托管实现，返回的就是 Concentus.Structs.OpusDecoder。
        IOpusDecoder decoder = OpusCodecFactory.CreateDecoder(SampleRate, Channels);
        try
        {
            var packet = new short[SamplesPerRecord * Channels];
            int write = 0;

            for (long i = 0; i < recordCount; i++)
            {
                int recOff = HeaderSize + (int)(i * RecordSize);

                uint declared = BinaryPrimitives.ReadUInt32BigEndian(opusBytes.AsSpan(recOff, 4));
                if (declared != PayloadSize)
                    throw new InvalidDataException(
                        $"新典 Opus 第 {i} 条记录的包长声明为 {declared}，应为 {PayloadSize}（容器损坏或版本不符）。");

                int got = decoder.Decode(
                    opusBytes.AsSpan(recOff + PayloadOffset, PayloadSize), packet, SamplesPerRecord, false);
                if (got != SamplesPerRecord)
                    throw new InvalidDataException(
                        $"新典 Opus 第 {i} 条记录解出 {got} 帧，应为 {SamplesPerRecord}（容器损坏或版本不符）。");

                for (int k = 0; k < got * Channels; k++)
                {
                    short v = packet[k];
                    dest[write] = (byte)v;
                    dest[write + 1] = (byte)(v >> 8);
                    write += 2;
                }
            }
        }
        finally
        {
            (decoder as IDisposable)?.Dispose();
        }
    }
}
