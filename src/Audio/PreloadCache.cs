using ThbgmPlayer.Data;

namespace ThbgmPlayer.Audio;

/// <summary>
/// 预读缓存：把「马上要播的曲子」提前在后台读进内存，切曲时零等待
/// （docs/2026-09-06-checkpoint-preload-plan.md）。
///
/// 整轨驻留内存的确定性模型不变 —— 预读只是把可预测的读取提前到空闲时间。
///
/// 三个预测源（都在 MainWindow 触发）：
///   ① 列表顺序预读：播放后预读下一首（随机模式不预测）
///   ② 灵界版配对预读：播带灵界版的曲子时预读另一版本（仅 TH13）
///   ③ 选中即预读：光标停留 ~250ms 后预读选中的曲子（防抖，乱扫不触发）
///
/// ── 容量与内存 ──
/// 固定 2 槽 LRU。加上正在播的那首和 PcmFileSource 的复用池，
/// 常态 ≈ 播放 20MB + 预读 2×20MB ≈ 60MB，全遇到最大轨（48MB）也才 ~190MB。
/// 被淘汰或 Clear 的条目立刻 Dispose，缓冲区回池。
///
/// ── 在途读取 ──
/// 同一键同时在读只有一份；读完入缓存。不取消在途读取（一次 20–50ms，
/// 取消逻辑不值这个复杂度），过期的结果大不了落进缓存被 LRU 顶掉。
/// 预读失败（没配路径、读不到文件）安静吞掉 —— 预读是尽人事，播放路径有自己
/// 的报错渠道，不该被预读的异常干扰。
/// </summary>
public static class PreloadCache
{
    private const int Capacity = 2;

    private sealed class Entry
    {
        public required string Key;
        public required IAudioSource Source;
    }

    private static readonly object Gate = new();
    private static readonly LinkedList<Entry> Lru = new();   // 头部 = 最新
    private static readonly HashSet<string> InFlight = new(StringComparer.Ordinal);
    private static long _generation;   // Clear 时 +1，让在途读取的过期结果直接丢弃

    /// <summary>缓存键：作品 + 曲序 + 版本（主版 / 灵界版是两条不同的数据）。</summary>
    public static string KeyOf(GameDef game, TrackDef track, bool useAlt) =>
        $"{game.Id}:{track.No}:{(useAlt && track.HasAlt ? "alt" : "main")}";

    /// <summary>
    /// 后台预读。已在缓存或已在读就什么都不做；调用方不 await，失败也安静。
    /// </summary>
    public static void Preload(GameDef game, TrackDef track, bool useAlt)
    {
        var key = KeyOf(game, track, useAlt);
        long gen;

        lock (Gate)
        {
            if (InFlight.Contains(key) || FindNodeLocked(key) is not null) return;
            InFlight.Add(key);
            gen = _generation;
        }

        Task.Run(() =>
        {
            IAudioSource? src = null;
            try
            {
                // 与 PlayerEngine.Pick 同一条规则：要灵界版且有灵界版才取 Alt
                var td = useAlt && track.Alt is not null ? track.Alt : track;
                src = AudioSourceFactory.Create(game, td);
            }
            catch
            {
                // 没配路径 / 文件读不到：预读是尽人事，安静放弃
            }

            lock (Gate)
            {
                InFlight.Remove(key);

                // 读取期间发生过 Clear（比如用户改了路径）：这条是旧文件的音源，丢
                if (src is null || gen != _generation)
                {
                    if (src is not null) { try { src.Dispose(); } catch { } }
                    return;
                }

                Lru.AddFirst(new Entry { Key = key, Source = src });
                while (Lru.Count > Capacity)
                {
                    var tail = Lru.Last!.Value;
                    Lru.RemoveLast();
                    try { tail.Source.Dispose(); } catch { /* 关不掉就算了 */ }
                }
            }
        });
    }

    /// <summary>
    /// 取出一条预读好的音源（取出即移出缓存，音源有播放位置状态，不可复用）。
    /// 没命中（没预读过 / 还在读）返回 null，调用方走同步读取的老路。
    /// </summary>
    public static IAudioSource? TryTake(GameDef game, TrackDef track, bool useAlt)
    {
        var key = KeyOf(game, track, useAlt);
        lock (Gate)
        {
            var node = FindNodeLocked(key);
            if (node is null) return null;
            Lru.Remove(node);
            return node.Value.Source;
        }
    }

    /// <summary>清空缓存（改了路径设置后缓存里的音源就指向旧文件了，必须丢）。
    /// 同时把代数 +1：在途读取完成时发现自己过期，结果直接 Dispose，不会污染缓存。</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            _generation++;
            foreach (var e in Lru)
            {
                try { e.Source.Dispose(); } catch { /* 忽略 */ }
            }
            Lru.Clear();
        }
    }

    /// <summary>当前缓存条数（诊断用）。</summary>
    public static int Count
    {
        get { lock (Gate) return Lru.Count; }
    }

    private static LinkedListNode<Entry>? FindNodeLocked(string key)
    {
        for (var n = Lru.First; n is not null; n = n.Next)
        {
            if (n.Value.Key == key) return n;
        }
        return null;
    }
}
