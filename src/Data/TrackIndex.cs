using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThbgmPlayer.Data;

/// <summary>
/// 一条音轨（可能是主版，或挂在某主版下的灵界版）。
/// 字段与内嵌索引 tracks.json 的缩写键一一对应，详见 dependence/03_tools/csv_to_tracksjson.py。
/// </summary>
public sealed class TrackDef
{
    [JsonPropertyName("n")] public int No { get; init; }              // 显示序号（音乐室顺序）
    [JsonPropertyName("t")] public string Title { get; init; } = "";  // 曲名，日语原名
    [JsonPropertyName("f")] public string? File { get; init; }        // 音频文件名，仅 source=wav 的作品有
    [JsonPropertyName("s")] public long Start { get; init; }         // 数据起点（zwav: 相对 dat 开头；wav: 相对文件开头）
    [JsonPropertyName("i")] public long Intro { get; init; }         // intro 字节数
    [JsonPropertyName("l")] public long Length { get; init; }        // total 字节数（intro + loop）
    [JsonPropertyName("r")] public int Rate { get; init; }           // 采样率
    [JsonPropertyName("c")] public int Channels { get; init; }       // 声道数
    [JsonPropertyName("b")] public int Bits { get; init; }           // 位深

    // ---- tf 系（黄昏作）循环点：以「整数样本」存储 ----
    // sfl / WAV cue / .ogg.ini 给出的本就是整数样本，存样本可彻底消除秒↔样本的往返舍入。
    // 对外的 *Sec 属性按 Rate 换算得到，调用方无需改动。
    [JsonPropertyName("lss")] public long? LoopStartSample { get; init; }   // 循环起点样本；null = 无循环点
    [JsonPropertyName("les")] public long? LoopEndSample { get; init; }     // 循环终点样本；null = 循环到曲末
    [JsonPropertyName("ds")] public long? DurationSample { get; init; }     // 整曲样本数
    [JsonPropertyName("comp")] public string? Composer { get; init; }      // 作曲者
    [JsonPropertyName("theme")] public string? Theme { get; init; }        // 昼夜主题（仅 TH07.5）

    /// <summary>tf 系音轨：有循环点。</summary>
    [JsonIgnore] public bool HasLoopSeconds => LoopStartSample is not null;

    /// <summary>循环起点秒（由样本换算）。</summary>
    [JsonIgnore] public double? LoopStartSec =>
        LoopStartSample is null ? null : LoopStartSample.Value / (double)Rate;

    /// <summary>循环终点秒；null 表示循环到曲末。</summary>
    [JsonIgnore] public double? LoopEndSec =>
        LoopEndSample is null ? null : LoopEndSample.Value / (double)Rate;

    /// <summary>整曲时长秒。</summary>
    [JsonIgnore] public double? DurationSec =>
        DurationSample is null ? null : DurationSample.Value / (double)Rate;

    /// <summary>
    /// tf 一次性曲（有整曲长度但无循环点，如 ED / Staff Roll）。
    /// 播放按 one-shot：一遍停、无 N/X/F；导出不受此影响 —— 整曲作为循环段正常吃 N/X/F。
    /// </summary>
    [JsonIgnore] public bool IsTfOneShot => DurationSample is not null && !HasLoopSeconds;

    [JsonPropertyName("alt")] public TrackDef? Alt { get; init; }    // 副版（TH13 灵界版 / 新典原编曲）

    /// <summary>该音轨是否有副版可切换。</summary>
    [JsonIgnore] public bool HasAlt => Alt is not null;

    /// <summary>循环段长度（字节）。</summary>
    [JsonIgnore] public long LoopLength => Length - Intro;

    /// <summary>单帧字节数（声道 × 位深/8）。</summary>
    [JsonIgnore] public int BlockAlign => Channels * (Bits / 8);

    /// <summary>每秒字节数。</summary>
    [JsonIgnore] public long BytesPerSecond => (long)Rate * BlockAlign;

    /// <summary>intro 时长。tf 系从秒字段取（索引不存字节，字节路径对 tf 全是 0）。</summary>
    [JsonIgnore] public TimeSpan IntroTime => HasLoopSeconds
        ? TimeSpan.FromSeconds(LoopStartSec ?? 0)
        : BytesToTime(Intro);

    /// <summary>整轨时长（intro + loop 一遍）。tf 系取 DurationSec。</summary>
    [JsonIgnore] public TimeSpan LengthTime => HasLoopSeconds || DurationSec is not null
        ? TimeSpan.FromSeconds(DurationSec ?? 0)
        : BytesToTime(Length);

    /// <summary>循环段时长。tf 系：loop_end 缺省时循环到曲末。</summary>
    [JsonIgnore] public TimeSpan LoopTime => HasLoopSeconds
        ? TimeSpan.FromSeconds(Math.Max(0, (LoopEndSec ?? DurationSec ?? 0) - (LoopStartSec ?? 0)))
        : BytesToTime(LoopLength);

    /// <summary>字节数换算成时长，按本轨格式。</summary>
    public TimeSpan BytesToTime(long bytes) =>
        BytesPerSecond <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds((double)bytes / BytesPerSecond);

    /// <summary>时长换算成对齐到帧边界的字节数。</summary>
    public long TimeToBytes(TimeSpan t)
    {
        if (BlockAlign <= 0) return 0;
        long bytes = (long)Math.Round(t.TotalSeconds * BytesPerSecond);
        return bytes - (bytes % BlockAlign);
    }

    public override string ToString() => $"{No:D2} {Title}";
}

/// <summary>一部作品。</summary>
public sealed class GameDef
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";        // 内部代号，如 th13
    [JsonPropertyName("code")] public string Code { get; init; } = "";    // 显示代号，如 TH13

    /// <summary>
    /// 作品完整标题（日文原名）。取自各游戏目录下自带的「おまけ.txt」首行，
    /// 由 csv_to_tracksjson.py 的 NAME 表写入。
    /// </summary>
    [JsonPropertyName("name")] public string Name { get; init; } = "";

    [JsonPropertyName("source")] public string Source { get; init; } = "zwav"; // zwav | wav | tfsuica | tfogg | ncopus
    [JsonPropertyName("dir")] public string Dir { get; init; } = "";      // 常见目录名，仅作设置界面提示，不参与路径推断

    // ---- 副版切换按钮文案（数据驱动）----
    // 同一套主版/副版机制（TrackDef.Alt）在不同作品语义不同：TH13 是「霊界版」，
    // 新典（TH06NC）是「新编曲/原编曲」。这里由索引给出按钮在两个状态下的文字，
    // 播放器据此显隐通用切换按钮；两者都缺省（null）表示该作品不出按钮，走旧交互。
    [JsonPropertyName("lm")] public string? MainLabel { get; init; }   // 主版状态下的按钮文字，如「新典」
    [JsonPropertyName("la")] public string? AltLabel { get; init; }    // 副版状态下的按钮文字，如「原典」

    /// <summary>该作品是否带副版切换标签（两个标签都齐才算）。</summary>
    [JsonIgnore] public bool HasVariantLabels => MainLabel is not null && AltLabel is not null;

    /// <summary>
    /// tf 系（黄昏作）容器文件相对路径，按覆盖优先序排列：后面的包覆盖前面包的同名条目
    /// （如 th135.pak + th135b.pak）。主系列为 null。
    /// 解析器按 source + 扩展名路由：tfsuica → SuicaReader（th075bgm.dat）；
    /// tfogg 下 .dat → XorContainerReader，.pak / .cga / .cgb → pakReader。
    /// </summary>
    [JsonPropertyName("cont")] public List<string>? Containers { get; init; }

    /// <summary>是否 tf 系（黄昏作）音源。</summary>
    [JsonIgnore] public bool IsTfSource => Source is "tfsuica" or "tfogg";

    /// <summary>是否新典（TH06NC）Opus 音源。</summary>
    [JsonIgnore] public bool IsNcSource => Source == "ncopus";
    [JsonPropertyName("tracks")] public List<TrackDef> Tracks { get; init; } = new();

    /// <summary>
    /// 作品标题的分隔符。官方文本用的是 **U+301C WAVE DASH（〜）**，
    /// 但它和更常见的全角波浪号 U+FF5E（～）长得几乎一样、码点却不同。
    /// 只认其中一个的话，另一批作品就会切不开 —— 表现是主标题没变短，
    /// 而且从结果上看不出原因（肉眼看两个字符一模一样）。
    /// </summary>
    private static readonly char[] TitleSeparators = { '\u301C', '\uFF5E' };

    /// <summary>
    /// 作品名的主标题部分（分隔符之前），如「東方紅魔郷」。
    /// 用这个而不是 <see cref="Code"/> 来显示，代号留给设置页和窄列。
    /// </summary>
    [JsonIgnore] public string ShortName
    {
        get
        {
            var s = Name.Trim().TrimStart('○', '●', '・');   // おまけ 首行有个 ○ 前缀
            int i = s.IndexOfAny(TitleSeparators);
            return (i >= 0 ? s[..i] : s).Trim().Trim('　', ' ');
        }
    }

    [JsonIgnore] public bool IsWavSource => Source == "wav";

    /// <summary>可见曲目数（灵界版挂在 Alt 里，不单独计数）。</summary>
    [JsonIgnore] public int TrackCount => Tracks.Count;

    /// <summary>带灵界版的曲目数。</summary>
    [JsonIgnore] public int AltCount => Tracks.Count(t => t.HasAlt);
}

/// <summary>内嵌索引根对象。</summary>
internal sealed class IndexDoc
{
    [JsonPropertyName("version")] public int Version { get; init; }
    [JsonPropertyName("games")] public List<GameDef> Games { get; init; } = new();
}

/// <summary>
/// 曲目索引。数据源：内嵌资源 tracks.json.gz（由 tracklist.csv 生成，346 条记录）。
/// 外部覆盖（tracks.override.json）在第二步接入，此处预留 ApplyOverride 入口。
/// </summary>
public static class TrackIndex
{
    private const string ResourceName = "tracks.json.gz";
    private const string TfResourceName = "tracks.tf.json.gz";   // 黄昏作索引；资源缺失时静默跳过
    private const string NcResourceName = "tracks.nc.json.gz";   // 新典（TH06NC）索引；资源缺失时静默跳过

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    static TrackIndex()
    {
        var games = new List<GameDef>(LoadDoc(ResourceName).Games);

        // 黄昏作索引是增量资源：主系列先装好，tf 游戏按编号序混排进来
        // （th07 → th075 → th08 ...，与 thwiki 惯例一致）。资源不存在（尚未生成/旧构建）
        // 时静默跳过 —— 主系列行为完全不变；索引本身有问题也不拖死主系列。
        var tfDoc = TryLoadDoc(TfResourceName);
        if (tfDoc is not null) games.AddRange(tfDoc.Games);

        // 新典（TH06NC）索引同为增量资源，缺失时静默跳过。
        var ncDoc = TryLoadDoc(NcResourceName);
        if (ncDoc is not null) games.AddRange(ncDoc.Games);

        Games = games.OrderBy(GameOrder).ThenBy(g => g.Id, StringComparer.OrdinalIgnoreCase).ToList();
        ById = Games.ToDictionary(g => g.Id, StringComparer.OrdinalIgnoreCase);
    }

    private static IndexDoc LoadDoc(string resourceName)
    {
        using var asmStream = typeof(TrackIndex).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"找不到内嵌资源 {resourceName}，请确认 csproj 中 EmbeddedResource 的 LogicalName。");
        using var gz = new GZipStream(asmStream, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<IndexDoc>(gz, JsonOpts)
            ?? throw new InvalidOperationException($"内嵌索引 {resourceName} 解析结果为空。");
    }

    private static IndexDoc? TryLoadDoc(string resourceName)
    {
        if (typeof(TrackIndex).Assembly.GetManifestResourceStream(resourceName) is null) return null;
        try { return LoadDoc(resourceName); }
        catch { return null; }
    }

    /// <summary>
    /// 作品排序键 = 编号值：th07 → 7，th075 → 7.5，th095 → 9.5，th128 → 12.8。
    /// 主系列与黄昏作用同一把尺子，编号序即最终列表顺序。
    /// 编号后带字母后缀的**变体作**（如新典 th06nc）不属于编号序，统一排到列表末尾。
    /// </summary>
    private static double GameOrder(GameDef g)
    {
        if (!g.Id.StartsWith("th", StringComparison.OrdinalIgnoreCase))
            return 999;

        var rest = g.Id[2..];
        int end = 0;
        while (end < rest.Length && char.IsAsciiDigit(rest[end])) end++;
        var digits = rest[..end];

        // 带字母后缀 → 变体作（th06nc 新典），排末尾
        if (end < rest.Length) return 999;

        return digits.Length switch
        {
            2 when double.TryParse(digits, out var a) => a,
            3 when double.TryParse(digits, out var b) => b / 10.0,   // 075→7.5, 095→9.5, 128→12.8, 105→10.5
            _ => 999,
        };
    }

    /// <summary>全部作品，按作品顺序（TH06 → TH20）。</summary>
    public static IReadOnlyList<GameDef> Games { get; }

    /// <summary>按内部代号取作品，如 "th13"。</summary>
    public static IReadOnlyDictionary<string, GameDef> ById { get; }

    /// <summary>总曲目数（不含灵界版）。</summary>
    public static int TotalTracks => Games.Sum(g => g.TrackCount);

    /// <summary>
    /// 应用外部覆盖配置。目前只记录意图，合并逻辑随第二步一起实现。
    /// 覆盖文件不存在时静默跳过 —— 用户没创建就不该有任何影响。
    /// </summary>
    public static void ApplyOverride(string? overridePath)
    {
        // TODO(第二步): 读取 overridePath，按 (gameId, trackNo) 合并，只覆盖显式写了的字段。
        // 设计见 DESIGN_v3.md §4：L1 外部覆盖 > L2 内嵌；未配置的作品不报错。
    }
}
