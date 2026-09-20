using System.Windows;
using System.Windows.Controls;
using ThbgmPlayer.Core;

namespace ThbgmPlayer.Viz;

/// <summary>
/// 五块可视化区域（A/B/C+J/D + 封面占位）。**它自己不画** —— 只做两件事：
/// 按 7:3 / 2.3:1 把面板摆好，以及把「一帧」分发给它们。
///
/// 为什么单开一个控件：**要有两处宿主** —— 贴附（或自由）的附件窗口，以及主窗口最大化时
/// 嵌进去的那一块（方案 §十）。两处的内容必须完全一样，所以布局只能有一份。
/// 它**不含状态条**：那是附件窗口特有的（调试声源报错），内嵌时没有它的位置。
///
/// ⚠️ 布局自检要读里面几个容器的**实际尺寸**，而它们的 <c>x:Name</c> 在这个控件自己的
/// 命名域里 —— 于是自检要用 <see cref="FrameworkElement.FindName"/> 取，
/// 不能再像以前那样直接点窗口的字段。
/// </summary>
public partial class VizSurfaceHost : UserControl
{
    private VizPanel[]? _panels;

    public VizSurfaceHost()
    {
        InitializeComponent();
    }

    /// <summary>四块自绘面板（封面不是 <see cref="VizPanel"/>，不在此列）。只建一次。</summary>
    public VizPanel[] Panels => _panels ??= new[] { PanelA, PanelB, PanelCJ, PanelD };

    /// <summary>
    /// 四块面板用的渲染器实例。**两处宿主必须给同一份**（见 <see cref="VizRenderers"/>）——
    /// 尤其是 D 的余辉，各持一份的话「窗口化 ↔ 最大化」切一次团雾就没了。
    /// 默认自带一份，是为了让单独用这个控件（比如自检）不需要额外装配。
    /// </summary>
    public VizRenderers Renderers { get; set; } = new();

    /// <summary>
    /// 按设置里的**面板开关**决定哪几块真的画（幂等）。
    ///
    /// 做法是「有渲染器就画、没有就空白」—— <see cref="VizPanel.Renderer"/> 为 null 时
    /// 面板什么都不画，于是不必去动 XAML 的可见性，也就**不会牵动 2.3:1 / 7:3 那套分区比例**。
    ///
    /// ⚠️ 这是刻意的：关掉一块只是不画、不重排布局 —— 否则每开关一次整块画面都会跳一下。
    /// 封面是纯 XAML 装饰，那个才切可见性。
    ///
    /// ⚠️ 两处宿主各调各的：这是**每块宿主**的状态（内嵌开着、附件窗口关着是合理的组合），
    /// 与渲染器实例的共享无关。
    /// </summary>
    public void ApplyPanelVisibility()
    {
        var viz = AppSettings.Current.Viz;

        PanelA.Renderer = viz.ShowA ? Renderers.Spectrum : null;
        PanelB.Renderer = viz.ShowB ? Renderers.Oscilloscope : null;
        PanelCJ.Renderer = viz.ShowC ? Renderers.LevelPhase : null;
        PanelD.Renderer = viz.ShowD ? Renderers.Lissajous : null;

        CoverCell.Visibility = viz.ShowCover ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 把一帧发给四块面板并重画。
    ///
    /// ⚠️ **只有正在显示的那一套该被调**（另一套看不见，重画纯属白费）——
    /// 由调用方（附件窗口 / 主窗口）负责挑，这里不做判断。
    /// </summary>
    public void Render(VizFrame frame)
    {
        var panels = Panels;

        for (int i = 0; i < panels.Length; i++)
        {
            panels[i].Frame = frame;
            panels[i].Redraw();
        }
    }
}
