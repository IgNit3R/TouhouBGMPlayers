using System.IO;
using NAudio.Wave;
using ThbgmPlayer.Core;
using ThbgmPlayer.Data;

namespace ThbgmPlayer.Audio;

/// <summary>
/// 按内嵌索引里的字节偏移，直接从原始游戏文件开一个音频源。
///
/// 路径来自 settings.json，没有任何默认值、不做任何扫描（DESIGN_v3.md §3）。
/// 写死的只有三个名字：thbgm.dat、bgm\、th06_NN.wav（文件名来自索引，不硬编码）。
/// </summary>
public static class AudioSourceFactory
{
    public static IAudioSource Create(GameDef game, TrackDef track)
    {
        string? dir = AppSettings.Current.GetPath(game.Id);
        if (string.IsNullOrWhiteSpace(dir))
            throw new InvalidOperationException($"未设置 {game.Code} 的路径（设置 → 路径）。");

        if (game.IsTfSource)
            return CreateTf(game, track, dir);

        string path = game.IsWavSource
            ? ResolveWavPath(game, dir, track)
            : ResolveDatPath(game, dir);

        if (track.Bits != 16)
            throw new NotSupportedException($"{game.Code} 该轨是 {track.Bits}bit，目前只支持 16bit PCM。");

        var format = new WaveFormat(track.Rate, track.Bits, track.Channels);
        return new PcmFileSource(path, track.Start, track.Intro, track.Length, format);
    }

    /// <summary>
    /// 黄昏作（tf 系）：从容器（Suica / XOR dat / TFPK / cga）解出整条音频字节，
    /// 按魔数路由到内存解码源。循环点是秒（索引里存秒），字节换算在音频源内部做。
    /// </summary>
    private static IAudioSource CreateTf(GameDef game, TrackDef track, string dir)
    {
        if (string.IsNullOrEmpty(track.File))
            throw new InvalidOperationException($"{game.Code} 第 {track.No} 首在索引里没有文件名。");

        using var set = new TfContainerSet(game, dir);
        byte[] bytes = set.GetEntry(track.File);

        // 条目魔数路由：tfogg 是 OggS（ogg），tfsuica 是 RIFF（wav）
        if (bytes.Length >= 4 && bytes.AsSpan(0, 4).SequenceEqual("OggS"u8))
            return new OggMemorySource(bytes, track.LoopStartSec, track.LoopEndSec);
        if (bytes.Length >= 4 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8))
            return new WavEntrySource(bytes, track.LoopStartSec, track.LoopEndSec);

        // TFWA：实测（th145.pak 全部 897 个 TFWA 条目）是 SE 音效＝约 31B 头 + 裸 PCM，
        // 全量扫描零 OGG 载荷；内嵌索引也没有任何 TFWA 轨，正常播放不会走到这里。
        // 头布局（采样率@9 / 声道@17 / 位深@19）与载荷对齐尚无权威规范，先明确拒绝而不是解出垃圾。
        if (bytes.Length >= 4 && bytes.AsSpan(0, 4).SequenceEqual("TFWA"u8))
            throw new NotSupportedException(
                $"{game.Code} 条目 {track.File} 是 TFWA 容器（SE 音效，裸 PCM 载荷），暂不支持播放。");

        string head = Convert.ToHexString(bytes, 0, Math.Min(4, bytes.Length));
        throw new NotSupportedException($"{game.Code} 条目 {track.File} 的魔数不是 OggS/RIFF/TFWA（开头 {head}），无法播放。");
    }

    /// <summary>常规 20 作：路径指到 thbgm.dat 所在的目录。</summary>
    private static string ResolveDatPath(GameDef game, string dir)
    {
        string p = Path.Combine(dir, PathValidator.DatFileName);
        if (!File.Exists(p))
            throw new FileNotFoundException($"{game.Code}：目录下找不到 {PathValidator.DatFileName}", p);
        return p;
    }

    /// <summary>
    /// TH06：路径指到含 bgm\ 的那一级（即 tsa\kouma），音频是 bgm\th06_NN.wav。
    /// 容错：万一用户直接指到了 bgm 目录本身，就别再往下拼一层。
    /// </summary>
    private static string ResolveWavPath(GameDef game, string dir, TrackDef track)
    {
        if (string.IsNullOrEmpty(track.File))
            throw new InvalidOperationException($"{game.Code} 第 {track.No} 首在索引里没有文件名。");

        string bgm = Path.Combine(dir, PathValidator.WavSubDirectory);
        if (!Directory.Exists(bgm) && File.Exists(Path.Combine(dir, track.File)))
            bgm = dir;

        string p = Path.Combine(bgm, track.File);
        if (!File.Exists(p))
            throw new FileNotFoundException(
                $"{game.Code}：找不到 {PathValidator.WavSubDirectory}\\{track.File}", p);
        return p;
    }
}
