using System.Windows;
using ThbgmPlayer.Viz;

// UseWindowsForms=true 时 SDK 会全局注入 System.Windows.Forms / System.Drawing，
// 与 WPF 的 Application / Brush / Color / Point / Size 等撞名（CS0104）。
// csproj 里已用 <Using Remove> 摘掉注入，这里再用别名兜底，确保不受注入影响。
using Application = System.Windows.Application;

namespace ThbgmPlayer;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 启动分流。App.xaml 已去掉 StartupUri，这里是唯一的窗口创建点。
    ///
    ///   ThbgmPlayer.exe                   → 主窗口（与以前完全一致）
    ///   ThbgmPlayer.exe --viz [音频路径]  → 只开可视化附件窗口（隔离期入口）
    ///   ThbgmPlayer.exe --viz-selftest    → 跑自检、打印报告、按退出码返回
    ///
    /// ⚠️ 不要在这里改 ShutdownMode（保持默认 OnLastWindowClose）：
    /// <c>--viz</c> 路径下没有主窗口，改成 OnMainWindowClose 反而会让进程**永不退出**（方案 §7-R1）。
    /// 接入期给附件窗口设 <c>Owner</c>（天然「随属主关闭」）才是根治点。
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var launch = VizCommandLine.Parse(e.Args);

        if (launch.Mode == VizLaunchMode.SelfTest)
        {
            // 自检不开窗口 → 用进程退出码把结果带给调用方
            Shutdown(VizSelfTest.Run());
            return;
        }

        if (launch.Mode == VizLaunchMode.Viz)
        {
            var viz = new VizWindow(launch.AudioPath, launch.DelayMs);
            MainWindow = viz;
            viz.Show();
            return;
        }

        var main = new MainWindow();
        MainWindow = main;
        main.Show();
    }
}
