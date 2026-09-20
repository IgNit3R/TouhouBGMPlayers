using System.Windows.Media;
using ThbgmPlayer.UI;

namespace ThbgmPlayer.Viz;

/// <summary>
/// 渲染用的观感资源：画刷 + DPI 折算。
///
/// <b>颜色一律从主题取</b>（<see cref="Theme.Get"/> 是项目里代码侧取色板的唯一入口），
/// 这里一个十六进制字面量都不留 —— 换色板时只改 DarkTheme.xaml，渲染器不用动。
/// 传进去的兜底色只用于「资源还没合并 / 键名写错」的场合（<c>PromptDialog</c> 同款约定）。
///
/// 画刷分配（见 DarkTheme.xaml 的注释，与定稿参数 docs/2026-09-19-research-visualization.md §7 一致）：
/// <list type="bullet">
/// <item><see cref="LineL"/> = VizLineL #3A96DD → 示波器左声道、利萨如、相位指针</item>
/// <item><see cref="LineR"/> = Emphasis #9CDCFE → 示波器右声道（不新增色相，复用说明文字强调色）</item>
/// <item><see cref="Bar"/>  = VizBar #2E86C4 → 频谱柱、电平柱体</item>
/// </list>
///
/// ⚠️ <b>DPI 折算</b>：WPF 的坐标单位是 DIP（1/96 英寸），而验证页所有绘制都在
/// <b>物理像素</b>坐标里（canvas backing store 放大过 <c>devicePixelRatio</c> 倍）。
/// 两边的「1」不是一回事：
/// <list type="bullet">
/// <item>参照页里**不带 d** 的线宽（<c>lineWidth=1</c> 的中线、刻度线）= 1 物理像素
///   → WPF 里得写 <see cref="HairLine"/>（= 1/dpiScale DIP）才是同样粗细。</item>
/// <item>参照页里**带 d** 的线宽（<c>d*1.6</c> 之类）= 1.6 CSS px = 1.6 DIP
///   → WPF 里直接写 1.6，所见即所得。</item>
/// </list>
/// 弄混这两类的后果是「在 150% 缩放的屏上细线粗了一圈」—— 正是 <see cref="HairLine"/> 存在的理由。
/// </summary>
public sealed class VizStyle
{
    // ------------------------------------------------------------------ 画刷

    /// <summary>画布底（BgDeep #171717）。</summary>
    public Brush Background { get; }

    /// <summary>中线 / 刻度线（Border #3F3F45）。</summary>
    public Brush Grid { get; }

    /// <summary>底槽 / 分屏主分割线（BgElevated #2E2E31）。</summary>
    public Brush Track { get; }

    /// <summary>面板内小标签（TextFaint #71717A）。</summary>
    public Brush Label { get; }

    /// <summary>数值文字（TextDim #ADADB4）。</summary>
    public Brush Value { get; }

    /// <summary>左声道 / 利萨如 / 相位指针（VizLineL #3A96DD）。</summary>
    public Brush LineL { get; }

    /// <summary>右声道（Emphasis #9CDCFE）。</summary>
    public Brush LineR { get; }

    /// <summary>频谱柱 / 电平柱体（VizBar #2E86C4）。</summary>
    public Brush Bar { get; }

    /// <summary>余量区警示（Warn #CEA86A）。</summary>
    public Brush Warn { get; }

    // ------------------------------------------------------------------ DPI

    /// <summary>
    /// 设备像素 / DIP。由 <see cref="RefreshDpi"/> 从实际视觉树取，默认 1.0（100% 缩放）。
    /// 可写：窗口被拖到另一块不同缩放的屏上时，WPF 会重排，面板跟着刷新。
    /// </summary>
    public double DpiScale { get; private set; } = 1.0;

    /// <summary>1 物理像素对应的 DIP 值。参照页里所有「1 个 canvas 单位」的线宽都该用它。</summary>
    public double HairLine => 1.0 / (DpiScale > 0 ? DpiScale : 1.0);

    private VizStyle()
    {
        Background = Theme.Get("BgDeep", "#FF171717");
        Grid = Theme.Get("Border", "#FF3F3F45");
        Track = Theme.Get("BgElevated", "#FF2E2E31");
        Label = Theme.Get("TextFaint", "#FF71717A");
        Value = Theme.Get("TextDim", "#FFADADB4");
        LineL = Theme.Get("VizLineL", "#FF3A96DD");
        LineR = Theme.Get("Emphasis", "#FF9CDCFE");
        Bar = Theme.Get("VizBar", "#FF2E86C4");
        Warn = Theme.Get("Warn", "#FFCEA86A");
    }

    /// <summary>
    /// 建一份。必须在 <c>Application.Current.Resources</c> 就位之后调用（即窗口构造之后），
    /// 否则全部走兜底色 —— 兜底色与主题同值，所以肉眼看不出来，但主题改动会不生效。
    /// </summary>
    public static VizStyle Create() => new();

    /// <summary>从视觉树刷新 DPI。取样失败（还没挂进树）时保持原值，不抛。</summary>
    public void RefreshDpi(Visual visual)
    {
        try
        {
            double s = VisualTreeHelper.GetDpi(visual).DpiScaleX;
            if (s > 0) DpiScale = s;
        }
        catch
        {
            // 还没挂进视觉树之类。DpiScale 保持原值即可，1.0 的兜底也够用。
        }
    }
}
