using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using ThbgmPlayer.Core;
using ThbgmPlayer.Data;

namespace ThbgmPlayer.Audio;

/// <summary>解析后的导出参数（三个数值都已确定，不再有 null）。</summary>
public readonly record struct ExportParams(int LoopCount, double ExtraSeconds, double FadeSeconds);

/// <summary>
/// 把一首曲子按「普通模式」的时间线渲染成 wav 文件。
///
/// 与播放走同一个 <see cref="LoopSampleProvider"/>，所以导出的时间线和听到的一致：
/// <c>intro + N×loop + X + F</c>。导出一律按普通模式 —— 无限循环没有终点，
/// 设置里的 N / X / F 就是终点（DESIGN_v3.md §8.1）。
/// </summary>
public static class WavExporter
{
    /// <summary>解析出实际生效的参数。null 的字段回落到播放参数。</summary>
    public static ExportParams ResolveParams()
    {
        var (n, x, f) = AppSettings.Current.Export.Resolve(AppSettings.Current.Playback);
        return new ExportParams(Math.Max(0, n), Math.Max(0, x), Math.Max(0, f));
    }

    /// <summary>默认文件名：<c>{作品代号}_{曲号}_{曲名}.wav</c>。</summary>
    /// <summary>
    /// 默认文件名：<c>{作品代号}_{曲号}_{曲名}.wav</c>。
    /// 导出**副版**时追加该作品自己的副版名（TH13 =「灵界版」、新典 =「原典」），
    /// 否则主版与副版会写成同一个文件名互相覆盖。该曲没有副版时（如新典 #16）不加后缀。
    /// </summary>
    public static string DefaultFileName(GameDef game, TrackDef track, bool useAlt = false)
    {
        string name = $"{game.Code}_{track.No:00}_{track.Title}";
        if (useAlt && track.Alt is not null)
            name += "_" + (game.AltLabel ?? "灵界版");
        return SafeFileName(name) + ".wav";
    }

    /// <summary>替换掉 Windows 文件名里的非法字符。</summary>
    public static string SafeFileName(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s.Trim();
    }

    /// <summary>输出目录：设置里指定了就用，否则用程序目录下的 <c>export\</c>。</summary>
    public static string OutputDirectory
    {
        get
        {
            var d = AppSettings.Current.Export.Directory;
            return string.IsNullOrWhiteSpace(d) ? AppPaths.ExportDirectory : d;
        }
    }

    /// <summary>渲染一首。返回实际写出的完整路径。</summary>
    /// <param name="useAlt">灵界版；该曲没有灵界版时自动退回主版。</param>
    /// <param name="outputPath">指定则不走默认命名。</param>
    public static string Export(GameDef game, TrackDef track, bool useAlt, ExportParams p,
                                string? outputPath = null,
                                IProgress<(string File, double Ratio)>? progress = null,
                                CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var td = useAlt && track.Alt is not null ? track.Alt : track;
        string path = outputPath ?? Path.Combine(OutputDirectory, DefaultFileName(game, track, useAlt));

        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var src = AudioSourceFactory.Create(game, td);

        var pb = new PlaybackSettings
        {
            LoopMode = LoopMode.Normal,     // 导出永远按普通模式，与当前循环模式无关
            LoopCount = p.LoopCount,
            ExtraSeconds = p.ExtraSeconds,
            FadeSeconds = p.FadeSeconds,
        };

        var loop = new LoopSampleProvider(src, pb);

        // LoopSampleProvider 对外是 IEEE float，落盘要 16bit PCM，中间挂一层转换
        var toWave = new SampleToWaveProvider16(loop);
        var format = new WaveFormat(td.Rate, 16, td.Channels);

        int bytesPerFrame = td.Channels * 2;
        long wantFrames = (long)Math.Round(loop.TotalTime.TotalSeconds * td.Rate);
        long wantBytes = Math.Max(0, wantFrames * bytesPerFrame);

        using var writer = new WaveFileWriter(path, format);

        byte[] buf = new byte[bytesPerFrame * 8192];
        long done = 0;

        while (done < wantBytes)
        {
            ct.ThrowIfCancellationRequested();

            int chunk = (int)Math.Min(buf.Length, wantBytes - done);

            // NAudio 3 把 IWaveProvider.Read 也 Span 化了：int Read(Span<byte>)，
            // 不再是 Read(byte[], int, int)。落盘那一侧 WaveFileWriter.Write 仍收数组。
            int got = toWave.Read(buf.AsSpan(0, chunk));
            if (got <= 0) break;

            writer.Write(buf, 0, got);
            done += got;
            progress?.Report((path, (double)done / wantBytes));
        }

        return path;
    }

    /// <summary>在后台线程上渲染，避免大批量导出时卡住界面。</summary>
    public static Task<string> ExportAsync(GameDef game, TrackDef track, bool useAlt, ExportParams p,
                                           string? outputPath = null,
                                           IProgress<(string File, double Ratio)>? progress = null,
                                           CancellationToken ct = default) =>
        Task.Run(() => Export(game, track, useAlt, p, outputPath, progress, ct), ct);
}
