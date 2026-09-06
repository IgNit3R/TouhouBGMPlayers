using System.Windows;
using Media = System.Windows.Media;

namespace ThbgmPlayer.UI;

/// <summary>
/// 从应用主题取画刷。
///
/// PromptDialog / ExportDialog 是用代码搭的，拿不到 XAML 的 StaticResource，
/// 只能显式取。不这么做的话它们各硬编码一份颜色，改色板时必然会漏。
///
/// 取不到（资源还没合并、键名写错、设计器环境）时按备用色值构造一个，
/// 保证既不炸也不至于看不见。
/// </summary>
internal static class Theme
{
    public static Media.Brush Get(string key, string fallbackHex)
    {
        try
        {
            if (Application.Current?.Resources[key] is Media.Brush b)
                return b;
        }
        catch
        {
            // 资源未就位，走下面的回退
        }

        return new Media.SolidColorBrush((Media.Color)Media.ColorConverter.ConvertFromString(fallbackHex));
    }

    private static Media.ImageSource? _appIcon;

    /// <summary>
    /// 应用图标（标题栏左上角）。给代码搭的对话框用；XAML 窗口直接写
    /// Icon="Resources/bgmplayer.ico"。图标已作为 Resource 打进程序集，加载失败就 null，
    /// 窗口退回默认图标，不影响功能。
    /// </summary>
    public static Media.ImageSource? AppIcon
    {
        get
        {
            if (_appIcon is not null) return _appIcon;
            try
            {
                var img = new Media.Imaging.BitmapImage(
                    new Uri("pack://application:,,,/Resources/bgmplayer.ico", UriKind.Absolute));
                img.Freeze();   // 跨线程也能用（对话框在 UI 线程，保险起见）
                _appIcon = img;
            }
            catch
            {
                // 资源没打进去就算了，返回 null 让窗口用默认图标
            }
            return _appIcon;
        }
    }
}
