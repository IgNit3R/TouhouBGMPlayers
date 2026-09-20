using System.IO;
using System.Windows;
using ThbgmPlayer.Core;
using ThbgmPlayer.UI;

namespace ThbgmPlayer.Viz;

/// <summary>
/// 可视化附件窗口。
///
/// 隔离期（命令行 <c>--viz [音频路径]</c>）它就是一个普通的自由窗口；
/// 接入期由 <c>IVizHost</c> 驱动几何（贴主窗口右侧、等高、最大化时内嵌）。
/// 后者本次全部实现，但挂的是 <c>FreeHost</c>，所以现在走的是「自由模式」分支。
///
/// M0 只有窗口本身：几何持久化、字体、状态条。
/// 取数与渲染（<c>IVizFeed</c> / <c>VizAnalyzer</c> / 五块渲染器）从 M1 起逐个填。
///
/// ⚠️ 生命周期：<c>App.xaml</c> 未设 <c>ShutdownMode</c>（默认 OnLastWindowClose），
/// 窗口全关即退出进程 —— 隔离期只有一个窗口时正好是对的，**不要**改成 OnMainWindowClose：
/// <c>--viz</c> 路径下 MainWindow 未必如期赋值，改了会「永不退出」（方案 §7-R1）。
/// </summary>
public partial class VizWindow : Window
{
    /// <summary>命令行给的音频路径（可为 null）。M1 起交给调试声源。</summary>
    public string? DebugSource { get; }

    /// <summary>
    /// 窗口是否真的显示过。没显示过时 <see cref="Window.Left"/> / <see cref="Window.Top"/>
    /// 是 NaN（WPF 用 NaN 表示「交给系统摆」），把这些值写进设置会把配置写脏，
    /// 下一次恢复几何就会出问题 —— 所以只在显示过之后才回写。
    /// </summary>
    private bool _wasShown;

    public VizWindow(string? debugSource = null)
    {
        InitializeComponent();

        DebugSource = debugSource;
        Closing += VizWindow_Closing;
        Loaded += (_, _) => _wasShown = true;

        RestoreGeometry();
        Theme.ApplyUserFont(this);

        if (!string.IsNullOrWhiteSpace(debugSource))
            Title = $"可视化 — {Path.GetFileName(debugSource)}";
    }

    /// <summary>
    /// 顶部状态条（方案 §7-R7）：设备缺失、解码失败之类不致命的问题走这里，
    /// 窗口照常打开、渲染器保持静止态，绝不弹异常。</summary>
    public void ShowStatus(string text)
    {
        StatusText.Text = text;
        StatusBar.Visibility = Visibility.Visible;
    }

    /// <summary>清掉状态条。</summary>
    public void ClearStatus()
    {
        StatusText.Text = "";
        StatusBar.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 贴附模式（接入期由 <c>IVizHost</c> / M5 置位）。贴附时位置由宿主算出来，
    /// 用户拖它没有意义，还会和宿主的定位互相打架 → 拖动必须关掉。
    ///
    /// 刻意**不从 <c>AppSettings.Viz.Attached</c> 初始化**：隔离期没有主体，
    /// 就算设置里勾了「贴附」也得按自由窗口处理，否则会变成一个既拖不动、
    /// 又没人给它算位置的死窗口。判定归宿主，窗口只认这个开关。
    /// </summary>
    public bool AttachedMode { get; set; }

    /// <summary>
    /// 拖动窗口。<c>WindowStyle=None</c> + <c>CaptionHeight=0</c> 之后没有可拖的标题栏，
    /// 只能由内容区接管；整块画布都能拖 —— 里面没有可点控件，不会误触。
    /// （边缘的缩放由 WindowChrome 在 WM_NCHITTEST 那层处理，不走这里。）
    /// </summary>
    private void Root_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (AttachedMode) return;

        // DragMove 在左键并非按下时会抛 InvalidOperationException
        if (e.ButtonState != System.Windows.Input.MouseButtonState.Pressed) return;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // 极少见的按键状态竞态。拖不起来就算了，不能因此把窗口搞崩。
        }
    }

    /// <summary>
    /// 恢复上次的窗口几何。越界（屏幕拔掉了 / 分辨率变了）就只留默认位置，
    /// 与 <c>MainWindow.RestoreWindowGeometry</c> 同一套判据，避免窗口跑到看不见的地方。
    /// </summary>
    private void RestoreGeometry()
    {
        var viz = AppSettings.Current.Viz;

        if (viz.Width > 0) Width = viz.Width;
        if (viz.Height > 0) Height = viz.Height;

        if (viz.Left is double l && viz.Top is double t)
        {
            bool onScreen =
                l > -SystemParameters.VirtualScreenWidth + 120 && l < SystemParameters.VirtualScreenWidth - 120 &&
                t > -SystemParameters.VirtualScreenHeight + 120 && t < SystemParameters.VirtualScreenHeight - 120;
            if (onScreen)
            {
                Left = l;
                Top = t;
            }
        }
    }

    private void VizWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // 没显示过就不回写。自检路径会构造一个不 Show 的窗口，
        // 那时 Left/Top 还是 NaN、Width/Height 是从设置里读出来的，
        // 回写一遍等于把「期望值」当成「实际值」固化下来，没有意义还容易写脏。
        if (!_wasShown) return;

        var viz = AppSettings.Current.Viz;

        // 最大化 / 最小化时用 RestoreBounds（还原后的尺寸），与主窗口同一套处理思路
        if (WindowState == WindowState.Normal)
        {
            if (IsPositiveFinite(Width)) viz.Width = Width;
            if (IsPositiveFinite(Height)) viz.Height = Height;
            if (double.IsFinite(Left)) viz.Left = Left;
            if (double.IsFinite(Top)) viz.Top = Top;
        }
        else
        {
            var b = RestoreBounds;
            if (!b.IsEmpty)
            {
                if (IsPositiveFinite(b.Width)) viz.Width = b.Width;
                if (IsPositiveFinite(b.Height)) viz.Height = b.Height;
                if (double.IsFinite(b.Left)) viz.Left = b.Left;
                if (double.IsFinite(b.Top)) viz.Top = b.Top;
            }
        }

        // 命令行给的路径记下来，下次 --viz 不带参数也能接着听（调试期便利）
        if (!string.IsNullOrWhiteSpace(DebugSource)) viz.DebugSource = DebugSource!;

        AppSettings.Current.Save();
    }

    private static bool IsPositiveFinite(double v) => double.IsFinite(v) && v > 0;
}
