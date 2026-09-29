namespace ThbgmPlayer.Audio;

/// <summary>
/// 一条音轨的**存储码率统计**（压缩后落在磁盘上的口径，不是解码后喂声卡的 PCM 码率 ——
/// 两个口径对压缩格式差一倍以上，见 docs/2026-09-30-codec-container-naming.md §7.6）。
///
/// 放在 Audio 层而不是 UI 层的理由与 <see cref="WaveformPeaks"/> 相同：
/// 它是**探针的产物**（由 <see cref="VorbisPages"/> / <see cref="BitrateProbe"/> 生成），
/// UI 只是消费者；反过来放会让 Audio 到 UI 形成反向依赖。
///
/// ⚠️ **一次算全量**：min 与 p05/p25/p50/p75/p95、max、avg 全部保留。
/// 当前 UI 只用 min~max（2026-09-30 用户拍板：真实极值口径），但实测它会被极少数
/// 静音页 / 爆音页带偏（单曲极差 1.4~21.4 倍），将来若改用 p05~p95 稳定带，
/// 只应是"换个字段读"，绝不该是"重新探一遍容器"。
/// </summary>
public sealed class BitrateStats
{
    /// <summary>全曲最低瞬时码率（bps）。近静音页能把它拉得很低（实测全库最低 11.7 kbps）。</summary>
    public double MinBps { get; }

    /// <summary>第 5 百分位（bps）。稳定带下沿。</summary>
    public double P05Bps { get; }

    /// <summary>第 25 百分位（bps）。</summary>
    public double P25Bps { get; }

    /// <summary>中位数（bps）。</summary>
    public double P50Bps { get; }

    /// <summary>第 75 百分位（bps）。</summary>
    public double P75Bps { get; }

    /// <summary>第 95 百分位（bps）。稳定带上沿。</summary>
    public double P95Bps { get; }

    /// <summary>全曲最高瞬时码率（bps）。爆音页能把它顶得很高（实测全库最高 894.6 kbps）。</summary>
    public double MaxBps { get; }

    /// <summary>整曲平均码率（bps）= 文件字节数 × 8 ÷ 总时长。</summary>
    public double AvgBps { get; }

    /// <summary>参与统计的 Ogg 页数。0 表示这不是页扫出来的（CBR 常数，见 <see cref="Cbr"/>）。</summary>
    public int Pages { get; }

    /// <summary>是否 VBR（码率是范围）。PCM 与新典 Opus 都是 false（格式决定的常数）。</summary>
    public bool IsVbr { get; }

    public BitrateStats(double minBps, double p05Bps, double p25Bps, double p50Bps,
                        double p75Bps, double p95Bps, double maxBps,
                        double avgBps, int pages, bool isVbr)
    {
        MinBps = minBps;
        P05Bps = p05Bps;
        P25Bps = p25Bps;
        P50Bps = p50Bps;
        P75Bps = p75Bps;
        P95Bps = p95Bps;
        MaxBps = maxBps;
        AvgBps = avgBps;
        Pages = pages;
        IsVbr = isVbr;
    }

    /// <summary>
    /// CBR 常数（PCM / 新典 Opus）：所有分位全是同一个值，<see cref="Pages"/> 为 0
    /// （不是页扫出来的）、<see cref="IsVbr"/> 为 false。
    /// </summary>
    public static BitrateStats Cbr(double bps) =>
        new(bps, bps, bps, bps, bps, bps, bps, bps, 0, isVbr: false);
}
