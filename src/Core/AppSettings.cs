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
    [JsonPropertyName("width")] public double Width { get; set; } = 1040;
    [JsonPropertyName("height")] public double Height { get; set; } = 720;
    [JsonPropertyName("left")] public double? Left { get; set; }
    [JsonPropertyName("top")] public double? Top { get; set; }
    [JsonPropertyName("maximized")] public bool Maximized { get; set; }

    /// <summary>
    /// UI 字体。默认把 Yu Gothic UI 排在最前，保证日文曲名显示正常；
    /// 系统缺字体时会按逗号依次回退。
    /// </summary>
    [JsonPropertyName("fontFamily")] public string FontFamily { get; set; } =
        "Yu Gothic UI, Meiryo UI, Microsoft YaHei UI";
}

/// <summary>最近播放，用于启动时恢复。</summary>
public sealed class LastPlayed
{
    [JsonPropertyName("game")] public string Game { get; set; } = "";
    [JsonPropertyName("trackNo")] public int TrackNo { get; set; }
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
