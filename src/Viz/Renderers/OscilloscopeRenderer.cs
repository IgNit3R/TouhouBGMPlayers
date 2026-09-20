using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ThbgmPlayer.Viz.Renderers;

/// <summary>
/// B · 立体声示波器：<b>上下分屏</b>，上半 L、下半 R（用户 2026-09-20 定，替代最初的叠加显示）。
///
/// 逐项对齐验证页 <c>.workbuddy/viz-demo/index.html</c> 的 B 区，几个容易写错的点：
/// <list type="bullet">
/// <item><b>时域缓冲 2048 帧，只画末尾 12ms。</b><c>win = floor(0.012 × sr)</c>（44100 → 529），
///   从 <c>n - win</c> 开始取。不是画全部 2048 —— 那会糊成一片。</item>
/// <item><b>横向抽样比是「每 2 个物理像素一点」</b>（参照页 <c>cols = floor(W/2)</c>，
///   那边的 W 已是物理像素）。所以这里要 <c>× DpiScale</c> 再除 2，不能直接除 2。</item>
/// <item><b>未播放时整条波形的 y 乘 0</b>（参照页 <c>*(playing?1:0)</c>）→ 画成一条直线。
///   注意是「归零」而不是「冻结在最后一帧」，这是定稿语义的一部分。</item>
/// </list>
///
/// 与 canvas 的一处刻意差异：线端与折角用 <c>Round</c>，canvas 默认是 <c>butt/miter</c>。
/// 波形折线的尖角在 miter 下会甩出细刺，Round 明显更干净 —— 这不需要靠调参试出来。
/// </summary>
public sealed class OscilloscopeRenderer : IVizRenderer
{
    /// <summary>时域窗口长度：12ms（参照页 <c>0.012</c>）。</summary>
    private const double WindowSeconds = 0.012;

    /// <summary>波形线宽。参照页 <c>d*1.6</c> 个 canvas 单位，折算回 CSS px 就是 1.6（见 VizStyle 的 DPI 说明）。</summary>
    private const double TraceThickness = 1.6;

    /// <summary>声道标签字号（参照页 <c>10*d</c>）。</summary>
    private const double LabelSize = 10.0;

    /// <summary>标签左边距（参照页 <c>6*d</c>）。</summary>
    private const double LabelLeft = 6.0;

    /// <summary>标签基线相对半区顶部的距离（参照页 <c>yT + 13*d</c>，canvas 的 y 是基线）。</summary>
    private const double LabelBaseline = 13.0;

    /// <summary>波形上下留白（参照页 <c>hh/2 - 6*d</c>）。</summary>
    private const double VerticalPadding = 6.0;

    /// <summary>横向抽样：每几个物理像素取一个点（参照页 <c>cols = floor(W/2)</c>）。</summary>
    private const double PixelsPerColumn = 2.0;

    private static readonly Typeface MonoFace = new("Consolas");

    // 波形几何：**复用**（每帧 Clear + 重新 Open），刻意不冻结 —— 冻结了就不能再改。
    //
    // 为什么值得单独写一段：方案 §二 那张渲染路线表里，`Canvas + Polyline/Path` 被判「每帧分配几何」
    // 而否掉，我们选了 `DrawingVisual + RenderOpen`。但**「免元素分配」不等于「零分配」** ——
    // 几何对象本身若每帧新建，照样是「每帧分配几何」。
    //
    // ⚠️ 实测结论（自检「每帧分配字节数」）：**复用省下的很有限** —— 这一块量出来仍是
    // 17.7KB/帧，说明 `StreamGeometry.Clear()` 把内部点缓冲也放掉了，重新填点要重新分配；
    // 复用挣到的只是包装对象本身（几百字节）。这笔开销是「每帧重画一条折线」的固有代价，
    // 除非改画法（例如原地改点，公开 API 做不到），否则压不下去。故保留复用但不指望它省钱。
    private StreamGeometry? _geoWaveL;
    private StreamGeometry? _geoWaveR;

    // 缓存的绘制资源，随 DPI 变化重建
    private double _cachedDpi = -1;
    private Pen? _penGrid;
    private Pen? _penTrack;
    private Pen? _penL;
    private Pen? _penR;
    private FormattedText? _textL;
    private FormattedText? _textR;

    public void Render(DrawingContext dc, VizFrame f, Size size, VizStyle style)
    {
        double w = size.Width;
        double h = size.Height;
        if (w <= 1 || h <= 1) return;   // 还没排完版，画了也是白画

        EnsureResources(style);

        dc.DrawRectangle(style.Background, null, new Rect(0, 0, w, h));

        // 上下分屏的主分割线（参照页那块 lineWidth=1 的 #2E2E31）
        double half = h / 2;
        dc.DrawLine(_penTrack!, new Point(0, half), new Point(w, half));

        int n = VizFrame.WaveFrames;

        int win = (int)(WindowSeconds * f.SampleRate);
        if (win > n) win = n;
        if (win < 2) win = 2;

        int cols = (int)(w * style.DpiScale / PixelsPerColumn);
        if (cols < 2) cols = 2;

        // 半区中心到峰值的距离。面板压得很扁时这个值会趋近 0 甚至为负 ——
        // 夹到 0 让它退化成一条直线，而不是把波形画反。
        double amp = half / 2 - VerticalPadding;
        if (amp < 0) amp = 0;

        for (int ch = 0; ch < 2; ch++)
        {
            float[] td = ch == 0 ? f.WaveL : f.WaveR;
            double top = ch == 0 ? 0 : half;
            double center = top + half / 2;

            // 本半区中线（参照页 lineWidth=1 的 #3F3F45）
            dc.DrawLine(_penGrid!, new Point(0, center), new Point(w, center));

            // 声道标签：canvas 的 fillText 给的是基线，WPF 的 DrawText 要左上角，差一个字号
            dc.DrawText(ch == 0 ? _textL! : _textR!,
                        new Point(LabelLeft, top + LabelBaseline - LabelSize));

            // 几何复用：Clear 掉上一帧的折线再重画，不新建对象。
            // （原来是先攒进一个 List<Point> 再建几何 —— List 虽然复用，几何本身每帧新建。）
            StreamGeometry geo = ch == 0
                ? (_geoWaveL ??= new StreamGeometry())
                : (_geoWaveR ??= new StreamGeometry());
            geo.Clear();

            using (StreamGeometryContext g = geo.Open())
            {
                for (int i = 0; i <= cols; i++)
                {
                    int idx = n - win + (int)((double)win * i / cols);
                    if (idx < 0) idx = 0;
                    else if (idx >= n) idx = n - 1;

                    // ⚠️ **不许在这里看 `f.Active`**：`WaveL/WaveR` 已经由分析器按淡出进度
                    // 缩放好了（暂停 → 逐帧缩小到 0，终止 → 直接全 0），照画即可。
                    // 曾经这里写的是 `f.Active ? td[idx] : 0.0` —— 结果是**暂停瞬间把波形直接拍成直线**，
                    // 淡出对这块面板完全失效，观感上就是「衰减没多久突然往小跳一下」。
                    // 自检里有一条专项（B 淡影）盯着它。
                    double y = td[idx];
                    double x = (double)i / cols * w;
                    var p = new Point(x, center - y * amp);

                    if (i == 0) g.BeginFigure(p, false, false);
                    else g.LineTo(p, true, false);
                }
            }

            dc.DrawGeometry(null, ch == 0 ? _penL! : _penR!, geo);
        }
    }

    // ------------------------------------------------------------------ 资源

    private void EnsureResources(VizStyle style)
    {
        double dpi = style.DpiScale > 0 ? style.DpiScale : 1.0;
        if (_penL is not null && Math.Abs(_cachedDpi - dpi) < 1e-9) return;
        _cachedDpi = dpi;

        // 中线 / 分割线是「1 物理像素」→ 用 HairLine 折算；波形是 CSS px → 直接用数字
        _penGrid = MakePen(style.Grid, style.HairLine);
        _penTrack = MakePen(style.Track, style.HairLine);
        _penL = MakePen(style.LineL, TraceThickness);
        _penR = MakePen(style.LineR, TraceThickness);

        _textL = MakeText("L", style.Label, dpi);
        _textR = MakeText("R", style.Label, dpi);
    }

    private static Pen MakePen(Brush brush, double thickness)
    {
        var p = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        if (p.CanFreeze) p.Freeze();
        return p;
    }

    private static FormattedText MakeText(string s, Brush brush, double pixelsPerDip) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            MonoFace, LabelSize, brush, pixelsPerDip);
}
