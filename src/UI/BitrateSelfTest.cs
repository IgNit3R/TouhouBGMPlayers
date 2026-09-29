using System.Buffers.Binary;
using System.Text;
using ThbgmPlayer.Audio;
using ThbgmPlayer.Core;
using ThbgmPlayer.Data;
using ThbgmPlayer.Viz;

namespace ThbgmPlayer.UI;

/// <summary>
/// 码率探针与面板参数串的离屏自检。条目格式与 <c>WaveformSelfTest</c> 一致。
///
/// 覆盖三层：
/// <list type="bullet">
/// <item><see cref="VorbisPages"/> 纯算术 —— 合成 Ogg（页长 / 采样率手工可算，期望值逐个断言）。</item>
/// <item><see cref="TrackInfoText"/> 拼串 —— 四类容器 + 占位 + 副版共 5 条**逐字**比对。</item>
/// <item><see cref="BitrateProbe"/> 真文件抽查 —— 配了路径才跑（口径照 <c>WaveformSelfTest.CheckRealFile</c>），
/// 对 tfogg 每部抽一首；报出来的 min/max 可与 196 首 Python 探针对表
/// （`.workbuddy/vorbis_page_bps_196.csv`），移植对不对一眼可见。</item>
/// </list>
/// </summary>
internal static class BitrateSelfTest
{
    public static List<VizSelfTest.Result> Run() => new()
    {
        CheckPagesSynthetic(),
        CheckPagesRejects(),
        CheckCbr(),
        CheckContainerName(),
        CheckFormatStrings(),
        CheckKbpsRounding(),
        CheckRealFileProbe(),
    };

    // ---------------------------------------------------------------- VorbisPages：合成 Ogg

    /// <summary>
    /// 合成一条四个页的 OGG：识别头（sr=48000）/ comment 占位 / 两个音频页。
    /// 音频页样本数都是 960（= 20ms @48kHz），页长由段表推得：
    /// 480B 包 → 2 段 → 页 509B ⇒ 203,600 bps；960B 包 → 4 段 → 页 991B ⇒ 396,400 bps。
    /// </summary>
    private static VizSelfTest.Result CheckPagesSynthetic()
    {
        const string Title = "Vorbis 页算术（合成 Ogg：min / 分位 / max / 页数）";

        try
        {
            byte[] ogg = Concat(
                Page(0, IdHeader(sampleRate: 48_000)),
                Page(0, new byte[1]),                    // comment+setup 占位：granule 0，天然跳过
                Page(960, new byte[480]),
                Page(1920, new byte[960]));

            var s = VorbisPages.Scan(ogg);
            if (s is null)
                return new VizSelfTest.Result(Title, false, "合法合成 OGG 返回 null");

            var bad = new List<string>();
            void Expect(double actual, double expected, string name)
            {
                if (Math.Abs(actual - expected) > 1e-6)
                    bad.Add($"{name}={actual:0.###}（期望 {expected:0.###}）");
            }

            Expect(s.MinBps, 203_600, "min");
            Expect(s.P05Bps, 213_240, "p05");
            Expect(s.P50Bps, 300_000, "p50");
            Expect(s.P95Bps, 386_760, "p95");
            Expect(s.MaxBps, 396_400, "max");
            Expect(s.AvgBps, 300_000, "avg");
            if (s.Pages != 2) bad.Add($"pages={s.Pages}（期望 2）");
            if (!s.IsVbr) bad.Add("IsVbr=false（期望 true）");

            return bad.Count == 0
                ? new VizSelfTest.Result(Title, true, "min 203600 / max 396400 / 2 页，与手算一致")
                : new VizSelfTest.Result(Title, false, string.Join("；", bad));
        }
        catch (Exception ex)
        {
            return new VizSelfTest.Result(Title, false, ex.Message);
        }
    }

    /// <summary>非法输入一律 null，不许抛：RIFF 字节、只有头页没有音频页、空数组。</summary>
    private static VizSelfTest.Result CheckPagesRejects()
    {
        const string Title = "Vorbis 页算术（非法输入静默 null）";

        try
        {
            var cases = new (byte[] Data, string Name)[]
            {
                (Array.Empty<byte>(), "空数组"),
                (new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00 }, "RIFF 头"),
                (Concat(Page(0, IdHeader(48_000)), Page(0, new byte[1])), "只有头两页无音频页"),
            };

            var bad = new List<string>();
            foreach (var (data, name) in cases)
                if (VorbisPages.Scan(data) is not null)
                    bad.Add($"{name}：应返回 null");

            return bad.Count == 0
                ? new VizSelfTest.Result(Title, true, "三类非法输入全部 null")
                : new VizSelfTest.Result(Title, false, string.Join("；", bad));
        }
        catch (Exception ex)
        {
            return new VizSelfTest.Result(Title, false, ex.Message);
        }
    }

    // ---------------------------------------------------------------- TrackInfoText：拼串

    private static VizSelfTest.Result CheckCbr()
    {
        const string Title = "CBR 工厂（常数 / IsVbr=false / 段文案）";

        try
        {
            var s = BitrateStats.Cbr(192_000);
            var bad = new List<string>();

            if (s.AvgBps != 192_000 || s.MinBps != 192_000 || s.MaxBps != 192_000) bad.Add("字段不是常数");
            if (s.IsVbr) bad.Add("IsVbr 应为 false");
            if (s.Pages != 0) bad.Add("Pages 应为 0（非页扫产物）");
            if (TrackInfoText.Bitrate(s) != "192 kbps") bad.Add($"段文案={TrackInfoText.Bitrate(s)}");

            return bad.Count == 0
                ? new VizSelfTest.Result(Title, true, "192 kbps 单值段文案正确")
                : new VizSelfTest.Result(Title, false, string.Join("；", bad));
        }
        catch (Exception ex)
        {
            return new VizSelfTest.Result(Title, false, ex.Message);
        }
    }

    /// <summary>四个容器名 + unknown 兜底。改显示名先读 docs/2026-09-30-codec-container-naming.md §3.1。</summary>
    private static VizSelfTest.Result CheckContainerName()
    {
        const string Title = "容器名映射（Source → 显示名）";

        try
        {
            var map = new (string Source, string Expected)[]
            {
                ("zwav", "PCM Raw"),
                ("wav", "PCM RIFF/WAVE"),
                ("tfsuica", "PCM RIFF/WAVE"),
                ("tfogg", "Vorbis Ogg"),
                ("ncopus", "Opus (custom container)"),
                ("bogus", "unknown"),
            };

            var bad = map.Where(c => TrackInfoText.ContainerName(c.Source) != c.Expected)
                         .Select(c => $"{c.Source} → {TrackInfoText.ContainerName(c.Source)}（期望 {c.Expected}）")
                         .ToList();

            return bad.Count == 0
                ? new VizSelfTest.Result(Title, true, "5 个 Source 全对，未知值落 unknown")
                : new VizSelfTest.Result(Title, false, string.Join("；", bad));
        }
        catch (Exception ex)
        {
            return new VizSelfTest.Result(Title, false, ex.Message);
        }
    }

    /// <summary>整串**逐字**比对：四类容器 + 未就绪占位 + 副版字段切换。</summary>
    private static VizSelfTest.Result CheckFormatStrings()
    {
        const string Title = "面板参数串（四类容器 / 占位 / 副版）";

        try
        {
            // PCM Raw：zwav，93 秒整（Length = 176400 B/s × 93s）
            var zwavGame = new GameDef { Id = "th07", Code = "TH07", Source = "zwav" };
            var zwavTrack = new TrackDef { No = 1, Rate = 44100, Channels = 2, Bits = 16, Length = 16_405_200 };

            // Vorbis：tfogg，134 秒（DurationSample 驱动 LengthTime）
            var oggGame = new GameDef { Id = "th135", Code = "TH13.5", Source = "tfogg" };
            var oggTrack = new TrackDef { No = 2, Rate = 44100, Channels = 2, Bits = 16,
                                           LoopStartSample = 882_000, DurationSample = 5_909_400 };

            // Opus：ncopus，120 秒 @48kHz
            var ncGame = new GameDef { Id = "th06nc", Code = "TH06NC", Source = "ncopus" };
            var ncTrack = new TrackDef { No = 1, Rate = 48000, Channels = 2, Bits = 16,
                                          DurationSample = 5_760_000 };

            // 副版：主版 44100/16bit，Alt 22050（TH13 霊界版口径），90 秒
            var altMain = new TrackDef { No = 3, Rate = 44100, Channels = 2, Bits = 16, Length = 21_168_000,
                                          Alt = new TrackDef { No = 3, Rate = 22050, Channels = 2, Bits = 16,
                                                               Length = 7_938_000 } };

            // 实测口径的 VBR 统计（th135 futo2.ogg：min 11.7k / max 250.9k）
            var vbr = new BitrateStats(11_700, 160_000, 185_000, 194_300, 210_000, 224_800,
                                       250_900, 191_500, 87, isVbr: true);

            var cases = new (string Actual, string Expected, string Name)[]
            {
                (TrackInfoText.Format(zwavGame, zwavTrack, false, BitrateProbe.Instant(zwavGame, zwavTrack, false)),
                 "PCM Raw / 1411 kbps / 44100Hz / 16bit / 2ch stereo / 01:33", "zwav"),
                (TrackInfoText.Format(oggGame, oggTrack, false, vbr),
                 "Vorbis Ogg / 12-251 kbps VBR / 44100Hz / 16bit / 2ch stereo / 02:14", "tfogg+VBR"),
                (TrackInfoText.Format(oggGame, oggTrack, false, null),
                 "Vorbis Ogg / … kbps / 44100Hz / 16bit / 2ch stereo / 02:14", "tfogg 未就绪"),
                (TrackInfoText.Format(ncGame, ncTrack, false, BitrateProbe.Instant(ncGame, ncTrack, false)),
                 "Opus (custom container) / 192 kbps / 48000Hz / 16bit / 2ch stereo / 02:00", "ncopus"),
                (TrackInfoText.Format(zwavGame, altMain, true, BitrateProbe.Instant(zwavGame, altMain, true)),
                 "PCM Raw / 706 kbps / 22050Hz / 16bit / 2ch stereo / 01:30", "副版 22050"),
            };

            var bad = cases.Where(c => c.Actual != c.Expected)
                           .Select(c => $"{c.Name}：{c.Actual}（期望 {c.Expected}）")
                           .ToList();

            return bad.Count == 0
                ? new VizSelfTest.Result(Title, true, "5 条逐字一致（含 1411 / 706 / 192 取整）")
                : new VizSelfTest.Result(Title, false, string.Join("；", bad));
        }
        catch (Exception ex)
        {
            return new VizSelfTest.Result(Title, false, ex.Message);
        }
    }

    private static VizSelfTest.Result CheckKbpsRounding()
    {
        const string Title = "kbps 取整（AwayFromZero）";

        try
        {
            var cases = new (double Bps, int Expected)[]
            {
                (1_411_200, 1411),
                (705_600, 706),      // 705.6 进位
                (192_000, 192),
                (11_700, 12),        // 11.7 进位
                (250_900, 251),
            };

            var bad = cases.Where(c => TrackInfoText.Kbps(c.Bps) != c.Expected)
                           .Select(c => $"{c.Bps:0} → {TrackInfoText.Kbps(c.Bps)}（期望 {c.Expected}）")
                           .ToList();

            return bad.Count == 0
                ? new VizSelfTest.Result(Title, true, "5 组取整全对")
                : new VizSelfTest.Result(Title, false, string.Join("；", bad));
        }
        catch (Exception ex)
        {
            return new VizSelfTest.Result(Title, false, ex.Message);
        }
    }

    // ---------------------------------------------------------------- BitrateProbe：真文件抽查

    /// <summary>
    /// 配了路径才跑（否则跳过，不算失败）：对每部 tfogg 抽第一首跑完整探针链
    /// （容器解包 + 页遍历）。报出的 min/max 可与 Python 探针对表验证移植。
    /// </summary>
    private static VizSelfTest.Result CheckRealFileProbe()
    {
        const string Title = "真文件抽查（配了路径才跑：tfogg 页级码率范围）";

        try
        {
            var games = TrackIndex.Games
                .Where(g => g.Source == "tfogg"
                         && AppSettings.Current.GetPath(g.Id) is not null
                         && g.Tracks.Count > 0)
                .ToList();

            if (games.Count == 0)
                return new VizSelfTest.Result(Title, true, "未配置任何 tfogg 游戏路径 ⇒ 跳过（不算通过也不算失败）");

            bool ok = true;
            var rows = new List<string>();

            foreach (var game in games)
            {
                var track = game.Tracks[0];
                var stats = BitrateProbe.Async(game, track, false).GetAwaiter().GetResult();

                if (stats is null)
                {
                    rows.Add($"{game.Id} {track.File}：探不出（跳过）");
                    continue;
                }

                // 合理性：min>0、max≥min、均值夹在中间、页数>0、max 低于荒谬上限（实测全库最高 894.6k）
                bool sane = stats.MinBps > 0
                         && stats.MaxBps >= stats.MinBps
                         && stats.AvgBps >= stats.MinBps - 1e-9
                         && stats.AvgBps <= stats.MaxBps + 1e-9
                         && stats.Pages > 0
                         && stats.MaxBps < 10_000_000;
                ok &= sane;

                rows.Add($"{game.Id} {track.File}：{TrackInfoText.Kbps(stats.MinBps)}-{TrackInfoText.Kbps(stats.MaxBps)} kbps，{stats.Pages} 页");
            }

            return new VizSelfTest.Result(Title, ok, string.Join("；", rows));
        }
        catch (Exception ex)
        {
            return new VizSelfTest.Result(Title, false, ex.Message);
        }
    }

    // ---------------------------------------------------------------- 合成 Ogg 的零件

    /// <summary>拼一条 OGG 字节流。</summary>
    private static byte[] Concat(params byte[][] pages)
    {
        int total = pages.Sum(p => p.Length);
        var ogg = new byte[total];
        int off = 0;
        foreach (var p in pages)
        {
            Buffer.BlockCopy(p, 0, ogg, off, p.Length);
            off += p.Length;
        }
        return ogg;
    }

    /// <summary>
    /// 造一个 Ogg 页。探针只读 granule 与段表，serial / seq / CRC 全部留零。
    /// 段表按包长自动展开（包长 ÷ 255 向上取整，末段为余数；255 表示还有下一段）。
    /// </summary>
    private static byte[] Page(long granule, byte[] packet)
    {
        int segs = (packet.Length + 254) / 255;
        var p = new byte[27 + segs + packet.Length];

        "OggS"u8.CopyTo(p);
        BinaryPrimitives.WriteInt64LittleEndian(p.AsSpan(6), granule);
        p[26] = (byte)segs;

        int off = 27;
        int rest = packet.Length;
        while (rest >= 255)
        {
            p[off++] = 255;
            rest -= 255;
        }
        p[off] = (byte)rest;

        packet.CopyTo(p, 27 + segs);
        return p;
    }

    /// <summary>
    /// Vorbis identification header（固定 30 字节）："\u0001vorbis" + version + channels +
    /// sample_rate + 三个码率槽 + blocksizes + framing。探针只读 sample_rate（偏移 12）。
    /// </summary>
    private static byte[] IdHeader(int sampleRate)
    {
        var p = new byte[30];
        "\u0001vorbis"u8.CopyTo(p);
        p[11] = 2;                                                  // channels
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(12), sampleRate);
        return p;
    }
}
