using System.Windows;
using System.Windows.Media;

namespace ThbgmPlayer.Viz;

/// <summary>
/// 一块自绘面板：<see cref="FrameworkElement"/> + <see cref="DrawingVisual"/>。
///
/// <b>本项目第一次用 <c>DrawingVisual</c></b>（此前全项目只用过一次 <c>VisualTreeHelper</c>），
/// 所以这里把「为什么不走 Shape / Canvas」写下来：
/// <list type="bullet">
/// <item>波形、频谱柱是**每帧全变**的图元（200+ 采样点、52 根柱），用 <c>Polyline</c> /
///   <c>Rectangle</c> 之类的元素意味着每帧重建视觉树、触发一次布局 —— 60Hz 下必卡。</item>
/// <item><see cref="DrawingVisual"/> 只承载绘制指令，不参与布局、不产生元素，
///   重绘就是「换一批指令」。</item>
/// </list>
///
/// ⚠️ 三处容易漏、漏了就直接白屏的样板：
/// <list type="number">
/// <item><see cref="AddVisualChild"/> —— 不挂上去，视觉树里就没有它，画了也不显示。</item>
/// <item><see cref="VisualChildrenCount"/> / <see cref="GetVisualChild"/> ——
///   <see cref="FrameworkElement"/> 默认认为自己没有子视觉，重写前 WPF 会跳过它的渲染。</item>
/// <item>继承来的 <c>DrawingVisual</c> 不参与命中测试（没有背景就不命中），所以这里
///   <c>IsHitTestVisible=false</c> 只是明说而已 —— 窗口的拖动事件要能穿透到根 Border。</item>
/// </list>
/// 不需要重写 Measure/Arrange：本类没有内在尺寸，外层 Grid 用的是 <c>*</c> 行，
/// 布局由 Grid 决定；默认的 <c>MeasureOverride</c> 返回 (0,0) 正是我们要的。
/// </summary>
public sealed class VizPanel : FrameworkElement
{
    private readonly DrawingVisual _visual = new();
    private readonly VizStyle _style = VizStyle.Create();

    /// <summary>画法。null 时面板为空白（用于「该面板未启用」）。</summary>
    public IVizRenderer? Renderer { get; set; }

    /// <summary>当前帧。由渲染节拍在每帧重绘前更新。</summary>
    public VizFrame? Frame { get; set; }

    /// <summary>
    /// 观感资源（画刷 + DPI）。
    ///
    /// ⚠️ <b>刻意不叫 <c>Style</c></b>：<see cref="FrameworkElement"/> 自己有一个
    /// <c>Style</c> 属性（WPF 的样式，<c>System.Windows.Style</c>），叫同名会**隐藏基类成员**
    /// （CS0108），而且 XAML 里任何一句 <c>Style="…"</c> 都会试图往这个属性上写 —— 类型对不上，运行时炸。
    /// </summary>
    public VizStyle Skin => _style;

    public VizPanel()
    {
        AddVisualChild(_visual);
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        IsHitTestVisible = false;
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index)
    {
        if (index != 0) throw new ArgumentOutOfRangeException(nameof(index));
        return _visual;
    }

    /// <summary>
    /// 重画一次。<c>RenderOpen()</c> 会在打开时**清空**原有指令，
    /// 所以「没有渲染器 / 还没有帧 / 尺寸为 0」时什么都不画 = 画面空白，不需要额外清屏。
    /// </summary>
    public void Redraw()
    {
        using DrawingContext dc = _visual.RenderOpen();

        var renderer = Renderer;
        var frame = Frame;
        double w = ActualWidth;
        double h = ActualHeight;

        if (renderer is null || frame is null || w <= 0 || h <= 0)
            return;

        renderer.Render(dc, frame, new Size(w, h), _style);
    }

    /// <summary>尺寸一变就得重画 —— 面板是像素级重绘，不会自己跟着缩。</summary>
    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        Redraw();
    }

    /// <summary>挂进视觉树时取一次真实 DPI（构造期还拿不到）。</summary>
    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        _style.RefreshDpi(this);
    }

    /// <summary>窗口被拖到另一块不同缩放的显示器上时，WPF 会重排并通知这里。</summary>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _style.RefreshDpi(this);
        Redraw();
    }
}
