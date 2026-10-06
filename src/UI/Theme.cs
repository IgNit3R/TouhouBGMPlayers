using System.Windows;
using ThbgmPlayer.Core;
using Media = System.Windows.Media;

namespace ThbgmPlayer.UI;

/// <summary>
/// 从应用主题取画刷。
///
/// PromptDialog / ExportDialog 是用代码搭的，拿不到 XAML 的 StaticResource，
/// 只能显式取。不这么做的话它们各硬编码一份颜色，改色板时必然会漏。
///
/// 三级查找：
///   ① 应用层资源字典（ThemeManager 加载的当前主题色表）；
///   ② 常驻的深色基准表（ThemeManager.Canonical）—— 键名写错、字典没换过来时
///      退到「已知正确的深色」，界面绝不至于看不见；
///   ③ Transparent —— 两级都没有也不抛，交由调用方继续跑。
///
/// 旧签名曾带十六进制兜底实参：随浅色主题引入一并删除 —— 「代码里不留颜色
/// 字面量」是硬规矩，且一套写死的兜底色在任何一套主题下都只会误导。
/// </summary>
internal static class Theme
{
    public static Media.Brush Get(string key)
    {
        try
        {
            if (Application.Current?.Resources[key] is Media.Brush b)
                return b;
        }
        catch
        {
            // 资源未就位，走下一级
        }

        try
        {
            if (ThemeManager.Canonical[key] is Media.Brush f)
                return f;
        }
        catch
        {
            // 兜底源也没有这个键，走 Transparent
        }

        return Media.Brushes.Transparent;
    }

    /// <summary>
    /// 用户设置的界面字体。设置为空或字体名坏了（设置文件被手改）返回 null ——
    /// 赋值给 Window.FontFamily 等于用默认，绝不让字体把程序搞崩。
    /// 不缓存：设置里改了字体之后开的窗口要立刻用上新字体。
    /// </summary>
    public static Media.FontFamily? UserFontFamily => Resolve(AppSettings.Current.Ui.FontFamily);

    /// <summary>用户设置的曲名字体（日文内容专用），语义同 <see cref="UserFontFamily"/>。</summary>
    public static Media.FontFamily? UserContentFontFamily => Resolve(AppSettings.Current.Ui.ContentFontFamily);

    private static Media.FontFamily? Resolve(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        try { return new Media.FontFamily(s); }
        catch { return null; }
    }

    /// <summary>
    /// 把用户在设置里选的界面字体套到窗口上（主窗口、设置窗口统一走这里；
    /// 代码对话框直接在初始化器里用 <see cref="UserFontFamily"/>）。
    /// </summary>
    public static void ApplyUserFont(Window w)
    {
        var f = UserFontFamily;
        if (f is not null) w.FontFamily = f;
    }

    /// <summary>把曲名字体套到指定元素上（曲目表、正在播放区、导出清单）。
    /// 走 TextElement 附加属性：Control.FontFamilyProperty 与 TextBlock.FontFamilyProperty
    /// 其实是同一个依赖属性（Control 是 AddOwner 注册），所以这一调用对两者都生效，
    /// 不需要按类型分派。</summary>
    public static void ApplyContentFont(FrameworkElement el)
    {
        var f = UserContentFontFamily;
        if (f is not null) System.Windows.Documents.TextElement.SetFontFamily(el, f);
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
