using System.Windows;
using System.Windows.Controls;
using Media = System.Windows.Media;

namespace ThbgmPlayer.UI;

/// <summary>
/// 只有一行输入框的小对话框，供新建 / 重命名播放列表使用。
/// 用代码搭建而不是 XAML —— 内容固定、不需要样式复用，少一个文件更好维护。
/// </summary>
public static class PromptDialog
{
    /// <returns>用户输入的内容（已 Trim）；点取消或内容为空返回 null。</returns>
    public static string? Show(Window owner, string title, string label, string? initial)
    {
        var bg = Theme.Get("BgDeep", "#FF171717");
        var fg = Theme.Get("Text", "#FFEDEDED");
        var dim = Theme.Get("TextDim", "#FFADADB4");
        var boxBg = Theme.Get("BgElevated", "#FF2E2E31");
        var btnBg = Theme.Get("BgElevated", "#FF2E2E31");
        var border = Theme.Get("Border", "#FF3F3F45");

        var tb = new TextBox
        {
            Text = initial ?? "",
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(5, 4, 5, 4),
            Background = boxBg,
            Foreground = fg,
            CaretBrush = fg,
            BorderBrush = border,
        };

        var ok = new Button
        {
            Content = "确定", IsDefault = true, MinWidth = 76,
            Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0),
            Background = btnBg,
            Foreground = fg, BorderBrush = border,
        };
        var cancel = new Button
        {
            Content = "取消", IsCancel = true, MinWidth = 76,
            Padding = new Thickness(12, 4, 12, 4),
            Background = btnBg,
            Foreground = fg, BorderBrush = border,
        };

        string? result = null;

        var win = new Window
        {
            Owner = owner,
            Title = title,
            Icon = Theme.AppIcon,
            Width = 440, Height = 178,
            MinWidth = 380, MinHeight = 178,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = bg,
            Foreground = fg,
            FontFamily = owner.FontFamily,
            FontSize = 13,
            Content = new StackPanel
            {
                Margin = new Thickness(14),
                Children =
                {
                    new TextBlock { Text = label, Foreground = dim },
                    tb,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(0, 16, 0, 0),
                        Children = { ok, cancel },
                    },
                },
            },
        };

        ok.Click += (_, _) => { result = tb.Text.Trim(); win.DialogResult = true; };
        cancel.Click += (_, _) => win.DialogResult = false;
        win.Loaded += (_, _) => { tb.Focus(); tb.SelectAll(); };

        return win.ShowDialog() == true && !string.IsNullOrEmpty(result) ? result : null;
    }
}
