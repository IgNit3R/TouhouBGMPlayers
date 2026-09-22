using ThbgmPlayer.Audio;
using ThbgmPlayer.Data;

namespace ThbgmPlayer.UI;

/// <summary>
/// 整轨峰值的**纯内存缓存**（懒生成：切到某曲才扫，扫完放这儿）。
///
/// 为什么不落盘：每曲才 ~80KB，整个列表几 MB 就装下了，而写盘要受
/// 「运行时禁止写 APPDATA、只能写 exe 旁」这条硬约束管 —— 为一个纯派生数据去占用户的目录不划算。
///
/// 键**直接复用 <see cref="PreloadCache.KeyOf"/>**（`{作品}:{曲序}:{alt|main}`）：
/// 主版/副版是两条不同的音频，必须分开缓存；两份不同的键格式迟早会分叉。
/// </summary>
public static class WaveformCache
{
    /// <summary>最多留几首。最坏 ≈ 64 × 94KB ≈ 6MB —— 上限存在的意义是防长期播放累积，不是省内存。</summary>
    private const int Capacity = 64;

    private static readonly object Gate = new();

    /// <summary>键 → 峰值。<c>null</c> 表示"扫过但没扫出来"，**不缓存**（见 <see cref="Put"/>）。</summary>
    private static readonly Dictionary<string, WaveformPeaks> Map = new(StringComparer.Ordinal);
    private static readonly LinkedList<string> Lru = new();       // 头 = 最近用过

    public static string KeyOf(GameDef game, TrackDef track, bool useAlt) =>
        PreloadCache.KeyOf(game, track, useAlt);

    /// <summary>取。命中就把该键挪到 LRU 头部。</summary>
    public static WaveformPeaks? Get(GameDef game, TrackDef track, bool useAlt)
    {
        string key = KeyOf(game, track, useAlt);

        lock (Gate)
        {
            if (!Map.TryGetValue(key, out var peaks)) return null;

            Touch(key);
            return peaks;
        }
    }

    /// <summary>
    /// 存。<paramref name="peaks"/> 为 <c>null</c> 时**什么都不做** ——
    /// 扫描失败（没配路径 / 容器不支持）不该被记成"永远没有"，换个设置再切回来时应当重试。
    /// </summary>
    public static void Put(GameDef game, TrackDef track, bool useAlt, WaveformPeaks? peaks)
    {
        if (peaks is null) return;

        string key = KeyOf(game, track, useAlt);

        lock (Gate)
        {
            if (Map.ContainsKey(key))
            {
                Map[key] = peaks;
                Touch(key);
                return;
            }

            Map[key] = peaks;
            Lru.AddFirst(key);

            while (Map.Count > Capacity && Lru.Last is { } last)
            {
                Map.Remove(last.Value);
                Lru.RemoveLast();
            }
        }
    }

    /// <summary>丢掉全部。**游戏路径一改就要清** —— 旧峰值属于旧文件，留着会画出一个不存在的波形。</summary>
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
