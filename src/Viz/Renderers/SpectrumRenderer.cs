using System.Windows;
using System.Windows.Media;

namespace ThbgmPlayer.Viz.Renderers;

/// <summary>
/// A · 频谱条：52 根**对数**频柱（20Hz–20kHz），**无峰值帽**（用户 2026-09-20 定稿）。
///
/// 逐项对齐验证页 <c>.workbuddy/viz-demo/index.html</c> 的 <c>specDraw()</c>。几处必须记住的点：
/// <list type="bullet">
/// <item><b>柱高不在这里算。</b>参照页的 <c>v[b] += (tg-v[b])*…</c> 是跨帧递推，已经由
///   <see cref="VizAnalyzer"/> 算好放在 <see cref="VizFrame.Bars"/> 里。渲染器只读成品值 ——
///   自己再平滑一遍等于两道弹道串联，手感会变（「渲染器无状态」这条纪律的由来）。</item>
/// <item><b>频带划分（哪几个 bin 归哪根柱）也在分析器里</b>，同理由。</item>
/// <item><b>参照页里那几个「像素」是物理像素，不是 DIP。</b>它写的是不带 <c>d</c> 的字面量
///   （<c>SH-4</c> / <c>bw-2</c> / <c>fillRect(0,SH-2,SW,1)</c>），而画布坐标就是物理像素，
///   所以要乘 <see cref="VizStyle.HairLine"/> 折算。柱高那条 <c>SH-8</c> 也是同样性质。</item>
/// <item><b>不做「最小柱高」补偿。</b>参照页对很小的 v 就是画一条亚像素高的柱 —— 强行拉到
///   1px 会让安静段看起来比参照页更响。</item>
/// </list>
/// </summary>
public sealed class SpectrumRenderer : IVizRenderer
{
    /// <summary>柱子的落地线距底边（参照页 <c>SH-4-h</c> 的 4）。物理像素。</summary>
    private const double BottomInset = 4.0;

    /// <summary>柱高满量程时相对面板总高的收减（参照页 <c>v*(SH-8)</c> 的 8）。物理像素。</summary>
    private const double HeightInset = 8.0;

    /// <summary>柱两侧各留的缝（参照页 <c>b*bw+1</c> 与 <c>max(1, bw-2)</c>）。物理像素。</summary>
    private const double BarGap = 1.0;

    /// <summary>基线距底边（参照页 <c>fillRect(0, SH-2, SW, 1)</c>）。物理像素。</summary>
    private const double BaseLineInset = 2.0;

    private const int Bars = VizFrame.BarCount;

    public void Render(DrawingContext dc, VizFrame f, Size size, VizStyle style)
    {
        double w = size.Width;
        double h = size.Height;
        if (w <= 1 || h <= 1) return;   // 还没排完版，画了也是白画

        dc.DrawRectangle(style.Background, null, new Rect(0, 0, w, h));

        double px = style.HairLine;                // 1 物理像素 = 多少 DIP
        double bottom = h - BottomInset * px;      // 柱子落地线
        double span = h - HeightInset * px;        // 满高（v=1 时的柱高）
        if (span < 0) span = 0;

        double slot = w / Bars;                    // 单根柱占的槽宽（DIP；比例无量纲，不必折算）
        double gap = BarGap * px;
        double barW = slot - 2 * gap;
        if (barW < px) barW = px;                  // 参照页的 max(1, bw-2)

        for (int b = 0; b < Bars; b++)
        {
            double v = f.Bars[b];
            if (v <= 0) continue;                  // 高 0 的柱本来就不可见，跳过省事
            if (v > 1) v = 1;

            double bh = v * span;
            if (bh <= 0) continue;
            dc.DrawRectangle(style.Bar, null, new Rect(b * slot + gap, bottom - bh, barW, bh));
        }

        // 底边基线：参照页画在 SH-2 处、1 物理像素高，横跨整宽
        dc.DrawRectangle(style.Grid, null, new Rect(0, h - BaseLineInset * px, w, px));
    }
}
