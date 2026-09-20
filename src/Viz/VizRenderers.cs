using ThbgmPlayer.Viz.Renderers;

namespace ThbgmPlayer.Viz;

/// <summary>
/// 四块面板的渲染器实例。**两处宿主共用同一份**。
///
/// 为什么必须是同一份：<b>D 的余辉是渲染器侧状态</b>（最近 20 帧的轨迹）。
/// 两处宿主各持一份的话，「窗口化 ↔ 主窗口最大化」每切一次，团雾就从零重来 ——
/// 表现为「一最大化，画面就空一下」。
///
/// A / B / C 本身是无状态的（方案 §3.4），共享它们没有副作用，所以四块一起共享，
/// 不再区分「有状态的那一块单独处理」—— 少一条特例。
///
/// ⚠️ 面板开关（哪几块真的画）不在这里：那是**每块宿主各自**的事，
/// 见 <see cref="VizSurfaceHost.ApplyPanelVisibility"/>。这里只管**实例**。
/// </summary>
public sealed class VizRenderers
{
    public IVizRenderer Spectrum { get; } = new SpectrumRenderer();

    public IVizRenderer Oscilloscope { get; } = new OscilloscopeRenderer();

    public IVizRenderer LevelPhase { get; } = new LevelPhaseRenderer();

    /// <summary>唯一带状态的一块 —— 共享它正是这个类存在的理由。</summary>
    public IVizRenderer Lissajous { get; } = new LissajousRenderer();
}
