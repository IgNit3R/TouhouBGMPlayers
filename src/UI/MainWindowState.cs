using System.Windows;
using ThbgmPlayer.Core;
using ThbgmPlayer.Data;

namespace ThbgmPlayer.UI;

/// <summary>
/// 主窗口重建（换主题）时跨窗口搬运的那一小撮状态。
///
/// ⚠️ 刻意不搬播放状态（当前曲目 / 位置 / 是否在播 / alt / 音量）：
/// 那些住在 <see cref="AppServices.Engine"/> 里，引擎跨窗口存活，搬了反而会把位置搞丢。
/// 窗口几何也不经 settings.json：CaptureState 取的是 RestoreBounds
/// （最大化时 Width 已是屏幕尺寸，直接搬会把「还原尺寸」弄丢），
/// 由新窗口直接应用，Closing 的 rebuilding 分支不再写 Ui.*。
/// public 只因 MainWindow.CaptureState / RestoreState 是 public（XAML 分部类里
/// internal 类型当 public 方法的签名会被 CS0050/CS0051 拦下）；类型本身只在主题
/// 切换链路里流转。
/// </summary>
public sealed class MainWindowState
{
    // ---- 窗口几何 ----
    public double Left;
    public double Top;
    public double Width;
    public double Height;
    public WindowState State;

    // ---- 播放列表（三元组找回，照 RebuildPlaylistsKeepSelection 的写法） ----
    public string? PlaylistGameId;
    public bool PlaylistIsFavorites;
    public CustomPlaylist? PlaylistCustom;

    // ---- 曲目表 ----
    public TrackRef? SelectedRef;
    public double GridScrollOffset;
}
