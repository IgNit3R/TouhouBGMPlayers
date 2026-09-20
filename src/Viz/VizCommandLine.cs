using System.Globalization;

namespace ThbgmPlayer.Viz;

/// <summary>启动模式。</summary>
internal enum VizLaunchMode
{
    /// <summary>无参数：照旧开主窗口。</summary>
    Main,
    /// <summary><c>--viz [音频路径]</c>：只开可视化窗口（隔离期入口）。</summary>
    Viz,
    /// <summary><c>--viz-selftest</c>：跑自检并退出，不开窗口。</summary>
    SelfTest,
}

/// <summary>解析结果。<see cref="DelayMs"/> 为 null = 没给，用设置里记住的值。</summary>
internal readonly record struct VizLaunch(VizLaunchMode Mode, string? AudioPath, double? DelayMs = null);

/// <summary>
/// 命令行解析。单独拎出来是为了能被 <c>--viz-selftest</c> 直接驱动 ——
/// 路径含空格、开关顺序颠倒这些都不该靠手测去发现。
///
/// 约定（方案 §7-R9）：一律用 <c>e.Args</c>（运行时已正确切分），
/// 不自己拼命令行字符串 —— 手工拼遇到带空格的路径必然出错。
/// </summary>
internal static class VizCommandLine
{
    public const string VizSwitch = "--viz";
    public const string SelfTestSwitch = "--viz-selftest";

    /// <summary>
    /// 听觉延迟对齐偏移（毫秒）：<c>--viz-delay 120</c> 或 <c>--viz-delay=120</c>。
    /// 方案 §2「偏移可配（默认 100ms）」的落点 —— 隔离期不必去手改 settings.json。
    /// </summary>
    public const string DelaySwitch = "--viz-delay";

    /// <summary>偏移的合法范围（毫秒）。超出按上限夹住；负数/非数字直接忽略。</summary>
    public const double MinDelayMs = 0;
    public const double MaxDelayMs = 1000;

    public static VizLaunch Parse(string[] args)
    {
        bool selfTest = false;
        bool viz = false;
        string? path = null;
        double? delay = null;

        if (args is not null)
        {
            // ⚠️ 用带下标的 for 而不是 foreach：--viz-delay 的空格形式要吃下**一个**参数
            for (int i = 0; i < args.Length; i++)
            {
                string raw = args[i];
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string a = raw.Trim();

                if (Eq(a, SelfTestSwitch)) { selfTest = true; continue; }
                if (Eq(a, VizSwitch)) { viz = true; continue; }

                // 也接受 --viz=路径 这种写法（有些快捷方式 / shell 会这么传）
                if (a.StartsWith(VizSwitch + "=", StringComparison.OrdinalIgnoreCase))
                {
                    viz = true;
                    string v = a[(VizSwitch.Length + 1)..];
                    if (v.Length > 0) path ??= v;
                    continue;
                }

                if (Eq(a, DelaySwitch))
                {
                    if (i + 1 < args.Length && TryDelay(args[i + 1], out double d))
                    {
                        delay = d;
                        i++;                       // 吃掉数值那一个参数
                    }
                    continue;                      // 没跟数值就整个忽略，别把它当路径
                }

                if (a.StartsWith(DelaySwitch + "=", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryDelay(a[(DelaySwitch.Length + 1)..], out double d)) delay = d;
                    continue;
                }

                // 其余 -- 开头的一律忽略（留给接入期加开关）
                if (a.StartsWith("--", StringComparison.Ordinal)) continue;

                // 第一个非开关参数当音频路径
                path ??= a;
            }
        }

        // 自检优先级最高：它是诊断入口，不该被别的开关带偏
        if (selfTest) return new VizLaunch(VizLaunchMode.SelfTest, null, delay);
        if (viz) return new VizLaunch(VizLaunchMode.Viz, path, delay);
        return new VizLaunch(VizLaunchMode.Main, null, delay);
    }

    /// <summary>
    /// 解析毫秒数并夹到合法范围。**用 <see cref="CultureInfo.InvariantCulture"/>** ——
    /// 德语等区域会把小数点写成逗号，而命令行是机器给的，永远按点号读。
    /// </summary>
    private static bool TryDelay(string s, out double ms)
    {
        ms = 0;
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return false;
        if (double.IsNaN(v) || v < MinDelayMs) return false;

        ms = v > MaxDelayMs ? MaxDelayMs : v;
        return true;
    }

    private static bool Eq(string s, string sw) => string.Equals(s, sw, StringComparison.OrdinalIgnoreCase);
}
