using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using ThbgmPlayer.Core;

namespace ThbgmPlayer.Viz;

/// <summary>
/// <c>--viz-selftest</c> 的自检入口。返回进程退出码：0 = 全绿，1 = 有失败项。
///
/// M0 只覆盖「骨架 + 入口」这几项；M3 的放置判定用例表（方案 §3.6）挂到
/// <c>VizPlacementSelfTest</c>，由本类统一调用 —— 自检是 M3 结束后的回归门槛。
///
/// 纪律：**只读**。除了自己那个临时日志/临时设置文件，不碰用户的 settings.json。
/// </summary>
internal static class VizSelfTest
{
    private sealed record Result(string Name, bool Ok, string Detail);

    /// <summary>写在自己的程序目录里（AppPaths 的硬约束：不写 AppData / 临时目录）。</summary>
    private static string LogFile => Path.Combine(AppPaths.BaseDirectory, "viz-selftest.txt");

    private static string ScratchSettingsFile =>
        Path.Combine(AppPaths.BaseDirectory, "viz-selftest-settings.json");

    public static int Run()
    {
        var results = new List<Result>
        {
            CheckCommandLine(),
            CheckThemeBrushes(),
            CheckSettingsLocation(),
            CheckSettingsRoundTrip(),
            CheckVizWindow(),
        };

        int fail = results.Count(r => !r.Ok);

        var log = new StringBuilder();
        log.AppendLine("bgmplayer 可视化自检");
        log.AppendLine($"时间   {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        log.AppendLine($"版本   {AppPaths.AppVersion}");
        log.AppendLine($"目录   {AppPaths.BaseDirectory}");
        log.AppendLine();
        foreach (var r in results)
        {
            log.AppendLine($"{(r.Ok ? "[ ok ]" : "[FAIL]")} {r.Name}");
            if (r.Detail.Length > 0) log.AppendLine($"         {r.Detail}");
        }
        log.AppendLine();
        log.AppendLine(fail == 0 ? $"全部通过（{results.Count} 项）" : $"失败 {fail} / {results.Count} 项");

        Emit(log.ToString());
        return fail == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- 用例

    /// <summary>入口分流：路径含空格、开关顺序、无参默认值。</summary>
    private static Result CheckCommandLine()
    {
        var a = VizCommandLine.Parse(new[] { "--viz" });
        var b = VizCommandLine.Parse(new[] { "--viz", @"C:\My Music\th17_13.wav" });
        var c = VizCommandLine.Parse(new[] { @"C:\My Music\th17_13.wav", "--viz" });
        var d = VizCommandLine.Parse(Array.Empty<string>());
        var e = VizCommandLine.Parse(new[] { "--viz-selftest" });
        var f = VizCommandLine.Parse(new[] { "--viz", "--viz-selftest" });

        bool ok =
            a.Mode == VizLaunchMode.Viz && a.AudioPath is null &&
            b.Mode == VizLaunchMode.Viz && b.AudioPath == @"C:\My Music\th17_13.wav" &&
            c.Mode == VizLaunchMode.Viz && c.AudioPath == @"C:\My Music\th17_13.wav" &&
            d.Mode == VizLaunchMode.Main && d.AudioPath is null &&
            e.Mode == VizLaunchMode.SelfTest &&
            f.Mode == VizLaunchMode.SelfTest;

        string detail =
            $"--viz→{a.Mode}；--viz \"含空格路径\"→{Show(b.AudioPath)}；路径在前→{Show(c.AudioPath)}；" +
            $"无参→{d.Mode}；--viz-selftest→{e.Mode}；两开关并存→{f.Mode}";

        return new Result("命令行分流（--viz / --viz-selftest / 无参）", ok, detail);
    }

    /// <summary>
    /// 主题画刷真的在应用级资源里。注意不能只看 <c>Theme.Get</c> 的返回值 ——
    /// 取不到时它会静默用兜底色，颜色一模一样，反而查不出「键写错了」。
    /// </summary>
    private static Result CheckThemeBrushes()
    {
        var res = Application.Current?.Resources;
        if (res is null)
            return new Result("主题画刷 VizBar / VizLineL", false, "Application.Current.Resources 不可用");

        bool bar = res["VizBar"] is SolidColorBrush b1 && b1.Color == FromHex("#FF2E86C4");
        bool lin = res["VizLineL"] is SolidColorBrush b2 && b2.Color == FromHex("#FF3A96DD");

        // 顺带确认复用到的既有画刷也都在（渲染器要按 key 取）
        var reuse = new[] { "BgDeep", "BgPanel", "BgElevated", "Border", "Emphasis", "Warn", "TextDim", "TextFaint" };
        var missing = reuse.Where(k => res[k] is not SolidColorBrush).ToArray();

        bool ok = bar && lin && missing.Length == 0;
        string detail = $"VizBar={Show(bar)} VizLineL={Show(lin)}；" +
                        (missing.Length == 0 ? "复用画刷 8 个都在" : $"缺复用画刷：{string.Join(", ", missing)}");

        return new Result("主题画刷 VizBar / VizLineL + 复用画刷", ok, detail);
    }

    /// <summary>
    /// 硬约束核对（DESIGN_v3 §9）：设置必须落在 exe 旁，且路径里不能出现 AppData / Temp。
    /// </summary>
    private static Result CheckSettingsLocation()
    {
        string dir = Path.GetDirectoryName(AppPaths.SettingsFile) ?? "";
        string baseDir = AppPaths.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        bool besideExe = string.Equals(dir.TrimEnd(Path.DirectorySeparatorChar), baseDir, StringComparison.OrdinalIgnoreCase);
        bool noAppData = !Contains(dir, "AppData") && !Contains(dir, "IsolatedStorage");
        bool noTemp = !Contains(dir, "\\Temp") && !Contains(dir, "\\Windows\\");

        bool ok = besideExe && noAppData && noTemp;
        string detail = $"settings.json → {dir}；exe 旁={Show(besideExe)} 无 AppData={Show(noAppData)} 无 Temp={Show(noTemp)} 可写={Show(AppPaths.IsWritable)}";

        return new Result("设置落盘位置（exe 旁 / 不写 AppData）", ok, detail);
    }

    /// <summary>
    /// VizSettings 的序列化往返。两段：
    ///   ① 内存里拿 <see cref="AppSettings.Current"/> 走一遍 序列化→反序列化，值必须不变；
    ///   ② 用程序目录下一个临时文件走 <see cref="AppPaths.WriteTextAtomic"/> 真实落盘再读回，随后删掉。
    /// 这样既能验到真实类型（JsonPropertyName 写错、字段漏进 JSON 都能发现），
    /// 又不必动用户的 settings.json。
    /// </summary>
    private static Result CheckSettingsRoundTrip()
    {
        var opts = new JsonSerializerOptions { WriteIndented = true, AllowTrailingCommas = true };

        // ① 内存往返
        var cur = AppSettings.Current;
        var before = cur.Viz;
        AppSettings? back;
        try
        {
            back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(cur, opts), opts);
        }
        catch (Exception ex)
        {
            return new Result("VizSettings 序列化往返", false, ex.Message);
        }
        if (back is null)
            return new Result("VizSettings 序列化往返", false, "反序列化返回 null");

        var after = back.Viz;
        bool memOk =
            after.Width == before.Width && after.Height == before.Height &&
            after.Left == before.Left && after.Top == before.Top &&
            after.Attached == before.Attached && after.EmbedWhenMaximized == before.EmbedWhenMaximized &&
            after.EmbeddedWidth == before.EmbeddedWidth && after.LatencyOffsetMs == before.LatencyOffsetMs &&
            after.DebugSource == before.DebugSource &&
            after.ShowA == before.ShowA && after.ShowB == before.ShowB &&
            after.ShowC == before.ShowC && after.ShowD == before.ShowD &&
            after.ShowCover == before.ShowCover;

        // ② 真实落盘往返（临时文件，用完即删）
        string probeNote = "";
        bool diskOk = false;
        try
        {
            var probe = new AppSettings();
            probe.Viz.Width = 612.5;
            probe.Viz.Height = 401;
            probe.Viz.Left = -1234;
            probe.Viz.Top = 77;
            probe.Viz.Attached = true;
            probe.Viz.EmbedWhenMaximized = false;
            probe.Viz.EmbeddedWidth = 388;
            probe.Viz.LatencyOffsetMs = 87.5;
            probe.Viz.DebugSource = @"D:\曲\th17_13.wav";
            probe.Viz.ShowA = false;
            probe.Viz.ShowD = false;

            diskOk = AppPaths.WriteTextAtomic(ScratchSettingsFile, JsonSerializer.Serialize(probe, opts));
            if (diskOk)
            {
                var r = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(ScratchSettingsFile), opts);
                var v = r?.Viz;
                diskOk = v is not null &&
                         v.Width == 612.5 && v.Height == 401 &&
                         v.Left == -1234 && v.Top == 77 &&
                         v.Attached && !v.EmbedWhenMaximized &&
                         v.EmbeddedWidth == 388 && v.LatencyOffsetMs == 87.5 &&
                         v.DebugSource == @"D:\曲\th17_13.wav" &&
                         !v.ShowA && v.ShowB && v.ShowC && !v.ShowD && v.ShowCover;
                probeNote = diskOk ? "落盘往返 ok" : "写进去读回来不一致";
            }
            else
            {
                probeNote = "程序目录不可写，落盘往返跳过";
            }
        }
        catch (Exception ex)
        {
            diskOk = false;
            probeNote = ex.Message;
        }
        finally
        {
            try { if (File.Exists(ScratchSettingsFile)) File.Delete(ScratchSettingsFile); } catch { }
            try
            {
                string tmp = ScratchSettingsFile + ".tmp";
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch { }
        }

        bool ok = memOk && diskOk;
        string detail = $"内存往返={Show(memOk)}；{probeNote}；" +
                        $"当前值 {before.Width:0.#}×{before.Height:0.#} 延迟 {before.LatencyOffsetMs:0.#}ms";

        return new Result("VizSettings 序列化往返（内存 + 落盘）", ok, detail);
    }

    /// <summary>
    /// VizWindow 能不能构造起来：XAML 解析（含 StaticResource 解析）、几何恢复、字体套用。
    ///
    /// ⚠️ 故意**不 Close**。ShutdownMode 是默认的 OnLastWindowClose，关掉最后一个窗口会
    /// 直接触发进程退出，自检报告就打不出来了。构造后进程退出时 WPF 会自己收尾，
    /// 而 <c>Closing</c> 里因为有「没显示过不回写」的保护，也不会污染设置。
    /// </summary>
    private static Result CheckVizWindow()
    {
        try
        {
            var w = new VizWindow(@"C:\__viz_selftest_no_such_file__.ogg");
            string detail = $"构造 ok；尺寸 {w.Width:0.#}×{w.Height:0.#}；标题「{w.Title}」；" +
                            $"状态条 {(w.FindName("StatusBar") is UIElement el && el.Visibility == Visibility.Visible ? "可见" : "已折叠")}";
            return new Result("VizWindow 构造 / XAML 解析 / 几何恢复", true, detail);
        }
        catch (Exception ex)
        {
            return new Result("VizWindow 构造 / XAML 解析 / 几何恢复", false, ex.GetType().Name + "：" + ex.Message);
        }
    }

    // ---------------------------------------------------------------- 输出

    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    /// <summary>
    /// 输出自检报告。程序是 WinExe（没有自己的控制台），直接 <c>Console.WriteLine</c> 会被静默丢掉，
    /// 所以先挂到父进程的控制台（从终端跑时父进程就是那个终端）；挂不上也无所谓，
    /// 报告**始终**另存一份到 exe 旁的 viz-selftest.txt。
    /// </summary>
    private static void Emit(string text)
    {
        try
        {
            AttachConsole(AttachParentProcess);
            Console.Out.Write(text);
            Console.Out.Flush();
        }
        catch
        {
            // 没有可挂的控制台，文件那一份就够了
        }

        try { AppPaths.WriteTextAtomic(LogFile, text); } catch { }
    }

    // ---------------------------------------------------------------- 小工具

    private static Color FromHex(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static bool Contains(string s, string what) =>
        s.Contains(what, StringComparison.OrdinalIgnoreCase);

    private static string Show(bool b) => b ? "是" : "否";

    private static string Show(string? s) =>
        string.IsNullOrEmpty(s) ? "<无>" : s;
}
