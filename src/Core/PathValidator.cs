// 注意：WPF 项目里 System.IO 不在隐式 using 中（SDK 主动摘除，
// 否则 System.IO.Path 会和生成代码里的 System.Windows.Shapes.Path 撞名）。
// 所以凡是用到 File / Directory / Path 的文件都要显式写这一行，不能依赖隐式 using。
using System.IO;
using System.Text;
using ThbgmPlayer.Data;

namespace ThbgmPlayer.Core;

/// <summary>某部作品的路径校验结果。</summary>
public enum PathStatus
{
    /// <summary>未设置路径 —— 列表里灰显，不可播。</summary>
    NotSet,
    /// <summary>校验通过。</summary>
    Ok,
    /// <summary>有疑点但不阻断，仍可尝试播放（界面标黄）。</summary>
    Warning,
    /// <summary>校验失败。</summary>
    Failed,
}

public sealed class ValidateResult
{
    public PathStatus Status { get; init; }
    public string Message { get; init; } = "";

    public static readonly ValidateResult NotSet =
        new() { Status = PathStatus.NotSet, Message = "未设置" };

    public static ValidateResult Ok(string msg = "正常") =>
        new() { Status = PathStatus.Ok, Message = msg };

    public static ValidateResult Warn(string msg) =>
        new() { Status = PathStatus.Warning, Message = msg };

    public static ValidateResult Fail(string msg) =>
        new() { Status = PathStatus.Failed, Message = msg };
}

/// <summary>
/// 路径校验。只读取原始游戏文件，不做任何解包或写入。
///
/// 校验依据（均已在真实数据上验证，见 tools/ 下的探测脚本）：
///   1. thbgm.dat 头部 16 字节 ZWAV 头：magic="ZWAV"，version=1，
///      byte[9]=主版本 BCD，byte[8]=小数位 ×16（0x00/0x30/0x50/0x80 对应 .0/.3/.5/.8）。
///      用头里的作品号和用户填的路径比对，可抓出"把 TH13 目录指给 TH14"这类手滑。
///   2. 文件大小 == 16 + Σ(所有数据块字节数)，实测 20 作 Δ 全为 0。
///   3. TH06 无 dat，音频是 bgm\th06_NN.wav，校验 17 个文件是否齐全。
///
/// 校验失败只标黄、不阻断播放（DESIGN_v3.md §4.2）。
/// </summary>
public static class PathValidator
{
    /// <summary>dat 文件名。全程序唯一写死的文件名 —— 不做扫描就必须按固定名找。</summary>
    public const string DatFileName = "thbgm.dat";

    /// <summary>TH06 音频子目录名。同为写死。</summary>
    public const string WavSubDirectory = "bgm";

    /// <summary>TH06 循环点文件（目前循环点已固化进索引，此文件仅作存在性提示）。</summary>
    public const string Th06PosFileName = "紅魔郷MD.DAT";

    /// <summary>新典（TH06NC）音频数据根子目录；其下为 <see cref="NcBgmSubDirectory"/> 等。</summary>
    public const string NcDataSubDirectory = "data";

    /// <summary>新典主版（新编曲）子目录。</summary>
    public const string NcBgmSubDirectory = "bgm";

    /// <summary>新典 Alt（原编曲）子目录。</summary>
    public const string NcAltSubDirectory = "bgm2";

    /// <summary>新典容器轻量探测用的第一首文件名。</summary>
    public const string NcFirstTrackFileName = "th06_01.opus";

    private const string Magic = "ZWAV";
    private const int HeaderSize = 16;

    /// <summary>校验一部作品的路径。</summary>
    public static ValidateResult Validate(GameDef game, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return ValidateResult.NotSet;

        if (game.IsTfSource)
            return ValidateTf(game, path);

        if (game.IsNcSource)
            return ValidateNc(game, path);

        return game.IsWavSource ? ValidateWav(game, path) : ValidateZwav(game, path);
    }

    // ---------- 黄昏作（tf 系）：容器组 ----------

    /// <summary>
    /// 黄昏作没有明文魔数可用：th075 的 Suica、th105/123 的双层 XOR 加密 dat 都是密文，
    /// cga/cgb 也无魔数，只有 TFPK（.pak）头 4 字节是明文 "TFPK"。
    /// 所以 tf 侧以「存在性 + 大小>0」为主，TFPK 才做魔数探测。
    /// </summary>
    private static ValidateResult ValidateTf(GameDef game, string path)
    {
        if (!Directory.Exists(path))
            return ValidateResult.Fail("目录不存在");

        var containers = game.Containers;
        if (containers is null || containers.Count == 0)
            return ValidateResult.Fail("索引缺少容器信息");

        // 主包缺失 → Fail；补包缺失 → Warn（不阻断，只用主包内容）
        var missing = new List<string>();
        long firstSize = 0;
        int present = 0;
        for (int i = 0; i < containers.Count; i++)
        {
            string p = Path.Combine(path, containers[i]);
            if (!File.Exists(p))
            {
                if (i == 0)
                    return ValidateResult.Fail($"找不到主容器 {containers[0]}");
                missing.Add(containers[i]);
                continue;
            }
            if (present == 0) firstSize = new FileInfo(p).Length;
            present++;
        }

        if (present == 0)
            return ValidateResult.Fail("没有任何容器文件");

        // 轻魔数探测（只读头部 ≤16 字节）
        string headMsg;
        try
        {
            using var fs = File.OpenRead(Path.Combine(path, containers[0]));
            Span<byte> buf = stackalloc byte[16];
            int n = fs.Read(buf);
            headMsg = n >= 4 && Encoding.ASCII.GetString(buf[..4]) == "TFPK"
                ? $"{present} 个容器 · TFPK · 首包 {firstSize / 1024 / 1024} MB"
                : $"{present} 个容器 · 加密容器，仅校验存在性";
        }
        catch (Exception ex)
        {
            return ValidateResult.Fail($"读取失败：{ex.Message}");
        }

        if (missing.Count > 0)
            return ValidateResult.Warn($"缺少补丁容器 {string.Join("、", missing)}（将只用主包内容）");

        return ValidateResult.Ok(headMsg);
    }

    // ---------- 新典（TH06NC）：data\bgm / data\bgm2 下的自定义 Opus 容器 ----------

    /// <summary>
    /// 新典路径指到游戏根目录。主版（新编曲）在 data\bgm、Alt（原编曲）在 data\bgm2。
    /// 主版缺失 → Fail；Alt 缺失 → Warn（主版仍可播，仅 Alt 不可用）。
    /// 自定义容器：40 字节头 + N×488 定长记录，做一次轻量结构校验（不阻断，仅降级为 Warn）。
    /// </summary>
    private static ValidateResult ValidateNc(GameDef game, string path)
    {
        if (!Directory.Exists(path))
            return ValidateResult.Fail("目录不存在");

        string bgmPath = Path.Combine(path, NcDataSubDirectory, NcBgmSubDirectory, NcFirstTrackFileName);
        string altPath = Path.Combine(path, NcDataSubDirectory, NcAltSubDirectory, NcFirstTrackFileName);

        if (!File.Exists(bgmPath))
            return ValidateResult.Fail($"找不到 {NcDataSubDirectory}\\{NcBgmSubDirectory}\\{NcFirstTrackFileName}");

        string? reason = null;
        try
        {
            long size = new FileInfo(bgmPath).Length;
            long body = size - 40;
            if (size < 40 + 488 || body % 488 != 0)
                reason = "容器长度不符（可能版本不一致）";
            else
            {
                using var fs = File.OpenRead(bgmPath);
                Span<byte> rec = stackalloc byte[4];
                fs.Seek(40, SeekOrigin.Begin);
                if (fs.Read(rec) == 4 && System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(rec) != 480)
                    reason = "记录头包长不是 480（可能版本不一致）";
            }
        }
        catch (Exception ex)
        {
            return ValidateResult.Fail($"读取失败：{ex.Message}");
        }

        if (!File.Exists(altPath))
            return ValidateResult.Warn($"缺少 {NcDataSubDirectory}\\{NcAltSubDirectory}\\（原编曲版），Alt 不可用");
        if (reason is not null)
            return ValidateResult.Warn(reason);

        return ValidateResult.Ok($"{game.Tracks.Count} 首 · opus");
    }

    // ---------- 常规 20 作：thbgm.dat ----------

    private static ValidateResult ValidateZwav(GameDef game, string path)
    {
        if (!Directory.Exists(path))
            return ValidateResult.Fail("目录不存在");

        var datPath = Path.Combine(path, DatFileName);
        if (!File.Exists(datPath))
            return ValidateResult.Fail($"找不到 {DatFileName}");

        // 1) 头部
        byte[] head;
        long fileSize;
        try
        {
            using var fs = File.OpenRead(datPath);
            fileSize = fs.Length;
            if (fileSize < HeaderSize)
                return ValidateResult.Fail("文件过小，不是有效的 thbgm.dat");
            head = new byte[HeaderSize];
            fs.ReadExactly(head, 0, HeaderSize);
        }
        catch (Exception ex)
        {
            return ValidateResult.Fail($"读取失败：{ex.Message}");
        }

        if (Encoding.ASCII.GetString(head, 0, 4) != Magic)
            return ValidateResult.Fail("不是 ZWAV 格式（头部魔数不符）");

        int version = head[4];
        if (version != 1)
            return ValidateResult.Warn($"未预期的 ZWAV 版本 {version}");

        // 2) 头里的作品号 vs 用户填的路径所对应的作品
        var (major, frac, ok) = ParseGameNumber(game.Id);
        if (ok)
        {
            int actualMajor = BcdToInt(head[9]);
            int actualFrac = head[8] / 16;
            if (actualMajor != major || actualFrac != frac)
                return ValidateResult.Fail(
                    $"作品号不符：文件是 TH{actualMajor}{(actualFrac != 0 ? "." + actualFrac : "")}，这里应为 {game.Code}");
        }

        // 3) 文件大小 == 16 + Σ 所有数据块
        long expected = HeaderSize + game.Tracks.Sum(t => t.Length + (t.Alt?.Length ?? 0));
        if (fileSize != expected)
            return ValidateResult.Warn(
                $"大小不符（实际 {fileSize:N0}，索引预期 {expected:N0}，差 {fileSize - expected:+N0}），可能版本不一致");

        return ValidateResult.Ok($"{game.Tracks.Count} 首 · {fileSize / 1024 / 1024} MB");
    }

    // ---------- TH06：bgm\*.wav ----------

    private static ValidateResult ValidateWav(GameDef game, string path)
    {
        // 容错：用户若直接指到 bgm 目录本身（下面直接就是 wav），上退一级
        if (game.Tracks.Count > 0 &&
            File.Exists(Path.Combine(path, game.Tracks[0].File ?? "")) &&
            !Directory.Exists(Path.Combine(path, WavSubDirectory)))
        {
            var parent = Directory.GetParent(path.TrimEnd(Path.DirectorySeparatorChar))?.FullName;
            if (parent is not null)
                path = parent;
        }

        if (!Directory.Exists(path))
            return ValidateResult.Fail("目录不存在");

        var bgmDir = Path.Combine(path, WavSubDirectory);
        if (!Directory.Exists(bgmDir))
            return ValidateResult.Fail($"找不到 {WavSubDirectory}\\ 子目录");

        var missing = game.Tracks.Where(t => !File.Exists(Path.Combine(bgmDir, t.File ?? ""))).ToList();
        if (missing.Count == game.Tracks.Count)
            return ValidateResult.Fail($"{WavSubDirectory}\\ 下没有任何 th06_NN.wav");
        if (missing.Count > 0)
            return ValidateResult.Warn($"缺少 {missing.Count} 个文件（如 {missing[0].File}）");

        return ValidateResult.Ok($"{game.Tracks.Count} 首 · wav");
    }

    /// <summary>
    /// 把作品代号解析成 (主版本, 小数位)。
    /// 两位数：th13 → (13, 0)；三位数：th128 → (12, 8)，th095 → (9, 5)。
    /// </summary>
    public static (int Major, int Frac, bool Ok) ParseGameNumber(string id)
    {
        if (!id.StartsWith("th", StringComparison.OrdinalIgnoreCase))
            return (0, 0, false);
        var digits = id[2..];
        if (digits.Length == 2 && int.TryParse(digits, out var m2))
            return (m2, 0, true);
        if (digits.Length == 3 &&
            int.TryParse(digits[..2], out var m3) &&
            int.TryParse(digits.AsSpan(2), out var f3))
            return (m3, f3, true);
        return (0, 0, false);
    }

    private static int BcdToInt(byte b) => ((b >> 4) * 10) + (b & 0xF);
}
