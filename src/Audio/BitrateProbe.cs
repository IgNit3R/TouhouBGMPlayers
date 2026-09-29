using ThbgmPlayer.Core;
using ThbgmPlayer.Data;

namespace ThbgmPlayer.Audio;

/// <summary>
/// 码率探针：给「当前曲目信息」面板取 <see cref="BitrateStats"/>。
/// 结构照 <see cref="TrackScanner"/>：即时部分同步算，要读文件的部分后台算、失败静默。
///
/// 三类音源的码率来源（口径与常数值见 docs/2026-09-30-codec-container-naming.md §7）：
/// <list type="bullet">
/// <item>PCM（zwav / wav / tfsuica）：格式常数，<c>采样率 × 声道 × 位深</c>，零 IO。</item>
/// <item>新典 Opus（ncopus）：容器定长 ⇒ 恒 192 kbps，零 IO（见 <see cref="NcOpusPayloadBps"/>）。</item>
/// <item>Vorbis（tfogg）：逐页范围，必须读容器条目 ⇒ <see cref="Instant"/> 返回 null、走 <see cref="Async"/>。</item>
/// </list>
///
/// ⚠️ **必须独立开容器**：播放链上的 <c>TfContainerSet</c> 由工厂持有且随源释放，
/// 这里自己开一套（只读、不解码）。已知代价：<c>TfContainerSet</c> 无缓存，
/// 每次探针都重建目录表 —— 但页遍历只有几毫秒级，结果由 <c>UI/TrackInfoCache</c> 兜住，
/// 同一曲不会反复探。**绝不碰 <see cref="PreloadCache"/>**：预读源是给切曲零等待用的。
/// </summary>
public static class BitrateProbe
{
    /// <summary>
    /// 新典 Opus 容器的载荷存储码率（bps）。**与 <c>Nc/OpusMemorySource.cs</c> 的容器常量同源**：
    /// 40B 头 + N×488B 定长记录，每记录 480B 裸包 = 960 帧 @48kHz/20ms ⇒ 480×8÷0.02 = 192,000。
    /// 那边改容器布局时这里必须跟着改（自检里也有 192 的断言兜底）。
    /// </summary>
    private const double NcOpusPayloadBps = 192_000;

    /// <summary>
    /// 即时取码率：纯算术、零 IO。只有 tfogg 返回 <c>null</c>（范围必须读容器才能算）。
    /// </summary>
    public static BitrateStats? Instant(GameDef game, TrackDef track, bool useAlt)
    {
        var td = TrackScanner.Pick(track, useAlt);
        return game.Source switch
        {
            "tfogg"  => null,
            "ncopus" => BitrateStats.Cbr(NcOpusPayloadBps),
            _        => BitrateStats.Cbr((double)td.Rate * td.Channels * td.Bits),   // PCM
        };
    }

    /// <summary>
    /// 后台探 tfogg 的页级码率范围：只读容器、**不解码**（解码那一遍留给播放链，这里只走页头）。
    /// 非 tfogg 同步返回 <see cref="Instant"/> 的结果。任何失败（没配路径 / 容器异常 /
    /// 条目不是 Ogg）都静默返回 <c>null</c> —— 面板占位保留，不弹窗。
    /// </summary>
    public static Task<BitrateStats?> Async(GameDef game, TrackDef track, bool useAlt,
                                            CancellationToken ct = default)
    {
        if (game.Source != "tfogg")
            return Task.FromResult(Instant(game, track, useAlt));

        return Task.Run(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                string? dir = AppSettings.Current.GetPath(game.Id);
                if (string.IsNullOrWhiteSpace(dir)) return null;   // 没配路径 → 静默

                var td = TrackScanner.Pick(track, useAlt);
                if (string.IsNullOrEmpty(td.File)) return null;

                using var set = new TfContainerSet(game, dir);
                return VorbisPages.Scan(set.GetEntry(td.File));    // 与播放同一条取字节路径
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch
            {
                return null;
            }
        }, ct);
    }
}
