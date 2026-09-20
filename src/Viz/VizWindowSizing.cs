using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ThbgmPlayer.Viz;

/// <summary>
/// 窗口缩放的 Win32 拦截：**贴附模式下只允许拖右边缘** —— 因而高度被锁死。
///
/// 为什么走拦截 <c>WM_SIZING</c> 而不是 <c>ResizeMode</c> + <c>SizeChanged</c> 回写（方案 §3.7）：
/// 拦截是在**调整之前**就把矩形改掉，拖起来没有橡皮筋闪烁；回写则是「先变大再被拉回」，
/// 而且回写本身还会再触发一轮事件。
///
/// P/Invoke 纪律严格照 <c>src/Core/MediaKeysHotkey.cs</c>（项目唯一先例）：
/// <list type="bullet">
/// <item><c>DllImport</c> **就地声明**，不建公共的 NativeMethods 类；</item>
/// <item>拿到 HWND 之后才 <c>AddHook</c>（<see cref="VizWindow"/> 在 <c>OnSourceInitialized</c> 调 <see cref="Attach"/>）；</item>
/// <item>**在 <c>Closing</c> 摘钩，不在 <c>Closed</c> 摘** —— 那时 HWND 已销毁，<c>RemoveHook</c> 会悬在半空；</item>
/// <item><see cref="Dispose"/> 幂等。</item>
/// </list>
///
/// ⚠️ **隔离期 <see cref="Attached"/> 恒为 false**（没有主体），此时钩子完全不插手，
/// 窗口四边都能拖、高度也随用户 —— 那本来就是自由窗口该有的样子。
/// 因此这条的实机行为要等 M5/M6 接上宿主才能看到；本次能验证的只有下面那个纯函数
/// <see cref="CorrectSize"/>（方案 §九 M3 的验收要求也正是「Attached 分支可被自检覆盖」）。
///
/// ⚠️ 进程是**系统 DPI 感知**（仓库无 <c>app.manifest</c>，方案 R6），所以 <c>lParam</c> 里的
/// 物理像素与 DIP 之间只差一个固定的系统缩放比，用 <c>GetDpiForWindow</c> 折一次就够。
/// </summary>
public sealed class VizWindowSizing : IDisposable
{
    private const int WmSizing = 0x0214;

    // WM_SIZING 的 wParam：拖的是哪条边 / 哪个角
    public const int WmszLeft = 1;
    public const int WmszRight = 2;
    public const int WmszTop = 3;
    public const int WmszTopLeft = 4;
    public const int WmszTopRight = 5;
    public const int WmszBottom = 6;
    public const int WmszBottomLeft = 7;
    public const int WmszBottomRight = 8;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private readonly Window _window;
    private HwndSource? _source;

    /// <summary>贴附模式：true → 只允许拖右边缘（位置与高度都锁死）。</summary>
    public bool Attached { get; set; }

    /// <summary>最小宽度（DIP）。判之前用 <c>GetDpiForWindow</c> 折算成物理像素。</summary>
    public double MinWidth { get; set; } = VizPlacementLogic.MinWidth;

    public VizWindowSizing(Window window) => _window = window;

    /// <summary>钩子是否已装上。诊断用。</summary>
    public bool IsHooked => _source is not null;

    /// <summary>
    /// 装钩子。幂等（重复调用先摘旧的）。窗口还没拿到 HWND 时**返回 false 且什么都不做** ——
    /// 自检路径正是如此（构造了窗口但没 Show），这里绝不能抛。
    /// </summary>
    public bool Attach()
    {
        Detach();

        IntPtr hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd == IntPtr.Zero) return false;

        _source = HwndSource.FromHwnd(hwnd);
        if (_source is null) return false;

        _source.AddHook(WndProc);
        return true;
    }

    /// <summary>摘钩子。可重复调用。</summary>
    public void Detach()
    {
        _source?.RemoveHook(WndProc);
        _source = null;
    }

    public void Dispose() => Detach();

    // ------------------------------------------------------------------ 纯逻辑（自检覆盖的就是这两条）

    /// <summary>DIP → 物理像素。拿不到 DPI（返回 0）时按 96 处理；结果至少为 1。</summary>
    public static int DipToPx(double dip, uint dpi)
    {
        if (dpi == 0) dpi = 96;
        double px = dip * dpi / 96.0;
        int rounded = (int)Math.Round(px);
        return rounded < 1 ? 1 : rounded;
    }

    /// <summary>
    /// 修正系统提议的窗口矩形（**物理像素**）。纯函数，自检直接驱动。
    ///
    /// 贴附模式（<paramref name="attached"/> = true）下：
    /// <list type="bullet">
    /// <item>拖的**不是右边缘** → 整条驳回，连位置都不许变（返回 <paramref name="current"/>）。
    ///   上/下边缘与四个角都在此列 —— 这就是「高度锁定」的实现。</item>
    /// <item>拖的正好是**右边缘** → 只放行 <c>Right</c>，<c>Left/Top/Bottom</c> 一律按当前值钉死。
    ///   拖右边缘本来就只改宽度，这里再把 Top/Bottom 显式钉一遍，是为了不依赖
    ///   「系统不会乱提」这个假设。</item>
    /// <item>宽度不得小于 <paramref name="minWidthPx"/>。</item>
    /// </list>
    /// 自由模式（<paramref name="attached"/> = false）原样返回提议值。
    /// </summary>
    public static (int Left, int Top, int Right, int Bottom) CorrectSize(
        int edge,
        (int Left, int Top, int Right, int Bottom) current,
        (int Left, int Top, int Right, int Bottom) proposed,
        int minWidthPx,
        bool attached)
    {
        if (!attached) return proposed;
        if (edge != WmszRight) return current;

        int minRight = current.Left + (minWidthPx < 1 ? 1 : minWidthPx);
        int right = proposed.Right < minRight ? minRight : proposed.Right;

        return (current.Left, current.Top, right, current.Bottom);
    }

    // ------------------------------------------------------------------ 钩子

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmSizing) return IntPtr.Zero;

        // 自由模式完全不插手：四边可拖、高度随用户（隔离期就是这样）
        if (!Attached) return IntPtr.Zero;

        // 拿不到当前矩形就放弃干预 —— 宁可让用户拖得难看，也不要写出一个错位的矩形
        if (!GetWindowRect(hwnd, out NativeRect cur)) return IntPtr.Zero;

        NativeRect want = Marshal.PtrToStructure<NativeRect>(lParam);

        var rect = CorrectSize(
            wParam.ToInt32(),
            (cur.Left, cur.Top, cur.Right, cur.Bottom),
            (want.Left, want.Top, want.Right, want.Bottom),
            DipToPx(MinWidth, GetDpiForWindow(hwnd)),
            true);

        want.Left = rect.Left;
        want.Top = rect.Top;
        want.Right = rect.Right;
        want.Bottom = rect.Bottom;
        Marshal.StructureToPtr(want, lParam, false);

        // WM_SIZING 的约定：改好的矩形留在 lParam 里、标记已处理，系统照它继续。
        // 这里**必须**标记 handled，否则 WPF 还会再走一遍默认处理。
        handled = true;
        return IntPtr.Zero;
    }
}
