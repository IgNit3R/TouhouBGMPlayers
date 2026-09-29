using ThbgmPlayer.Audio;
using ThbgmPlayer.Data;

namespace ThbgmPlayer.UI;

/// <summary>
/// 「当前曲目信息」面板那行参数串的**纯函数拼装**：容器名映射、码率取整、通道文案。
/// 零 IO、零 WPF 依赖 ⇒ 可离屏自检（<c>BitrateSelfTest</c> 逐字断言）。
///
/// 格式（用户 2026-09-30 拍板）：容器名 / 码率 / 采样率 / 深度 / 通道 / 单轨时长，分隔符 " / "。
/// 示例：`Vorbis Ogg / 12-251 kbps VBR / 44100Hz / 16bit / 2ch stereo / 02:14`。
/// 命名口径见 docs/2026-09-30-codec-container-naming.md §3.1（要改显示名先读它）。
/// </summary>
public static class TrackInfoText
{
    /// <summary>
    /// <see cref="GameDef.Source"/> → 显示名。
    /// ⚠️ Source 是**路由开关不是格式**（值域混了三种命名口径，见同文档 §1）——
    /// 这张映射是当前 29 作实测矩阵的折叠：tfogg 的 196 条索引轨全部是 OggS（已三方印证）、
    /// th06 与 th075 同为 PCM in RIFF/WAVE。若未来索引 / 素材变化导致显示与实际解码错配，
    /// 再考虑改用运行时魔数现判（权威判定点在 <c>AudioSourceFactory.CreateTf</c>）。
    /// </summary>
    public static string ContainerName(string source) => source switch
    {
        "zwav"             => "PCM Raw",
        "wav" or "tfsuica" => "PCM RIFF/WAVE",
        "tfogg"            => "Vorbis Ogg",
        "ncopus"           => "Opus (custom container)",
        _                  => "unknown",
    };

    /// <summary>
    /// 拼整串。<paramref name="stats"/> 为 <c>null</c>（tfogg 首探未归）时码率段占位，
    /// 探针回来会整串重拼一遍 —— 其余字段此刻就位，不会闪空。
    /// </summary>
    public static string Format(GameDef game, TrackDef track, bool useAlt, BitrateStats? stats)
    {
        var td = TrackScanner.Pick(track, useAlt);
        string bps = stats is null ? "… kbps" : Bitrate(stats);

        return $"{ContainerName(game.Source)} / {bps} / {td.Rate}Hz / {td.Bits}bit / " +
               $"{ChannelText(td.Channels)} / {TrackRow.FormatTime(td.LengthTime)}";
    }

    /// <summary>
    /// 码率段。CBR 是单值；VBR 是 min~max 范围（2026-09-30 用户拍板：真实极值口径，
    /// 不用 p05~p95 稳定带 —— 数据都在 <see cref="BitrateStats"/> 里，换口径改这里即可）。
    /// </summary>
    public static string Bitrate(BitrateStats stats) =>
        stats.IsVbr
            ? $"{Kbps(stats.MinBps)}-{Kbps(stats.MaxBps)} kbps VBR"
            : $"{Kbps(stats.AvgBps)} kbps";

    /// <summary>bps → 整数 kbps（四舍五入 AwayFromZero：705.6 → 706、1411.2 → 1411）。</summary>
    public static int Kbps(double bps) =>
        (int)Math.Round(bps / 1000.0, MidpointRounding.AwayFromZero);

    /// <summary>通道段。1/2 声道给出常见叫法，其余只报数（全库实测只有 1 与 2）。</summary>
    public static string ChannelText(int channels) =>
        channels switch { 1 => "1ch mono", 2 => "2ch stereo", _ => $"{channels}ch" };
}
