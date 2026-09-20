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
    /// </summary>
    public long IdleStoppedCount { get; private set; }

    /// <summary>不播之后最多再跑这么久就退订。淡影正常约 0.5s 收敛，这里是兜底。</summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(1.5);

    public void Start()
    {
        _idleSince = -1;
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
        // 同一帧的重复通知直接吞掉（见类型注释）
        if (e is RenderingEventArgs args)
        {
            if (args.RenderingTime == _lastRenderingTime) return;
            _lastRenderingTime = args.RenderingTime;
        }

        // Update 内部按 60Hz 自我节流；被跳过时它什么都不做，我们照旧渲染上一帧 ——
        // 高频屏上画面是「同一帧画两遍」，肉眼不可辨，但平滑弹道稳定在 60Hz。
        _analyzer.Update();

        FrameCount++;
        _render();

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
}
