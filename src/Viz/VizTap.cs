using NAudio.Wave;

namespace ThbgmPlayer.Viz;

/// <summary>
/// 可视化分接节点（方案 §3.2）：**原样透传**上游样本，顺手拷进环形缓冲。
/// 插在**音量之后、16bit 转换之前**（`PlayerEngine.cs:86`）——
/// 「取样点在音量后」是已拍板的：所见即所听，音量拉到 0 画面跟着归零（§8.1#6）。
///
/// <b>这是音频线程上唯一会跑的新代码。</b>三条纪律：
/// <list type="number">
/// <item><b>零分配</b>：`Read` 里不 new 任何东西（环是构造期预分配的）——
///   音频线程上一处 `new` 就会让播放期间的 GC 持续抖动。</item>
/// <item><b>无锁</b>：单写单读 + `Volatile` 写指针（见 <see cref="VizRing"/>）。
///   音频线程上锁竞争的代价远大于收益。</item>
/// <item><b>返回值原样透传</b>：`IsPlaying` / 「播完」由自己的状态表达，
///   绝不靠返回值 —— 返回 0 会让 <c>WasapiPlayer</c> 判定流结束并触发 <c>PlaybackStopped</c>。</item>
/// </list>
///
/// ⚠️ 它被 <c>PlayerEngine</c> **长期持有**：切输出设备会重建 <c>WasapiPlayer</c>，
/// 但**不重建它** —— 否则环里攒的数据与订阅方（可视化的 <see cref="VizAnalyzer"/>）会一起断掉。
/// </summary>
public sealed class VizTap : ISampleProvider, IVizFeed
{
    /// <summary>
    /// 「还在出声」的判据窗口（毫秒）：音频线程最近这么久内还在拉数据就算在播。
    ///
    /// 为什么用**活动性**而不是让引擎回写一个布尔：这样**没有第二个真相源**。
    /// 暂停时播放线程不再拉数据 → 窗口过后自动变 false → 画面走「淡影」（方案 §2 / M4 的语义）；
    /// 切设备那一下的停顿只有几毫秒，越不出这个窗口，所以不会闪。
    /// </summary>
    private const long ActiveWindowMs = 250;

    private readonly ISampleProvider _src;
    private readonly VizRing _ring = new();
    private readonly int _channels;

    /// <summary>延迟对齐的帧数偏移。**可改**（设置里改了要立即生效，不必重启）。</summary>
    private int _latencyFrames;

    private long _lastPullMs;
    private volatile bool _disposed;

    /// <param name="src">上游节点（引擎里是音量节点）。</param>
    /// <param name="latencyOffsetMs">延迟对齐偏移（毫秒）。取值见 <c>AppSettings.Viz.LatencyOffsetMs</c>。</param>
    public VizTap(ISampleProvider src, double latencyOffsetMs)
    {
        _src = src ?? throw new ArgumentNullException(nameof(src));
        _channels = src.WaveFormat.Channels > 0 ? src.WaveFormat.Channels : 2;
        _latencyFrames = VizRing.MsToFrames(latencyOffsetMs, src.WaveFormat.SampleRate);
    }

    // ------------------------------------------------------------------ 音频链

    public WaveFormat WaveFormat => _src.WaveFormat;

    public int Read(Span<float> buffer)
    {
        int read = _src.Read(buffer);
        if (read <= 0) return read;

        // ⚠️ 按 BlockAlign 向下取整：最多丢掉**凑不满一帧**的尾部样本，
        // 绝不把半帧写进环 —— 那会让之后所有样本的声道全部错位。
        int frames = read / _channels;
        if (frames > 0)
        {
            _ring.Write(buffer.Slice(0, frames * _channels), frames, _channels);
            Volatile.Write(ref _lastPullMs, Environment.TickCount64);
        }

        return read;
    }

    // ------------------------------------------------------------------ IVizFeed

    public int SampleRate => _src.WaveFormat.SampleRate;

    public int Channels => _channels;

    /// <summary>见 <see cref="ActiveWindowMs"/> 的说明：看音频线程最近有没有在拉数据。</summary>
    public bool IsPlaying =>
        !_disposed && Environment.TickCount64 - Volatile.Read(ref _lastPullMs) < ActiveWindowMs;

    public int ReadLatest(Span<float> dstL, Span<float> dstR, int frames) =>
        _ring.ReadLatest(dstL, dstR, frames, _latencyFrames);

    /// <summary>
    /// 改延迟对齐偏移（毫秒）—— 设置里改完**立即生效**，不必重启。
    /// 只在 UI 线程写、UI 线程读（<see cref="ReadLatest"/> 也在 UI 线程），
    /// 而且 int 写入本身是原子的，所以不必加锁。
    /// </summary>
    public void SetLatencyOffset(double ms) =>
        _latencyFrames = VizRing.MsToFrames(ms, _src.WaveFormat.SampleRate);

    /// <summary>引擎释放时调。释放后 <see cref="IsPlaying"/> 恒为 false。</summary>
    public void Dispose() => _disposed = true;
}
