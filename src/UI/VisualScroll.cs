using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ThbgmPlayer.UI;

/// <summary>
/// DataGrid 内部 ScrollViewer 垂直偏移的读写。主窗口重建（换主题）时
/// 用来把曲目表的滚动位置搬过去。
/// ⚠️ 读取在旧窗口关闭前做没问题；写入必须等新窗口布局完成（Loaded 之后）：
/// 排版没完成时 ScrollToVerticalOffset 会被 clamp 成 0。
/// </summary>
internal static class VisualScroll
{
    public static double OffsetOf(DependencyObject root)
    {
        var sv = Find(root);
        return sv is null || !double.IsFinite(sv.VerticalOffset) ? 0 : sv.VerticalOffset;
    }

    public static void SetOffset(DependencyObject root, double offset)
    {
        var sv = Find(root);
        if (sv is null || !double.IsFinite(offset) || offset <= 0) return;
        sv.ScrollToVerticalOffset(offset);
    }

    private static ScrollViewer? Find(DependencyObject node)
    {
        if (node is ScrollViewer sv) return sv;
        int n = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < n; i++)
        {
            if (Find(VisualTreeHelper.GetChild(node, i)) is ScrollViewer f) return f;
        }
        return null;
    }
}
