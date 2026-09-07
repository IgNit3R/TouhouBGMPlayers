using System.Buffers.Binary;
using System.IO;
using System.Text;
using NAudio.Wave;

namespace ThbgmPlayer.Audio;

/// <summary>
/// Suica（th075）等来源的「完整 WAV 文件字节」内存音源：解析 RIFF 头取 fmt 与 data 块，
/// 裸 PCM 驻留内存，读取语义 / 循环语义与 <see cref="OggMemorySource"/>、<see cref="PcmFileSource"/> 完全一致。
///
/// 手写 RIFF 遍历而不是走 WaveFileReader：只需要 fmt + data 两个块，几十行就够，
/// 还能容忍末尾截断的 chunk（cue 之类的附属块出问题时不影响音频本体）。
/// 循环点来自内嵌索引的秒值，换算规则见 <see cref="TfMemory.CalcIntroTotal"/>。
/// </summary>
public sealed class WavEntrySource : IAudioSource
{
    private byte[] _data = Array.Empty<byte>();
    private long _position;

    /// <param name="wavBytes">完整的 WAV 文件字节（th075 Suica 条目即合法 WAV）。</param>
    /// <param name="loopStartSec">循环起点秒；null 表示不循环曲。</param>
    /// <param name="loopEndSec">循环终点秒；null 表示循环到曲末。</param>
    public WavEntrySource(byte[] wavBytes, double? loopStartSec, double? loopEndSec)
    {
        ArgumentNullException.ThrowIfNull(wavBytes);
        (int channels, int rate, int bits, long dataOffset, long dataSize) = ParseRiff(wavBytes);

        if (bits != 16)
            throw new NotSupportedException($"目前只支持 16bit PCM，实际是 {bits}bit。");

        Format = new WaveFormat(rate, bits, channels);
        int ba = Format.BlockAlign;

        // 末尾不满一帧的残字节丢弃，保证整轨帧对齐
        long actualBytes = ba > 0 ? dataSize - dataSize % ba : dataSize;
        if (actualBytes <= 0)
            throw new InvalidDataException("WAV 的 data 块为空。");

        (long intro, long total) = TfMemory.CalcIntroTotal(
            actualBytes, ba, Format.AverageBytesPerSecond, loopStartSec, loopEndSec);
        IntroBytes = intro;
        TotalBytes = total;

        _data = TfMemory.Rent((int)actualBytes);
        Buffer.BlockCopy(wavBytes, (int)dataOffset, _data, 0, (int)actualBytes);
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
    /// 遍历 RIFF/WAVE chunk，取 fmt 参数与 data 块位置。
    /// 容错：声明的 chunk 大小越过文件末尾时按剩余字节处理（容忍截断的末块）。
    /// </summary>
    private static (int Channels, int Rate, int Bits, long DataOffset, long DataSize) ParseRiff(byte[] wav)
    {
        if (wav.Length < 12
            || !wav.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            || !wav.AsSpan(8, 4).SequenceEqual("WAVE"u8))
            throw new InvalidDataException("不是合法的 RIFF/WAVE 文件。");

        int channels = 0, rate = 0, bits = 0;
        bool hasFmt = false;
        long dataOffset = -1, dataSize = 0;

        int pos = 12;
        while (pos + 8 <= wav.Length)
        {
            string id = Encoding.ASCII.GetString(wav, pos, 4);
            uint declared = BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(pos + 4));
            int body = pos + 8;
            // 声明大小超出文件时按剩余字节算（容忍截断的末块）
            long size = Math.Min(declared, (long)(wav.Length - body));
            if (size < 0) break;

            if (id == "fmt " && size >= 16 && !hasFmt)
            {
                channels = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(body + 2));
                rate = (int)BinaryPrimitives.ReadUInt32LittleEndian(wav.AsSpan(body + 4));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(body + 14));
                ushort tag = BinaryPrimitives.ReadUInt16LittleEndian(wav.AsSpan(body));
                if (tag != 1)   // 1 = WAVE_FORMAT_PCM
                    throw new NotSupportedException($"不支持的 WAV 编码格式 tag={tag}，只支持 PCM。");
                hasFmt = true;
            }
            else if (id == "data")
            {
                dataOffset = body;
                dataSize = size;
            }

            pos = body + (int)size + ((int)size & 1);   // chunk 按字对齐
        }

        if (!hasFmt || channels <= 0 || rate <= 0 || bits <= 0)
            throw new InvalidDataException("WAV 缺少合法的 fmt 块。");
        if (dataOffset < 0 || dataSize <= 0)
            throw new InvalidDataException("WAV 缺少 data 块。");

        return (channels, rate, bits, dataOffset, dataSize);
    }
}
