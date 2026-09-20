using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ThbgmPlayer.Viz;

/// <summary>宿主（主窗口）的状态。放置判定按它分流。</summary>
public enum VizHostState
{
    Normal,
    Maximized,
    Minimized,
}

/// <summary>可视化的放置方式。</summary>
public enum VizPlacementMode
{
    /// <summary>自由窗口：几何由附件窗口自己保管（隔离期就是这个）。</summary>
    Free,

    /// <summary>贴附在宿主右侧、与宿主等高。</summary>
    AttachedNormal,

    /// <summary>宿主最大化且开了「最大化时内嵌」：附件窗口隐藏，画面搬进宿主里。</summary>
    AttachedEmbedded,
}

/// <summary>宿主窗口矩形（**DIP**）。</summary>
public readonly record struct VizHostBounds(double Left, double Top, double Width, double Height)
{
    /// <summary>右边缘。<b>「贴右侧」就是让附件窗口的 Left 等于它</b>。</summary>
    public double Right => Left + Width;

    /// <summary>下边缘。</summary>
    public double Bottom => Top + Height;
}

/// <summary>显示器工作区（**DIP**，已排除任务栏）。</summary>
public readonly record struct VizWorkArea(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;
    public double Height => Bottom - Top;
}

/// <summary>
/// 宿主几何抽象（方案 §3.6）。**本次隔离期没有主体**，但没有它两条需求
/// （「与宿主等高」「最大化时内嵌」）就没法写、更没法自动回归 ——
/// 抽象出来之后这两条本次就能写完并被自检覆盖，接入期只是把 <see cref="FreeHost"/>
/// 换成 `MainWindowHost`（拿到主窗口的 `Left/Top/Width/Height` 与
/// `SystemParameters.WorkArea`），判定逻辑一行不动。
/// </summary>
public interface IVizHost
{
    /// <summary>false = 自由模式（隔离期）。</summary>
    bool IsAttached { get; }

    /// <summary>宿主窗口状态。</summary>
    VizHostState State { get; }

    /// <summary>宿主窗口矩形（DIP）。</summary>
    VizHostBounds Bounds { get; }

    /// <summary>宿主所在显示器的工作区（DIP）。**「绝不跑出屏幕」靠它**。</summary>
    VizWorkArea WorkArea { get; }

    /// <summary>移动 / 缩放 / 状态变化。订阅方据此重算放置。</summary>
    event Action? Changed;
}

/// <summary>
/// 隔离期的空宿主：<see cref="IsAttached"/> 恒为 false → 放置判定永远走
/// <see cref="VizPlacementMode.Free"/> 分支，几何由窗口自己保管（持久化已在 M0 落地）。
///
/// <see cref="Bounds"/> / <see cref="WorkArea"/> 是可写的**占位值**：自由模式下没人读它们，
/// 留着是为了 M5 换宿主时判定逻辑的形状不用变。<see cref="NotifyChanged"/> 供测试与 M5 主动触发。
/// </summary>
public sealed class FreeHost : IVizHost
{
    public bool IsAttached => false;

    public VizHostState State { get; set; } = VizHostState.Normal;

    public VizHostBounds Bounds { get; set; }

    public VizWorkArea WorkArea { get; set; }

    public event Action? Changed;

    public FreeHost() { }

    public FreeHost(VizHostBounds bounds, VizWorkArea workArea)
    {
        Bounds = bounds;
        WorkArea = workArea;
    }

    /// <summary>主动通知订阅方重算（宿主几何变了）。</summary>
    public void NotifyChanged() => Changed?.Invoke();
}

/// <summary>
/// 真宿主（M6 接入）：包住主窗口，把它的**几何 / 状态 / 所在显示器工作区**喂给放置判定。
///
/// 它**自己订阅**窗口的三个事件并转成一个 <see cref="Changed"/> ——
/// 判定侧就不必知道「宿主变了」在 WPF 里是三个事件（移动 / 缩放 / 状态）。
/// </summary>
public sealed class WindowHost : IVizHost, IDisposable
{
    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint pt, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;

        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>MONITORINFO：<c>cbSize + rcMonitor + rcWork + dwFlags</c>（4+16+16+4 = 40 字节）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint CbSize;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    private readonly Window _window;
    private bool _disposed;

    public WindowHost(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _window.LocationChanged += OnWindowChanged;
        _window.SizeChanged += OnWindowChanged;
        _window.StateChanged += OnWindowChanged;
    }

    /// <summary>真宿主永远是「贴附」的 —— 自由模式那个是 <see cref="FreeHost"/>。</summary>
    public bool IsAttached => true;

    public VizHostState State => _window.WindowState switch
    {
        WindowState.Maximized => VizHostState.Maximized,
        WindowState.Minimized => VizHostState.Minimized,
        _ => VizHostState.Normal,
    };

    /// <summary>
    /// 宿主矩形（DIP）。
    /// ⚠️ **未 Show 的窗口 <c>Left/Top</c> 就是 <c>NaN</c>** —— 所以判定侧必须有兜底，
    /// 见 <see cref="VizPlacementLogic.Usable"/>。这里不做修正：修正会把"没就绪"伪装成"就绪"。
    /// </summary>
    public VizHostBounds Bounds => new(
        _window.Left,
        _window.Top,
        _window.ActualWidth > 0 ? _window.ActualWidth : _window.Width,
        _window.ActualHeight > 0 ? _window.ActualHeight : _window.Height);

    /// <summary>
    /// 宿主**所在那块显示器**的工作区（DIP，已排除任务栏）——「绝不跑出屏幕」靠它。
    ///
    /// ⚠️ 不能用 <c>SystemParameters.WorkArea</c>：那是**主显示器**的，
    /// 主窗口在副屏时拿它去算「贴右侧」会把窗口定位到主屏边缘去。
    ///
    /// ⚠️ 也不能用 <c>MonitorFromWindow</c>：它按整窗与各屏的重叠挑，
    /// 于是主窗口刚跨过去一半（中心已在新屏、右边缘还在旧屏）时工作区就跳了 ——
    /// 表现为「**主窗口还没完全拉过去，可视化窗口先跳过去了**」（用户 2026-09-21 实测报的）。
    /// 贴附窗口的去处由**宿主的右边缘**决定，所以这里就按右边缘那一点取显示器。
    /// </summary>
    public VizWorkArea WorkArea
    {
        get
        {
            IntPtr hwnd = new WindowInteropHelper(_window).Handle;
            if (hwnd != IntPtr.Zero)
            {
                var b = Bounds;
                double scale = DpiScale(hwnd);

                // 右边缘往右 1px、顶边往下 8px 那一点 —— 它落在哪块屏，贴附窗口就去哪块。
                IntPtr monitor = double.IsFinite(b.Right)
                    ? MonitorFromPoint(
                        new NativePoint((int)Math.Round((b.Right + 1) * scale),
                                        (int)Math.Round((b.Top + 8) * scale)),
                        MonitorDefaultToNearest)
                    : MonitorFromWindow(hwnd, MonitorDefaultToNearest);

                var info = new MonitorInfo { CbSize = (uint)Marshal.SizeOf<MonitorInfo>() };

                if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
                {
                    // ⚠️ GetMonitorInfo 给的是**物理像素**，这里要 DIP。
                    // 进程是系统 DPI 感知的（方案 R6），所以整块屏只有一个固定缩放比。
                    return new VizWorkArea(
                        info.Work.Left / scale, info.Work.Top / scale,
                        info.Work.Right / scale, info.Work.Bottom / scale);
                }
            }

            // 还没有 HWND（构造期）→ 退回主显示器的工作区，聊胜于无
            var fallback = SystemParameters.WorkArea;
            return new VizWorkArea(fallback.Left, fallback.Top, fallback.Right, fallback.Bottom);
        }
    }

    public event Action? Changed;

    private void OnWindowChanged(object? sender, EventArgs e) => Changed?.Invoke();

    private static double DpiScale(IntPtr hwnd)
    {
        uint dpi = GetDpiForWindow(hwnd);
        return dpi > 0 ? dpi / 96.0 : 1.0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _window.LocationChanged -= OnWindowChanged;
        _window.SizeChanged -= OnWindowChanged;
        _window.StateChanged -= OnWindowChanged;
    }
}
