using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace ThbgmPlayer.Core;

/// <summary>
/// 全局多媒体键（播放/暂停、上一首、下一首）。用 RegisterHotKey 拦截系统媒体键：
/// 注册成功后按键以 WM_HOTKEY 送达本窗口 —— 不管是否聚焦，且此时 WPF 键事件
/// 不会再收到它们（系统拦截了），所以不会和窗口里的 PreviewKeyDown 兜底重复触发。
///
/// 三个键「全有或全无」：任何一个注册失败（多半是被别的程序抢先注册了）就整体
/// 放弃，退回到「仅聚焦时响应」。只拦一部分键的行为对用户来说更费解。
/// </summary>
public sealed class MediaKeysHotkey : IDisposable
{
    public const int PlayPauseId = 1;
    public const int PrevId = 2;
    public const int NextId = 3;

    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkMediaNextTrack = 0xB0;
    private const uint VkMediaPrevTrack = 0xB1;
    private const uint VkMediaPlayPause = 0xB3;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly System.Windows.Window _window;
    private readonly Action<int> _onKey;
    private HwndSource? _source;
    private IntPtr _hwnd;

    /// <summary>三个键都注册成功为 true；false = 被占用或尚未注册。</summary>
    public bool IsActive { get; private set; }

    /// <param name="onKey">UI 线程回调，参数是 PlayPauseId / PrevId / NextId。</param>
    public MediaKeysHotkey(System.Windows.Window window, Action<int> onKey)
    {
        _window = window;
        _onKey = onKey;
    }

    /// <summary>注册。幂等：重复调用会先注销旧的再来。</summary>
    public void Register()
    {
        Unregister();

        _hwnd = new WindowInteropHelper(_window).Handle;
        if (_hwnd == IntPtr.Zero) return;

        _source = HwndSource.FromHwnd(_hwnd);
        if (_source is null) return;
        _source.AddHook(WndProc);

        bool ok =
            RegisterHotKey(_hwnd, PlayPauseId, ModNoRepeat, VkMediaPlayPause) &&
            RegisterHotKey(_hwnd, PrevId, ModNoRepeat, VkMediaPrevTrack) &&
            RegisterHotKey(_hwnd, NextId, ModNoRepeat, VkMediaNextTrack);

        if (!ok)
        {
            Unregister();
            return;
        }
        IsActive = true;
    }

    public void Unregister()
    {
        if (_hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, PlayPauseId);
            UnregisterHotKey(_hwnd, PrevId);
            UnregisterHotKey(_hwnd, NextId);
        }
        _source?.RemoveHook(WndProc);
        _source = null;
        _hwnd = IntPtr.Zero;
        IsActive = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            _onKey(wParam.ToInt32());
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose() => Unregister();
}
