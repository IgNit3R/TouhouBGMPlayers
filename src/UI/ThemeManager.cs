using System.Windows;
using ThbgmPlayer.Core;
using Application = System.Windows.Application;

namespace ThbgmPlayer.UI;

/// <summary>
/// settings.json（ui.theme）里存的主题名常量与归一化。
/// </summary>
public static class ThemeNames
{
    public const string Dark = "dark";
    public const string Light = "light";

    /// <summary>
    /// 归一化：空 / 未知值一律退回深色。老 settings.json 没有 theme 字段、
    /// 或文件被手改出奇怪值时走这里，保证启动永远拿得到一个合法主题。
    /// </summary>
    public static string Normalize(string? s) =>
        string.IsNullOrWhiteSpace(s) ? Dark
      : string.Equals(s, Light, StringComparison.OrdinalIgnoreCase) ? Light
      : Dark;
}

/// <summary>
/// 主题字典的加载与切换。
///
/// 三层结构（拆自原 Themes/DarkTheme.xaml：色表与样式合一的时代结束了）：
///   MergedDictionaries[0] = Themes/Colors.Dark.xaml 或 Colors.Light.xaml（色表）
///   MergedDictionaries[1] = Themes/Controls.xaml（样式，StaticResource 指向色表的键）
///
/// 两条铁律（违反任意一条要么启动炸要么切换只换一半）：
///   ① 色表必须排在样式之前：StaticResource 是解析期沿查找链求值，顺序反了
///      Controls.xaml 里的引用直接 XamlParseException；
///   ② 色表只在应用层加载这一份：Controls.xaml 内部不许再合并色表，
///      每多合并一次就多一套 Brush 实例，切换时另一个实例纹丝不动。
///
/// 样式层为什么也要一起重建：Style 的 Setter 在解析期就把 Brush 实例烘进
/// Setter.Value，只换色表不重载样式，控件会继续钉在旧颜色上。
/// </summary>
public static class ThemeManager
{
    private const string ControlsUri = "pack://application:,,,/Themes/Controls.xaml";

    private static string ColorsUri(string name) => name == ThemeNames.Light
        ? "pack://application:,,,/Themes/Colors.Light.xaml"
        : "pack://application:,,,/Themes/Colors.Dark.xaml";

    private static ResourceDictionary? _canonical;

    /// <summary>当前生效的主题名（已归一化）。</summary>
    public static string Current { get; private set; } = ThemeNames.Dark;

    /// <summary>
    /// 常驻的深色色表实例，充当 <see cref="Theme.Get(string)"/> 的兜底源：
    /// 当前字典查不到某个键（键名写错、字典没换过来）时退到「已知正确的深色」，
    /// 界面绝不至于看不见。刻意与运行中的字典保持两份独立实例 —— 它代表
    /// 「机器校验过的深色基准」，不随切换变化。
    /// </summary>
    internal static ResourceDictionary Canonical =>
        _canonical ??= new ResourceDictionary { Source = new Uri(ColorsUri(ThemeNames.Dark), UriKind.Absolute) };

    private static ResourceDictionary Load(string uri) =>
        new() { Source = new Uri(uri, UriKind.Absolute) };

    /// <summary>
    /// 启动时调用一次：按 settings.json 挂载对应色表 + 样式层。
    /// 必须在创建任何窗口之前调（含 --viz-selftest 分支 —— 自检的 XAML 也要解析资源）。
    /// </summary>
    public static void Initialize()
    {
        Current = ThemeNames.Normalize(AppSettings.Current.Ui.Theme);
        ApplyResources(Current);
    }

    /// <summary>
    /// 换字典。两个条目必须一起重建：色表换过去，样式层重新解析一次。
    /// </summary>
    internal static void ApplyResources(string name)
    {
        var res = Application.Current.Resources;
        res.MergedDictionaries.Clear();
        res.MergedDictionaries.Add(Load(ColorsUri(name)));   // [0] 色表：随主题换
        res.MergedDictionaries.Add(Load(ControlsUri));       // [1] 样式：必须重新解析
    }

    /// <summary>重入闸：连点菜单 / 菜单与设置页同时触发时，只放第一轮进去。</summary>
    private static int _switching;

    /// <summary>
    /// 换主题并立即生效。链路：写设置 → 快照 → 摘可视化 → 换字典 →
    /// 建新窗（应用快照）→ 关旧窗。中途炸了回滚到原主题并保住原窗口。
    ///
    /// ⚠️ 顺序敏感的两处：
    /// · 必须**先建后关**：ShutdownMode 是默认 OnLastWindowClose，
    ///   先关后开会让窗口计数瞬间归零，进程可能被直接带走；
    /// · 可视化必须**先摘**：附件窗口的 Owner 是旧主窗口，等 Close 级联带走
    ///   时序不可控，且 WindowHost 订阅要显式 Dispose（见 MainWindow.DetachViz）。
    /// </summary>
    public static void ApplyTheme(string rawName, Window? owner)
    {
        string name = ThemeNames.Normalize(rawName);
        string prev = Current;

        // 同名：只把设置同步一次就走（连点 / 重复进入）
        if (name == Current)
        {
            AppSettings.Current.Ui.Theme = name;
            AppSettings.Current.Save();
            return;
        }

        if (Interlocked.Exchange(ref _switching, 1) == 1) return;
        try
        {
            // ① 先快照 —— 后面任何一步炸了，旧窗口还在、状态没丢
            var old = owner as MainWindow;
            MainWindowState? snapshot = old?.CaptureState();

            // ② 落盘。写失败（只读目录）不阻断：主题在本次运行内仍然生效
            AppSettings.Current.Ui.Theme = name;
            AppSettings.Current.Save();

            // ③ 先把旧窗口的可视化摘干净（附件窗口自己把几何写回 settings）
            old?.MarkRebuilding();
            old?.DetachViz();

            // ④ 换字典。失败立刻回滚、不往下走（旧窗口虽丢了可视化，主界面还活着）
            try { ApplyResources(name); }
            catch
            {
                ApplyResources(prev);
                AppSettings.Current.Ui.Theme = prev;
                AppSettings.Current.Save();
                throw;
            }
            Current = name;

            if (old is null || snapshot is null) return;   // 无主窗口可重建（防御，正常到不了）

            // ⑤ 建新窗 → 更新引用 → 应用快照 → Show（⚠️ Show 放最后，早了会闪一下旧色）
            MainWindow next;
            try { next = new MainWindow(); }
            catch (Exception ex) { Rollback(prev, old, snapshot, ex); return; }

            try
            {
                Application.Current.MainWindow = next;
                next.RestoreState(snapshot);
                next.Show();
            }
            catch (Exception ex)
            {
                next.Close();
                Rollback(prev, old, snapshot, ex);
                return;
            }

            // ⑥ 关旧窗（rebuilding 标记让它的 Closing 走让位分支）
            old.Close();
        }
        finally
        {
            Volatile.Write(ref _switching, 0);
        }
    }

    /// <summary>切换失败时回到原主题，并尽量把旧窗口还原成可用状态。</summary>
    private static void Rollback(string prev, MainWindow old, MainWindowState s, Exception ex)
    {
        try
        {
            ApplyResources(prev);
            Current = prev;
            AppSettings.Current.Ui.Theme = prev;
            AppSettings.Current.Save();

            old.ClearRebuilding();
            old.RestoreState(s);
            old.ReattachViz();   // DetachViz 摘掉的可视化按设置重新装回来
            old.Activate();
        }
        catch
        {
            // 回滚本身再炸就没有更好的去处了，保进程要紧
        }

        MessageBox.Show($"切换主题失败，已回到原主题。\n\n{ex.Message}", "主题",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
