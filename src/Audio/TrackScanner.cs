using ThbgmPlayer.Data;

namespace ThbgmPlayer.Audio;

/// <summary>
/// 整轨扫描：把一首曲子从头到尾读一遍，算出波形面板要的峰值包络（<see cref="WaveformPeaks"/>）。
///
/// 做法**照抄两处现成先例**：
/// · <c>WavExporter.Export</c>（`Audio/WavExporter.cs:76` 建源、`:101-115` 循环读到末尾）——
///   独立建一条源、顺序读完、释放；
/// · <c>PreloadCache.Preload</c>（`Audio/PreloadCache.cs:49-68`）—— 后台 `Task.Run`、失败静默。
///
/// ⚠️ **必须独立建源**：播放链上那条源是引擎持有的，共用会打断正在播的音频。
/// 四种解码源构造时就把整轨解进内存，所以"扫一遍"只是把已解好的数据顺序读一次，很快。
/// ⚠️ **不要动 <c>PreloadCache</c>**：那是预读缓存，`TryTake` 会把源取走、破坏"切曲零等待"。
///
/// ⚠️ 已知代价：切曲后再扫描，等于整轨又被解码一次（CPU 翻倍，好在都在后台线程）。
/// </summary>
public static class TrackScanner
{
    /// <summary>
    /// 读整轨时的单次块大小（帧）。**照抄 <c>WavExporter</c> 的 `bytesPerFrame * 8192`**
    /// （`Audio/WavExporter.cs:98`）—— 那条路径已经跑通所有源（含新典那种对包对齐有讲究的
    /// Opus 容器），别自己另挑一个数。
    /// </summary>
    private const int BatchFrames = 8192;

    /// <summary>
    /// 选哪一版扫。**与 <c>PlayerEngine</c>（`:327-328`）和 <c>PreloadCache</c>（`:67`）同一条规则** ——
    /// 抽出来是为了让自检能拿它跟引擎的规则逐例比对，避免三处各写一份而漂移。
    /// </summary>
    public static TrackDef Pick(TrackDef track, bool useAlt) =>
        useAlt && track.Alt is not null ? track.Alt : track;

    /// <summary>
    /// 从**已建好的**源扫出峰值。可注入假源 ⇒ 这条能在自检里离屏验。
    ///
    /// 返回 <c>null</c> 表示这次扫不出东西：源不是 16bit、长度为 0、或者被取消了。
    /// （调用方想知道是"取消"还是"失败"，看它自己的 <see cref="CancellationToken"/> 即可 ——
    /// 不为此多造一个返回类型。）
    /// </summary>
    public static WaveformPeaks? Scan(IAudioSource source,
                                      CancellationToken ct = default,
                                      IProgress<double>? progress = null)
    {
        var fmt = source.Format;
        if (fmt.BitsPerSample != 16 || fmt.BlockAlign <= 0 || fmt.SampleRate <= 0) return null;

        long totalBytes = source.TotalBytes;
        long totalFrames = totalBytes / fmt.BlockAlign;
        if (totalFrames <= 0) return null;

        int bucketCount = (int)((totalFrames + WaveformPeaks.FramesPerBucket - 1) / WaveformPeaks.FramesPerBucket);
        if (bucketCount <= 0) return null;

        var min = new sbyte[bucketCount];
        var max = new sbyte[bucketCount];

        // 每桶的样本数（一帧 = 所有声道各一个样本，交错存放）
        int samplesPerBucket = WaveformPeaks.FramesPerBucket * fmt.Channels;

        byte[] buf = new byte[fmt.BlockAlign * BatchFrames];

        source.Seek(0);

        int bucket = 0;
        int inBucket = 0;
        int curMin = sbyte.MaxValue;    // 起手取反端点，保证第一帧一定刷新它们
        int curMax = sbyte.MinValue;

        int got;
        while ((got = source.Read(buf, 0, buf.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();

            for (int off = 0; off + 1 < got; off += 2)
            {
                // PCM16 LE → 取**高 8 位**：96px 高的面板用 256 级足够，省一半内存
                int v = (sbyte)buf[off + 1];

                if (v < curMin) curMin = v;
                if (v > curMax) curMax = v;

                if (++inBucket < samplesPerBucket) continue;

                if (bucket < bucketCount) { min[bucket] = (sbyte)curMin; max[bucket] = (sbyte)curMax; }
                bucket++;
                inBucket = 0;
                curMin = sbyte.MaxValue;
                curMax = sbyte.MinValue;
            }

            if (progress is not null)
                progress.Report(totalBytes <= 0 ? 0 : Math.Min(1.0, (double)source.PositionBytes / totalBytes));
        }

        // 尾巴那半桶也要收（否则最后一小段没波形）—— 只在真收到过样本时收
        if (inBucket > 0 && bucket < bucketCount)
        {
            min[bucket] = (sbyte)curMin;
            max[bucket] = (sbyte)curMax;
        }

        double secPerBucket = (double)WaveformPeaks.FramesPerBucket / fmt.SampleRate;
        double seconds = (double)totalFrames / fmt.SampleRate;

        // 循环入口位置也在这里一起带出去：它是**运行时**的量（索引里可能没有），
        // 而且只有扫描这一趟手里正好握着源。换算一次，免得下游各自再算一遍而漂移。
        double intro = (double)source.IntroBytes / fmt.BlockAlign / fmt.SampleRate;

        return new WaveformPeaks(min, max, seconds, secPerBucket, intro);
    }

    /// <summary>
    /// 后台扫一首曲子的**指定版本**。独立建源、不碰播放链；失败静默返回 <c>null</c>。
    ///
    /// ⚠️ 能取消的只是峰值循环读 —— **整轨解码在源构造里，取消不了**。
    /// 所以快速连切曲子会留下几个"跑完即弃"的后台扫描（都在后台线程，不卡 UI）。这是已知代价。
    /// </summary>
    public static Task<WaveformPeaks?> ScanAsync(GameDef game, TrackDef track, bool useAlt,
                                                 CancellationToken ct = default,
                                                 IProgress<double>? progress = null) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var td = Pick(track, useAlt);
                using var source = AudioSourceFactory.Create(game, td);
                return Scan(source, ct, progress);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception)
            {
                // 没配路径 / 容器不支持（TFWA）等等 —— 静默，面板显示"无波形"即可，别弹窗
                return null;
            }
        }, ct);
}
