using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ThbgmPlayer.Core;
using ThbgmPlayer.Data;
using Media = System.Windows.Media;

namespace ThbgmPlayer.UI;

/// <summary>
/// 「关于」对话框。用代码搭建，与 PromptDialog / ExportDialog 一致，
/// 画刷统一从主题取 —— 之前用 MessageBox，是系统浅色，和播放器整体不搭。
///
/// 内容：版本、构建时间、内嵌曲目规模，以及可滚动的依赖表。
/// 不再显示程序目录（用户要求去掉）。
/// </summary>
public static class AboutDialog
{
    /// <summary>(名称, 版本, 许可)</summary>
    private static (string Name, string Version, string License)[] Dependencies()
    {
        // 不在这里塞说明行 —— 那是注释不是依赖。说明行单独渲染并撑满整宽。
        return new (string, string, string)[]
        {
            (".NET", Environment.Version.ToString(), "MIT"),
            ("NAudio", AssemblyVersionOf("NAudio"), "MIT"),
            ("NAudio.Core", AssemblyVersionOf("NAudio.Core"), "MIT"),
            ("WindowsDesktop（WPF）", "随 .NET", "MIT"),
            ("Windows Forms", "随 .NET", "MIT"),
        };
    }

    private static string AssemblyVersionOf(string name)
    {
        try
        {
            var v = Assembly.Load(name).GetName().Version;
            return v is null ? "—" : v.ToString(3);
        }
        catch
        {
            // 程序集没被加载（还没用到）时显示不出版本，不算错误
            return "—";
        }
    }

    /// <summary>构建时间。取不到注入的元数据就退回 exe 的修改时间。</summary>
    private static string BuildTime()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            foreach (var a in asm.GetCustomAttributes<AssemblyMetadataAttribute>())
            {
                if (a.Key == "BuildTimestamp" && !string.IsNullOrWhiteSpace(a.Value))
                    return a.Value!;
            }
        }
        catch
        {
            // 元数据读不到，走下面的回退
        }

        try
        {
            var path = Environment.ProcessPath;
            if (path is not null && System.IO.File.Exists(path))
                return System.IO.File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch
        {
            // 都取不到就不显示时间，也不该让对话框打不开
        }

        return "—";
    }

    public static void Show(Window owner)
    {
        var bg = Theme.Get("BgDeep", "#FF171717");
        var fg = Theme.Get("Text", "#FFEDEDED");
        var dim = Theme.Get("TextDim", "#FFADADB4");
        var panel = Theme.Get("BgPanel", "#FF222224");
        var border = Theme.Get("Border", "#FF3F3F45");
        var btnBg = Theme.Get("BgElevated", "#FF2E2E31");
        var em = Theme.Get("Emphasis", "#FF9CDCFE");

        TextBlock Info(string text, Media.Brush color, double size, bool bold = false) =>
            new()
            {
                Text = text,
                Foreground = color,
                FontSize = size,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                Margin = new Thickness(0, 3, 0, 0),
            };

        // ---- 依赖表 ----
        var depRows = new StackPanel();
        foreach (var (name, ver, lic) in Dependencies())
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var c0 = new TextBlock { Text = name, Foreground = dim, FontFamily = new Media.FontFamily("Consolas") };
            var c1 = new TextBlock { Text = ver, Foreground = dim, FontFamily = new Media.FontFamily("Consolas") };
            var c2 = new TextBlock { Text = lic, Foreground = dim, FontFamily = new Media.FontFamily("Consolas") };

            Grid.SetColumn(c0, 0);
            Grid.SetColumn(c1, 1);
            Grid.SetColumn(c2, 2);
            g.Children.Add(c0);
            g.Children.Add(c1);
            g.Children.Add(c2);
            depRows.Children.Add(g);
        }

        var depScroll = new ScrollViewer
        {
            MaxHeight = 150,
            Content = depRows,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(8, 6, 8, 6),
        };
        Border depBox = new()
        {
            Background = panel,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Child = depScroll,
        };

        var close = new Button
        {
            Content = "关闭",
            IsDefault = true,
            IsCancel = true,
            MinWidth = 84,
            Padding = new Thickness(12, 4, 12, 4),
            Background = btnBg,
            Foreground = fg,
            BorderBrush = border,
        };

        var root = new StackPanel { Margin = new Thickness(16) };
        // 标题文字与窗口标题保持一致（三处：这里、MainWindow.xaml、UpdateStatus）
        root.Children.Add(Info("东方ProjectBGM播放器", fg, 15, bold: true));
        root.Children.Add(new TextBlock { Text = $"版本 {AppPaths.AppVersion}", Foreground = em, Margin = new Thickness(0, 4, 0, 0) });
        root.Children.Add(new TextBlock { Text = $"构建 {BuildTime()}", Foreground = dim, Margin = new Thickness(0, 2, 0, 0) });
        root.Children.Add(new TextBlock
        {
            Text = $"内嵌曲目 {TrackIndex.TotalTracks} 首 / {TrackIndex.Games.Count} 部作品（另含 {TrackIndex.Games.Sum(g => g.AltCount)} 首灵界版）",
            Foreground = dim,
            Margin = new Thickness(0, 2, 0, 0),
        });
        root.Children.Add(new TextBlock { Text = "依赖", Foreground = dim, Margin = new Thickness(0, 14, 0, 4) });
        root.Children.Add(depBox);

        // 说明行单独渲染，撑满整个对话框宽度 —— 之前塞在依赖表里被列宽 210 截掉了"框"字。
        root.Children.Add(new TextBlock
        {
            Text = "※ 仅使用了 WinForms 下的 FolderBrowserDialog 一个类型",
            Foreground = dim,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 6, 2, 0),
        });

        root.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
            Children = { close },
        });

        var win = new Window
        {
            Owner = owner,
            Title = "关于",
            Icon = Theme.AppIcon,
            Width = 460,
            Height = 440,
            MinWidth = 400,
            MinHeight = 360,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = bg,
            Foreground = fg,
            FontFamily = owner.FontFamily,
            FontSize = 13,
            Content = root,
        };

        close.Click += (_, _) => win.Close();
        win.ShowDialog();
    }
}
