using NAudio.Wave;

namespace ThbgmPlayer.Audio;

/// <summary>
/// 音频源：只负责「从原始游戏文件里按字节偏移读出裸 PCM」。
/// 不管循环、不管播放、不做任何解包 —— 全程 seek + read（DESIGN_v3.md §1）。
///
/// 目前只有一个实现 <see cref="PcmFileSource"/>：ZWAV 的 thbgm.dat 与 TH06 的 wav
/// 在读取层完全一样，差别只在文件路径怎么拼，由 <see cref="AudioSourceFactory"/> 处理。
///
/// 日后接 tf 侧时只要再实现本接口即可（TfpkSource / OggSource），播放内核无需改动。
/// </summary>
public interface IAudioSource : IDisposable
{
    /// <summary>原始 PCM 格式。绝大多数是 44100/16bit/2ch，TH13 灵界版是 22050。</summary>
    WaveFormat Format { get; }

    /// <summary>intro 段字节数，相对音轨起点。</summary>
    long IntroBytes { get; }

    /// <summary>整轨字节数（= intro + loop），相对音轨起点。</summary>
    long TotalBytes { get; }

    /// <summary>当前读指针，相对音轨起点。</summary>
    long PositionBytes { get; }

    /// <summary>定位到相对音轨起点的字节偏移。自动夹取范围并对齐到帧边界。</summary>
    void Seek(long offsetFromTrackStart);

    /// <summary>读裸 PCM，最多读到音轨末尾。返回实际读到的字节数。</summary>
    int Read(byte[] buffer, int offset, int count);
}
