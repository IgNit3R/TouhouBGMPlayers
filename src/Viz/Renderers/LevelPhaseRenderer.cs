using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ThbgmPlayer.Viz.Renderers;

/// <summary>画字时锚点怎么对齐（canvas 的 <c>textAlign</c> 三态）。</summary>
internal enum TextAnchor
{
    Left,
    Center,
    Right,
}

/// <summary>
/// C · L/R 电平表（竖条）+ J · 相位相关（横条挂在下方）—— 一块面板同时画这两个。
///
/// 逐项对齐验证页 <c>.workbuddy/viz-demo/index.html</c> 的 C/J 段。定稿口径：
/// <list type="bullet">
/// <item>量程固定 <b>-36…0 dB</b>（用户定稿：电平从 -36 起步），刻度 -36 / -24 / -18 / -12 / -6 / 0。</item>
/// <item><b>-6 dB 以上是余量区，用 Warn 色</b>（方案 §303 明确保留「-6dB 余量 Warn」）；
///   其余部分是正常柱体 Bar 色。余量区是**颜色分段**，不是峰值保持线。</item>
/// <item><b>没有峰值保持线、没有峰值帽</b>（用户否决）。<see cref="VizFrame.PeakL"/> /
///   <see cref="VizFrame.PeakR"/> 目前无人使用，留着备用。</item>
/// <item>J 的左右半槽**统一灰底**（Track），负半区不做特殊着色（用户定稿：那个黄不要）。</item>
/// </list>
///
/// 单位换算见 <see cref="VizStyle"/> 的长注释：参照页写成 <c>N*d</c> 的是 CSS px → WPF 直接写 N；
/// 写成裸 <c>N</c> 的是物理像素 → 要乘 <see cref="VizStyle.HairLine"/>。本文件里只有刻度线与
/// 中轴线是后者。
///
/// 读数文字用 <see cref="CultureInfo.InvariantCulture"/> 格式化 —— 德语等区域会把小数点写成
/// 逗号，而参照页是 JS 的 <c>toFixed</c>，永远是点号。
/// </summary>
public sealed class LevelPhaseRenderer : IVizRenderer
{
    // ------------------------------------------------------------------ C 的几何（参照页全部带 d → 直接是 DIP）

    /// <summary>刻度区顶部（参照页 <c>my0 = 26*d</c>）。</summary>
    private const double TopInset = 26.0;

    /// <summary>刻度区底部距面板底边（参照页 <c>my1 = H - 64*d</c>，给 J 让位）。</summary>
    private const double BottomInset = 64.0;

    /// <summary>单根竖条宽（参照页 <c>mbw = 46*d</c>）。</summary>
    private const double BarWidth = 46.0;

    /// <summary>两根竖条的间距（参照页 <c>mgap = 30*d</c>）。</summary>
    private const double BarGap = 30.0;

    /// <summary>刻度线相对柱体左右各外伸的长度（参照页 <c>bx1-12*d</c> / <c>…+12*d</c>）。</summary>
    private const double ScaleOverhang = 12.0;

    /// <summary>
    /// 刻度数字的右边界（参照页 <c>bx1-16*d</c>，右对齐）。
    /// 与 <see cref="ScaleTextWidth"/> 一起构成「左侧要给刻度数字留多宽」，所以是公开常量。
    /// </summary>
    public const double ScaleNumberRight = 16.0;

    /// <summary>刻度数字相对刻度线的基线偏移（参照页 <c>ty+3*d</c>）。</summary>
    private const double ScaleNumberBaseline = 3.0;

    /// <summary>电平读数在柱顶上方（参照页 <c>my0-8*d</c>）。</summary>
    private const double ReadoutAbove = 8.0;

    /// <summary>L / R 标签在柱底下方（参照页 <c>my1+16*d</c>）。</summary>
    private const double ChannelLabelBelow = 16.0;

    // ------------------------------------------------------------------ J 的几何

    /// <summary>J 横条中心距面板底边（参照页 <c>jy = H-26*d</c>）。</summary>
    private const double PhaseFromBottom = 26.0;

    /// <summary>J 槽半高（参照页 <c>jy±4*d</c> → 总高 8）。</summary>
    private const double PhaseHalfHeight = 4.0;

    /// <summary>J 中轴线的半长（参照页 <c>jy±8*d</c>）。</summary>
    private const double PhaseAxisHalf = 8.0;

    /// <summary>J 刻度数字的基线（参照页 <c>jy+18*d</c>）。</summary>
    private const double PhaseNumberBaseline = 18.0;

    /// <summary>J 读数在横条上方（参照页 <c>jy-12*d</c>）。</summary>
    private const double PhaseReadoutAbove = 12.0;

    /// <summary>J 指针半宽（参照页 <c>jx-1.5*d</c>，总宽 3）。</summary>
    private const double PhasePointerHalfWidth = 1.5;

    /// <summary>J 指针半高（参照页 <c>jy±7*d</c>）。</summary>
    private const double PhasePointerHalfHeight = 7.0;

    // ------------------------------------------------------------------ 量程与字体

    /// <summary>量程下界（dB）。</summary>
    private const double DbMin = -36.0;

    /// <summary>量程跨度（dB）：-36…0。</summary>
    private const double DbSpan = 36.0;

    /// <summary>余量区上界（dB）：这一段以上用 Warn 色。</summary>
    private const double HeadroomDb = -6.0;

    /// <summary>RMS 取对数的地板（参照页 <c>Math.max(1e-4, rms)</c>）→ 读数最低 -80dB，显示成 -inf。</summary>
    private const double RmsFloor = 1e-4;

    private const double FontSize = 11.0;

    /// <summary>
    /// 刻度数字的预留宽度（DIP）。按最长的那个刻度标签（「-36」，三个字符 @ Consolas 11）估。
    /// </summary>
    public const double ScaleTextWidth = 20.0;

    /// <summary>
    /// 展示刻度数字时，柱体最窄能被压到参照页宽度的这个比例。
    /// 再窄就**放弃刻度数字**、把宽度让给柱体 —— 柱顶的 dB 读数仍在，量纲不至于丢。
    /// </summary>
    private const double MinBarScaleWithNumbers = 0.5;

    /// <summary>参照页那套柱体几何的总宽（两根柱 + 一个间距）= 46×2 + 30 = 122。</summary>
    private const double BarBlockFull = 2 * BarWidth + BarGap;

    /// <summary>
    /// <b>与参照页逐像素一致</b>所需的最小面板宽度（DIP）：<c>2 × (16 + 20 + 15 + 46) = 194</c>。
    /// 够这个宽，下面的自适应就是 no-op —— 柱体居中、几何与参照页一字不差。
    ///
    /// 低于它柱子就要等比缩。参照页那块画布本来就 ≥194 宽，所以那边看不出这个问题；
    /// 我们按 7:3 分栏后右列只有 101～170，一直在下面。背景见
    /// <c>docs/2026-09-20-build-40-viz-m2.md</c> §7.5。
    /// </summary>
    public const double ReferenceWidth =
        2 * (ScaleNumberRight + ScaleTextWidth + BarGap / 2 + BarWidth);

    /// <summary>
    /// 还能保住刻度数字的最小面板宽度（DIP）= <c>36 + 12 + 122×0.5 = 109</c>。
    /// 低于它就放弃刻度数字。再往下柱体与刻度线也只是继续变窄，<b>永不越界</b>。
    /// </summary>
    public const double MinWidthWithNumbers =
        ScaleNumberRight + ScaleTextWidth + ScaleOverhang + BarBlockFull * MinBarScaleWithNumbers;

    private static readonly double[] Ticks = { -36, -24, -18, -12, -6, 0 };

    private static readonly Typeface MonoFace = new("Consolas");

    /// <summary>
    /// 会变的数值文字按**字符串**缓存。
    ///
    /// 为什么需要：自检量出的「每帧分配字节数」里，这块面板是四个里最高的 ——
    /// 因为它每帧要新建 3 个 <see cref="FormattedText"/>（两个电平读数 + 一个相关读数），
    /// 而 <c>FormattedText</c> 的构造要走一遍文本排版，不是小开销。
    ///
    /// 缓存天然有界：电平读数只可能是 <c>-inf</c> 或 [-36, 0] 内的 1 位小数（≈360 种），
    /// 相关读数只可能是 [-1, +1] 的 2 位小数（≈200 种）。<see cref="ValueCacheLimit"/> 只是兜底
    /// —— 万一某天读数跑出这个范围（例如削顶后 dB &gt; 0），也不会让缓存无限长。
    /// </summary>
    private readonly Dictionary<string, FormattedText> _valueCache = new();

    /// <summary>缓存条目上限（兜底用）。</summary>
    private const int ValueCacheLimit = 1024;

    // 静态层缓存（键 = 面板尺寸 + DPI —— 底槽与刻度的位置全由它们决定）
    private DrawingGroup? _layer;
    private double _layerW = -1;
    private double _layerH = -1;
    private double _layerDpi = -1;

    /// <summary>DPI 变了要整盘重建（字号、pixelsPerDip 都变了）。</summary>
    private double _cachedDpi = -1;
    private FormattedText[]? _tickTexts;
    private FormattedText? _labelL;
    private FormattedText? _labelR;
    private FormattedText? _phaseNeg;
    private FormattedText? _phaseZero;
    private FormattedText? _phasePos;

    public void Render(DrawingContext dc, VizFrame f, Size size, VizStyle style)
    {
        double w = size.Width;
        double h = size.Height;
        if (w <= 1 || h <= 1) return;

        EnsureResources(style);

        Metrics m = BuildMetrics(w, h);

        // ---- 静态层：一条指令代掉二十来次绘制 ----
        dc.DrawDrawing(StaticLayer(m, style));

        // ---- 动态层 ----
        DrawDynamic(dc, f, style, m);
    }

    // ------------------------------------------------------------------ 排版

    /// <summary>
    /// 一帧的排版结果。**静态层与动态层必须用同一份** —— 分头算迟早会漂，所以算一次往下传。
    /// </summary>
    private readonly record struct Metrics(
        BarLayout Bars,
        double W, double H,
        double My0, double My1, double Y6,
        double Jy, double Jx0, double Jx1, double Jcx);

    private static Metrics BuildMetrics(double w, double h)
    {
        double my0 = TopInset;
        double my1 = Math.Max(my0 + 1, h - BottomInset);   // 面板压扁时别让区间翻过来（除零也在 DbToY 里）

        // 横向量自适应：宽容时与参照页逐像素一致，窄了按优先级退让（见 ComputeLayout 的注释）
        BarLayout lay = ComputeLayout(w);

        // J 的横条就挂在两柱外伸的那个跨度上 —— 与刻度线同宽
        double jx0 = lay.SpanLeft;
        double jx1 = lay.SpanRight;

        return new Metrics(lay, w, h, my0, my1, DbToY(HeadroomDb, my0, my1),
                           h - PhaseFromBottom, jx0, jx1, (jx0 + jx1) / 2);
    }

    // ------------------------------------------------------------------ 绘制

    /// <summary>
    /// 把**每帧都一样**的那一层预录成一个冻结的 <see cref="DrawingGroup"/>：
    /// 背景、两根底槽与 L/R 标签、刻度线与刻度数字、J 的左右半槽/中轴/端点刻度。
    ///
    /// 为什么值得单开一层：自检的「每帧分配字节数」量出这块面板是四块里最高的（26.8KB/帧），
    /// 而它**一条几何都没有** —— 开销全在每帧十几次 <c>DrawText</c> 各自的 glyph run 上，
    /// 其中**十一次画的字根本不变**。预录之后这些字只在尺寸/DPI 变化时录一次，
    /// 每帧只剩一条 <c>DrawDrawing</c>。
    ///
    /// 拆分依据：静态的那些都**不与动态的柱体/指针重叠**（标签在 <c>my1</c> 之下、
    /// 刻度数字在两柱左侧、J 的槽在中轴上下），所以把它们提前画不会被遮住 ——
    /// 与原来的绘制顺序**视觉等价**。
    ///
    /// ⚠️ 代价：**拖动窗口时每帧尺寸都在变 → 每帧都要重录这一层**，那一刻比原来还贵一点。
    /// 拖动是瞬态、稳态才是日常，这个交换划算。
    /// </summary>
    private DrawingGroup StaticLayer(in Metrics m, VizStyle style)
    {
        if (_layer is not null
            && Math.Abs(_layerW - m.W) < 0.01
            && Math.Abs(_layerH - m.H) < 0.01
            && Math.Abs(_layerDpi - style.DpiScale) < 1e-9)
            return _layer;

        _layerW = m.W;
        _layerH = m.H;
        _layerDpi = style.DpiScale;

        var group = new DrawingGroup();
        using (DrawingContext dc = group.Open())
        {
            dc.DrawRectangle(style.Background, null, new Rect(0, 0, m.W, m.H));

            // 刻度线 + 刻度数字（两条柱的公共刻度）
            for (int t = 0; t < Ticks.Length; t++)
            {
                double ty = DbToY(Ticks[t], m.My0, m.My1);
                dc.DrawLine(GetPen(), new Point(m.Jx0, ty), new Point(m.Jx1, ty));
                if (m.Bars.Numbers)   // 太窄时放弃刻度数字（柱顶的 dB 读数仍在，量纲不至于丢）
                    DrawText(dc, _tickTexts![t], m.Bars.BarA - ScaleNumberRight,
                             ty + ScaleNumberBaseline, TextAnchor.Right);
            }

            for (int ch = 0; ch < 2; ch++)
            {
                double bx = ch == 0 ? m.Bars.BarA : m.Bars.BarB;

                // 底槽
                dc.DrawRectangle(style.Track, null, Rect01(bx, m.My0, m.Bars.BarWidth, m.My1 - m.My0));

                // L / R 标签（柱底下方居中）
                DrawTextFitted(dc, ch == 0 ? _labelL! : _labelR!,
                               bx + m.Bars.BarWidth / 2, m.W, m.My1 + ChannelLabelBelow);
            }

            // J：左右半槽统一灰底（负半区不特殊着色）+ 中轴
            dc.DrawRectangle(style.Track, null,
                Rect01(m.Jx0, m.Jy - PhaseHalfHeight, m.Jcx - m.Jx0, PhaseHalfHeight * 2));
            dc.DrawRectangle(style.Track, null,
                Rect01(m.Jcx, m.Jy - PhaseHalfHeight, m.Jx1 - m.Jcx, PhaseHalfHeight * 2));
            dc.DrawLine(GetPen(), new Point(m.Jcx, m.Jy - PhaseAxisHalf),
                                   new Point(m.Jcx, m.Jy + PhaseAxisHalf));

            // 端点刻度必须用 DrawTextFitted：横条一贴到面板边缘，居中的「-1 / +1」就会被裁掉半个
            DrawTextFitted(dc, _phaseNeg!, m.Jx0, m.W, m.Jy + PhaseNumberBaseline);
            DrawTextFitted(dc, _phaseZero!, m.Jcx, m.W, m.Jy + PhaseNumberBaseline);
            DrawTextFitted(dc, _phasePos!, m.Jx1, m.W, m.Jy + PhaseNumberBaseline);
        }

        if (group.CanFreeze) group.Freeze();

        _layer = group;
        return group;
    }

    /// <summary>
    /// 每帧真会变的那几样：余量段、柱体、两处读数、J 的指针。
    /// ⚠️ 指针必须画在静态层**之后** —— 它要压在 J 的中轴上面。
    /// </summary>
    private void DrawDynamic(DrawingContext dc, VizFrame f, VizStyle style, in Metrics m)
    {
        double bw = m.Bars.BarWidth;

        for (int ch = 0; ch < 2; ch++)
        {
            float rms = ch == 0 ? f.RmsL : f.RmsR;
            double bx = ch == 0 ? m.Bars.BarA : m.Bars.BarB;

            double db = 20.0 * Math.Log10(Math.Max(RmsFloor, rms));
            double yv = Math.Max(m.My0, DbToY(db, m.My0, m.My1));

            // 余量区（-6dB 以上）：先画这一段，柱体再盖住下面
            if (yv < m.Y6)
                dc.DrawRectangle(style.Warn, null, Rect01(bx, yv, bw, m.Y6 - yv));

            double top = Math.Max(yv, m.Y6);
            dc.DrawRectangle(style.Bar, null, Rect01(bx, top, bw, m.My1 - top));

            // 读数（柱顶上方居中；面板窄时自动往里挪，见 DrawTextFitted）
            string readout = db <= DbMin
                ? "-inf"
                : db.ToString("0.0", CultureInfo.InvariantCulture);
            DrawTextFitted(dc, ValueText(readout, style),
                           bx + bw / 2, m.W, m.My0 - ReadoutAbove);
        }

        double corr = f.Correlation;
        if (corr > 1) corr = 1;
        else if (corr < -1) corr = -1;

        string sign = corr >= 0 ? "+" : "";
        DrawTextFitted(dc,
                       ValueText("相关 " + sign + corr.ToString("0.00", CultureInfo.InvariantCulture), style),
                       m.Jcx, m.W, m.Jy - PhaseReadoutAbove);

        // 指针
        double jx = m.Jcx + corr * (m.Jx1 - m.Jcx);
        dc.DrawRectangle(style.LineL, null,
            Rect01(jx - PhasePointerHalfWidth, m.Jy - PhasePointerHalfHeight,
                   PhasePointerHalfWidth * 2, PhasePointerHalfHeight * 2));
    }

    // ------------------------------------------------------------------ 横向量自适应

    /// <summary>
    /// 横向排版结果。<b>只有布局自检会读它</b> —— 用来对宽度做边界扫描，验证
    /// 「柱体与刻度线永不越界」这条契约。绘制路径解构一次即用，不缓存。
    /// </summary>
    internal readonly struct BarLayout
    {
        public BarLayout(double barWidth, double barGap, double barA, double barB, bool numbers)
        {
            BarWidth = barWidth;
            BarGap = barGap;
            BarA = barA;
            BarB = barB;
            Numbers = numbers;
        }

        /// <summary>单根柱宽。</summary>
        public double BarWidth { get; }

        /// <summary>两根柱之间的间距。</summary>
        public double BarGap { get; }

        /// <summary>左柱左边缘。</summary>
        public double BarA { get; }

        /// <summary>右柱左边缘。</summary>
        public double BarB { get; }

        /// <summary>是否绘制刻度数字（窄面板下会被放弃）。</summary>
        public bool Numbers { get; }

        /// <summary>刻度线 / 底槽的左端（含外伸）。</summary>
        public double SpanLeft => BarA - ScaleOverhang;

        /// <summary>刻度线 / 底槽的右端（含外伸）。</summary>
        public double SpanRight => BarB + BarWidth + ScaleOverhang;
    }

    /// <summary>
    /// 按面板宽度算横向排版。三条优先级，从硬到软：
    /// <list type="number">
    /// <item><b>柱体与刻度线不许越界</b> —— 硬约束，任何宽度都成立（这也是它叫「自适应」而不是
    ///   「够宽才行」的原因）。</item>
    /// <item><b>尽量保住刻度数字</b>（唯一的量纲），但压在下面时不让柱子细过
    ///   <see cref="MinBarScaleWithNumbers"/>。</item>
    /// <item><b>尽量接近参照页几何</b>：宽度 ≥ <see cref="ReferenceWidth"/>（194）时缩放系数
    ///   <c>s = 1</c>、柱体居中，与参照页**逐像素一致** —— 自适应此时是 no-op。</item>
    /// </list>
    ///
    /// 摆位用一行表达式就够：<c>BarA = max(居中位置, 左侧需求)</c>。
    /// 宽裕时居中位置更大 → 居中（参照页行为）；紧张时贴住左侧需求、把右侧的富余也用上
    /// —— 刻度数字只占左边，右边只要一点点外伸。
    /// </summary>
    internal static BarLayout ComputeLayout(double w)
    {
        if (w < 1) return new BarLayout(0, 0, 0, 0, false);

        bool numbers = w >= MinWidthWithNumbers;

        // 左侧要给刻度数字让位；右侧只需要刻度外伸。放弃数字后两侧都只留外伸，
        // 刻度线正好从面板左边缘起笔。
        double leftNeed = numbers ? ScaleNumberRight + ScaleTextWidth : ScaleOverhang;

        double avail = w - leftNeed - ScaleOverhang;
        if (avail < 0) avail = 0;

        double s = avail >= BarBlockFull ? 1.0 : avail / BarBlockFull;
        double bw = BarWidth * s;
        double gap = BarGap * s;
        double block = 2 * bw + gap;

        double centered = (w - block) / 2;
        double a = Math.Max(centered, leftNeed);

        return new BarLayout(bw, gap, a, a + bw + gap, numbers);
    }

    // ------------------------------------------------------------------ 小工具

    /// <summary>
    /// dB → y（参照页 <c>ydb</c>）：<c>y1 - (clamp(db,-36,0)+36)/36*(y1-y0)</c>。
    /// 注意 clamp 的作用是**超出量程的值贴着上下边**，不是裁掉。
    /// </summary>
    private static double DbToY(double db, double y0, double y1)
    {
        if (db < DbMin) db = DbMin;
        else if (db > DbMin + DbSpan) db = DbMin + DbSpan;
        return y1 - (db - DbMin) / DbSpan * (y1 - y0);
    }

    /// <summary>把可能为负的宽高夹成 0 —— <see cref="Rect"/> 的构造函数不接受负尺寸。</summary>
    private static Rect Rect01(double x, double y, double w, double h) =>
        new(x, y, w < 0 ? 0 : w, h < 0 ? 0 : h);

    /// <summary>
    /// 按 canvas 的 <c>fillText</c> 语义画字：<paramref name="anchorY"/> 是**基线**，
    /// 而 WPF 的 <see cref="DrawingContext.DrawText"/> 给的是左上角 —— 差一个
    /// <see cref="FormattedText.Baseline"/>（不是字号，字号大一圈会把字整体压低）。
    /// </summary>
    private static void DrawText(DrawingContext dc, FormattedText text,
                                 double anchorX, double anchorY, TextAnchor anchor)
    {
        double left = anchor switch
        {
            TextAnchor.Center => anchorX - text.Width / 2,
            TextAnchor.Right => anchorX - text.Width,
            _ => anchorX,
        };
        dc.DrawText(text, new Point(left, anchorY - text.Baseline));
    }

    /// <summary>
    /// 同上，但**放不下时不许出界**：往里挪到贴边，而不是让 <c>ClipToBounds</c> 把字裁掉半个。
    ///
    /// 为什么需要它：J 的端点刻度 <c>-1</c> / <c>+1</c> 是**居中**画在横条两端的
    /// （<c>jx0 = BarA - ScaleOverhang</c>、<c>jx1</c> 同理），面板一窄，横条端点就贴到面板边缘上，
    /// 居中的双字符标签必然各裁掉半个 —— 实测在 101 宽的面板上（窗口拉到最小时）
    /// 只剩「1」「0」「+」，负号与末位数字都没了。
    ///
    /// ⚠️ 宽裕时（字放得下）与 <see cref="DrawText"/> 的 <c>Center</c> 分支**逐像素一致** ——
    /// 这不是妥协，是同一套规则的退化分支。参照页画布够宽，所以那边从来遇不到。
    /// </summary>
    private static void DrawTextFitted(DrawingContext dc, FormattedText text,
                                       double centerX, double panelWidth, double anchorY)
    {
        double left = FittedLeft(text.Width, centerX, panelWidth);
        dc.DrawText(text, new Point(left, anchorY - text.Baseline));
    }

    /// <summary>
    /// 居中文字被钳进面板后的左边界。位置计算从 <see cref="DrawTextFitted"/> 里抽出来，
    /// 是为了让自检能**扫参数空间**验它 —— 像素级检查分不出「贴边」与「被裁」（两者都碰到边缘），
    /// 只有几何能。
    ///
    /// 契约两条：① 面板放得下时结果必落在 <c>[0, panelWidth - textWidth]</c>；
    /// ② 放得下且居中位置本来就合法时**原样返回** —— 这是「宽裕时与参照页逐像素一致」的前提。
    /// </summary>
    internal static double FittedLeft(double textWidth, double centerX, double panelWidth)
    {
        double left = centerX - textWidth / 2;

        if (textWidth >= panelWidth) return (panelWidth - textWidth) / 2;   // 面板比字还窄：无解，居中
        if (left < 0) return 0;
        if (left > panelWidth - textWidth) return panelWidth - textWidth;
        return left;
    }

    private static FormattedText MakeText(string s, Brush brush, double pixelsPerDip) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            MonoFace, FontSize, brush, pixelsPerDip > 0 ? pixelsPerDip : 1.0);

    /// <summary>
    /// 取一段**会变的**数值文字。与 <see cref="MakeText"/> 的区别只在于查缓存 ——
    /// 见 <see cref="_valueCache"/> 的注释（它在自检的分配量里是这块面板最大的一笔）。
    /// </summary>
    private FormattedText ValueText(string s, VizStyle style)
    {
        if (_valueCache.TryGetValue(s, out var cached)) return cached;

        if (_valueCache.Count >= ValueCacheLimit) _valueCache.Clear();   // 兜底，正常到不了

        var text = MakeText(s, style.Value, style.DpiScale);
        _valueCache[s] = text;
        return text;
    }

    private Pen? _pen;

    /// <summary>刻度线与中轴线都是「1 物理像素」（参照页没给它们设 lineWidth，沿用的是 1）。</summary>
    private Pen GetPen() => _pen!;

    private void EnsureResources(VizStyle style)
    {
        double dpi = style.DpiScale > 0 ? style.DpiScale : 1.0;
        if (_tickTexts is not null && Math.Abs(_cachedDpi - dpi) < 1e-9) return;
        _cachedDpi = dpi;

        _valueCache.Clear();   // 字号与 pixelsPerDip 都变了，缓存整盘作废

        _pen = new Pen(style.Grid, style.HairLine);
        if (_pen.CanFreeze) _pen.Freeze();

        _tickTexts = new FormattedText[Ticks.Length];
        for (int t = 0; t < Ticks.Length; t++)
            _tickTexts[t] = MakeText(Ticks[t].ToString("0", CultureInfo.InvariantCulture), style.Label, dpi);

        _labelL = MakeText("L", style.Label, dpi);
        _labelR = MakeText("R", style.Label, dpi);

        // J 的三个刻度：-1 / 0 / +1（参照页正好也写成这样）
        _phaseNeg = MakeText("-1", style.Label, dpi);
        _phaseZero = MakeText("0", style.Label, dpi);
        _phasePos = MakeText("+1", style.Label, dpi);
    }
}
