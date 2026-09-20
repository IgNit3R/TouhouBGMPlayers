using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ThbgmPlayer.Audio;
using ThbgmPlayer.Core;
using ThbgmPlayer.Data;
using ThbgmPlayer.Viz;

// 同上：别名兜底，避免与 System.Drawing 的 Brush / Color 撞名（CS0104）。
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace ThbgmPlayer.UI;

/// <summary>设置窗口「路径」页的一行：一部作品。</summary>
public sealed class PathRow : ViewModelBase
{
    // 画刷统一取自主题（Themes/DarkTheme.xaml），不要在这里另写一份 ——
    // 否则调整色板时这里有遗漏，界面上就会出现两套颜色。
    // 取不到（设计器里、或键名写错）时回退到同色值的硬编码，保证不炸也不瞎。
    private static readonly Brush BrOk = ThemeBrush("Ok", "#FF4EC9B0");
    private static readonly Brush BrWarn = ThemeBrush("Warn", "#FFCEA86A");
    private static readonly Brush BrFail = ThemeBrush("Fail", "#FFD4696B");
    private static readonly Brush BrDim = ThemeBrush("TextFaint", "#FF71717A");
    private static readonly Brush BrPending = ThemeBrush("TextDim", "#FFADADB4");

    private static Brush ThemeBrush(string key, string fallbackHex)
    {
        try
        {
            if (System.Windows.Application.Current?.Resources[key] is Brush b)
                return b;
        }
        catch
        {
            // 资源还没就位时静默回退
        }
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallbackHex));
    }

    private string? _path;
    private string _statusText = "未设置";
    private Brush _statusBrush = BrDim;

    public PathRow(GameDef game) => Game = game;

    public GameDef Game { get; }

    public string Code => Game.Code;

    /// <summary>输入框提示：说明该指到哪一级目录。</summary>
    public string Hint => Game.IsWavSource
        ? $"指到包含 {PathValidator.WavSubDirectory}\\ 的那一级（常见目录名 {Game.Dir}）"
        : $"指到 {PathValidator.DatFileName} 所在的目录（常见目录名 {Game.Dir}）";

    public string? Path
    {
        get => _path;
        set
        {
            if (!SetField(ref _path, value)) return;
            // 改了路径就作废上次校验结果，等点「应用」重新校验
            StatusText = string.IsNullOrWhiteSpace(value) ? "未设置" : "待校验";
            StatusBrush = StatusText == "未设置" ? BrDim : BrPending;
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public Brush StatusBrush
    {
        get => _statusBrush;
        private set => SetField(ref _statusBrush, value);
    }

    /// <summary>把校验结果反映到界面上。</summary>
    public void ApplyStatus(ValidateResult r)
    {
        StatusText = r.Status switch
        {
            PathStatus.NotSet => "未设置",
            PathStatus.Ok => r.Message,
            _ => r.Message,
        };
        StatusBrush = r.Status switch
        {
            PathStatus.Ok => BrOk,
            PathStatus.Warning => BrWarn,
            PathStatus.Failed => BrFail,
            _ => BrDim,
        };
    }
}

public partial class SettingsWindow : Window
{
    private readonly List<PathRow> _rows = new();

    /// <summary>播放参数是否真的被改过。主窗口据此决定要不要重建当前曲目的时间线。</summary>
    public bool PlaybackChanged { get; private set; }

    /// <summary>打开时选中第几个标签页：0 路径 / 1 播放参数 / 2 导出 / 3 外观。</summary>
    public int InitialTab { get; set; }

    public SettingsWindow()
    {
        InitializeComponent();
        Theme.ApplyUserFont(this);   // 用户字体（XAML 里写的是默认链）

        foreach (var g in TrackIndex.Games)
        {
            var row = new PathRow(g) { Path = AppSettings.Current.GetPath(g.Id) };
            _rows.Add(row);
        }
        PathRows.ItemsSource = _rows;

        LoadPlayback();
        LoadExport();
        LoadUi();
        LoadViz();

        // 初始标签页要等控件树建好再切，构造期间切会被后续初始化覆盖
        Loaded += (_, _) =>
        {
            if (InitialTab >= 0 && InitialTab < Tabs.Items.Count)
                Tabs.SelectedIndex = InitialTab;
        };

        if (!AppPaths.IsWritable)
            WritableWarning.Visibility = Visibility.Visible;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: PathRow row }) return;

        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = $"选择 {row.Code} 的目录" + Environment.NewLine + row.Hint,
            SelectedPath = string.IsNullOrWhiteSpace(row.Path) ? "" : row.Path,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            row.Path = dlg.SelectedPath;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PathRow row })
            row.Path = null;
    }

    // ---------- 播放参数 ----------

    private void LoadPlayback()
    {
        var pb = AppSettings.Current.Playback;
        ModeCombo.SelectedIndex = (int)pb.LoopMode;
        LoopCountBox.Text = pb.LoopCount.ToString();
        ExtraBox.Text = pb.ExtraSeconds.ToString("0.##");
        FadeBox.Text = pb.FadeSeconds.ToString("0.##");
        SeekFadeBox.Text = pb.FadeOnSeekSeconds.ToString("0.###");
        GlobalKeysBox.IsChecked = pb.GlobalMediaKeys;
        LoadDeviceCombo(pb.OutputDeviceId);
    }

    /// <summary>
    /// 填充输出设备下拉：第一项固定是「系统默认（跟随系统）」，后面是枚举到的端点。
    /// 保存的设备已经不在（被拔了）时选回系统默认，应用后设置也会被掰回来。
    /// </summary>
    private void LoadDeviceCombo(string? selectedId)
    {
        DeviceCombo.Items.Clear();

        var def = new ComboBoxItem { Content = "系统默认（跟随系统切换）", Tag = null };
        DeviceCombo.Items.Add(def);
        DeviceCombo.SelectedItem = def;

        foreach (var d in OutputDevices.RenderEndpoints())
        {
            var item = new ComboBoxItem { Content = d.Name, Tag = d.Id };
            DeviceCombo.Items.Add(item);
            if (selectedId is not null && d.Id == selectedId)
                DeviceCombo.SelectedItem = item;
        }
    }

    private void ApplyPlayback()
    {
        var pb = AppSettings.Current.Playback;

        var mode = (LoopMode)ModeCombo.SelectedIndex;
        int n = ParseInt(LoopCountBox.Text, pb.LoopCount, 0, 999);
        double extra = ParseDouble(ExtraBox.Text, pb.ExtraSeconds, 0, 3600);
        double fade = ParseDouble(FadeBox.Text, pb.FadeSeconds, 0, 60);
        double seekFade = ParseDouble(SeekFadeBox.Text, pb.FadeOnSeekSeconds, 0, 1);

        if (pb.LoopMode != mode || pb.LoopCount != n ||
            !Nearly(pb.ExtraSeconds, extra) || !Nearly(pb.FadeSeconds, fade) ||
            !Nearly(pb.FadeOnSeekSeconds, seekFade))
        {
            pb.LoopMode = mode;
            pb.LoopCount = n;
            pb.ExtraSeconds = extra;
            pb.FadeSeconds = fade;
            pb.FadeOnSeekSeconds = seekFade;
            PlaybackChanged = true;
        }

        // 回填规范化后的值：填了非法字符时，用户能直接看到实际生效的是多少
        LoopCountBox.Text = n.ToString();
        ExtraBox.Text = extra.ToString("0.##");
        FadeBox.Text = fade.ToString("0.##");
        SeekFadeBox.Text = seekFade.ToString("0.###");

        // 全局多媒体键开关。也置 PlaybackChanged —— 主窗口靠它在关闭后重挂热键
        bool gk = GlobalKeysBox.IsChecked == true;
        if (pb.GlobalMediaKeys != gk)
        {
            pb.GlobalMediaKeys = gk;
            PlaybackChanged = true;
        }

        // 输出设备。Tag 为 null 的那项是「系统默认」
        var deviceId = (DeviceCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        if (pb.OutputDeviceId != deviceId)
        {
            pb.OutputDeviceId = deviceId;
            PlaybackChanged = true;   // 主窗口靠它把新设备应用到引擎
        }
    }

    // ---------- 导出 ----------

    private void LoadExport()
    {
        var exp = AppSettings.Current.Export;
        var pb = AppSettings.Current.Playback;

        ExpLoopFollow.IsChecked = exp.LoopCount is null;
        ExpExtraFollow.IsChecked = exp.ExtraSeconds is null;
        ExpFadeFollow.IsChecked = exp.FadeSeconds is null;

        // 跟随的时候框里显示的是「当前会用到多少」，让用户知道跟的是几
        ExpLoopBox.Text = (exp.LoopCount ?? pb.LoopCount).ToString();
        ExpExtraBox.Text = (exp.ExtraSeconds ?? pb.ExtraSeconds).ToString("0.##");
        ExpFadeBox.Text = (exp.FadeSeconds ?? pb.FadeSeconds).ToString("0.##");
        ExpDirBox.Text = WavExporter.OutputDirectory;

        SyncExportBoxes();
    }

    private void ApplyExport()
    {
        var exp = AppSettings.Current.Export;
        var pb = AppSettings.Current.Playback;

        // 勾了「跟随」就存 null，运行时实时取播放参数的值
        exp.LoopCount = ExpLoopFollow.IsChecked == true ? null : ParseExportInt(ExpLoopBox.Text, exp.LoopCount);
        exp.ExtraSeconds = ExpExtraFollow.IsChecked == true ? null : ParseExportDouble(ExpExtraBox.Text, exp.ExtraSeconds);
        exp.FadeSeconds = ExpFadeFollow.IsChecked == true ? null : ParseExportDouble(ExpFadeBox.Text, exp.FadeSeconds);
        exp.Directory = ExpDirBox.Text.Trim();

        // 回填规范化后的值，非法输入能看到实际生效的是多少
        ExpLoopBox.Text = (exp.LoopCount ?? pb.LoopCount).ToString();
        ExpExtraBox.Text = (exp.ExtraSeconds ?? pb.ExtraSeconds).ToString("0.##");
        ExpFadeBox.Text = (exp.FadeSeconds ?? pb.FadeSeconds).ToString("0.##");
    }

    private void SyncExportBoxes()
    {
        ExpLoopBox.IsEnabled = ExpLoopFollow.IsChecked != true;
        ExpExtraBox.IsEnabled = ExpExtraFollow.IsChecked != true;
        ExpFadeBox.IsEnabled = ExpFadeFollow.IsChecked != true;
    }

    /// <summary>取消「跟随」时，把播放参数的当前值填进去当起点。</summary>
    private void ExpFollow_Changed(object sender, RoutedEventArgs e)
    {
        var pb = AppSettings.Current.Playback;

        if (sender == ExpLoopFollow && ExpLoopFollow.IsChecked != true)
            ExpLoopBox.Text = pb.LoopCount.ToString();
        else if (sender == ExpExtraFollow && ExpExtraFollow.IsChecked != true)
            ExpExtraBox.Text = pb.ExtraSeconds.ToString("0.##");
        else if (sender == ExpFadeFollow && ExpFadeFollow.IsChecked != true)
            ExpFadeBox.Text = pb.FadeSeconds.ToString("0.##");

        SyncExportBoxes();
    }

    private void ExpDirBrowse_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择导出目录",
            SelectedPath = Directory.Exists(ExpDirBox.Text.Trim()) ? ExpDirBox.Text.Trim() : "",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            ExpDirBox.Text = dlg.SelectedPath;
    }

    // ---------- 外观 ----------

    private void LoadUi()
    {
        FillFontCombo(FontCombo, AppSettings.Current.Ui.FontFamily);
        FillFontCombo(ContentFontCombo, AppSettings.Current.Ui.ContentFontFamily);
    }

    /// <summary>
    /// 把「默认回退链 + 系统已安装字体」填进下拉并回选已保存的值。
    /// 条目统一用界面字体显示名称（图标字体自渲染会把名字画成符号）。
    /// 旧版预设存的是回退链：整条等于默认链 → 默认项，否则按首字体名迁移。
    /// </summary>
    private static void FillFontCombo(ComboBox combo, string? saved)
    {
        combo.Items.Clear();

        var def = new ComboBoxItem
        {
            Content = "默认（Yu Gothic UI → Meiryo UI → Microsoft YaHei UI）",
            Tag = UiSettings.DefaultFontChain,
        };
        combo.Items.Add(def);
        combo.SelectedItem = def;

        foreach (var name in System.Windows.Media.Fonts.SystemFontFamilies
                                           .Select(f => f.Source)
                                           .Distinct(StringComparer.OrdinalIgnoreCase)
                                           .OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
        {
            combo.Items.Add(new ComboBoxItem { Content = name, Tag = name });
        }

        if (string.Equals(saved, UiSettings.DefaultFontChain, StringComparison.OrdinalIgnoreCase))
        {
            combo.SelectedItem = def;
            return;
        }

        var first = (saved ?? "").Split(',')[0].Trim();
        foreach (ComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Tag as string, first, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                break;
            }
        }
    }

    private void FontCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FontCombo.SelectedItem is not ComboBoxItem { Tag: string tag }) return;

        AppSettings.Current.Ui.FontFamily = tag;
        // 写全名：本类有个同名属性 FontFamily，避免解析歧义
        FontFamily = new System.Windows.Media.FontFamily(tag);   // 立刻预览
    }

    private void ContentFontCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ContentFontCombo.SelectedItem is not ComboBoxItem { Tag: string tag }) return;

        AppSettings.Current.Ui.ContentFontFamily = tag;
        ContentFontSample.FontFamily = new System.Windows.Media.FontFamily(tag);   // 样张立刻预览
    }

    // ---------- 数值解析 ----------

    /// <summary>播放参数专用：解析失败或超出范围就用回原值，绝不让非法输入把设置写坏。</summary>
    private static int ParseInt(string s, int fallback, int min, int max)
    {
        if (!int.TryParse(s.Trim(), out var v)) return fallback;
        return Math.Clamp(v, min, max);
    }

    private static double ParseDouble(string s, double fallback, double min, double max)
    {
        if (!double.TryParse(s.Trim(), out var v)) return fallback;
        return Math.Clamp(v, min, max);
    }

    /// <summary>
    /// 导出参数专用。与上面两个的区别是 fallback 可空 ——
    /// 导出参数的 null 有含义（跟随播放参数），不能套用非空版本的回退值。
    /// </summary>
    private static int? ParseExportInt(string s, int? fallback) =>
        int.TryParse(s.Trim(), out var v) ? Math.Clamp(v, 0, 999) : fallback;

    private static double? ParseExportDouble(string s, double? fallback) =>
        double.TryParse(s.Trim(), out var v) ? Math.Clamp(v, 0, 3600) : fallback;

    private static bool Nearly(double a, double b) => Math.Abs(a - b) < 1e-6;

    // ---------- 应用 / 关闭 ----------

    private void Apply_Click(object sender, RoutedEventArgs e) => ApplyAll();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        ApplyAll();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>点了「应用」之后触发（此时对话框还开着）。主窗口订阅它把副作用
    /// （切输出设备、重挂热键、时间线重算等）**立即**应用，而不是等关窗。</summary>
    public event Action? Applied;

    /// <summary>各标签页共用底部按钮，一次性全部应用并保存。</summary>
    private void ApplyAll()
    {
        ApplyPaths();
        ApplyPlayback();
        ApplyExport();
        ApplyViz();
        AppSettings.Current.Save();
        Applied?.Invoke();
    }

    // ------------------------------------------------------------------ 可视化

    /// <summary>
    /// 可视化页（M6 接入）。设置项与 <c>AppSettings.Viz</c> 一一对应，
    /// 其中**总开关 <c>Enabled</c> 与主窗口的快速开关是同一个值** ——
    /// 所以这里改完，主窗口那边的勾会在 <c>Applied</c> 回调里跟着刷新。
    /// </summary>
    private void LoadViz()
    {
        var viz = AppSettings.Current.Viz;

        VizEnabledCheck.IsChecked = viz.Enabled;
        VizAttachCheck.IsChecked = viz.Attached;
        VizEmbedCheck.IsChecked = viz.EmbedWhenMaximized;
        VizLatencyBox.Text = viz.LatencyOffsetMs.ToString("0.#", CultureInfo.InvariantCulture);

        VizShowACheck.IsChecked = viz.ShowA;
        VizShowBCheck.IsChecked = viz.ShowB;
        VizShowCCheck.IsChecked = viz.ShowC;
        VizShowDCheck.IsChecked = viz.ShowD;
        VizShowCoverCheck.IsChecked = viz.ShowCover;
        VizLayersBox.Text = viz.TrailLayers.ToString(CultureInfo.InvariantCulture);
    }

    private void ApplyViz()
    {
        var viz = AppSettings.Current.Viz;

        viz.Enabled = VizEnabledCheck.IsChecked == true;
        viz.Attached = VizAttachCheck.IsChecked == true;
        viz.EmbedWhenMaximized = VizEmbedCheck.IsChecked == true;

        // 偏移：非法输入就当没改（不把输入框里的垃圾写进配置）。
        // 上限复用命令行那一份，避免同一个数值在两个地方各写一遍后漂移。
        if (double.TryParse(VizLatencyBox.Text, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out double ms) && ms >= 0)
        {
            viz.LatencyOffsetMs = ms > VizCommandLine.MaxDelayMs ? VizCommandLine.MaxDelayMs : ms;
        }

        viz.ShowA = VizShowACheck.IsChecked == true;
        viz.ShowB = VizShowBCheck.IsChecked == true;
        viz.ShowC = VizShowCCheck.IsChecked == true;
        viz.ShowD = VizShowDCheck.IsChecked == true;
        viz.ShowCover = VizShowCoverCheck.IsChecked == true;

        // 余辉层数：非法输入就当没改；夹在 [4, MaxTrailLayers]（渲染器的槽位数组只有那么大）
        if (int.TryParse(VizLayersBox.Text, NumberStyles.Integer,
                         CultureInfo.InvariantCulture, out int layers))
        {
            if (layers < VizSettings.MinTrailLayers) layers = VizSettings.MinTrailLayers;
            if (layers > VizSettings.MaxTrailLayers) layers = VizSettings.MaxTrailLayers;
            viz.TrailLayers = layers;
        }
    }

    /// <summary>校验全部路径、写回设置并保存。只有点「应用 / 确定」才会走到这里。</summary>
    private void ApplyPaths()
    {
        int ok = 0, warn = 0, fail = 0, notSet = 0;

        foreach (var row in _rows)
        {
            var result = PathValidator.Validate(row.Game, row.Path);
            row.ApplyStatus(result);
            AppSettings.Current.SetPath(row.Game.Id, row.Path);

            switch (result.Status)
            {
                case PathStatus.Ok: ok++; break;
                case PathStatus.Warning: warn++; break;
                case PathStatus.Failed: fail++; break;
                default: notSet++; break;
            }
        }

        var sb = new System.Text.StringBuilder();
        sb.Append($"共 {_rows.Count} 作：可用 {ok}");
        if (warn > 0) sb.Append($" · 有疑点 {warn}");
        if (fail > 0) sb.Append($" · 失败 {fail}");
        if (notSet > 0) sb.Append($" · 未设置 {notSet}");
        sb.Append("。未设置或有问题的作品在列表里灰显，不影响其余作品。");
        PathSummary.Text = sb.ToString();
        // 保存统一由 ApplyAll 做一次
    }
}
