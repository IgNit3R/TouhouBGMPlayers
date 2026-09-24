using System.Windows;
using System.Windows.Media;
using ThbgmPlayer.Audio;

namespace ThbgmPlayer.UI;

/// <summary>
/// 整轨静态波形面板（主窗口新行的左栏）。
///
/// 显示的是**整个文件**（intro + loop 全画出来），**不受循环点影响** —— 循环点只作为一条竖向标记出现。
///
/// ⚠️ 为什么不用 <c>Viz/VizPanel</c>（可视化那套）：
/// 它把 <c>IsHitTestVisible</c> 设成 false 且只认没有输入的 <c>IVizRenderer</c> ——
/// 而这里**要接滚轮**（缩放），收不到鼠标事件就没法做。
/// 用 <c>OnRender</c> 还有一点附带好处：可以自己先铺一层铺满 bounds 的底槽矩形，
/// 于是整块区域天然可命中（<c>FrameworkElement</c> 自己没有 Background 属性）。
///
/// 绘制频率是 100ms 轮询（且只在播放头挪动 ≥1 像素时）+ 尺寸变化，不是可视化那边的 60Hz ——
/// 所以「用 DrawingVisual 免元素分配」的理由在这里不成立，直接用 OnRender 更简单。
///
/// 本面板**不认识引擎**：谁喂它 <see cref="SetTrack"/> / <see cref="SetEnginePosition"/> 谁负责取数。
/// </summary>
public sealed class WaveformPanel : FrameworkElement
{
    // 取色统一走 Theme.Get（与 VizStyle 同一条规矩：代码里不出现十六进制字面量）。
    // ⚠️ 必须在 Application 资源就位之后再取 —— 本面板由窗口 XAML 构造，
    // 那时 App.xaml 的主题已经合并好了，所以构造函数里取是安全的。
    private readonly Brush _track;
    private readonly Brush _wave;
    private readonly Brush _markBrush;
    private readonly Brush _headBrush;
    private readonly Pen _midPen;

    /// <summary>竖线的笔按 DPI 重建（见 <see cref="RebuildPensIfNeeded"/>）；这个记着上次用的 DPI。</summary>
    private double _penDpi = -1;
    private Pen _markPen = null!;
    private Pen _headPen = null!;

    private WaveformPeaks? _peaks;

    /// <summary>可见时间区间（秒）。切曲复位成整轨；滚轮缩放改它（第 5 步）。</summary>
    private ViewRange _view;

    /// <summary>曲目本身是不是"一次性"（黄昏作 ED / Staff Roll）。循环入口标记要压掉这种情况。</summary>
    private bool _isTfOneShot;

    /// <summary>播放头在**文件**坐标下的位置（秒）。<c>null</c> = 不知道，不画。</summary>
    private double? _filePos;

    public WaveformPanel()
    {
        // 底槽：整块都要画到，才有命中区域（见类型注释）
        _track = Theme.Get("BgElevated", "#FF2E2E31");

        // 中线：静音时给一个位置参照，否则空面板看不出"中间在哪"
        _midPen = FrozenPen(Theme.Get("Border", "#FF3F3F45"), 1);

        _wave = Theme.Get("VizBar", "#FF2E86C4");

        // ⚠️ 标记用 **VizMark（红）**，不是主题的 Accent ——
        // Accent 是 `#FF0E639C`（深蓝），和波形的 VizBar `#2E86C4` 撞色，标记几乎看不见。
        // 挑主题色要查实际色值，别按名字猜（"Accent"听着像强调色，其实是 UI 蓝）。
        _markBrush = Theme.Get("VizMark", "#FFD4696B");
        _headBrush = Theme.Get("Text", "#FFE6E6E6");

        // 笔在首次渲染时按 DPI 建（构造时还没进可视树，拿不到真实 DPI）
        _markPen = FrozenPen(_markBrush, 1);
        _headPen = FrozenPen(_headBrush, 1);
    }

    /// <summary>有波形可画（供调用方决定要不要算位置）。</summary>
    public bool HasPeaks => _peaks is not null;

    /// <summary>当前可见区间（只读）。自检直接断言它，省得靠像素反推。</summary>
    public ViewRange View => _view;

    /// <summary>
    /// 滚轮**水平（时间轴）**缩放：`wheelDelta` 正 = 放大，锚点 = 面板内的 x。
    ///
    /// ⚠️ 锚点只在**那一刻**生效：只要引擎位置在更新（即正在播放），
    /// 下一拍 <see cref="SetEnginePosition"/> 的**跟随**就会把视图重新对到播放头身上。
    /// 于是行为自然分成两种，都不用额外开关：
    /// · **播放中**缩放 ⇒ 视图跟着播放头走（缩放看的是"正在放的这一段"）；
    /// · **暂停时**缩放 ⇒ 位置不再更新，缩放就**停在光标处**（缩放看的是"我指的那一段"）。
    ///
    /// 也开放给自检直接调用 —— 离屏布局里鼠标事件的坐标拿不准。
    /// </summary>
    public void Zoom(int wheelDelta, double anchorX)
    {
        if (_peaks is null || ActualWidth <= 1) return;

        _view = WaveformMath.ZoomAt(_view, wheelDelta, anchorX, ActualWidth,
                                    _peaks.SecondsPerBucket, _peaks.TotalSeconds);
        InvalidateVisual();
    }

    protected override void OnMouseWheel(System.Windows.Input.MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        Zoom(e.Delta, e.GetPosition(this).X);
        e.Handled = true;   // 别让父级再吃一手（以后万一套进滚动容器会跳）
    }

    /// <summary>
    /// 换曲：给一份新峰值。<paramref name="peaks"/> 为 <c>null</c> = 还没扫出来（显示"无波形"）。
    ///
    /// ⚠️ <paramref name="isTfOneShot"/> 收的是**状态本身**、不是它的否定 ——
    /// 第一版这里收的是 <c>mayShowMark</c>（= 取过反的），结果调用处忘了再取反，
    /// 标记的显示条件**整个反过来**了（普通曲子不画、一次性曲目反而画）。
    /// 参数里别传否定值，这类错编译器抓不到、肉眼也要试很久。
    ///
    /// ⚠️ **视图复位到整轨**：换曲后要能一眼看到整首曲子的形状，而不是停在上一首的缩放位置。
    /// </summary>
    public void SetTrack(WaveformPeaks? peaks, bool isTfOneShot)
    {
        _peaks = peaks;
        _isTfOneShot = isTfOneShot;
        _view = new ViewRange(0, peaks?.TotalSeconds ?? 0);
        _filePos = null;

        InvalidateVisual();
    }

    /// <summary>
    /// 喂引擎的两个位置量：<c>ProgressPosition</c>（时间线）与 <c>LoopPosition</c>（本遍循环内已播）。
    /// 折算成"文件位置"的算术在 <see cref="WaveformMath.FilePosition"/> 里 —— 面板只负责画。
    ///
    /// ⚠️ 由**帧回调**驱动（跟 vsync），不是 100ms 轮询 —— 详见主窗口 <c>HookWaveFrame</c> 的注释。
    /// 所以这里**不再设"挪动不足 1 像素就不重绘"的门槛**：那正是播放头一格一跳的原因之一
    /// （1× 时 400px 摊 3 分钟 ⇒ 一格 = 0.45 秒 ✗）。成本由"只在播放中订阅帧回调"控制，
    /// 而不是靠在每次回调里省一次重绘。
    /// </summary>
    public void SetEnginePosition(TimeSpan progress, TimeSpan loopPosition)
    {
        if (_peaks is null || _view.Span <= 0)
        {
            if (_filePos is null) return;

            _filePos = null;
            InvalidateVisual();
            return;
        }

        double next = WaveformMath.FilePosition(progress, loopPosition, TimeSpan.FromSeconds(_peaks.IntroSeconds));

        // 跟随（用户定）：播放头走到可视区正中就开始滚，滚到波形尾进入视野为止。
        // ✅ 未缩放时 total − span == 0 ⇒ 公式退化成"起点 0" ⇒ **整轨视图不滚动**，不需要特例。
        _view = new ViewRange(WaveformMath.FollowStart(next, _view.Span, _peaks.TotalSeconds), _view.Span);

        _filePos = next;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var b = new Rect(0, 0, ActualWidth, ActualHeight);
        if (b.Width <= 1 || b.Height <= 1) return;   // 还没排完版

        RebuildPensIfNeeded();
        double dpi = _penDpi;

        // ① 底槽 —— 必须铺满：它同时是"可命中区域"
        dc.DrawRectangle(_track, null, b);

        double mid = b.Height / 2;
        dc.DrawLine(_midPen, new Point(0, mid), new Point(b.Width, mid));

        // ② 还没数据：**什么都不画**（只留底槽与中线）。
        // ⚠️ 曾经在这里居中显示「无波形」，2026-09-24 按用户要求去掉 ✗：那三个字在两种情况下都会出现 ——
        // ① 未播放时；② 黄昏作部分作品**曲子已经在放、波形还在后台扫**的那几秒（会让人以为这首没波形）。
        // 用户的要求是这两种情况都**不显示** ⇒ 面板保持空槽。
        var peaks = _peaks;
        if (peaks is null || peaks.BucketCount == 0 || _view.Span <= 0) return;

        double w = b.Width;
        double h = b.Height;

        // ③ 波形柱：一个 x 像素一根，纵向是「这像素覆盖的桶」里 min..max 的极值
        double half = Math.Max(1, (h - 2) / 2);   // 上下各留 1px；面板很扁时至少给 1px 半高
        int columns = (int)Math.Ceiling(w);

        for (int px = 0; px < columns; px++)
        {
            var (from, to) = WaveformMath.BucketSpan(_view, px, px + 1, w, peaks.SecondsPerBucket, peaks.BucketCount);

            int lo = sbyte.MaxValue, hi = sbyte.MinValue;
            for (int k = from; k <= to; k++)
            {
                if (peaks.Min[k] < lo) lo = peaks.Min[k];
                if (peaks.Max[k] > hi) hi = peaks.Max[k];
            }

            if (hi < lo) continue;   // 空区间（理论上不会，夹取保证了 to ≥ from）

            double yTop = mid - hi / 127.0 * half;
            double yBottom = mid - lo / 127.0 * half;

            // 静音段 min == max == 0 ⇒ 高度为 0，看不见。给 1px 下限，
            // 否则安静的地方会整段"断掉"，看起来像没扫到。
            if (yBottom - yTop < 1) yBottom = yTop + 1;

            dc.DrawRectangle(_wave, null, new Rect(px, yTop, 1, yBottom - yTop));
        }

        // ④ 循环入口标记 —— **必须画在波形之上**：
        // 波形满幅时能占满整个高度，画在下面会被整条盖住 ✗（用户实机第一眼就看出来了）。
        // 标记是"读图用的参照线"，它的可读性优先于"别压住波形"。
        if (WaveformMath.ShouldShowLoopMark(_isTfOneShot, peaks.IntroSeconds, peaks.TotalSeconds))
        {
            double mx = SnapX(XOf(peaks.IntroSeconds, w), dpi);
            if (mx >= 0 && mx <= w) dc.DrawLine(_markPen, new Point(mx, 0), new Point(mx, h));
        }

        // ⑤ 播放头画在最上面（它是最要紧的读数，连标记也不该压住它）
        // ⚠️ 它跟标记不同：**故意不吸附像素网格**。
        // 标记是静态的 ⇒ 吸附换清晰度（用户要的"红"就是这个）；播放头一直在动 ⇒
        // 吸附会把位置量化到整列，1× 时一格 = 0.45 秒，看起来就是一格一跳 ✗。
        // 不吸附则是一条带抗锯齿的柔边线，**滑行**着走 —— 动的东西优先要平滑。
        if (_filePos is double fp)
        {
            double hx = XOf(fp, w);
            if (hx >= 0 && hx <= w) dc.DrawLine(_headPen, new Point(hx, 0), new Point(hx, h));
        }
    }

    /// <summary>时间 → 面板内 x（超出可见区间时返回界外值，调用方自己判要不要画）。</summary>
    private double XOf(double seconds, double width) => (seconds - _view.Start) / _view.Span * width;

    /// <summary>
    /// 按当前 DPI 重建那两支竖线的笔：**笔宽 = 1 设备像素**（不是 1 DIP）。
    /// ⚠️ 1 DIP 在 150% 缩放下是 1.5 设备像素 ⇒ 又糊又淡，和"没吸附"是同一个下场。
    /// </summary>
    private void RebuildPensIfNeeded()
    {
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (dpi <= 0) dpi = 1;
        if (Math.Abs(dpi - _penDpi) < 1e-6) return;

        _penDpi = dpi;
        _markPen = FrozenPen(_markBrush, 1.0 / dpi);
        _headPen = FrozenPen(_headBrush, 1.0 / dpi);
    }

    /// <summary>
    /// 把竖线吸附到**设备像素中心**。
    ///
    /// ⚠️ 不吸附的话，1px 竖线落在两列像素的交界上 ⇒ 每列各覆盖 50% + 抗锯齿混色：
    /// 一条鲜红的线看起来是**灰紫**的 —— 实机反馈就是「换成红色之后看着还是灰的」，
    /// 而红 #D4696B 与波形蓝 #2E86C4 各半混出来正是 (129,119,151)，和截图里的灰完全对得上。
    /// </summary>
    private static double SnapX(double x, double dpi) => (Math.Floor(x * dpi) + 0.5) / dpi;

    /// <summary>建一支冻结的画笔（冻结后渲染端能缓存，也才允许跨线程参与）。</summary>
    private static Pen FrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        if (pen.CanFreeze) pen.Freeze();
        return pen;
    }
}
