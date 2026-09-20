using System.Windows;
using System.Windows.Media;

namespace ThbgmPlayer.Viz;

/// <summary>
/// 一块可视化面板的画法。
///
/// <b>必须无状态</b>（方案 §3.4）：所有跨帧的东西（平滑、余辉、峰值保持）都已经在
/// <see cref="VizAnalyzer"/> 里算完，渲染器只做「读帧 → 画」。这样五个面板才能
/// 共享同一帧、各自独立重绘，而不会互相干扰。
///
/// 唯一的例外是 D 利萨如的余辉 —— 那是**画布级**的累加，WPF 的 <c>DrawingVisual</c>
/// 每帧重绘、留不住上一帧。它的做法在 M2 落地时单独定（方案里已标注为待解决项）。
/// </summary>
public interface IVizRenderer
{
    /// <summary>
    /// 画一帧。
    /// </summary>
    /// <param name="dc">绘制上下文。坐标系原点在面板左上角，单位 DIP，尺寸见 <paramref name="size"/>。</param>
    /// <param name="frame">本帧分析结果（复用对象，不要缓存引用）。</param>
    /// <param name="size">面板可用尺寸（DIP）。已经扣掉边框与内边距。</param>
    /// <param name="style">观感资源。渲染器不得自己 new 颜色。</param>
    void Render(DrawingContext dc, VizFrame frame, Size size, VizStyle style);
}
