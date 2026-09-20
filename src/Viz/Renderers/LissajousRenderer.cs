using System.Windows;
using System.Windows.Media;
using ThbgmPlayer.Core;

namespace ThbgmPlayer.Viz.Renderers;

/// <summary>
/// D · 利萨如：把 L 当横轴、R 当纵轴点出来的散点轨迹，带**余辉**。
///
/// 逐项对齐验证页 <c>.workbuddy/viz-demo/index.html</c> 的 D 段：
/// <list type="bullet">
/// <item>十字轴半长 <c>r = min(W,H)/2 - 8*d</c>（那个 8 带 d → 8 DIP）。</item>
/// <item>投影 <c>px = cx + L*r</c>、<c>py = cy - R*r</c> —— <b>纵轴用 R 且向上取正</b>。</item>
/// <item>抽样：<c>st = floor(2048/400)</c>，即**均匀跳点**而不是求平均，取到 410 个点
///   （<c>i = 0,5,10,…,2045</c>）。跳点会轻微走样，但这是定稿画面，照抄。</item>
/// <item>线宽 <c>max(0.75, d*0.6)</c>：前者是裸 canvas 单位（物理像素）→ 乘 HairLine；
///   后者带 d（CSS px）→ 直接写数字。</item>
/// <item>未播放时**不画新轨迹**，但同时把覆盖用的黑度从 <c>0.16</c> 降到 <c>0.05</c>
///   —— 也就是余辉衰减得更慢、留成一团淡影。这就是用户定稿的「暂停 = 衰减成淡影」。</item>
/// </list>
///
/// <b>与参照页的机制差异（唯一一处，且是刻意选择）</b>：
/// 参照页的余辉靠 canvas 是**持久位图** —— 每帧先用半透明底色把上一帧盖掉一点，于是
/// 内容按 <c>0.84^n</c> 指数衰减。WPF 的 <c>DrawingVisual</c> 每帧整体重建，**没有持久层**，
/// 所以必须自己模拟。两种做法：
/// <list type="table">
/// <item><description><b>① 历史位图（RenderTargetBitmap 乒乓）</b>：语义与参照页逐像素一致，
///   但 <c>RenderTargetBitmap.Render</c> 是**同步 + 回读**，每帧一次在 UI 线程上扛着，
///   面板越大越贵，是 WPF 里有名的性能坑。</description></item>
/// <item><description><b>② 历史图层（本实现）</b>：把最近 <see cref="TrailLayers"/> 帧的
///   跳点后的坐标各存一份，按 <c>0.84^age</c> 的透明度自旧到新叠着画。
///   单条笔画经过的像素亮度与参照页**完全同值**（都是 <c>0.84^τ</c>），
///   反复经过的像素两边都会饱和到全不透明；差异只在抗锯齿的半覆盖像素上，肉眼不可辨。
///   代价是每帧 20 次 <c>DrawGeometry</c>（指令数），换来**零回读、零 GPU 往返**。</description></item>
/// </list>
/// ⚠️ <b>20 次绘制 ≠ 20 份几何数据。</b>几何按**槽位**缓存（见 <see cref="_geos"/> 的注释）：
/// 稳态下每帧只重建「刚写入的那个槽」，其余 19 条复用。差别是实打实的字节数 ——
/// 每帧 20 条 × 410 点 ≈ 160KB 的 gen0 垃圾 vs 1 条 ≈ 8KB，
/// 由自检「每帧分配字节数」那一项盯着（跨版本漂移一眼可见）。
/// <list type="bullet">
/// 选 ②。若哪天观感对不上，换成 ① 只需改本文件（面板与管线都不用动）。
///
/// ⚠️ <b>本渲染器是唯一带状态的</b>（方案 §199 明确允许「每宿主一份」）：历史图层与透明度
/// 都是跨帧递推量。为把递推钉在**分析帧**上（而不是 vsync 帧），递推的触发条件看
/// <see cref="VizFrame.Revision"/> —— 144Hz 屏上同一帧会被画多次，只推一次才不会让余辉变短。
/// 其余三块渲染器仍是纯函数，别照这个抄。
/// </summary>
public sealed class LissajousRenderer : IVizRenderer
{
    /// <summary>十字轴半长相对面板短边的收减（参照页 <c>8*d</c>，带 d → DIP）。</summary>
    private const double AxisInset = 8.0;

    /// <summary>线宽下限（参照页 <c>0.75</c>，裸单位 = 物理像素 → 乘 HairLine）。</summary>
    private const double TraceThicknessPx = 0.75;

    /// <summary>线宽的另一半（参照页 <c>d*0.6</c>，带 d → DIP）。</summary>
    private const double TraceThicknessDip = 0.6;

    /// <summary>参照页的抽样目标点数 <c>NP</c>（决定跳点步长，不是最终点数）。</summary>
    private const int TargetPoints = 400;

    /// <summary>余辉层数。参照页的轨迹在 <c>0.84^20 ≈ 0.03</c> 处基本看不见了，取 20 正好覆盖。</summary>
    private const int TrailLayers = 20;

    /// <summary>
    /// **实际画几层**（设置里可调，默认 10 —— 见 <see cref="VizSettings.TrailLayers"/>）。
    ///
    /// ⚠️ 这是可视化里**唯一真正有效的性能旋钮**。实测（用户 200Hz 屏）：
    /// 20 层约 25ms/帧、关掉 D 掉到 5.1ms ⇒ **成本正比于层数**（约 1ms/层）；
    /// 而把每层折线的段数**砍半只降 9%** ⇒ 不是每段的账。
    /// 原因在「墨量」—— 每层铺下的**线长 × 线宽**，而 Lissajous 的路径长度
    /// **随信号幅度增长**（响的时候点在整块团里乱窜、走的路线更长），
    /// 这就是「音乐越响越掉帧」的由来。层数少一半，墨量少一半。
    ///
    /// 见 docs/2026-09-21-viz-frame-pacing-pending.md。
    /// </summary>
    private static int DrawnLayers
    {
        get
        {
            int n = AppSettings.Current.Viz.TrailLayers;

            if (n < VizSettings.MinTrailLayers) n = VizSettings.MinTrailLayers;
            if (n > TrailLayers) n = TrailLayers;

            return n;
        }
    }

    /// <summary>
    /// 投影进几何时的**点间距**（抽稀）。数据窗口仍是 <see cref="PointCount"/> 个样本，
    /// 只是画的时候每隔 <see cref="PointStride"/> 个取一个。
    ///
    /// 抽到什么程度：让相邻两点的屏幕间距约 2~3px。400 点投到两三百像素的团上，
    /// 本来就有 2~3 点/像素 —— 稠的那部分纯属白送。
    ///
    /// ⚠️ 这一改**没有解决掉帧**：段数砍半、帧距只降 9%（实测）—— 真正的杠杆是
    /// <see cref="DrawnLayers"/>。留着它是因为它把每帧分配减半、无害。
    /// （教训：当时按「每段成本」推断，测出来是「每层成本」。**先有测量再下结论**。）
    /// </summary>
    private const int PointStride = 2;
    /// <summary>播放中的每帧衰减（参照页覆盖色 <c>0.16</c> → 保留 <c>0.84</c>）。</summary>
    private const float ActiveFade = 0.84f;

    /// <summary>暂停时的每帧衰减（参照页覆盖色 <c>0.05</c> → 保留 <c>0.95</c>）→ 淡影留得久。</summary>
    private const float PausedFade = 0.95f;

    /// <summary>
    /// 透明度量化档数。画笔的透明度是<b>烘进画刷</b>的（见 <see cref="MakePen"/>），
    /// 量化成 32 档就能把画笔全部缓存下来、每帧一次分配都不做；1/31 的台阶在这种
    /// 半透明叠加上看不出来，而缓存下来的好处是实打实的。
    /// </summary>
    private const int AlphaSteps = 32;

    /// <summary>低于这个透明度就当作已消失（省掉一次不可见的绘制，也让余辉能真正「灭干净」）。</summary>
    private const float AlphaCutoff = 0.01f;

    private static readonly int Stride = Math.Max(1, VizFrame.WaveFrames / TargetPoints);

    private static readonly int PointCount = (VizFrame.WaveFrames + Stride - 1) / Stride;

    // ------------------------------------------------------------------ 跨帧状态（唯一带状态的渲染器）

    /// <summary>历史轨迹的 L 值（归一化 -1…1，**不是屏幕坐标**：面板改大改小时按新半径重投影）。</summary>
    private readonly float[] _trailL = new float[TrailLayers * PointCount];

    /// <summary>历史轨迹的 R 值（对应纵轴）。</summary>
    private readonly float[] _trailR = new float[TrailLayers * PointCount];

    /// <summary>每一层的当前透明度（与槽位对齐，不随 age 平移）。</summary>
    private readonly float[] _alpha = new float[TrailLayers];

    /// <summary>最新一层的槽位。初始指向末尾，好让第一次推入落在槽 0。</summary>
    private int _head = TrailLayers - 1;

    /// <summary>已填充的层数（启动初期不足 <see cref="TrailLayers"/>）。</summary>
    private int _count;

    /// <summary>上次递推对应的 <see cref="VizFrame.Revision"/>。</summary>
    private long _seenRevision = -1;

    /// <summary>上次清盘对应的 <see cref="VizFrame.ResetRevision"/>。</summary>
    private long _seenResetRevision = -1;

    /// <summary>
    /// 每个**槽位**一条复用的折线几何。
    ///
    /// 关键：它对应的是**槽位**，不是 age。槽位里的点只在被写入的那一帧才变，所以
    /// **每帧只需重建「刚写进去的那一个」**（20 → 1），其余 19 条原样复用 ——
    /// 这正是把「每帧分配几何」压下去的那一刀（方案 §二那张表的批评点）。
    /// 按 age 索引就会每帧全废，白忙一场。
    /// </summary>
    private readonly StreamGeometry?[] _geos = new StreamGeometry?[TrailLayers];

    /// <summary>槽位几何是否已过期（点变了 / 面板改过尺寸）。</summary>
    private readonly bool[] _geoDirty = new bool[TrailLayers];

    /// <summary>几何是按面板尺寸投影出来的：尺寸一变整盘作废，故记下建它时的尺寸。</summary>
    private double _builtWidth = -1;
    private double _builtHeight = -1;

    /// <summary>
    /// **仅供自检**：关掉槽位几何缓存（每帧全量重建 20 条），用来量「缓存到底省了多少」。
    /// 生产路径恒为 <c>false</c>。留着它是因为那条断言需要**对照** ——
    /// 否则「省了」只是一句话，不是数字。
    /// </summary>
    internal bool DisableGeometryCache { get; set; }

    /// <summary>
    /// 在册的余辉层数（诊断 / 自检用，不参与绘制）。
    /// 自检靠它验「递推钉在分析帧上、暂停后能灭干净」这条状态机 —— 否则只能靠肉眼看画面。
    /// </summary>
    public int TrailCount => _count;

    // 缓存的绘制资源，随 DPI 变化重建
    private double _cachedDpi = -1;
    private Pen? _penAxis;
    private Pen[]? _pens;

    public void Render(DrawingContext dc, VizFrame f, Size size, VizStyle style)
    {
        double w = size.Width;
        double h = size.Height;
        if (w <= 1 || h <= 1) return;   // 还没排完版，画了也是白画

        EnsureResources(style);

        // 余辉先进位，再画 —— 顺序反了的话最新一层的透明度会晚一帧生效。
        //
        // ⚠️ 两个版本号各管一件事，**不能合成一个判断**（也不是 else if）：
        //   ResetRevision 变了 = 归零（停止 / 切曲 / 播完）→ **立刻清盘**，不留残影；
        //   Revision 变了    = 有新数据 → 推一格余辉。
        // 归零后的那一帧两者可能同时变，所以要允许"先清盘、再推一格"。
        if (f.ResetRevision != _seenResetRevision)
        {
            ClearTrail();
            _seenResetRevision = f.ResetRevision;
        }

        if (f.Revision != _seenRevision)
        {
            Advance(f);
            _seenRevision = f.Revision;
        }

        dc.DrawRectangle(style.Background, null, new Rect(0, 0, w, h));

        double cx = w / 2;
        double cy = h / 2;
        double r = Math.Min(w, h) / 2 - AxisInset;
        if (r < 0) r = 0;   // 面板压得比 16 还扁时退化成中心一点，别把图翻过来

        // 面板尺寸变了 → **所有**槽位的几何都作废（它们按旧的中心与半径投影过）。
        // 这只会发生在用户拉窗口的那一瞬间，之后每帧只重建 1 条。
        if (Math.Abs(_builtWidth - w) > 0.01 || Math.Abs(_builtHeight - h) > 0.01)
        {
            _builtWidth = w;
            _builtHeight = h;
            Array.Fill(_geoDirty, true);
        }

        // 十字轴画在轨迹**下面**：参照页里轴每帧重画、永远是清晰的，
        // 只有最新那笔轨迹压在轴上面（旧轨迹被轴压住）。这一处差异是 1 物理像素的
        // 暗色细线，肉眼不可辨，不值得为它把绘制拆成两段。
        dc.DrawLine(_penAxis!, new Point(cx - r, cy), new Point(cx + r, cy));
        dc.DrawLine(_penAxis!, new Point(cx, cy - r), new Point(cx, cy + r));

        if (_count <= 0) return;

        // 自旧到新叠画：新的压在旧的上面（「over」合成），与参照页的位图累积同向。
        // ⚠️ 只画最近的 DrawnLayers 层 —— **成本正比于层数**（见 DrawnLayers 的注释）。
        int layers = Math.Min(_count, DrawnLayers);
        for (int age = layers - 1; age >= 0; age--)
        {
            int slot = _head - age;
            if (slot < 0) slot += TrailLayers;

            int level = (int)(_alpha[slot] * (AlphaSteps - 1) + 0.5f);
            if (level <= 0) continue;   // 透明或已灭

            dc.DrawGeometry(null, _pens![level], Geometry(slot, cx, cy, r));
        }
    }

    // ------------------------------------------------------------------ 归零

    /// <summary>
    /// 丢掉整盘历史。与「暂停 → 淡影慢灭」**不是一回事**：这是**立刻**清空，
    /// 因为方案 §2 要求「停止 / 切曲 / 播完」的画面清干净、不留淡影。
    /// 触发条件是 <see cref="VizFrame.ResetRevision"/> 变了（见 <see cref="Render"/> 里的说明）。
    /// </summary>
    private void ClearTrail()
    {
        Array.Clear(_alpha);
        _count = 0;
        _head = TrailLayers - 1;
    }

    // ------------------------------------------------------------------ 余辉递推（每个分析帧一次）

    /// <summary>
    /// 推进一格余辉。<b>只在 <see cref="VizFrame.Revision"/> 变化时调用</b>（见类型注释）。
    ///
    /// 两条分支对应参照页的两条路径：
    /// <list type="bullet">
    /// <item>播放中：所有在册层乘 <see cref="ActiveFade"/> 变暗，然后写入新的一层（满亮）。
    ///   于是第 n 老的一层正好是 <c>0.84^n</c>，与参照页同值。</item>
    /// <item>未播放：<b>不写入新层</b>（网格原地不动、旧轨迹冻在原地），只把整盘按
    ///   <see cref="PausedFade"/> 变暗 —— 就是「衰减成淡影」。</item>
    /// </list>
    /// 注意这里刻意**不做**「恢复播放时清空历史」：清空会让快速暂停/恢复时余辉整体闪一下。
    /// 靠透明度递推自己收敛就够了 —— 暂停期间旧层早就暗到看不清，恢复后它们继续按
    /// 同一个系数衰减，不会突然满亮（透明度是绝对值，不是相对值）。
    /// </summary>
    private void Advance(VizFrame f)
    {
        if (f.Active)
        {
            // 淡出**按「画几层」重新标定**：画满 20 层时指数正是 1（与原先的 0.84 完全一致）；
            // 调到 n 层时每格多退几步，于是**最老那一层仍然落在同一档亮度**上 ——
            // 观感是「雾薄了、渐变粗了」，而不是「外面多出一圈硬边」。
            // ⚠️ 名字不能叫 fade：同一方法里"暂停分支"已经用掉那个名字了（CS0136）。
            float decay = (float)Math.Pow(ActiveFade, (double)TrailLayers / DrawnLayers);
            for (int k = 0; k < TrailLayers; k++) _alpha[k] *= decay;

            _head++;
            if (_head >= TrailLayers) _head = 0;
            _alpha[_head] = 1f;

            int off = _head * PointCount;
            for (int p = 0; p < PointCount; p++)
            {
                int i = p * Stride;
                _trailL[off + p] = f.WaveL[i];
                _trailR[off + p] = f.WaveR[i];
            }

            _geoDirty[_head] = true;   // 这个槽的点换了，几何得重建（其余 19 条照旧复用）

            if (_count < TrailLayers) _count++;
            return;
        }

        // 暂停：把 dt 折算回「60Hz 的帧数」再取幂，这样 60/120/144Hz 上淡影的时长一致。
        // ⚠️ 名字不能叫 k —— 上面 `if (f.Active)` 块里那个 `for (int k = …)` 也是 k，
        // 内层遮蔽外层局部是 CS0136（编译期报错，不是警告）。
        float fade = (float)Math.Pow(PausedFade, f.Dt * 60.0);

        bool anyVisible = false;
        for (int i = 0; i < TrailLayers; i++)
        {
            float a = _alpha[i] * fade;
            if (a < AlphaCutoff) a = 0f;
            else anyVisible = true;
            _alpha[i] = a;
        }

        // 全灭就把在册层数也归零：不清的话，下次恢复播放前那几层零透明度的槽位还「占着」，
        // _count 与 _alpha 会各说各话。
        if (!anyVisible) _count = 0;
    }

    // ------------------------------------------------------------------ 几何

    /// <summary>
    /// 取某个槽位的折线几何。**只在它过期时重建** —— 稳态下每帧只有「刚写入的那个槽」过期，
    /// 所以是 1 条/帧而不是 20 条/帧。
    ///
    /// 为什么用「重建整条」而不是「Clear + 重填」：Geometry 一旦冻结就不能再改，
    /// 而冻结能让渲染端缓存它。重建一条只多一个对象，却省掉了可变几何在
    /// 已录制内容里被改动的那类麻烦。
    /// </summary>
    private StreamGeometry Geometry(int slot, double cx, double cy, double r)
    {
        // 自检的对照组：强制全盘过期 = 回到「每帧新建 20 条几何」的老做法
        if (DisableGeometryCache) _geoDirty[slot] = true;

        if (_geos[slot] is { } cached && !_geoDirty[slot]) return cached;

        _geoDirty[slot] = false;

        int off = slot * PointCount;
        var geo = new StreamGeometry();
        using (StreamGeometryContext g = geo.Open())
        {
            g.BeginFigure(new Point(cx + _trailL[off] * r, cy - _trailR[off] * r), false, false);

            for (int p = PointStride; p < PointCount; p += PointStride)
                g.LineTo(new Point(cx + _trailL[off + p] * r, cy - _trailR[off + p] * r), true, false);

            // 末点一定要收进来：否则折线的终点会差一截 —— 那是**看得出来的**
            int last = PointCount - 1;
            if (last > 0 && last % PointStride != 0)
                g.LineTo(new Point(cx + _trailL[off + last] * r, cy - _trailR[off + last] * r), true, false);
        }
        if (geo.CanFreeze) geo.Freeze();

        _geos[slot] = geo;
        return geo;
    }

    // ------------------------------------------------------------------ 资源

    private void EnsureResources(VizStyle style)
    {
        double dpi = style.DpiScale > 0 ? style.DpiScale : 1.0;
        if (_pens is not null && Math.Abs(_cachedDpi - dpi) < 1e-9) return;
        _cachedDpi = dpi;

        _penAxis = new Pen(style.Grid, style.HairLine);
        if (_penAxis.CanFreeze) _penAxis.Freeze();

        double thickness = Math.Max(TraceThicknessPx * style.HairLine, TraceThicknessDip);
        _pens = new Pen[AlphaSteps];
        for (int j = 0; j < AlphaSteps; j++)
            _pens[j] = MakePen(style.LineL, thickness, (double)j / (AlphaSteps - 1));
    }

    /// <summary>
    /// 把透明度烘进画刷的画笔。用 <c>Brush.Clone()</c> + <c>Brush.Opacity</c>
    /// 而不是去拆 <c>SolidColorBrush.Color</c> 拼 ARGB：主题里的画刷未必是纯色，
    /// 而且 <c>VizStyle</c> 立的规矩是「一个十六进制字面量都不留」，克隆能同时守住这两条。
    /// </summary>
    private static Pen MakePen(Brush source, double thickness, double alpha)
    {
        Brush b = source.Clone();
        b.Opacity = alpha;
        if (b.CanFreeze) b.Freeze();

        var p = new Pen(b, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        if (p.CanFreeze) p.Freeze();
        return p;
    }
}
