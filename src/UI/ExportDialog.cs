using System.IO;
using System.Windows;
using System.Windows.Controls;
using ThbgmPlayer.Audio;
using ThbgmPlayer.Core;
using ThbgmPlayer.Data;
using Media = System.Windows.Media;

namespace ThbgmPlayer.UI;

/// <summary>
/// 导出对话框。用代码搭建而不是 XAML —— 内容固定、不需要样式复用，
/// 也免得为「灵界版选项按曲目有无决定是否显示」这种动态部分去写触发器。
///
/// 三个数值字段各自独立地「跟随播放参数 / 覆盖」（DESIGN_v3.md §8.1）：
/// 勾上跟随就存 null，实时取播放参数的值；取消勾选并填值则固定下来，
/// 之后播放参数再变也不跟着变，同时也不影响播放参数。
/// </summary>
public static class ExportDialog
{
    // 类型是 Brush 而不是 SolidColorBrush：Theme.Get 从资源字典取，
    // 取到的是什么实现不定（也可能是渐变、图片画刷），不该在这里收窄。
    private static readonly Media.Brush Bg = Theme.Get("BgDeep", "#FF171717");
    private static readonly Media.Brush Fg = Theme.Get("Text", "#FFEDEDED");
    private static readonly Media.Brush Dim = Theme.Get("TextDim", "#FFADADB4");
    private static readonly Media.Brush Box = Theme.Get("BgElevated", "#FF2E2E31");
    private static readonly Media.Brush Btn = Theme.Get("BgElevated", "#FF2E2E31");
    private static readonly Media.Brush Bd = Theme.Get("Border", "#FF3F3F45");

    /// <param name="items">要导出的曲目，允许跨作品。</param>
    /// <param name="initialAlt">灵界版的初始勾选；没有曲目带灵界版时不显示这一项。</param>
    public static void Show(Window owner,
                            IReadOnlyList<(GameDef Game, TrackDef Track)> items,
                            bool initialAlt = false)
    {
        if (items.Count == 0) return;

        var exp = AppSettings.Current.Export;
        var pb = AppSettings.Current.Playback;

        // ---------- 曲目清单 ----------
        var listBox = new ListBox
        {
            Height = Math.Min(150, 26 + items.Count * 20),
            Background = Box, Foreground = Fg, BorderBrush = Bd,
            // 曲名是日文内容，用曲名字体（对话框其余部分是中文 UI 字体）
            FontFamily = Theme.UserContentFontFamily ?? Theme.UserFontFamily,
            ItemsSource = items.Select(x => $"{x.Game.Code} - {x.Track.No:00} - {x.Track.Title}").ToList(),
        };

        // ---------- 时间线参数 ----------
        var nBox = NumBox((exp.LoopCount ?? pb.LoopCount).ToString());
        var xBox = NumBox((exp.ExtraSeconds ?? pb.ExtraSeconds).ToString("0.##"));
        var fBox = NumBox((exp.FadeSeconds ?? pb.FadeSeconds).ToString("0.##"));

        var nFollow = FollowBox("跟随", exp.LoopCount is null);
        var xFollow = FollowBox("跟随", exp.ExtraSeconds is null);
        var fFollow = FollowBox("跟随", exp.FadeSeconds is null);

        // 勾上「跟随」就用播放参数的值，输入框跟着禁用
        void WireFollow(CheckBox cb, TextBox box)
        {
            box.IsEnabled = cb.IsChecked != true;
            cb.Checked += (_, _) => box.IsEnabled = false;
            cb.Unchecked += (_, _) => box.IsEnabled = true;
        }
        WireFollow(nFollow, nBox);
        WireFollow(xFollow, xBox);
        WireFollow(fFollow, fBox);

        var paramGrid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        paramGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        paramGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        paramGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int i = 0; i < 3; i++)
            paramGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddParamRow(paramGrid, 0, "循环次数 N", nBox, nFollow, "循环段重复几遍");
        AddParamRow(paramGrid, 1, "额外秒数 X", xBox, xFollow, "N 遍播满后再续播多少秒");
        AddParamRow(paramGrid, 2, "淡出秒数 F", fBox, fFollow, "额外加在最后的一段，边播边淡出");

        // ---------- 版本（仅当选中曲目里有带灵界版的） ----------
        RadioButton? rbMain = null, rbAlt = null;
        UIElement? versionRow = null;

        if (items.Any(x => x.Track.HasAlt))
        {
            rbMain = new RadioButton { Content = "主版", IsChecked = !initialAlt, Foreground = Fg, Margin = new Thickness(0, 0, 16, 0) };
            rbAlt = new RadioButton { Content = "霊界版", IsChecked = initialAlt, Foreground = Fg };

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            row.Children.Add(new TextBlock { Text = "版本", Width = 110, Foreground = Dim, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(rbMain);
            row.Children.Add(rbAlt);

            int lacking = items.Count(x => !x.Track.HasAlt);
            if (lacking > 0)
                row.Children.Add(new TextBlock
                {
                    Text = $"（其中 {lacking} 首没有霊界版，将导出主版）",
                    Foreground = Dim, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(16, 0, 0, 0),
                });

            versionRow = row;
        }

        // ---------- 输出目录 ----------
        var dirBox = new TextBox
        {
            Text = WavExporter.OutputDirectory, Padding = new Thickness(5, 4, 5, 4),
            Background = Box, Foreground = Fg, CaretBrush = Fg, BorderBrush = Bd,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var browse = new Button { Content = "浏览…", MinWidth = 76, Padding = new Thickness(12, 4, 12, 4), Background = Btn, Foreground = Fg, BorderBrush = Bd };

        var dirGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        dirGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dirGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(dirBox, 0);
        Grid.SetColumn(browse, 1);
        browse.Margin = new Thickness(8, 0, 0, 0);
        dirGrid.Children.Add(dirBox);
        dirGrid.Children.Add(browse);

        // ---------- 进度 ----------
        var progress = new ProgressBar { Height = 6, Minimum = 0, Maximum = 1, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 14, 0, 0) };
        var status = new TextBlock { Text = "", Foreground = Dim, Margin = new Thickness(0, 6, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };

        // ---------- 按钮 ----------
        var ok = new Button { Content = "导出", IsDefault = true, MinWidth = 84, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0), Background = Btn, Foreground = Fg, BorderBrush = Bd };
        var cancel = new Button { Content = "取消", MinWidth = 84, Padding = new Thickness(12, 4, 12, 4), Background = Btn, Foreground = Fg, BorderBrush = Bd };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0), Children = { ok, cancel },
        };

        // ---------- 组装 ----------
        var root = new StackPanel { Margin = new Thickness(14) };
        root.Children.Add(new TextBlock { Text = $"将导出 {items.Count} 首", Foreground = Dim, Margin = new Thickness(0, 0, 0, 6) });
        root.Children.Add(listBox);
        root.Children.Add(new TextBlock { Text = "时间线   intro → loop × N → 额外 X 秒 → 淡出 F 秒", Foreground = Dim, Margin = new Thickness(0, 16, 0, 0) });
        root.Children.Add(paramGrid);
        if (versionRow is not null) root.Children.Add(versionRow);
        root.Children.Add(new TextBlock { Text = "输出到", Foreground = Dim, Margin = new Thickness(0, 14, 0, 0) });
        root.Children.Add(dirGrid);
        root.Children.Add(progress);
        root.Children.Add(status);
        root.Children.Add(buttons);

        var win = new Window
        {
            Owner = owner,
            Title = $"导出音频（{items.Count} 首）",
            Icon = Theme.AppIcon,
            Width = 620, MinWidth = 520, Height = 512, MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Bg, Foreground = Fg,
            FontFamily = owner.FontFamily, FontSize = 13,
            Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root },
        };

        var cts = new CancellationTokenSource();
        bool running = false;

        browse.Click += (_, _) =>
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择导出目录",
                SelectedPath = Directory.Exists(dirBox.Text.Trim()) ? dirBox.Text.Trim() : "",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true,
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                dirBox.Text = dlg.SelectedPath;
        };

        cancel.Click += (_, _) =>
        {
            if (running) cts.Cancel();
            else win.DialogResult = false;
        };

        ok.Click += async (_, _) => await RunAsync();

        // 导出途中不许直接关窗，先中止
        win.Closing += (_, e) =>
        {
            if (!running) return;
            e.Cancel = true;
            cts.Cancel();
        };

        async Task RunAsync()
        {
            // 收集并保存参数（null = 跟随播放参数）
            exp.LoopCount = nFollow.IsChecked == true ? null : ParseInt(nBox.Text, exp.LoopCount);
            exp.ExtraSeconds = xFollow.IsChecked == true ? null : ParseDouble(xBox.Text, exp.ExtraSeconds);
            exp.FadeSeconds = fFollow.IsChecked == true ? null : ParseDouble(fBox.Text, exp.FadeSeconds);
            exp.Directory = dirBox.Text.Trim();
            AppSettings.Current.Save();

            var p = WavExporter.ResolveParams();
            bool useAlt = rbAlt?.IsChecked == true;

            running = true;
            ok.IsEnabled = false;
            browse.IsEnabled = false;
            nBox.IsEnabled = xBox.IsEnabled = fBox.IsEnabled = false;
            nFollow.IsEnabled = xFollow.IsEnabled = fFollow.IsEnabled = false;
            if (rbMain is not null) rbMain.IsEnabled = rbAlt!.IsEnabled = false;
            cancel.Content = "中止";
            progress.Visibility = Visibility.Visible;

            var reporter = new Progress<(string File, double Ratio)>(t =>
            {
                progress.Value = t.Ratio;
                status.Text = Path.GetFileName(t.File);
            });

            int done = 0;
            string? error = null;

            try
            {
                foreach (var (g, t) in items)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    status.Text = $"（{done + 1}/{items.Count}）{g.Code} - {t.No:00} - {t.Title}";

                    await WavExporter.ExportAsync(g, t, useAlt, p, null, reporter, cts.Token);
                    done++;
                    progress.Value = (double)done / items.Count;
                }
            }
            catch (OperationCanceledException)
            {
                error = "已中止";
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            running = false;
            progress.Visibility = Visibility.Collapsed;

            if (error is not null)
            {
                // 留在窗口里让用户看到原因，顺便能改参数重试
                status.Text = error;
                ok.IsEnabled = true;
                browse.IsEnabled = true;
                nBox.IsEnabled = nFollow.IsChecked != true;
                xBox.IsEnabled = xFollow.IsChecked != true;
                fBox.IsEnabled = fFollow.IsChecked != true;
                nFollow.IsEnabled = xFollow.IsEnabled = fFollow.IsEnabled = true;
                if (rbMain is not null) rbMain.IsEnabled = rbAlt!.IsEnabled = true;
                cancel.Content = "关闭";
                return;
            }

            MessageBox.Show(win,
                $"已导出 {done} 个文件到：\n{WavExporter.OutputDirectory}",
                "导出完成", MessageBoxButton.OK, MessageBoxImage.Information);

            win.DialogResult = true;   // 赋值即关闭
        }

        win.ShowDialog();
    }

    private static void AddParamRow(Grid g, int row, string label, TextBox box, CheckBox follow, string hint)
    {
        box.Margin = new Thickness(0, 3, 0, 3);

        var tb = new TextBlock
        {
            Text = label, Foreground = Dim,
            VerticalAlignment = VerticalAlignment.Center, ToolTip = hint,
        };

        Grid.SetRow(tb, row); Grid.SetColumn(tb, 0);
        Grid.SetRow(box, row); Grid.SetColumn(box, 1);
        Grid.SetRow(follow, row); Grid.SetColumn(follow, 2);

        g.Children.Add(tb);
        g.Children.Add(box);
        g.Children.Add(follow);
    }

    private static TextBox NumBox(string text) => new()
    {
        Text = text, Width = 84, Padding = new Thickness(5, 4, 5, 4),
        Background = Box, Foreground = Fg, CaretBrush = Fg, BorderBrush = Bd,
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    private static CheckBox FollowBox(string text, bool on) => new()
    {
        Content = text, IsChecked = on, Foreground = Dim,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(10, 0, 0, 0),
    };

    private static int? ParseInt(string s, int? fallback) =>
        int.TryParse(s.Trim(), out var v) ? Math.Clamp(v, 0, 999) : fallback;

    private static double? ParseDouble(string s, double? fallback) =>
        double.TryParse(s.Trim(), out var v) ? Math.Clamp(v, 0, 3600) : fallback;
}
