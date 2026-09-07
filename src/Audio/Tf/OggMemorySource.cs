using System.IO;
using NAudio.Wave;
using NVorbis;

namespace ThbgmPlayer.Audio;

/// <summary>
/// tf 侧内存音源共用的内部设施：缓冲池 + 秒→字节的循环点换算。
/// 缓冲池与 <see cref="PcmFileSource"/> 的「只留最大那块」是同一个思路（那边是 private，
/// 不能复用）——切曲时借一块、Dispose 时归还，只保留最大的那块，避免反复动 LOH。
/// </summary>
internal static class TfMemory
{
    private static byte[]? _pooled;
    private static readonly object PoolGate = new();

    /// <summary>取一块至少 size 字节的缓冲区。池里那块够大就直接借出。</summary>
    public static byte[] Rent(int size)
    {
        lock (PoolGate)
        {
            byte[]? b = _pooled;
            if (b is not null && b.Length >= size)
            {
                _pooled = null;   // 借出去了就不能再给别人
                return b;
            }
        }
        return new byte[size];
    }

    /// <summary>归还缓冲区，只保留最大的那块。</summary>
    public static void Return(byte[] buf)
    {
        lock (PoolGate)
        {
            if (_pooled is null || _pooled.Length < buf.Length)
                _pooled = buf;
        }
    }

    /// <summary>
    /// 按 tf 索引的循环语义换算 IntroBytes / TotalBytes：
    /// <list type="bullet">
    /// <item>loopStartSec 非 null（循环曲）：Intro = 秒×bytesPerSec 四舍五入后向下对齐到帧；
    ///       Total = min(实际 PCM 长度, loopEndSec 换算字节)，loopEndSec 为 null 表示循环到曲末。</item>
    /// <item>loopStartSec 为 null（不循环曲，如 ED / Staff Roll）：Intro = 0、Total = 实际 PCM 长度
    ///       —— loop 段=整曲；是否按 one-shot（一遍停）由调用方（播放/导出）决定，不由源定。</item>
    /// </list>
    /// 两个值都不会越过实际解码长度。
    /// </summary>
    public static (long IntroBytes, long TotalBytes) CalcIntroTotal(
        long actualBytes, int blockAlign, long bytesPerSec,
        double? loopStartSec, double? loopEndSec)
    {
        if (loopStartSec is not double ls)
            return (0, actualBytes);

        long intro = SecondsToBytes(ls, bytesPerSec, blockAlign);
        long total;
        if (loopEndSec is double le)
            total = Math.Min(actualBytes, SecondsToBytes(le, bytesPerSec, blockAlign));
        else
            total = actualBytes;

        // intro 永不越过实际解码长度（循环点在曲末之外时退回曲末）
        return (Math.Clamp(intro, 0, actualBytes), Math.Max(0, total));
    }

    /// <summary>秒 → 字节：先四舍五入再向下对齐到帧边界。</summary>
    private static long SecondsToBytes(double seconds, long bytesPerSec, int blockAlign)
    {
        if (seconds <= 0) return 0;
        long raw = (long)Math.Round(seconds * bytesPerSec);
        return blockAlign > 0 ? raw - raw % blockAlign : raw;
    }
}

/// <summary>
/// 黄昏作（th135+）BGM 的内存音源：OGG 字节 → NVorbis 解码 → 16bit 交错 PCM，整轨驻留内存。
///
/// 容器（tfpk / cga）解出的 OGG 字节不能直接喂播放内核（它只吃裸 PCM、seek+read 模式），
/// 本类型负责在切曲时一次性解码完成，之后折返 / seek 都是纯内存操作，
/// 与 <see cref="PcmFileSource"/> 的读取语义完全一致（DESIGN_v3.md §1）。
///
/// 采样率 / 声道数以解码出的实际值为准（tf 全部 44100 / 2ch，按实际值处理不做假设）。
/// 循环点来自内嵌索引的秒值，换算规则见 <see cref="TfMemory.CalcIntroTotal"/>。
/// </summary>
public sealed class OggMemorySource : IAudioSource
{
    private byte[] _data = Array.Empty<byte>();
    private long _position;

    /// <param name="oggBytes">完整的 OGG 流字节（容器解出的原始内容）。</param>
    /// <param name="loopStartSec">循环起点秒；null 表示不循环曲。</param>
    /// <param name="loopEndSec">循环终点秒；null 表示循环到曲末。</param>
    public OggMemorySource(byte[] oggBytes, double? loopStartSec, double? loopEndSec)
    {
        ArgumentNullException.ThrowIfNull(oggBytes);
        if (oggBytes.Length == 0)
            throw new InvalidDataException("OGG 字节为空，无法解码。");

        // 解码产物先落 MemoryStream（容量按 OGG 头里的总采样数预估），再搬进池化缓冲区。
        // 解码本身没法直接写进最终缓冲：总长只有解完才知道。
        var pcm = new MemoryStream();
        int channels;
        int sampleRate;

        // VorbisPizza 需要显式 Initialize()（NVorbis 旧版是构造时自动初始化）
        using (var vorbis = new VorbisReader(new MemoryStream(oggBytes), true))
        {
            vorbis.Initialize();
            channels = vorbis.Channels;
            sampleRate = vorbis.SampleRate;
            if (channels <= 0 || sampleRate <= 0)
                throw new InvalidDataException($"OGG 流头异常：{sampleRate}Hz / {channels}ch。");

            Format = new WaveFormat(sampleRate, 16, channels);

            // 1 秒的 float 缓冲 + 对应的 byte 暂存（全程复用，不进 LOH）
            var fbuf = new float[sampleRate * channels];
            var bbuf = new byte[fbuf.Length * 2];

            // ⚠️ 语义差异：VorbisPizza 的 ReadSamples(Span) 返回的是【帧数】，
            //    不是写入的浮点个数（旧 NVorbis 返回的是浮点个数）。
            //    按帧数换算成浮点再交给 FloatToPcm16 / 写 PCM，否则会只解出一半。
            int frames;
            while ((frames = vorbis.ReadSamples(fbuf.AsSpan())) > 0)
            {
                int floats = frames * channels;
                FloatToPcm16(fbuf, floats, bbuf);
                pcm.Write(bbuf, 0, floats * 2);
            }
        }

        long actualBytes = pcm.Length;
        if (actualBytes > int.MaxValue)
            throw new InvalidDataException("解码后的 PCM 超出 2GB 上限。");

        (long intro, long total) = TfMemory.CalcIntroTotal(
            actualBytes, Format.BlockAlign, Format.AverageBytesPerSecond, loopStartSec, loopEndSec);
        IntroBytes = intro;
        TotalBytes = total;

        if (actualBytes > 0)
        {
            _data = TfMemory.Rent((int)actualBytes);
            Buffer.BlockCopy(pcm.GetBuffer(), 0, _data, 0, (int)actualBytes);
        }
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

    /// <summary>float [-1,1] → 16bit LE 交错 PCM，越界值 clamp。</summary>
    private static void FloatToPcm16(float[] src, int count, byte[] dest)
    {
        for (int i = 0; i < count; i++)
        {
            float f = src[i];
            if (f < -1f) f = -1f;
            else if (f > 1f) f = 1f;
            short v = (short)MathF.Round(f * 32767f);
            dest[2 * i] = (byte)v;
            dest[2 * i + 1] = (byte)(v >> 8);
        }
    }
}
