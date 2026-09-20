using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThbgmPlayer.Core;

/// <summary>循环模式。</summary>
public enum LoopMode
{
    /// <summary>无限循环：intro 播一遍后，loop 段无限重复，不自动切曲。</summary>
    Infinite,
    /// <summary>普通：intro + loop×N + 额外 X 秒 + 淡出 F 秒，然后按列表顺序切下一首。</summary>
    Normal,
    /// <summary>随机：每首的播法与普通完全相同，区别只在下一首从当前列表随机取。</summary>
    Shuffle,
}

/// <summary>播放参数。普通模式时间线 = intro + N×loop + X + F。</summary>
public sealed class PlaybackSettings
{
    [JsonPropertyName("loopMode")] public LoopMode LoopMode { get; set; } = LoopMode.Infinite;
    [JsonPropertyName("loopCount")] public int LoopCount { get; set; } = 2;
    [JsonPropertyName("extraSeconds")] public double ExtraSeconds { get; set; } = 0;
    [JsonPropertyName("fadeSeconds")] public double FadeSeconds { get; set; } = 3;
    [JsonPropertyName("volume")] public double Volume { get; set; } = 0.8;
    [JsonPropertyName("fadeOnSeekSeconds")] public double FadeOnSeekSeconds { get; set; } = 0.03;

    /// <summary>
    /// 全局多媒体键：未聚焦时也响应 播放/暂停、上一首、下一首。
    /// 关掉则只在窗口聚焦时响应（且注册被别的程序占用时也会退化成这样）。
    /// </summary>
    [JsonPropertyName("globalMediaKeys")] public bool GlobalMediaKeys { get; set; } = true;

    /// <summary>
    /// 输出设备的 WASAPI 端点 ID；null / 空 = 系统默认（跟随系统切换，Windows 自动流路由）。
    /// 设备被拔掉后找不到时会自动退回系统默认，不会卡住。
    /// </summary>
    [JsonPropertyName("outputDeviceId")] public string? OutputDeviceId { get; set; }
}

/// <summary>
/// 导出参数。三个数值为 null 时跟随播放参数（用户定：默认同一套，改过之后独立保存，
/// 反过来不影响播放）。导出永远按普通模式的时间线，与当前循环模式无关。
/// </summary>
public sealed class ExportSettings
{
    [JsonPropertyName("loopCount")] public int? LoopCount { get; set; }
    [JsonPropertyName("extraSeconds")] public double? ExtraSeconds { get; set; }
    [JsonPropertyName("fadeSeconds")] public double? FadeSeconds { get; set; }
    [JsonPropertyName("directory")] public string Directory { get; set; } = "";

    /// <summary>解析出实际生效的参数（null 则回落到播放参数）。</summary>
    public (int LoopCount, double ExtraSeconds, double FadeSeconds) Resolve(PlaybackSettings pb) => (
        LoopCount ?? pb.LoopCount,
        ExtraSeconds ?? pb.ExtraSeconds,
        FadeSeconds ?? pb.FadeSeconds
    );
}

/// <summary>窗口与外观。</summary>
public sealed class UiSettings
{
    /// <summary>界面字体的默认回退链（日文显示优先，系统缺字体时按逗号依次回退）。</summary>
    public const string DefaultFontChain = "Yu Gothic UI, Meiryo UI, Microsoft YaHei UI";

    [JsonPropertyName("width")] public double Width { get; set; } = 1040;
    [JsonPropertyName("height")] public double Height { get; set; } = 720;
    [JsonPropertyName("left")] public double? Left { get; set; }
    [JsonPropertyName("top")] public double? Top { get; set; }
    [JsonPropertyName("maximized")] public bool Maximized { get; set; }

    /// <summary>
    /// UI 字体（中文界面文本）。可以是单个字体名，也可以是逗号分隔的回退链。
    /// </summary>
    [JsonPropertyName("fontFamily")] public string FontFamily { get; set; } = DefaultFontChain;

    /// <summary>
    /// 曲名字体（日文内容：曲目表、正在播放区、导出清单里的曲名与作品名）。
    /// 与界面字体分开 —— 中文字体与日文字体的擅长区不同，各选各的。
    /// </summary>
    [JsonPropertyName("contentFontFamily")] public string ContentFontFamily { get; set; } = DefaultFontChain;

    /// <summary>
    /// 【预留入口，未实现】从字体文件（.ttf / .otf / .ttc）加载界面字体。
    /// 将来实现时的要点，先记在这里：
    ///   · 显示名要从文件里读（GlyphTypeface.FamilyName）；.ttc 是多家族合集，需枚举
    ///   · 引用形式：new FontFamily(new Uri("file:///…"), "FamilyName")
    ///   · 文件丢失 / 被移动时要静默回退到 FontFamily，不能让程序起不来
    ///   · 非空时优先于 FontFamily
    /// </summary>
    [JsonPropertyName("fontFile")] public string? FontFile { get; set; }
}

/// <summary>最近播放，用于启动时恢复。</summary>
public sealed class LastPlayed
{
    [JsonPropertyName("game")] public string Game { get; set; } = "";
    [JsonPropertyName("trackNo")] public int TrackNo { get; set; }
}

/// <summary>
/// 可视化附件窗口。隔离期（--viz）与接入期共用同一份设置 —— 换了宿主不该换配置。
/// 面板开关只落盘、暂不做 UI（方案 §5）。
/// </summary>
public sealed class VizSettings
{
    /// <summary>
    /// **总开关**：是否启用可视化（M6 接入）。
    ///
    /// 两处入口**共用这一个值**：设置页的「可视化」标签页与主窗口的快速开关都读写它 ——
    /// 所以设置窗口关闭时主窗口要重读一次，否则两处的勾会各说各话。
    ///
    /// 默认 false：新功能默认关着，用户主动打开才出现。
    /// </summary>
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }

    /// <summary>附件窗口宽度。自由模式可拖；贴附模式下由宿主几何算出，这个值只作「期望宽度」。</summary>
    [JsonPropertyName("width")] public double Width { get; set; } = 560;

    /// <summary>附件窗口高度。自由模式用；接入期恒等于宿主高度，不再回写。</summary>
    [JsonPropertyName("height")] public double Height { get; set; } = 480;

    [JsonPropertyName("left")] public double? Left { get; set; }
    [JsonPropertyName("top")] public double? Top { get; set; }

    /// <summary>
    /// 是否贴附主窗口。**默认 true** —— 方案的窗口形态本来就是「贴主窗口右侧、顶对齐、等高」，
    /// 自由窗口是退路而不是默认。
    ///
    /// ⚠️ 接入前它默认 false：那时没有主体（隔离期），贴附无从谈起。
    /// 隔离期/`--viz` 路径下即使这个值是 true，也只是不起作用（没有宿主就没有放置可言）。
    /// </summary>
    [JsonPropertyName("attached")] public bool Attached { get; set; } = true;

    /// <summary>主窗口最大化时内嵌进主窗口右侧；关掉则退化为贴右侧（预留开关，方案 §3.6）。</summary>
    [JsonPropertyName("embedWhenMaximized")] public bool EmbedWhenMaximized { get; set; } = true;

    /// <summary>内嵌宽度（接入期由 GridSplitter 拖动写回）。</summary>
    [JsonPropertyName("embeddedWidth")] public double EmbeddedWidth { get; set; } = 420;

    /// <summary>
    /// 听觉延迟对齐偏移（毫秒）。读「约这么久之前」的样本，让画面和耳朵对齐 ——
    /// WASAPI 共享模式的输出缓冲本身就是这么大，不补偿的话画面会早于声音。
    /// </summary>
    [JsonPropertyName("latencyOffsetMs")] public double LatencyOffsetMs { get; set; } = 100;

    /// <summary>最近一次 --viz 带的音频路径（不带参数启动时接着听，调试期便利）。</summary>
    [JsonPropertyName("debugSource")] public string DebugSource { get; set; } = "";

    // ---- 面板开关：本次不做设置 UI，先在配置里留位（方案 §5） ----
    [JsonPropertyName("showA")] public bool ShowA { get; set; } = true;
    [JsonPropertyName("showB")] public bool ShowB { get; set; } = true;
    [JsonPropertyName("showC")] public bool ShowC { get; set; } = true;
    [JsonPropertyName("showD")] public bool ShowD { get; set; } = true;
    [JsonPropertyName("showCover")] public bool ShowCover { get; set; } = true;

    /// <summary>
    /// D 利萨如的**余辉层数**（<see cref="MinTrailLayers"/>～<see cref="MaxTrailLayers"/>）。
    ///
    /// ⚠️ 这是可视化里**唯一真正有效的性能旋钮**：实测成本**正比于层数**，
    /// **每层约 1.05ms**（用户 200Hz 屏标定：`帧距 ≈ 5.1 + 1.05 × 层数` 毫秒）。
    /// 原因是「墨量」—— 每层铺下的**线长 × 线宽**，而 Lissajous 的路径长度
    /// **随信号幅度增长**，这就是「音乐越响越掉帧」的由来。
    ///
    /// 标定（同一首曲子）：5 层 ≈ 9.4ms(~106fps) / **10 层 ≈ 16.3ms(~61fps)** /
    /// 15 层 ≈ 21.6ms(~46fps) / 20 层 ≈ 26.7ms(~37fps)。
    ///
    /// <b>默认取 10</b>（2026-09-21 用户拍板）：观感上仍是明显的团雾，但帧率从 37 提到 61 ——
    /// 新装的机器不该一打开就是 37fps。想要方案原定那份密实就调到 20
    /// （渲染器的淡出系数会跟着重标定，所以调回去就是原来的观感）。
    /// </summary>
    [JsonPropertyName("trailLayers")] public int TrailLayers { get; set; } = DefaultTrailLayers;

    /// <summary>余辉层数的上限：渲染器的槽位数组就开这么大，调不上去（= 方案原定的观感）。</summary>
    public const int MaxTrailLayers = 20;

    /// <summary>余辉层数的下限：再少就不像"雾"了。</summary>
    public const int MinTrailLayers = 4;

    /// <summary>余辉层数的默认值（见 <see cref="TrailLayers"/> 里的标定与取舍）。</summary>
    public const int DefaultTrailLayers = 10;
}

/// <summary>
/// 全部设置。只存在程序目录下的 settings.json —— 不写 AppData（DESIGN_v3.md §2）。
/// </summary>
public sealed class AppSettings
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;

    /// <summary>每部作品的手填路径，键为作品代号（th06…th20）。没有默认值、不做扫描。</summary>
    [JsonPropertyName("paths")] public Dictionary<string, string> Paths { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("playback")] public PlaybackSettings Playback { get; set; } = new();
    [JsonPropertyName("export")] public ExportSettings Export { get; set; } = new();
    [JsonPropertyName("ui")] public UiSettings Ui { get; set; } = new();
    [JsonPropertyName("lastPlayed")] public LastPlayed LastPlayed { get; set; } = new();
    [JsonPropertyName("viz")] public VizSettings Viz { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        // 用户可能用记事本改，宽容一点
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// 必须**公开**：System.Text.Json 反序列化要求类型有公开的无参构造函数。
    /// 写成 private 的话 Deserialize 会抛 NotSupportedException，
    /// 被 Load 的兜底 catch 吞掉之后表现为「每次启动设置都回到默认」——
    /// 文件明明存得好好的，却永远读不回来。
    /// 平时请用 <see cref="Current"/>，别直接 new。
    /// </summary>
    public AppSettings() { }

    private static AppSettings? _current;

    /// <summary>当前设置（进程内单例）。</summary>
    public static AppSettings Current => _current ??= Load();

    /// <summary>读取设置。文件不存在或格式损坏都返回全新实例，不抛异常。</summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var json = File.ReadAllText(AppPaths.SettingsFile);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            // 坏了就当没有，程序不该因为设置坏了就起不来。
            // 但原因必须留痕：不然「设置莫名回到默认」这类问题现场一闪就没了，根本没法查。
            // 只在真出错时才产生这个文件，正常运行时不会有。
            try
            {
                AppPaths.WriteTextAtomic(
                    Path.Combine(AppPaths.BaseDirectory, "settings.load-error.txt"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{AppPaths.SettingsFile}\n\n{ex}");
            }
            catch
            {
                // 连错误日志都写不出去就算了，不能因为这个把启动搞挂
            }
        }
        return new AppSettings();
    }

    /// <summary>
    /// 保存设置。目录不可写（例如被放进 Program Files）时静默失败，
    /// 由调用方决定是否提示用户"本次设置仅内存生效"。
    /// </summary>
    public bool Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(this, JsonOpts);
            return AppPaths.WriteTextAtomic(AppPaths.SettingsFile, json);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>取某作的路径，未设置返回 null。</summary>
    public string? GetPath(string gameId) =>
        Paths.TryGetValue(gameId, out var p) && !string.IsNullOrWhiteSpace(p) ? p : null;

    /// <summary>设置或清除（传 null/空白即清除）某作的路径。</summary>
    public void SetPath(string gameId, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            Paths.Remove(gameId);
        else
            Paths[gameId] = path;
    }
}
