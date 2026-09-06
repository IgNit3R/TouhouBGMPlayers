using System.Windows;

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
}

