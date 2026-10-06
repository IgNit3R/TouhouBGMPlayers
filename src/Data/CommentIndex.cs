using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThbgmPlayer.Data;

/// <summary>评论语言。目前 UI 只用单一语言显示（默认 Ja）；多语言支线落地后由设置驱动。</summary>
public enum CommentLanguage
{
    Ja,
    Zh,
}

/// <summary>一部作品的评论集：musicNo（字符串键）→ 该曲的日/中评论。</summary>
public sealed class CommentGame
{
    [JsonPropertyName("tracks")]
    public Dictionary<string, CommentEntry> Tracks { get; init; } = new();
}

/// <summary>单条评论。⚠️ 只读这五个字段 —— 曲名/标题按契约**不在乐评区显示**（曲目列表与正在播放行已有），
/// 其余字段（omake_title_ja/zh 等）用不到就让它自动忽略。TitleJa 是**播放器侧标题**（生成工具按标题重映射时写入），
/// 自检的「永久对齐校验」拿它与 <see cref="TrackIndex"/> 同 No 的标题逐条比对 —— 两套序再怎么漂，这里当场红。
/// OmakeJa / OmakeZh 是裏音楽コメント（おまけ.txt，当前仅 th06/07/08）：与表评论同曲同键，
/// 只有生成了 omake 数据的条目才有值（生成工具条件写键），其余反序列化为 null。</summary>
public sealed class CommentEntry
{
    [JsonPropertyName("title_ja")]
    public string? TitleJa { get; init; }

    [JsonPropertyName("comment_ja")]
    public string? Ja { get; init; }

    [JsonPropertyName("comment_zh")]
    public string? Zh { get; init; }

    [JsonPropertyName("comment_omake_ja")]
    public string? OmakeJa { get; init; }

    [JsonPropertyName("comment_omake_zh")]
    public string? OmakeZh { get; init; }
}

/// <summary>
/// 音乐室评论的内嵌索引（资源 <c>musiccmt.json.gz</c>，数据源 <c>docs/musiccmt/musiccmt.json</c>，
/// 由 <c>tools/make_musiccmt_resource.py</c> 生成 —— <b>乐评数据更新后要重跑该工具</b>）。
///
/// 加载方式与 <see cref="TrackIndex"/> 的可选增量资源同款：<b>缺资源 / 坏档都静默降级成空索引</b>，
/// 播放器其余功能完全不受影响。⚠️ 用静态字段初始化做懒加载（首次访问才读），**不走静态构造函数** ——
/// 那样一旦抛异常会固化成 <c>TypeInitializationException</c>，之后每次触碰都炸。
///
/// 查询口径：**按 <c>gameId + musicNo</c>**，主版/副版（灵界版、原典编曲）共用同一条 ——
/// 副版在索引里挂的是基础曲的 <c>No</c>，且副版评论本就是从基础曲复制的，无需特例。
///
/// 「无此作品」「有作品但没写评论」统一返回 <c>false</c>：UI 对两者的动作相同（整列收起）。
/// 将来若要区分（如显示「本曲无乐评」），只拆这一个方法。
/// </summary>
public static class CommentIndex
{
    private const string ResourceName = "musiccmt.json.gz";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>gameId → 评论集。键比较与 <c>TrackIndex.ById</c> 一致用 OrdinalIgnoreCase。</summary>
    private static readonly Dictionary<string, CommentGame> ById = LoadById();

    /// <summary>
    /// 🔑 多语言口子（用户 2026-09-24 定：**将来做进设置里**，不做外部切换）——
    /// 多语言支线落地时：设置页写这个属性、<see cref="TryGetComment(string,int,out string?)"/> 内部查表微调，
    /// 全部收敛在本文件；调用方（MainWindow）零语言状态、零改动。现在先固定 Ja（用户：先默认日语）。
    /// </summary>
    public static CommentLanguage CurrentLanguage { get; set; } = CommentLanguage.Ja;

    private static Dictionary<string, CommentGame> LoadById()
    {
        try
        {
            using var asmStream = typeof(CommentIndex).Assembly.GetManifestResourceStream(ResourceName);
            if (asmStream is null) return [];

            using var gz = new GZipStream(asmStream, CompressionMode.Decompress);
            return JsonSerializer.Deserialize<Dictionary<string, CommentGame>>(gz, JsonOpts)
                   ?? [];
        }
        catch
        {
            // 可选资源：缺失或坏档都静默降级 —— 乐评区不显示而已，不能拖死播放器。
            return [];
        }
    }

    /// <summary>已加载的作品数（自检用：资源没内嵌/坏档时为 0）。</summary>
    internal static int GameCount => ById.Count;

    /// <summary>自检用：取原始条目（含 title_ja，供对齐校验），不做语言选择与空值归并。</summary>
    internal static bool TryGetEntry(string gameId, int musicNo, out CommentEntry? entry)
    {
        entry = null;
        return ById.TryGetValue(gameId, out var game)
               && game.Tracks.TryGetValue(musicNo.ToString(CultureInfo.InvariantCulture), out entry);
    }

    /// <summary>按当前语言取评论。有非空正文返回 true。</summary>
    public static bool TryGetComment(string gameId, int musicNo, out string comment) =>
        TryGetComment(gameId, musicNo, CurrentLanguage, out comment);

    /// <summary>按指定语言取评论。有非空正文返回 true。</summary>
    public static bool TryGetComment(string gameId, int musicNo, CommentLanguage lang, out string comment)
    {
        comment = string.Empty;
        return TryGetEntry(gameId, musicNo, out var entry)
               && TryGetText(entry!, lang, out comment);
    }

    /// <summary>按当前语言取裏评论（おまけ.txt 的裏音楽コメント，当前仅 th06/07/08）。有非空正文返回 true。</summary>
    public static bool TryGetOmakeComment(string gameId, int musicNo, out string comment) =>
        TryGetOmakeComment(gameId, musicNo, CurrentLanguage, out comment);

    /// <summary>按指定语言取裏评论。有非空正文返回 true。无 omake 数据的条目自然返回 false。</summary>
    public static bool TryGetOmakeComment(string gameId, int musicNo, CommentLanguage lang, out string comment)
    {
        comment = string.Empty;
        if (!TryGetEntry(gameId, musicNo, out var entry) || entry is null) return false;

        var text = (lang == CommentLanguage.Zh ? entry.OmakeZh : entry.OmakeJa) ?? string.Empty;
        if (text.Length == 0) return false;

        comment = text;
        return true;
    }

    /// <summary>当前曲是否有裏评论（任一语言非空）——乐评区「表/裏」切换条可见性的判定口。
    /// 刻意不依赖 <see cref="CurrentLanguage"/>：切换条是否出现不该随语言设置变。</summary>
    public static bool HasOmake(string gameId, int musicNo)
    {
        return TryGetEntry(gameId, musicNo, out var entry)
               && entry is not null
               && (!string.IsNullOrEmpty(entry.OmakeJa) || !string.IsNullOrEmpty(entry.OmakeZh));
    }

    /// <summary>表评论的取值逻辑（裏评论字段名不同、结构同形，单独内联在上方）。</summary>
    private static bool TryGetText(CommentEntry entry, CommentLanguage lang, out string comment)
    {
        comment = string.Empty;

        var text = (lang == CommentLanguage.Zh ? entry.Zh : entry.Ja) ?? string.Empty;
        if (text.Length == 0) return false;

        comment = text;
        return true;
    }
}
