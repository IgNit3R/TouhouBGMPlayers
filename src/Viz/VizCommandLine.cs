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

/// <summary>解析结果。</summary>
internal readonly record struct VizLaunch(VizLaunchMode Mode, string? AudioPath);

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

    public static VizLaunch Parse(string[] args)
    {
        bool selfTest = false;
        bool viz = false;
        string? path = null;

        if (args is not null)
        {
            foreach (var raw in args)
            {
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

                // 其余 -- 开头的一律忽略（留给接入期加开关）
                if (a.StartsWith("--", StringComparison.Ordinal)) continue;

                // 第一个非开关参数当音频路径
                path ??= a;
            }
        }

        // 自检优先级最高：它是诊断入口，不该被别的开关带偏
        if (selfTest) return new VizLaunch(VizLaunchMode.SelfTest, null);
        if (viz) return new VizLaunch(VizLaunchMode.Viz, path);
        return new VizLaunch(VizLaunchMode.Main, null);
    }

    private static bool Eq(string s, string sw) => string.Equals(s, sw, StringComparison.OrdinalIgnoreCase);
}
