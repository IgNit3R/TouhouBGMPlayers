using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
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
    /// <summary>
    /// 运行时版本。**不是**构建用的 SDK 版本 —— SDK（10.0.401）程序里没有任何地方记录它，
    /// 想显示得在 csproj 里另注入。程序是框架依赖发布（SelfContained=false），所以这里取到的
    /// 是「跑这个 exe 的机器上装的那套 .NET」版本，换机器会变，这是预期行为。
    /// Microsoft.WindowsDesktop.App（WPF / WinForms）随基础运行时成套发，同号。
    /// </summary>
    private static string RuntimeVersion() => Environment.Version.ToString();

    /// <summary>
    /// 外部依赖树。(深度, 是否末项, 名称, 版本, 许可)。
    /// 深度 0 = 顶层，1 = 上一行的子项；「是否末项」决定树形导线画 ├ 还是 └，**只在深度 &gt; 0 时有意义**。
    ///
    /// 只列**直接引用**的第三方包；名字必须是程序集简单名（AssemblyVersionOf 走 AppDomain
    /// 扫描 + dll 元数据）。传递依赖不列，属实现细节 —— 也就是 NAudio 自己带出来的
    /// NAudio.Asio / Dmo / Midi / WinForms，以及 NAudio.Core 带出来的 System.Numerics.Tensors。
    /// 运行时不算「依赖」，它单独占信息区一行（见 Show）。
    /// </summary>
    private static (int Depth, bool IsLast, string Name, string Version, string License)[] Dependencies()
    {
        return new (int, bool, string, string, string)[]
        {
            // 输出链路真正调用的是 WasapiPlayer / WasapiPlayerBuilder，它们在 NAudio.Wasapi 里
            (0, false, "NAudio",        AssemblyVersionOf("NAudio"),        "MIT"),
            (1, false, "NAudio.Core",   AssemblyVersionOf("NAudio.Core"),   "MIT"),
            (1, true,  "NAudio.Wasapi", AssemblyVersionOf("NAudio.Wasapi"), "MIT"),
            // 黄昏作 OGG 解码（NVorbis 的现代分支，命名空间仍是 NVorbis）
            (0, false, "VorbisPizza",   AssemblyVersionOf("VorbisPizza"),   "MIT"),
            // 新典 Opus 解码，纯托管
            (0, false, "Concentus",     AssemblyVersionOf("Concentus"),     "BSD-3-Clause"),
        };
    }

    private static string AssemblyVersionOf(string name)
    {
        try
        {
            // 已经加载过的直接读，省一次磁盘 IO
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                var v0 = a.GetName().Version;
                if (v0 is not null) return v0.ToString(3);
            }

            // 没加载过就读 dll 的元数据 —— AssemblyName.GetAssemblyName 只解析文件头，
            // 不会真的把程序集载进进程（VorbisPizza / Concentus 只在播对应作品时才用得上，
            // 冷启动就开「关于」不该为了显示版本号把它们加载进来）。
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, name + ".dll");
            if (System.IO.File.Exists(path))
            {
                var v = AssemblyName.GetAssemblyName(path).Version;
                if (v is not null) return v.ToString(3);
            }
        }
        catch
        {
            // 取不到版本号不算错误，显示占位符即可，不能让对话框打不开
        }

        return "—";
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

        // ---- 依赖树 ----
        // 树形导线用 1px 矩形画，不用 ├ └ 制表符：Consolas 对 U+2500 区段的覆盖没保证，
        // WPF 走字体回退后字宽可能和 Consolas 不一致，名字起点就会漂。
        var depRows = new StackPanel();
        foreach (var (depth, isLast, name, ver, lic) in Dependencies())
        {
            bool child = depth > 0;

            // 顶层行**不预留**导线列：否则整列名字会被推右 18px，看着像"没靠左"。
            // 只有子项才占导线列并缩进。col0 + col1 恒为 210，所以版本/许可两列
            // 在任何行上起点都一样，仍然对齐。
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(child ? 18 : 0) });  // 树形导线
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(child ? 192 : 210) }); // 名称
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });  // 版本
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            if (child)
            {
                // 两行等分的网格：竖线贯穿整行（末项只贯穿上半，到下沿的横线为止），
                // 横线跨两行居中，正好接在中线上。这样无论行高多少都对齐。
                var guide = new Grid();
                guide.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                guide.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                var vertical = new Rectangle
                {
                    Width = 1,
                    Fill = dim,
                    Opacity = 0.45,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Margin = new Thickness(5, 0, 0, 0),
                };
                Grid.SetRow(vertical, 0);
                if (!isLast) Grid.SetRowSpan(vertical, 2);

                var horizontal = new Rectangle
                {
                    Height = 1,
                    Width = 8,
                    Fill = dim,
                    Opacity = 0.45,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(5, 0, 0, 0),
                };
                Grid.SetRowSpan(horizontal, 2);

                guide.Children.Add(vertical);
                guide.Children.Add(horizontal);
                Grid.SetColumn(guide, 0);
                g.Children.Add(guide);
            }

            var c0 = new TextBlock
            {
                Text = name,
                Foreground = dim,
                FontFamily = new Media.FontFamily("Consolas"),
                Margin = new Thickness(child ? 4 : 0, 0, 0, 0),
            };
            var c1 = new TextBlock { Text = ver, Foreground = dim, FontFamily = new Media.FontFamily("Consolas") };
            var c2 = new TextBlock { Text = lic, Foreground = dim, FontFamily = new Media.FontFamily("Consolas") };

            Grid.SetColumn(c0, 1);
            Grid.SetColumn(c1, 2);
            Grid.SetColumn(c2, 3);
            g.Children.Add(c0);
            g.Children.Add(c1);
            g.Children.Add(c2);
            depRows.Children.Add(g);
        }

        var depScroll = new ScrollViewer
        {
            // 当前 5 行（NAudio 带两个子项 + 两个解码器）只要 ≈ 110px，取 180 留足余量。
            // 再往上加依赖时这里会自动出滚动条，不会把窗口撑破。
            MaxHeight = 180,
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
        // 「灵界版」只算**没有** MainLabel/AltLabel 的那些作品（目前只有 TH13）。
        // 有标签的作品（新典 ↔ 原典）虽然也走同一套副版机制，但跟灵界版有本质区别，
        // 不能并进来计数 —— 之前直接 Sum(AltCount) 得出 30，等于把新典的 17 首原典版也算成灵界版。
        int legacyAlt = TrackIndex.Games.Where(g => !g.HasVariantLabels).Sum(g => g.AltCount);

        root.Children.Add(new TextBlock
        {
            Text = $"内嵌曲目 {TrackIndex.TotalTracks} 首 / {TrackIndex.Games.Count} 部作品"
                 + (legacyAlt > 0 ? $"（另含 {legacyAlt} 首灵界版）" : ""),
            Foreground = dim,
            Margin = new Thickness(0, 2, 0, 0),
        });
        // 运行时占信息区一行，不进下面的依赖树 —— 它不是第三方依赖，而是「跑这个 exe 需要装什么」。
        // 末尾的 ※ 与下方说明行呼应：那个说明注的就是 WinForms 的使用范围。
        root.Children.Add(new TextBlock
        {
            Text = $"运行时 .NET Desktop Runtime {RuntimeVersion()}（含 WPF / Windows Forms※）",
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
            // 高度跟随内容：依赖表露全就长、露不全也不会被裁。
            // 上限交给上面 ScrollViewer 的 MaxHeight 兜住，这里只留一个安全阀。
            SizeToContent = SizeToContent.Height,
            MinWidth = 400,
            MinHeight = 360,
            MaxHeight = 900,
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
