using System.IO;
using NAudio.Wave;

namespace ThbgmPlayer.Audio;

/// <summary>
/// 一个音轨的裸 PCM 音源，**整轨驻留内存**。
///
/// ZWAV 的 thbgm.dat（Start 已含 16 字节头）与 TH06 的 wav（Start 是该 wav 的实际头长，
/// 118~166 字节不等，不能假设 44）在读取层完全一样，所以共用一个实现。
///
/// ── 为什么不用流式读 ──
/// 循环播放时每次折返都要把文件指针往回 seek，而那次 seek 之后的第一次读发生在
/// **音频回调内部**。文件以 SequentialScan 打开时，系统会激进释放读过的页面，
/// 于是折返那一下很可能真的去读一次盘 —— 表现为「循环点附近卡滞，中盘没事」。
/// 整轨驻留内存后，折返只是一次 Array.Copy，与磁盘快慢完全无关。
///
/// ── 代价 ──
/// 每首常驻 4–48 MB（中位 20 MB），切曲时一次性读入（约 20–50 ms，热缓存下更快）。
/// 期间音频线程仍在播旧链，所以听不到空档。
/// 缓冲区走了一个「只留最大那块」的复用池，避免每次切曲都去动大对象堆（LOH）
/// 触发 gen2 回收 —— 那反而会造成新的停顿。
/// </summary>
public sealed class PcmFileSource : IAudioSource
{
    private static byte[]? _pooled;
    private static readonly object PoolGate = new();

    private byte[] _data = Array.Empty<byte>();
    private long _position;

    /// <param name="filePath">thbgm.dat 或 th06_NN.wav 的完整路径。</param>
    /// <param name="absoluteStart">音轨数据在文件里的绝对起点（索引里的 Start）。</param>
    /// <param name="introBytes">intro 字节数，相对音轨起点。</param>
    /// <param name="totalBytes">整轨字节数，相对音轨起点。</param>
    public PcmFileSource(string filePath, long absoluteStart, long introBytes, long totalBytes, WaveFormat format)
    {
        IntroBytes = introBytes;
        TotalBytes = totalBytes;
        Format = format;

        if (totalBytes <= 0 || totalBytes > int.MaxValue) return;

        _data = Rent((int)totalBytes);

        try
        {
            // bufferSize = 1：不经过 FileStream 自己的缓冲，直接读进目标数组，省一次拷贝
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                                          FileShare.ReadWrite, 1, FileOptions.SequentialScan);

            long start = Math.Clamp(absoluteStart, 0, Math.Max(0, fs.Length));
            long want = Math.Min(totalBytes, fs.Length - start);

            fs.Position = start;
            int got = 0;
            while (got < want)
            {
                int k = fs.Read(_data, got, (int)(want - got));
                if (k <= 0) break;
                got += k;
            }
            // 文件比索引短时，剩下的保持为 0（静音），不截短时间线
        }
        catch
        {
            // 读不到就静音播放，总比让切曲直接失败好
            Array.Clear(_data, 0, _data.Length);
        }
    }

    public WaveFormat Format { get; }
    public long IntroBytes { get; }
    public long TotalBytes { get; }
    public long PositionBytes => _position;

    /// <summary>定位。纯内存操作，不涉及任何磁盘 IO。</summary>
    public void Seek(long offsetFromTrackStart)
    {
        long p = Math.Clamp(offsetFromTrackStart, 0, TotalBytes);
        int ba = Format.BlockAlign;
        // 绝不停在半帧上，否则左右声道会错位（表现为整体发飘、爆音）
        if (ba > 0) p -= p % ba;
        _position = p;
    }

    public int Read(byte[] buffer, int offset, int count)
    {
        long remain = TotalBytes - _position;
        if (remain <= 0) return 0;

        int n = (int)Math.Min(count, remain);
        if (n > 0) Array.Copy(_data, _position, buffer, offset, n);
        _position += n;
        return n;
    }

    public void Dispose()
    {
        if (_data.Length > 0) Return(_data);
        _data = Array.Empty<byte>();
    }

    /// <summary>取一块至少 size 字节的缓冲区。池里那块够大就直接借出。</summary>
    private static byte[] Rent(int size)
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
    private static void Return(byte[] buf)
    {
        lock (PoolGate)
        {
            if (_pooled is null || _pooled.Length < buf.Length)
                _pooled = buf;
        }
    }
}
