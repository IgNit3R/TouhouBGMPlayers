using System.Diagnostics;
using System.Windows.Media;

namespace ThbgmPlayer.Viz;

/// <summary>
/// 渲染节拍：把 <c>CompositionTarget.Rendering</c> 转成「分析一帧 → 重绘一次」的循环。
///
/// 为什么不用 <c>DispatcherTimer</c>：那是**定时器**节拍，和显示器的刷新没有关系 ——
/// 要么在两次刷新之间白跑一帧（画面撕裂/抖动），要么跟不上刷新导致掉帧感。
/// <c>CompositionTarget.Rendering</c> 是 WPF 暴露的**帧回调**，由 DWM 的 vsync 驱动，
/// 天然与刷新率对齐（方案 §2「绘制跟 vsync」）。
///
/// ⚠️ 这个事件有个老毛病：**同一帧可能被通知多次**（历史上 WPF 会在渲染通道的每个
/// 阶段都 raise 一遍）。拿 <see cref="RenderingEventArgs.RenderingTime"/> 去重是标准对策，
/// 不去重的话 60Hz 显示器上实际会跑成 120Hz，白烧一倍 CPU。
///
/// <b>退订策略（方案 §3.5 / R2「绝不让它常驻空转」）</b>：不播了就**自己退订**，
/// 停止条件 = 「淡影收敛到位」或「撞上 <see cref="IdleTimeout"/> 硬上限」，判定见
/// <see cref="ShouldStop"/>（纯函数，自检直接驱动）。
/// **重新起搏由窗口负责** —— 会让画面重新动起来的事件（起播 / 换源 / 取消暂停）
/// 本来就都发生在窗口侧，这里不需要反向通知。
/// </summary>
public sealed class VizPump : IDisposable
{
    private readonly VizAnalyzer _analyzer;
    private readonly Action _render;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private TimeSpan _lastRenderingTime = TimeSpan.MinValue;
    private bool _running;
    private double _idleSince = -1;      // 进入「不播」状态的时刻；-1 = 当前不在该状态

    /// <param name="analyzer">取数与分析。</param>
    /// <param name="render">重绘回调（在 UI 线程上，跟着帧节拍跑）。</param>
    public VizPump(VizAnalyzer analyzer, Action render)
    {
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _render = render ?? throw new ArgumentNullException(nameof(render));
    }

    /// <summary>是否已在跑。重复 <see cref="Start"/> 是安全的（不会重复订阅）。</summary>
    public bool IsRunning => _running;

    /// <summary>已跑过的帧数（去重之后）。诊断用。</summary>
    public long FrameCount { get; private set; }

    /// <summary>
    /// 因为「落定」而自己退订的次数。<b>§8.1#4「暂停后 CPU 回落」这条验收就靠它举证</b>
    /// —— 只盯着任务管理器说「CPU 好像降了」是不算证据的。
    ///
    /// ⚠️ 它也是「掉帧感」的头号嫌疑的判据：**正常播放时它不该涨**。
    /// 涨了说明音频侧出现过 &gt;250ms 的断续（见 docs 里那条待查档）。
    /// </summary>
    public long IdleStoppedCount { get; private set; }

    // ------------------------------------------------------------------ 帧耗时统计
    //
    // 用途只有一个：**分诊** —— 有「掉帧感」时一眼看出是「分析慢」「绘制慢」还是
    // 「节拍自己停了又起」（IdleStoppedCount）。所以只记平均 + 峰值，不记分布：
    // 要的是判断走哪条路，不是出一份性能报告。
    // 见 docs/2026-09-21-viz-frame-pacing-pending.md。

    private const double StatsSmoothing = 0.05;

    /// <summary>tick → 毫秒。⚠️ 不能写成 <c>const</c>（<c>Stopwatch.Frequency</c> 不是编译期常量，CS0133）。</summary>
    private static readonly double MsPerTick = 1000.0 / Stopwatch.Frequency;

    private TimeSpan _lastIntervalTime = TimeSpan.MinValue;

    /// <summary>相邻两帧的间隔（毫秒）。60Hz 屏约 16.7、144Hz 约 6.9。</summary>
    public double IntervalAvgMs { get; private set; }

    /// <summary>
    /// 相邻两帧间隔的峰值（毫秒）。<b>掉帧最直接的证据</b> ——
    /// 它跳到两倍刷新周期就是**实打实丢了一帧**，比 CPU 耗时更能说明"卡了一下"。
    /// </summary>
    public double IntervalPeakMs { get; private set; }

    /// <summary>分析（含 FFT）的平均耗时（毫秒）。</summary>
    public double AnalyzeAvgMs { get; private set; }

    /// <summary>分析（含 FFT）的峰值耗时（毫秒）。</summary>
    public double AnalyzePeakMs { get; private set; }

    /// <summary>绘制（录绘制指令那一整段）的平均耗时（毫秒）。</summary>
    public double RenderAvgMs { get; private set; }

    /// <summary>绘制的峰值耗时（毫秒）。</summary>
    public double RenderPeakMs { get; private set; }

    /// <summary>把三个峰值清零（平均值与计数保留）——「复位 → 复现 → 读数」用。</summary>
    public void ResetPeaks()
    {
        IntervalPeakMs = 0;
        AnalyzePeakMs = 0;
        RenderPeakMs = 0;
    }

    /// <summary>不播之后最多再跑这么久就退订。淡影正常约 0.5s 收敛，这里是兜底。</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(1.5);

    public void Start()
    {
        _idleSince = -1;

        // ⚠️ 一起复位"上一帧时刻"：退订期间过掉的时间不该被算成"一帧卡了 3 秒"。
        // 这个字段同时被去重逻辑用（同一帧的重复通知要吞掉），复位的语义是一致的。
        _lastRenderingTime = TimeSpan.MinValue;
        _lastIntervalTime = TimeSpan.MinValue;

        if (_running) return;
        _running = true;
        CompositionTarget.Rendering += OnRendering;
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    public void Dispose() => Stop();

    /// <summary>
    /// 退订判定（纯函数，自检直接扫参数）。
    /// <list type="bullet">
    /// <item>**还在播 → 永远不停止。**</item>
    /// <item>不播了 → 淡影收敛到位就停；没收敛就继续跑，但最多等
    ///   <paramref name="idleTimeoutSeconds"/>。</item>
    /// </list>
    /// 硬上限是必须的：万一某个量卡在收敛阈值外（浮点异常、数据异常），不能让帧回调永远跑下去。
    /// </summary>
    internal static bool ShouldStop(bool active, bool settled, double idleSeconds, double idleTimeoutSeconds)
        => !active && (settled || idleSeconds >= idleTimeoutSeconds);

    private void OnRendering(object? sender, EventArgs e)
    {
        // 同一帧的重复通知直接吞掉（见类型注释）。
        // ⚠️ 顺手把合成时刻存进局部变量：模式变量 args 只在 if 块内确定赋值，
        // 后面（Accumulate 那行）是够不着的（CS0165 就是这么来的）。
        TimeSpan? renderingTime = null;
        if (e is RenderingEventArgs args)
        {
            if (args.RenderingTime == _lastRenderingTime) return;
            _lastRenderingTime = args.RenderingTime;
            renderingTime = args.RenderingTime;
        }

        // Update 内部按 60Hz 自我节流；被跳过时它什么都不做，我们照旧渲染上一帧 ——
        // 高频屏上画面是「同一帧画两遍」，肉眼不可辨，但平滑弹道稳定在 60Hz。
        long t0 = Stopwatch.GetTimestamp();
        _analyzer.Update();
        long t1 = Stopwatch.GetTimestamp();

        FrameCount++;
        _render();

        Accumulate(t1 - t0, Stopwatch.GetTimestamp() - t1, renderingTime);

        // ⚠️ 顺序要紧：**先画再判**。最后一帧得把落定后的画面画出去，
        // 否则画面会停在「还差一点」的样子上，而节拍已经退订、再也没人来补这一帧。
        var frame = _analyzer.Frame;
        double now = _clock.Elapsed.TotalSeconds;

        if (frame.Active)
        {
            _idleSince = -1;
            return;
        }

        if (_idleSince < 0)
        {
            _idleSince = now;      // 刚进入「不播」：先让它跑起来，下一帧再判
            return;
        }

        if (ShouldStop(frame.Active, _analyzer.IsSettled, now - _idleSince, IdleTimeout.TotalSeconds))
        {
            Stop();
            IdleStoppedCount++;
        }
    }

    /// <summary>
    /// 收一笔统计。**指数滑动平均**（不存历史、零分配）。
    ///
    /// ⚠️ 连计时本身也要顾虑开销：`Stopwatch.GetTimestamp()` 是几十纳秒级的，一帧两次可忽略；
    /// 但**不要**在这里加环缓冲或做分布统计 —— 那就变成「为了量它而拖慢它」，
    /// 而这段代码跑在每帧路径上。
    /// </summary>
    private void Accumulate(long analyzeTicks, long renderTicks, TimeSpan? renderingTime)
    {
        double analyze = analyzeTicks * MsPerTick;
        double render = renderTicks * MsPerTick;

        AnalyzeAvgMs += (analyze - AnalyzeAvgMs) * StatsSmoothing;
        RenderAvgMs += (render - RenderAvgMs) * StatsSmoothing;

        if (analyze > AnalyzePeakMs) AnalyzePeakMs = analyze;
        if (render > RenderPeakMs) RenderPeakMs = render;

        // 帧距：用 DWM 给的**合成时刻**算，而不是本地时钟 —— 那才是「这一帧什么时候该上屏」。
        if (renderingTime is TimeSpan now)
        {
            bool hasPrevious = _lastIntervalTime != TimeSpan.MinValue;
            double interval = hasPrevious ? (now - _lastIntervalTime).TotalMilliseconds : 0;

            if (CountInterval(hasPrevious, interval))
            {
                IntervalAvgMs += (interval - IntervalAvgMs) * StatsSmoothing;
                if (interval > IntervalPeakMs) IntervalPeakMs = interval;
            }

            _lastIntervalTime = now;
        }
    }

    /// <summary>
    /// 这个帧距样本要不要计入统计（纯函数，自检直接驱动）。
    ///
    /// 两条边界都不是假想，且**错了不会报错、只会让峰值变成垃圾**，进而把整个分诊带偏：
    /// <list type="bullet">
    /// <item><b>没有上一帧可比</b>（刚 <see cref="Start"/>）：退订期间过掉的时间
    ///   （可能几秒）会被算成「一帧卡了 3 秒」。</item>
    /// <item><b>非正的间隔</b>：时钟回退、或重启边界上合成时刻不单调。</item>
    /// </list>
    /// </summary>
    internal static bool CountInterval(bool hasPrevious, double intervalMs)
        => hasPrevious && intervalMs > 0;
}
