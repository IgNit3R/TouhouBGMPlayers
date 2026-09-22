namespace ThbgmPlayer.Audio;

/// <summary>
/// 整轨波形的**峰值包络**：每 <see cref="FramesPerBucket"/> 帧记一对 min/max。
///
/// 放在 Audio 层而不是 UI 层：它是**扫描的产物**（由 <see cref="TrackScanner"/> 生成），
/// UI 只是消费者。反过来放会让 Audio → UI 形成反向依赖。
///
/// ⚠️ 为什么先量化成固定精度的桶、绘制时再按像素宽度归并（而不是扫描时就按当前宽度算）：
/// 窗口一缩放，按宽度算出来的那份就废了、得重扫整轨；按固定桶存，缩放只是**在内存里重算一步**。
/// 这也顺便定了缩放的**物理上限** —— 放到「1 像素 ≈ 1 桶」就到头了，再往里是空放大
/// （想更深只能把桶存细，见 <see cref="FramesPerBucket"/>）。
///
/// 内存：每桶 2 字节（min + max 各一个 sbyte）⇒ 4 分钟立体声 ≈ 41k 桶 ≈ 82KB。
/// </summary>
public sealed class WaveformPeaks
{
    /// <summary>
    /// 每桶帧数。256 @44.1kHz ≈ 5.8ms —— 够细到看不出"块感"，
    /// 又让一整轨（12M 帧）只占 94KB。改小能让缩放上限更高，代价是内存按比例涨。
    /// </summary>
    public const int FramesPerBucket = 256;

    /// <summary>每桶的最小值。取 PCM16 的**高 8 位**：96px 高的面板用 256 级足够，省一半内存。</summary>
    public sbyte[] Min { get; }

    /// <summary>每桶的最大值。静音桶时 Min == Max == 0（PCM 的中点）。</summary>
    public sbyte[] Max { get; }

    /// <summary>整轨时长（秒）。绘制时靠它把时间换成 x 坐标。</summary>
    public double TotalSeconds { get; }

    /// <summary>
    /// 循环入口位置（秒）。**来自运行时的 <see cref="IAudioSource.IntroBytes"/>**，
    /// 不是查索引 —— 实测 tf 230 曲里有 21 曲（th135）索引里根本没有循环字段。
    /// 0 表示没有 intro（或整轨就是 intro）。
    /// </summary>
    public double IntroSeconds { get; }

    /// <summary>每桶覆盖的秒数（= <see cref="FramesPerBucket"/> / 采样率）。⚠️ 采样率随源变（Ogg 44.1k / 新典 Opus 48k），所以存下来而不是假定。</summary>
    public double SecondsPerBucket { get; }

    public WaveformPeaks(sbyte[] min, sbyte[] max, double totalSeconds, double secondsPerBucket,
                         double introSeconds = 0)
    {
        if (min.Length != max.Length)
            throw new ArgumentException("min / max 长度必须一致", nameof(max));

        Min = min;
        Max = max;
        TotalSeconds = totalSeconds;
        SecondsPerBucket = secondsPerBucket;
        IntroSeconds = introSeconds;
    }

    public int BucketCount => Min.Length;
}
