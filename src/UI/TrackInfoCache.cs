using ThbgmPlayer.Audio;
using ThbgmPlayer.Data;

namespace ThbgmPlayer.UI;

/// <summary>
/// Vorbis 码率探针结果的**纯内存缓存**（懒生成：选中 / 播放某曲才探，探完放这儿）。
/// 逐行照 <see cref="WaveformCache"/> 的做法：键复用 <see cref="PreloadCache.KeyOf"/>、
/// LRU 淘汰、<c>null</c> 不缓存（失败不该被记成"永远没有"）、路径一改就整表清。
///
/// 为什么不落盘：一个 <see cref="BitrateStats"/> 才几十字节，纯派生数据写盘要受
/// 「运行时禁止写 APPDATA、只能写 exe 旁」这条硬约束管，不值得占用户的目录。
///
/// ⚠️ **只缓存 Vorbis（tfogg）的探针结果**：PCM / Opus 的码率是格式常数，
/// <see cref="BitrateProbe.Instant"/> 每次现算零成本 —— 不进缓存，也不挤占这里的名额。
/// </summary>
public static class TrackInfoCache
{
    /// <summary>最多留几首。与 <see cref="WaveformCache"/> 同容量，防长期浏览累积。</summary>
    private const int Capacity = 64;

    private static readonly object Gate = new();

    /// <summary>键 → 统计。<c>null</c> 不缓存（见 <see cref="Put"/>）。</summary>
    private static readonly Dictionary<string, BitrateStats> Map = new(StringComparer.Ordinal);
    private static readonly LinkedList<string> Lru = new();       // 头 = 最近用过

    public static string KeyOf(GameDef game, TrackDef track, bool useAlt) =>
        PreloadCache.KeyOf(game, track, useAlt);

    /// <summary>取。命中就把该键挪到 LRU 头部。</summary>
    public static BitrateStats? Get(GameDef game, TrackDef track, bool useAlt)
    {
        string key = KeyOf(game, track, useAlt);

        lock (Gate)
        {
            if (!Map.TryGetValue(key, out var stats)) return null;

            Touch(key);
            return stats;
        }
    }

    /// <summary>
    /// 存。<paramref name="stats"/> 为 <c>null</c> 时**什么都不做** ——
    /// 探针失败（没配路径 / 条目不是 Ogg）不该被记成"永远没有"，换个设置再切回来时应当重试。
    /// </summary>
    public static void Put(GameDef game, TrackDef track, bool useAlt, BitrateStats? stats)
    {
        if (stats is null) return;

        string key = KeyOf(game, track, useAlt);

        lock (Gate)
        {
            if (Map.ContainsKey(key))
            {
                Map[key] = stats;
                Touch(key);
                return;
            }

            Map[key] = stats;
            Lru.AddFirst(key);

            while (Map.Count > Capacity && Lru.Last is { } last)
            {
                Map.Remove(last.Value);
                Lru.RemoveLast();
            }
        }
    }

    /// <summary>丢掉全部。**游戏路径一改就要清**：旧统计属于旧文件，留着会显示一个不存在的码率。</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            Map.Clear();
            Lru.Clear();
        }
    }

    private static void Touch(string key)
    {
        var node = Lru.Find(key);
        if (node is null) { Lru.AddFirst(key); return; }

        Lru.Remove(node);
        Lru.AddFirst(node);
    }
}
