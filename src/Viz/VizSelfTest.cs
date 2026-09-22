using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ThbgmPlayer.Core;
using ThbgmPlayer.Viz.Renderers;

namespace ThbgmPlayer.Viz;

/// <summary>
/// <c>--viz-selftest</c> 的自检入口。返回进程退出码：0 = 全绿，1 = 有失败项。
///
/// 覆盖范围随里程碑长：M0「骨架 + 入口」→ M1「取数链路 + 分析」→ M2「四个渲染器 + 余辉状态机」。
/// M3 的放置判定用例表（方案 §3.6）挂到 <c>VizPlacementSelfTest</c>，由本类统一调用
/// —— 自检是每个里程碑结束后的回归门槛。
///
/// 纪律：**只读**。除了自己那个临时日志/临时设置文件，不碰用户的 settings.json。
/// </summary>
internal static class VizSelfTest
{
    /// <summary>
    /// 一条用例的结果。<c>internal</c> 是为了让 <see cref="VizPlacementSelfTest"/>
    /// （方案 §3.6 的用例表）也能产出同一种条目 —— 自检报告是一张平表，不该有两套格式。
    /// </summary>
    internal sealed record Result(string Name, bool Ok, string Detail);

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
            CheckVizFrameShape(),
            CheckVizAnalyzer(),
            CheckVizRender(),
            CheckVizAfterglow(),
            CheckVizLayout(),
            CheckVizAdaptiveBars(),
            CheckVizAllocation(),
            CheckVizFade(),
            CheckVizTap(),
            CheckVizIdlePolicy(),
            CheckVizCover(),
            CheckVizRingWindow(),
            CheckVizCodecRoute(),
            CheckVizWindow(),
        };

        // M3：放置判定与窗口缩放的用例表（方案 §3.6 / §3.7），条目格式与上面一致
        results.AddRange(VizPlacementSelfTest.Run());

        // 2026-09-23：主窗口新增的整轨波形（分桶 / 缩放 / 跟随 / 文件位置 / 标记 / 键 / 选源 / 取消）
        results.AddRange(UI.WaveformSelfTest.Run());

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
            after.Enabled == before.Enabled &&
            after.TrailLayers == before.TrailLayers &&
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
            probe.Viz.Enabled = true;
            probe.Viz.TrailLayers = 12;

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
                         !v.ShowA && v.ShowB && v.ShowC && !v.ShowD && v.ShowCover && v.Enabled && v.TrailLayers == 12;
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

        // 全新装置的**默认值**也一并锁住：默认错了用户第一次点开就是错的观感。
        // 其中 Attached 默认 true 是 M6 改的（方案的窗口形态就是「贴右侧、等高」，自由是退路）。
        var fresh = new VizSettings();
        bool defaultsOk =
            fresh.Attached && !fresh.Enabled &&
            fresh.ShowA && fresh.ShowB && fresh.ShowC && fresh.ShowD && fresh.ShowCover &&
            fresh.TrailLayers == VizSettings.DefaultTrailLayers &&
            Math.Abs(fresh.LatencyOffsetMs - 100) < 1e-9 &&
            Math.Abs(fresh.Width - 560) < 1e-9 &&
            fresh.EmbedWhenMaximized;

        bool ok = memOk && diskOk && defaultsOk;
        string detail = $"内存往返={Show(memOk)}；默认值（贴附/关/五块全开/余辉{VizSettings.DefaultTrailLayers}/延迟100）={Show(defaultsOk)}；{probeNote}；" +
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

    /// <summary>
    /// 帧的形状与常量。看着像废话，其实是**回归闸门**：M2 调参时很容易顺手改
    /// <see cref="VizFrame.FftSize"/> 或柱数，而这两个数字被频带换算、渲染抽样、
    /// 验证页出处注释同时引用着，改一处漏一处就会静默画歪。锁在这里。
    /// </summary>
    private static Result CheckVizFrameShape()
    {
        var f = new VizFrame();
        f.TimeL[0] = 1f;
        f.WaveL[0] = 1f;
        f.Bars[0] = 1f;
        f.RmsL = 1f;
        f.Active = true;
        f.Clear();

        bool ok =
            VizFrame.FftSize == 4096 && VizFrame.TimeFrames == 4096 && VizFrame.WaveFrames == 2048 &&
            VizFrame.BarCount == 52 &&
            f.TimeL.Length == VizFrame.TimeFrames && f.TimeR.Length == VizFrame.TimeFrames &&
            f.WaveL.Length == VizFrame.WaveFrames && f.WaveR.Length == VizFrame.WaveFrames &&
            f.SpectrumDb.Length == VizFrame.FftSize / 2 && f.Bars.Length == VizFrame.BarCount &&
            f.TimeL[0] == 0f && f.WaveL[0] == 0f && f.Bars[0] == 0f && f.RmsL == 0f && !f.Active;

        string detail =
            $"FFT {VizFrame.FftSize} / 时域 {VizFrame.TimeFrames} / 波形窗 {VizFrame.WaveFrames} / 柱 {VizFrame.BarCount}；" +
            $"谱长 {f.SpectrumDb.Length}；Clear() 生效={Show(f.TimeL[0] == 0f && !f.Active)}";

        return new Result("VizFrame 常量与缓冲形状", ok, detail);
    }

    /// <summary>
    /// 分析器的数学。用一路已知的正弦喂进去，检查五件事：
    /// <list type="number">
    /// <item>峰值落在正确的 bin（1000Hz → 约 bin 93）；</item>
    /// <item>RMS 量级对（正弦理论值 0.707，首帧上冲平滑后 ≈0.42）；</item>
    /// <item>相关度方向对（同相 → 正，反相 → 负）；</item>
    /// <item>60Hz 限频真的生效（紧接的第二次调用必须被吞掉）；</item>
    /// <item><see cref="VizFrame.Revision"/> 只在真的重算时 +1（被吞掉的那次不许动）。</item>
    /// </list>
    /// 阈值刻意留了余量，只锁「量级与方向」，不锁小数点 —— 这样它抓得住
    /// 「算错了 / 符号反了 / 限频失效」，又不会在 M2 微调平滑系数时误报。
    /// </summary>
    private static Result CheckVizAnalyzer()
    {
        var feed = new FakeFeed { Frequency = 1000, Amplitude = 1f };
        var analyzer = new VizAnalyzer(feed);

        bool first = analyzer.Update();
        var frame = analyzer.Frame;

        // frame 是复用对象，后续 Update 会原地改写 —— 先把要断言的值快照下来
        int peakBin = 0;
        for (int k = 1; k < frame.SpectrumDb.Length; k++)
            if (frame.SpectrumDb[k] > frame.SpectrumDb[peakBin]) peakBin = k;

        int peakBar = 0;
        for (int b = 1; b < frame.Bars.Length; b++)
            if (frame.Bars[b] > frame.Bars[peakBar]) peakBar = b;

        int expectBin = (int)Math.Round(feed.Frequency / (feed.SampleRate / 2.0) * frame.SpectrumDb.Length);
        float barValue = frame.Bars[peakBar];
        float rms = frame.RmsL;
        long revFirst = frame.Revision;

        // 紧接的第二次调用必须被限频吞掉（同一毫秒内，距上次分析不足 16.7ms）。
        // 顺带验 Revision 的契约：**只有真的重算了才 +1**，被吞掉的调用不许动它
        // —— D 的余辉靠它分辨「这帧是新的吗」，动了就会在 144Hz 屏上多推余辉。
        bool second = analyzer.Update();
        long revSecond = frame.Revision;

        // 相关度是**平滑量**（每帧只走 15%，初值是中位 0）→ 先让它收敛，再断言**方向**。
        // ⚠️ 以前这里直接拿「第 1 帧的值」当断言：初值 0.8 时算出来 0.83，恰好压着 0.75 的线过，
        // 于是"看起来在验方向"、其实只是碰巧 —— 初值一改（0.8 → 0）它就崩。
        for (int i = 0; i < 12; i++) { Thread.Sleep(20); analyzer.Update(); }
        float corrInPhase = frame.Correlation;

        // 反相：L 与 R 符号相反 → 相关度该往负的方向走
        feed.InvertRight = true;
        for (int i = 0; i < 12; i++) { Thread.Sleep(20); analyzer.Update(); }
        float corrAntiPhase = analyzer.Frame.Correlation;

        bool ok =
            first && !second &&
            revFirst >= 1 && revSecond == revFirst &&
            Math.Abs(peakBin - expectBin) <= 3 &&
            peakBar >= 26 && peakBar <= 33 && barValue > 0.4f &&
            rms > 0.35f && rms < 0.50f &&
            corrInPhase > 0.75f && corrAntiPhase < -0.4f;

        string detail =
            $"首帧算={Show(first)} 紧接第二次={Show(second)}；" +
            $"Revision 首帧={revFirst} 限频后={revSecond}（须不变）；" +
            $"峰值 bin={peakBin}（期望 {expectBin}）；峰柱={peakBar} 高 {barValue:0.###}；" +
            $"RMS={rms:0.###}（理论 0.707，首帧平滑后≈0.42）；" +
            $"相关 同相 {corrInPhase:0.###} / 反相 {corrAntiPhase:0.###}";

        return new Result("VizAnalyzer：FFT 峰值 / RMS / 相关度 / 60Hz 限频 / Revision", ok, detail);
    }

    /// <summary>
    /// 四个渲染器 + <see cref="VizPanel"/> 的端到端。这条链路（<c>DrawingVisual</c> 挂载、
    /// <c>StreamGeometry</c>、<c>FormattedText</c>、主题画刷折算）平时只在真的开着窗口时才跑到，
    /// 一旦哪一步抛异常，用户看到的是「窗口空白」而不是报错 —— 所以在自检里真跑一遍。
    ///
    /// 三道检查，逐级加严：
    /// <list type="number">
    /// <item><b>不抛异常</b>，两个尺寸各来一次（320×160 与 120×40，覆盖「面板被拉扁」时的夹取分支）。</item>
    /// <item><b>真的出了像素</b>：左上角必须是底色 —— 这条同时证明「离屏位图真的被渲染了」
    ///   （全空的话通道值会是 0x00，会被这条抓住，而不是误判成「画了很多」）。</item>
    /// <item><b>画面必须随信号变化</b>：拿「有信号的帧」与「全零的静止帧」各画一遍，
    ///   <b>两幅图直接逐像素比</b>，变化量要够大。</item>
    /// </list>
    ///
    /// ⚠️ 第 3 条最初写成「比较两者<b>与底色的差异像素数</b>」，在 C+J 上**假失败**
    /// （亮 9244 / 静 9271，看起来像「有信号没多画东西」）。真相不是渲染器坏了：
    /// 电平柱是画在**自己的底槽**（<c>Track</c> 色）**里头**的，而底槽本来就不是底色 ——
    /// 柱体只是把那块区域**改了个颜色**，与底色的差异像素数当然不变。
    /// <b>教训：「与底色不同」这个度量看不见「在已上色区域内部改色」。</b>
    /// 要验「有没有响应信号」，就得两幅图直接比，别绕道底色。
    /// </summary>
    private static Result CheckVizRender()
    {
        // 让柱高/电平/相关度都收敛到稳态再取样：首帧只有上冲的一部分，
        // 拿首帧去比「亮没亮」容易在阈值边缘抖。
        var feed = new FakeFeed { Frequency = 440, Amplitude = 0.8f };
        var analyzer = new VizAnalyzer(feed);
        for (int i = 0; i < 5; i++)
        {
            analyzer.Update();
            Thread.Sleep(20);   // 跨过 60Hz 限频窗口，否则后四次全被吞掉
        }

        var live = analyzer.Frame;

        // 静止态：全零 + Active=false。新建一份而不是复用 live —— live 是循环复用的对象。
        var idle = new VizFrame();
        idle.SampleRate = live.SampleRate;
        idle.Clear();

        // 每个用例都要**新实例**：D 的余辉是跨帧状态，复用会把上一轮的残留带进来。
        // 用工厂方法而不是存实例 —— ①② 两处取样必须各拿一个干净的。
        var names = new[] { "A 频谱条", "B 示波器", "C+J 电平相位", "D 利萨如" };

        bool ok = true;
        var notes = new List<string>();

        for (int kind = 0; kind < names.Length; kind++)
        {
            string name = names[kind];

            // ① 两个尺寸都不许抛，且底色在位
            //   101×343 是**最窄的实际面板尺寸**（窗口拉到最小时 C+J 的宽 × 第一组的高），
            //   比它更极端的组合不会再出现；放在这里一并做冒烟。
            foreach (var (w, h) in new[] { (320, 160), (120, 40), (101, 343) })
            {
                var px = RenderPixels(MakeRenderer(kind), live, w, h, out string err);
                if (px is null)
                {
                    ok = false;
                    notes.Add($"{name} {w}×{h} 抛异常：{err}");
                    break;
                }
                if (!BackgroundPainted(px))
                {
                    ok = false;
                    notes.Add($"{name} {w}×{h} 连底色都没画上（离屏位图全空？）");
                    break;
                }
            }

            // ② 画面必须随信号变化
            var a = RenderPixels(MakeRenderer(kind), live, 320, 160, out string errA);
            var b = RenderPixels(MakeRenderer(kind), idle, 320, 160, out string errB);
            if (a is null || b is null)
            {
                ok = false;
                notes.Add($"{name} 比对时抛异常：{errA}{errB}");
                continue;
            }

            int changed = CountChanged(a, b);
            int need = Math.Max(64, CountNonBackground(b) / 10);   // 至少得变 10%（下限 64 像素）
            if (changed >= need)
            {
                notes.Add($"{name} 变化 {changed} / 门槛 {need}");
            }
            else
            {
                ok = false;
                notes.Add($"{name} 变化 {changed} ← 低于门槛 {need}，画面没响应信号");
            }
        }

        // ③ B 专项：**淡出中的波形不许被按成直线**。
        //    分析器已经把 `WaveL/WaveR` 按淡出进度缩放好了（暂停逐帧缩小、终止直接全 0），
        //    渲染器只需照画。曾经 B 里写的是 `f.Active ? td[idx] : 0.0` ——
        //    暂停瞬间直接拍平，淡出对这块面板完全失效，
        //    观感就是「衰减没多久突然往小跳一下」。
        //    这里用**任意非零幅度**（0.15）代表"还没衰减完的波形" —— 具体数值不重要，
        //    要锁的是「非零波形 + Active=false 时，必须照画」。
        var faded = new VizFrame();
        for (int i = 0; i < VizFrame.WaveFrames; i++)
        {
            float v = 0.15f * MathF.Sin(i * 0.05f);
            faded.WaveL[i] = v;
            faded.WaveR[i] = v;
        }

        var fadedPx = RenderPixels(new OscilloscopeRenderer(), faded, 320, 160, out string errFade);
        var flatPx = RenderPixels(new OscilloscopeRenderer(), new VizFrame(), 320, 160, out string errFlat);

        if (fadedPx is null || flatPx is null)
        {
            ok = false;
            notes.Add("B 淡影专项渲染抛异常：" + errFade + errFlat);
        }
        else
        {
            int fadedInk = InkInStrip(fadedPx, 320, 160, 150, 250);
            int flatInk = InkInStrip(flatPx, 320, 160, 150, 250);
            // 门槛 1.3：真被拍平的话条带墨迹**恰好等于**直线那一份（实测比值 1.0），
            // 所以不必卡太紧 —— 留余量，免得将来改线宽 / 窗口长度时误报。
            bool fadeOk = fadedInk > flatInk * 1.3;

            ok &= fadeOk;
            notes.Add(fadeOk
                ? $"B 淡影：条带墨迹 {fadedInk}（直线 {flatInk}）→ 波形按比例缩了，没被拍平"
                : $"✗ B 淡影：条带墨迹 {fadedInk} vs 直线 {flatInk} —— 波形被拍平（是不是又看 f.Active 了？）");
        }

        return new Result("四个渲染器端到端（离屏出像素 + 画面随信号变化）", ok,
            string.Join("；", notes));
    }

    /// <summary>用例序号 → 全新的渲染器实例。序号与 <c>names</c> 数组一一对应。</summary>
    private static IVizRenderer MakeRenderer(int kind) => kind switch
    {
        0 => new SpectrumRenderer(),
        1 => new OscilloscopeRenderer(),
        2 => new LevelPhaseRenderer(),
        _ => new LissajousRenderer(),
    };

    /// <summary>
    /// D 的余辉状态机。**唯一带状态的渲染器**，而它的状态机有两个容易写错的地方，
    /// 又都只能靠「多看几秒画面」才发现，所以在这里直接断言：
    /// <list type="number">
    /// <item><b>递推钉在分析帧上</b>：渲染跟 vsync，144Hz 屏上同一帧会被画 2～3 次；
    ///   余辉必须只在 <see cref="VizFrame.Revision"/> 变化时推一格，否则余辉长度会随刷新率缩水。</item>
    /// <item><b>暂停 = 衰减成淡影，且能灭干净</b>：暂停时不推新层、整盘按 ×0.95/帧 变暗；
    ///   若忘了「灭到看不见就把在册层数归零」，恢复播放时旧影会突然复活。</item>
    /// </list>
    /// 全程只动内存里的帧对象，不碰声卡、不碰文件。
    /// </summary>
    private static Result CheckVizAfterglow()
    {
        var renderer = new LissajousRenderer();

        var f = new VizFrame
        {
            SampleRate = 44100,
            Dt = 1.0 / 60.0,
            Active = true,
            Revision = 1,
        };

        // 同相正弦 → 屏上是一条 45° 斜线，形状不重要，重要的是「有点可画」
        for (int i = 0; i < VizFrame.WaveFrames; i++)
        {
            float v = 0.7f * MathF.Sin(2f * MathF.PI * 440f * i / 44100f);
            f.WaveL[i] = v;
            f.WaveR[i] = v;
        }

        const int W = 200;
        const int H = 200;

        var panel = new VizPanel { Renderer = renderer, Frame = f };
        panel.Measure(new Size(W, H));
        panel.Arrange(new Rect(0, 0, W, H));

        panel.Redraw();
        int afterFirst = renderer.TrailCount;

        panel.Redraw();                       // 同一个 Revision 再画一次（模拟 144Hz 的重复绘制）
        int afterDuplicate = renderer.TrailCount;

        for (int i = 0; i < 3; i++)           // 三个新分析帧
        {
            f.Revision++;
            panel.Redraw();
        }
        int afterThreeMore = renderer.TrailCount;

        // 暂停：×0.95/帧 → 约 90 帧落到 1% 阈值以下，给到 120 帧留足余量
        f.Active = false;
        for (int i = 0; i < 120; i++)
        {
            f.Revision++;
            panel.Redraw();
        }
        int afterPause = renderer.TrailCount;

        // 恢复播放：只能有新的那一层，旧影不能复活
        f.Active = true;
        f.Revision++;
        panel.Redraw();
        int afterResume = renderer.TrailCount;

        bool ok =
            afterFirst == 1 &&
            afterDuplicate == 1 &&
            afterThreeMore == 4 &&
            afterPause == 0 &&
            afterResume == 1;

        string detail =
            $"首帧 {afterFirst}（期望 1）；同帧重绘 {afterDuplicate}（期望 1，不许跟 vsync 推）；" +
            $"再推 3 帧 {afterThreeMore}（期望 4）；暂停 120 帧 {afterPause}（期望 0）；" +
            $"恢复 {afterResume}（期望 1，旧影不得复活）";

        return new Result("D 余辉状态机（按分析帧递推 / 暂停灭影 / 恢复不复活）", ok, detail);
    }

    /// <summary>
    /// 从真实 XAML 布局里量出四块区域与四个面板的实际尺寸 —— 把 M0 那条
    /// 「从截图量比例」（当时肉眼估成 3.9:1、实际 2.25:1，差了 70%）换成**布局实测**。
    ///
    /// 为什么现在能做：M2 的 <see cref="RenderPixels"/> 已经证明 `Measure/Arrange` 能在离屏跑起来
    /// —— 不必 Show 窗口（`ShutdownMode=OnLastWindowClose`，乱开窗口会把进程提前关掉），
    /// 直接对根元素排版，再读 `ColumnDefinition.ActualWidth` / `RowDefinition.ActualHeight`。
    ///
    /// <b>硬断言（结构，应绿）</b>：第一组 : 第二组 = 2.3 : 1；第一组内左 : 右 = 7 : 3；
    /// A : B = 1 : 1；D : 封面 = 1 : 1；四个面板在默认与最小尺寸下都不退化。
    /// 容差 3%（`UseLayoutRounding` 会把星号分配结果按整像素取整，实测偏差 &lt;0.5%）。
    ///
    /// <b>只报告、不判定（内容适配）</b>：报出 C+J 面板当前落在哪个自适应档位
    /// （见 <see cref="LevelPhaseRenderer.ReferenceWidth"/> / <see cref="LevelPhaseRenderer.MinWidthWithNumbers"/>）。
    /// 停在哪个档位是观感取舍，归用户；**越界这条硬契约**由 <see cref="CheckVizAdaptiveBars"/> 扫宽度来验。
    /// </summary>
    private static Result CheckVizLayout()
    {
        const string Title = "窗口布局实测（分区比例 + 面板尺寸 + 内容宽度适配）";

        try
        {
            var win = new VizWindow(null);

            if (win.Content is not FrameworkElement root)
                return new Result(Title, false, "窗口内容不是 FrameworkElement");

            // ⚠️ 五块区域现在在 **VizSurfaceHost** 里，那几个 x:Name 属于**它自己的命名域** ——
            // 必须在它身上 FindName。在窗口上找会返回 null（Window.FindName 不穿透子命名域），
            // 那样这条断言会以「x:Name 被改了？」的面目失败，把人往错方向带。
            var surface = win.Surface;
            if (surface.FindName("GroupFirst") is not Grid groupFirst ||
                surface.FindName("GroupSecond") is not Grid groupSecond ||
                surface.FindName("LeftColumn") is not Grid leftColumn)
                return new Result(Title, false, "找不到 GroupFirst / GroupSecond / LeftColumn（XAML 的 x:Name 被改了？）");

            var names = new[] { "A", "B", "C+J", "D" };
            var panels = new VizPanel?[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                // 名字就是 PanelA / PanelB / PanelCJ / PanelD（C+J 那块不带加号）
                string key = names[i] == "C+J" ? "PanelCJ" : "Panel" + names[i];
                panels[i] = surface.FindName(key) as VizPanel;
                if (panels[i] is null)
                    return new Result(Title, false, $"找不到 {key}");
            }

            var notes = new List<string>();
            bool ok = true;
            double cjMinWidth = -1;   // 最小尺寸下 C+J 面板的实际宽度（内容适配报告用）

            // 默认尺寸取 XAML 里声明的值；最小尺寸取 MinWidth/MinHeight。
            // ⚠️ 窗口没 Show，拿不到真实客户区 —— 但比的是**比例**，与绝对尺寸无关。
            var sizes = new (string Label, double W, double H)[]
            {
                ("默认", win.Width, win.Height),
                ("最小", win.MinWidth, win.MinHeight),
            };

            foreach (var (label, w, h) in sizes)
            {
                // ⚠️ 不要调 UpdateLayout()：它会把**从未 Show 过的 Window** 也拖进一次全量布局，
                // 而窗口级布局在没有 HwndSource 时会炸（实测 ArgumentOutOfRangeException）。
                // 不需要它 —— Grid 的列宽/行高就是在自己的 MeasureOverride 里定下来的，
                // Measure/Arrange 走完，ColumnDefinition.ActualWidth 已经有了。
                root.Measure(new Size(w, h));
                root.Arrange(new Rect(0, 0, w, h));

                double g1 = groupFirst.ActualHeight;
                double g2 = groupSecond.ActualHeight;
                double colL = groupFirst.ColumnDefinitions[0].ActualWidth;
                double colR = groupFirst.ColumnDefinitions[1].ActualWidth;
                // ⚠️ 列定义在 GroupFirst 上、**行定义在 LeftColumn 上** —— 两者不是同一个 Grid。
                // 一开始把两个都往 GroupFirst 上读，RowDefinitions 是空的 → ArgumentOutOfRangeException。
                double rowT = leftColumn.RowDefinitions[0].ActualHeight;
                double rowB = leftColumn.RowDefinitions[1].ActualHeight;
                double g2L = groupSecond.ColumnDefinitions[0].ActualWidth;
                double g2R = groupSecond.ColumnDefinitions[1].ActualWidth;

                bool shapeOk =
                    Near(g1 / g2, 2.3) &&
                    Near(colL / colR, 7.0 / 3.0) &&
                    Near(rowT / rowB, 1.0) &&
                    Near(g2L / g2R, 1.0);

                var degenerate = new List<string>();
                for (int i = 0; i < panels.Length; i++)
                    if (panels[i]!.ActualWidth < 40 || panels[i]!.ActualHeight < 30)
                        degenerate.Add(names[i]);

                string cellText = string.Join(" ", names.Select((n, i) => $"{n} {panels[i]!.ActualWidth:0}×{panels[i]!.ActualHeight:0}"));
                string ratioText =
                    $"{label} {w:0}×{h:0}：组比 {g1 / g2:0.###}（2.3）｜列比 {colL / colR:0.###}（2.333）｜" +
                    $"A:B {rowT / rowB:0.###}｜D:封面 {g2L / g2R:0.###}｜{cellText}";

                if (!shapeOk)
                {
                    ok = false;
                    notes.Add("⚠ 结构比例被改坏了 —— " + ratioText);
                }
                else if (degenerate.Count > 0)
                {
                    ok = false;
                    notes.Add($"⚠ 面板退化（<40×30）：{string.Join("、", degenerate)} —— " + ratioText);
                }
                else
                {
                    notes.Add(ratioText);
                }

                if (label == "最小") cjMinWidth = panels[2]!.ActualWidth;
            }

            // 顺带锁一处**跨文件重复声明**：「最小宽度 360」在 XAML 的 MinWidth 与
            // VizPlacementLogic.MinWidth 各写了一遍。两处漂移的后果很隐蔽 ——
            // 放置判定算出来的宽度会被窗口自己的 MinWidth 顶回去，看起来像"判定算错了"。
            bool minWidthInSync = Math.Abs(win.MinWidth - VizPlacementLogic.MinWidth) < 1e-9;
            if (!minWidthInSync) ok = false;

            // 内容宽度适配的档位报告（只报不判，见 CheckVizAdaptiveBars）
            double cjDefault = panels[2]!.ActualWidth;

            string Fit(double width) =>
                width >= LevelPhaseRenderer.ReferenceWidth
                    ? $"宽 {width:0}：与参照页逐像素一致"
                    : width >= LevelPhaseRenderer.MinWidthWithNumbers
                        ? $"宽 {width:0}：柱体等比缩，刻度数字保留"
                        : $"宽 {width:0}：放弃刻度数字，柱体吃满";

            string fit = "C+J 自适应档位 —— " + Fit(cjDefault) + "；最小尺寸 " + Fit(cjMinWidth) +
                         $"（阈值：与参照页一致 ≥{LevelPhaseRenderer.ReferenceWidth:0}、" +
                         $"保住数字 ≥{LevelPhaseRenderer.MinWidthWithNumbers:0}）" +
                         (minWidthInSync
                             ? $"；最小宽度 XAML↔判定一致（{VizPlacementLogic.MinWidth:0}）"
                             : $"；⚠ 最小宽度不一致：XAML {win.MinWidth:0} vs 判定 {VizPlacementLogic.MinWidth:0}");

            return new Result(Title, ok, string.Join("；", notes) + "｜" + fit);
        }
        catch (Exception ex)
        {
            // 带上第一帧栈 —— 布局类异常光看类型和消息定位不了（实测踩过）
            string where = (ex.StackTrace ?? "").Split('\n') is { Length: > 0 } lines
                ? lines[0].Trim()
                : "";
            return new Result(Title, false,
                ex.GetType().Name + "：" + ex.Message + (where.Length > 0 ? " ／ " + where : ""));
        }
    }

    /// <summary>
    /// C+J 的横向自适应契约：**任何宽度下柱体与刻度线都不越界**，且宽度够时几何与参照页一致。
    ///
    /// 这条契约是纯几何、无副作用，所以可以直接**扫一遍宽度**，而不是靠肉眼看画面 ——
    /// 面板宽度从 101（窗口最窄）到几百（窗口可自由拉宽）都能出现，靠眼睛逐个拖是测不全的。
    ///
    /// 它同时锁住「宽容时自适应是 no-op」这一点：宽度 ≥ <see cref="LevelPhaseRenderer.ReferenceWidth"/>
    /// 时必须保留刻度数字、柱宽与间距回到参照页值、且柱体居中。
    ///
    /// ⚠️ **别加「越宽越粗」的单调性断言** —— 降级阶梯处柱体是**故意跳粗**的
    /// （放弃刻度数字换来宽度）。
    /// </summary>
    private static Result CheckVizAdaptiveBars()
    {
        const string Title = "C+J 横向自适应（宽度扫描：永不越界 / 宽容时同参照页）";

        try
        {
            var notes = new List<string>();
            bool ok = true;

            double worstOverflow = 0;
            double narrowestWithNumbers = double.MaxValue;

            // 40 是远低于任何可达面板宽（窗口最窄时 C+J 也有 101）的下限，再往下几何会退化成
            // 零宽（不可达，也无害），没有断言的必要。
            for (double w = 40; w <= 600; w += 1)
            {
                var lay = LevelPhaseRenderer.ComputeLayout(w);

                // ① 硬契约：柱体与刻度线全部落在 [0, w] 内 —— 这正是「自适应」存在的理由
                double over = Math.Max(-lay.SpanLeft, lay.SpanRight - w);
                if (over > worstOverflow) worstOverflow = over;

                // ② 尺寸不许退化
                if (!(lay.BarWidth > 0) || !(lay.BarGap > 0) || !(lay.BarB > lay.BarA)) ok = false;

                // ③ 宽容时必须保住刻度数字
                if (w >= LevelPhaseRenderer.ReferenceWidth && !lay.Numbers) ok = false;

                // ④ 保留数字时左侧必须真的腾得出来 —— 数字右对齐在 BarA - ScaleNumberRight 处，
                //    放不下就会被 ClipToBounds 裁掉最左边几位（这正是 §7.5 那个缺陷的根因）
                if (lay.Numbers &&
                    lay.BarA + 1e-9 < LevelPhaseRenderer.ScaleNumberRight + LevelPhaseRenderer.ScaleTextWidth)
                    ok = false;

                if (lay.Numbers) narrowestWithNumbers = Math.Min(narrowestWithNumbers, w);
            }

            if (worstOverflow > 0.001) ok = false;

            // ⑤ 宽度 ≥ 参照页要求时：几何与参照页一致 + 柱体居中（自适应必须是 no-op）
            var atRef = LevelPhaseRenderer.ComputeLayout(LevelPhaseRenderer.ReferenceWidth);
            var atBig = LevelPhaseRenderer.ComputeLayout(400);
            bool sameGeometry =
                Math.Abs(atBig.BarWidth - atRef.BarWidth) < 1e-9 &&
                Math.Abs(atBig.BarGap - atRef.BarGap) < 1e-9 &&
                atBig.Numbers && atRef.Numbers;

            double bigBlock = 2 * atBig.BarWidth + atBig.BarGap;
            bool centered = Math.Abs(atBig.BarA - (400 - bigBlock) / 2) < 1e-9;
            if (!sameGeometry || !centered) ok = false;

            notes.Add($"最大越界 {worstOverflow:0.###}px（须 0）");
            notes.Add("柱宽 " +
                      $"101→{LevelPhaseRenderer.ComputeLayout(101).BarWidth:0.#} / " +
                      $"150→{LevelPhaseRenderer.ComputeLayout(150).BarWidth:0.#} / " +
                      $"170→{LevelPhaseRenderer.ComputeLayout(170).BarWidth:0.#} / " +
                      $"{LevelPhaseRenderer.ReferenceWidth:0}→{atRef.BarWidth:0.#} / 400→{atBig.BarWidth:0.#}");
            notes.Add($"刻度数字保到宽 {narrowestWithNumbers:0}（阈值 {LevelPhaseRenderer.MinWidthWithNumbers:0}）");
            notes.Add(centered && sameGeometry
                ? "≥参照页宽度：居中且几何一致（自适应为 no-op）"
                : "⚠ 宽容时几何与参照页不一致或没居中");

            // ⑥ 真画一遍：**最窄的实际面板尺寸**（101×343 —— 窗口拉到最小时 C+J 的宽 × 第一组的高）。
            //    几何算对了不等于画出来了 —— 这是 §七那条教训的同一个意思，所以不能只验"没抛异常"。
            var live = new VizFrame { SampleRate = 44100, Dt = 1.0 / 60.0, Active = true, Revision = 1 };
            live.RmsL = 0.50f;
            live.RmsR = 0.45f;
            live.Correlation = 0.20f;
            for (int i = 0; i < VizFrame.WaveFrames; i++)
            {
                float v = 0.6f * MathF.Sin(2f * MathF.PI * 220f * i / 44100f);
                live.WaveL[i] = v;
                live.WaveR[i] = v * 0.9f;
            }

            var idle = new VizFrame();
            idle.Clear();

            const int NarrowW = 101;
            const int NarrowH = 343;

            var narrowLive = RenderPixels(new LevelPhaseRenderer(), live, NarrowW, NarrowH, out string errN1);
            var narrowIdle = RenderPixels(new LevelPhaseRenderer(), idle, NarrowW, NarrowH, out string errN2);

            if (narrowLive is null || narrowIdle is null)
            {
                ok = false;
                notes.Add("⚠ 最窄尺寸渲染抛异常：" + errN1 + errN2);
            }
            else
            {
                int changed = CountChanged(narrowLive, narrowIdle);
                int need = Math.Max(64, CountNonBackground(narrowIdle) / 10);
                if (changed < need) ok = false;
                notes.Add($"最窄实际尺寸 {NarrowW}×{NarrowH}：变化 {changed} 像素（门槛 {need}）");
            }

            // ⑦ 文字贴边钳制：**像素检查分不出「贴边」与「被裁」**（两者都碰到边缘），所以不靠画面，
            //    直接对纯函数扫参数空间。这条规则是踩了两次才补上的（先刻度数字、再 J 的 -1/+1）。
            bool clampOk = true;
            int clampCases = 0;

            for (double tw = 4; tw <= 96; tw += 2)
            {
                for (double pw = 40; pw <= 320; pw += 1)
                {
                    for (int k = 0; k <= 4; k++)
                    {
                        double cx = pw * k / 4.0;
                        double left = LevelPhaseRenderer.FittedLeft(tw, cx, pw);
                        clampCases++;

                        // ① 面板放得下时必须落在界内
                        if (tw < pw && (left < -1e-9 || left + tw > pw + 1e-9)) clampOk = false;

                        // ② 居中位置本来就合法时不许动它（「宽裕时与参照页逐像素一致」的前提）
                        //    ⚠️ 名字不能叫 centered —— 外层第 ⑤ 步已经有个 bool centered，内层遮蔽外层是 CS0136
                        double centerLeft = cx - tw / 2;
                        if (centerLeft >= 0 && centerLeft + tw <= pw && Math.Abs(left - centerLeft) > 1e-9)
                            clampOk = false;
                    }
                }
            }

            if (!clampOk) ok = false;
            notes.Add($"文字贴边钳制 {clampCases} 组参数：全在界内且不改变合法居中解");

            return new Result(Title, ok, string.Join("；", notes));
        }
        catch (Exception ex)
        {
            return new Result(Title, false, ex.GetType().Name + "：" + ex.Message);
        }
    }

    // ------------------------------------------------------------------ M2 补充：渲染路径开销

    /// <summary>
    /// **每帧分配字节数** —— 把「低开销」从形容词变成报告里的一行数。
    ///
    /// 为什么需要它：方案 §二 那张渲染路线表把 `Canvas + Polyline/Path` 判为「每帧分配几何」并否掉，
    /// 选了 `DrawingVisual + RenderOpen`（备注写「免分配」）。但**「免元素分配」不等于「零分配」**
    /// —— WPF 每录一条绘制指令都要分配一个指令对象。所以只能量，不能声称。
    ///
    /// 度量**含 WPF 内部**的分配（`RenderOpen()` 的指令对象就在里面），这正是我们关心的量：
    /// 「每帧往 gen0 丢多少垃圾」。地板不会是 0，但**跨版本漂移一眼可见** ——
    /// 谁把「槽位复用几何」改回「每帧新建 20 条」，D 的数字会直接跳十几倍、断言立刻红。
    ///
    /// 用 `GC.GetAllocatedBytesForCurrentThread()`：只统计当前线程，正好覆盖这整条绘制路径
    /// （`RenderOpen` + 渲染器都在调用线程上）。刻意**不走** `RenderTargetBitmap` ——
    /// 那会把合成开销也混进来，反而看不清我们自己这一层。
    /// </summary>
    private static Result CheckVizAllocation()
    {
        const string Title = "每帧分配字节数（渲染路径，含 WPF 指令对象）";

        try
        {
            var feed = new FakeFeed { Frequency = 440, Amplitude = 0.8f };
            var analyzer = new VizAnalyzer(feed);
            analyzer.Update();

            var frame = analyzer.Frame;
            var names = new[] { "A 频谱条", "B 示波器", "C+J 电平相位", "D 利萨如" };

            // 上限取「实测值 ×~1.5」。**它不是性能目标，是用来抓回归的** ——
            // 谁把某块改出量级变化（D 的槽位复用改回每帧新建 20 条、C+J 的静态层被拆掉），
            // 这条会立刻红。当前实测：A 0.9 / B 17.7 / C+J 9.1 / D 22.4 KB/帧。
            var limits = new long[] { 4 * 1024, 24 * 1024, 14 * 1024, 32 * 1024 };

            const int Warmup = 60;    // 让 EnsureResources / JIT / WPF 内部的一次性分配先发生
            const int Frames = 300;   // D 的余辉要跑满 20 层才是稳态（20 帧），给足

            var notes = new List<string>();
            bool ok = true;

            long Measure(IVizRenderer renderer)
            {
                var panel = new VizPanel { Renderer = renderer, Frame = frame };
                panel.Measure(new Size(320, 160));
                panel.Arrange(new Rect(0, 0, 320, 160));

                for (int i = 0; i < Warmup; i++)
                {
                    frame.Revision++;    // 像真机一样：每帧都是新的一帧
                    panel.Redraw();
                }

                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < Frames; i++)
                {
                    frame.Revision++;
                    panel.Redraw();
                }
                long after = GC.GetAllocatedBytesForCurrentThread();

                return (after - before) / Frames;
            }

            var perFrame = new long[names.Length];
            for (int kind = 0; kind < names.Length; kind++)
            {
                perFrame[kind] = Measure(MakeRenderer(kind));

                bool good = perFrame[kind] <= limits[kind];
                if (!good) ok = false;

                notes.Add($"{names[kind]} {perFrame[kind] / 1024.0:0.#}KB/帧" +
                          (good ? "" : $" ← 超上限 {limits[kind] / 1024.0:0}KB"));
            }

            // 对照组：D 关掉槽位缓存 = 回到「每帧新建 20 条几何」。有对照，「省了多少」才是数字。
            long dUncached = Measure(new LissajousRenderer { DisableGeometryCache = true });
            double saved = dUncached > 0 ? perFrame[3] / (double)dUncached : 1;

            if (saved > 0.4)   // 缓存没生效（或只省一点点）→ 说明它没在干活
            {
                ok = false;
                notes.Add($"✗ D 槽位缓存没起作用：开 {perFrame[3] / 1024.0:0.#}KB vs 关 {dUncached / 1024.0:0.#}KB");
            }
            else
            {
                notes.Add($"D 槽位缓存对照：关掉 {dUncached / 1024.0:0.#}KB → 开启 {perFrame[3] / 1024.0:0.#}KB" +
                          $"（降到 {saved * 100:0}%）");
            }

            return new Result(Title, ok,
                string.Join("；", notes) + $"（320×160，预热 {Warmup} 帧后量 {Frames} 帧）");
        }
        catch (Exception ex)
        {
            return new Result(Title, false, ex.GetType().Name + "：" + ex.Message);
        }
    }

    // ------------------------------------------------------------------ M6：接入

    /// <summary>自检用的假上游：按需吐出「(2i, 2i+1)」这样成对的递增样本，便于逐样本断言。</summary>
    private sealed class FakeSampleSource : NAudio.Wave.ISampleProvider
    {
        public NAudio.Wave.WaveFormat WaveFormat { get; } =
            NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

        /// <summary>下一次 <c>Read</c> 交出多少个**浮点样本**（可设成奇数，验半帧取整）。</summary>
        public int Next { get; set; }

        private float _next;

        public int Read(Span<float> buffer)
        {
            int n = Math.Min(Next, buffer.Length);
            for (int i = 0; i < n; i++) buffer[i] = _next++;
            return n;
        }
    }

    /// <summary>
    /// <see cref="VizTap"/> —— M6 接入时**音频线程上唯一的新代码**，也是这轮风险最高的一块。
    ///
    /// 验三件事：
    /// <list type="number">
    /// <item><b>透传</b>：引擎拿到的样本一个不改（tap 只能观察，绝不能动声音）。</item>
    /// <item><b>写入与读回</b>：环里拿到的是同一批数据、L/R 没串位。</item>
    /// <item><b>半帧取整</b>：上游交出奇数个浮点样本时，**只丢那个凑不满一帧的尾巴**，
    ///   环里绝不出现错位的声道。</item>
    /// </list>
    /// 另外顺带确认「活动性即播放状态」这条设计（暂停后音频线程不再拉数据 → 自动变 false）。
    /// </summary>
    private static Result CheckVizTap()
    {
        const string Title = "音频分接节点（透传不改样本 / 环读回 / 半帧取整 / 活动性即播放状态）";

        try
        {
            var src = new FakeSampleSource();
            var tap = new VizTap(src, 0);

            bool ok = true;
            var rows = new List<string>();

            // 起步：没拉过数据 → 不算在播
            if (tap.IsPlaying) { ok = false; rows.Add("✗ 还没拉数据就报在播"); }

            var buf = new float[256];
            src.Next = 100;                      // 100 个浮点 = 50 帧（立体声）
            int read = tap.Read(buf);

            // ① 透传：一个样本都不能改
            bool passthrough = read == 100;
            for (int i = 0; i < 100 && passthrough; i++)
                if (buf[i] != i) passthrough = false;
            ok &= passthrough;
            rows.Add(passthrough ? "透传 100 个样本未改" : "✗ 透传改动了样本");

            // ② 读回：最后 50 帧 = 浮点 0..99 组成的 50 个 (2i, 2i+1)
            var l = new float[50];
            var r = new float[50];
            int got = tap.ReadLatest(l, r, 50);
            bool ringOk = got == 50;
            for (int i = 0; i < 50 && ringOk; i++)
                if (l[i] != 2 * i || r[i] != 2 * i + 1) ringOk = false;
            ok &= ringOk;
            rows.Add(ringOk ? "环读回 50 帧、L/R 未串位" : $"✗ 环读回不对（got={got}）");

            // ③ 半帧取整：上游给 101 个浮点（末尾那个 200 凑不满一帧）→ 环里只能到 199。
            //    若把尾样本也写进去，环尾就会出现 (199,200) 这种**错位帧**。
            //    注意 ReadLatest 取的是**最近** 2 帧，所以在第二批（100..200）里是 196..199。
            src.Next = 101;
            tap.Read(buf);

            var l1 = new float[2];
            var r1 = new float[2];
            tap.ReadLatest(l1, r1, 2);
            bool align = l1[0] == 196 && r1[0] == 197 && l1[1] == 198 && r1[1] == 199;
            ok &= align;
            rows.Add(align
                ? "半帧取整：奇数尾样本 200 未进环（帧仍对齐）"
                : $"✗ 半帧取整不对：({l1[0]},{r1[0]})({l1[1]},{r1[1]})，期望 (196,197)(198,199)");

            // ④ 活动性即播放状态：刚拉过 → 在播；睡过窗口 → 自动不在播
            bool activeAfterPull = tap.IsPlaying;
            Thread.Sleep(320);
            bool idleAfterWait = !tap.IsPlaying;
            ok &= activeAfterPull && idleAfterWait;
            rows.Add(activeAfterPull && idleAfterWait
                ? "拉过就在播 / 停拉 320ms 后自动不在播"
                : $"✗ 活动性判据不对（拉后 {activeAfterPull}、等待后 {!idleAfterWait}）");

            // ⑤ 释放后恒不在播
            tap.Dispose();
            if (tap.IsPlaying) { ok = false; rows.Add("✗ 释放后仍报在播"); }

            return new Result(Title, ok, string.Join("；", rows));
        }
        catch (Exception ex)
        {
            return new Result(Title, false, ex.GetType().Name + "：" + ex.Message);
        }
    }

    // ------------------------------------------------------------------ M4：节拍与语义收口

    /// <summary>
    /// 暂停「慢慢衰减到干净」与终止「立刻清零」两套语义（方案 §2，2026-09-21 修订过终点）。
    ///
    /// 为什么必须自检：**这种数值语义肉眼判不准**（还剩 15% 还是已经 0，在屏幕上不容易分清），
    /// 而「是渐进衰减还是瞬间清零」更是只能看数值。这里直接驱动 <see cref="VizAnalyzer"/>，
    /// 用假声源把播放 → 暂停 → 终止整条走一遍。
    ///
    /// 锁三件事：① 暂停是**渐进**的（第一帧只走一小步，不是瞬间清零）；
    /// ② 暂停最终**归零**（曾经是"停在 15%"，改掉了）；③ 终止是**立刻**清零。
    ///
    /// 顺带验一条**跨项约束**：衰减必须在节拍的 1.5s 硬上限之前走完 ——
    /// 否则硬上限会先触发、节拍提前退订，画面被截在"还差一点"的样子上再也补不回来。
    /// </summary>
    private static Result CheckVizFade()
    {
        const string Title = "暂停衰减到干净 / 终止立刻归零（§2 修订：过程渐进、终点为 0）";

        try
        {
            var feed = new FakeFeed { Frequency = 1000, Amplitude = 1f };
            var analyzer = new VizAnalyzer(feed);

            // 播放若干帧，把平滑值推到稳态（60Hz 限频 → 每次之间要睡过 16.7ms）
            for (int i = 0; i < 8; i++)
            {
                analyzer.Update();
                Thread.Sleep(20);
            }

            var f = analyzer.Frame;
            float liveRms = f.RmsL;
            float liveCorr = f.Correlation;
            float liveBar = Peak(f.Bars);
            float liveWave = Peak(f.WaveL);

            // 暂停 → 跑到落定（上限 100 帧 ≈ 1.7s，比节拍的 1.5s 上限宽松）。
            // 顺带抓**第一帧**的值：暂停必须是**衰减**（慢慢落），不是瞬间清零 —— 那是终止的语义。
            feed.IsPlaying = false;
            int framesToSettle = -1;
            float fade1Rms = 0f;
            for (int i = 0; i < 100; i++)
            {
                analyzer.Update();
                if (i == 0) fade1Rms = f.RmsL;
                if (analyzer.IsSettled) { framesToSettle = i + 1; break; }
                Thread.Sleep(20);
            }

            float pausedRms = f.RmsL;
            float pausedCorr = f.Correlation;
            float pausedBar = Peak(f.Bars);
            float pausedWave = Peak(f.WaveL);

            double settleSeconds = framesToSettle > 0 ? framesToSettle / (double)VizAnalyzer.AnalyzeHz : -1;

            // 终点一律是 0（2026-09-21 改的：原方案是"停在 15%"，实机下来 D 归零、其余停在 15%，
            // 五块两种收尾；而且 C 会显示 -27.2 这种"明明没声音却读出电平"的假数）。
            bool settledToZero =
                NearZero(pausedRms) && NearZero(pausedBar) && NearZero(pausedWave) && NearZero(pausedCorr);

            // 但**过程**必须是渐进的：第一帧只走了一小步（RMS 回落 8%/帧 → 还剩 ~92%）
            bool gradual = fade1Rms > liveRms * 0.5f;

            bool ok =
                framesToSettle > 0 &&
                settleSeconds < 1.5 &&              // 必须比节拍的硬上限先收敛
                settledToZero && gradual;

            // 归零（停止 / 切曲 / 播完）：**立刻**清干净，且要顶 ResetRevision 让 D 丢历史
            long revBefore = f.ResetRevision;
            analyzer.Reset();
            bool zeroed = f.RmsL == 0f && f.Correlation == 0f && !f.Active && analyzer.IsSettled &&
                          Peak(f.Bars) == 0f && Peak(f.WaveL) == 0f;
            bool revBumped = f.ResetRevision > revBefore;

            // 归零后继续跑几帧：不许又冒出东西（目标本来就是 0）
            for (int i = 0; i < 5; i++)
            {
                analyzer.Update();
                Thread.Sleep(20);
            }
            bool staysZero = f.RmsL == 0f && Peak(f.Bars) == 0f && Peak(f.WaveL) == 0f;

            ok &= zeroed && revBumped && staysZero;

            string detail =
                $"播放中 电平 {liveRms:0.###}／柱峰 {liveBar:0.###}／波峰 {liveWave:0.###}／相关 {liveCorr:0.###}；" +
                $"暂停首帧 电平 {fade1Rms:0.###}（{Ratio(fade1Rms, liveRms):P0}，须 >50% → 是渐进而非瞬间清零）；" +
                $"收敛后 电平 {pausedRms:0.###}／柱峰 {pausedBar:0.###}／波峰 {pausedWave:0.###}／相关 {pausedCorr:0.###}" +
                $"（四者都须≈0）；" +
                $"收敛于第 {framesToSettle} 帧（{settleSeconds:0.##}s，须 <1.5s）；" +
                $"终止 全零={Show(zeroed)} ResetRevision {revBefore}→{f.ResetRevision}；终止后不再冒出={Show(staysZero)}";

            return new Result(Title, ok, detail);
        }
        catch (Exception ex)
        {
            return new Result(Title, false, ex.GetType().Name + "：" + ex.Message);
        }
    }

    /// <summary>峰值的绝对值（柱高 / 波形都用它）。</summary>
    private static float Peak(float[] a)
    {
        float m = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            float v = MathF.Abs(a[i]);
            if (v > m) m = v;
        }
        return m;
    }

    private static float Ratio(float part, float whole) => whole > 1e-6f ? part / whole : 0f;

    /// <summary>是否已经归零（阈值比 <c>FadeEpsilon</c> 宽一点，容得下平滑的尾巴）。</summary>
    private static bool NearZero(float v) => MathF.Abs(v) <= 0.01f;

    /// <summary>
    /// 封面素材（M7）：**29 个作品 id 是不是都能解析出图**。
    ///
    /// 为什么值得单独一条：封面是**固化进程序集**的
    /// （`assets/cover/embed/*.jpg` + csproj 的 `&lt;Resource&gt;` + `<Link>`），
    /// 这条链上有三个会**静默失效**的地方 —— 文件没进 `embed/`、csproj 的 Link 写歪、id 拼错。
    /// 三者都只表现为「某作没封面」：跑起来看不出是哪一个坏，也没人会逐个点 29 首去试。
    ///
    /// 除了「都能解析」，还反向核对**程序集里到底有哪几张**
    /// （`Resource`-build 的项不进 `ManifestResourceNames`，得读 `.g.resources`）——
    /// 多放一张、改错名都能当场抓出来。
    /// </summary>
    private static Result CheckVizCover()
    {
        const string Title = "封面素材（29 个作品 id 全部可解析 / ≤512 / 副版缺则回退）";

        try
        {
            // 与数据索引一致的作品清单（官方 21 + 新典 1 + tasofro 7）
            var ids = new[]
            {
                "th06", "th07", "th08", "th09", "th095", "th10", "th11", "th12", "th125", "th128",
                "th13", "th14", "th143", "th15", "th16", "th165", "th17", "th18", "th185", "th19",
                "th20",
                "th06nc",
                "th075", "th105", "th123", "th135", "th145", "th155", "th175",
            };

            var missing = new List<string>();
            var tooBig = new List<string>();
            var problems = new List<string>();
            var expected = new List<string>();

            int altCount = 0;

            foreach (string id in ids)
            {
                expected.Add($"resources/cover/{id}.jpg");

                var image = VizCover.Load(id, alt: false);

                if (image is null)
                {
                    missing.Add(id);
                    continue;
                }

                if (image.PixelWidth > VizCover.MaxEdge || image.PixelHeight > VizCover.MaxEdge)
                    tooBig.Add($"{id}({image.PixelWidth}×{image.PixelHeight})");

                // 副版：**存在才用**。给了 `_alt` 的（th06nc）该解析出**另一张**；
                // 没给的（th13 之类）必须与主版**同一张** —— 这正是「不跟霊界版走」的实现。
                if (VizCover.Exists(id + VizCover.AltSuffix))
                {
                    altCount++;
                    expected.Add($"resources/cover/{id}{VizCover.AltSuffix}.jpg");

                    var altImage = VizCover.Load(id, alt: true);

                    if (altImage is null) problems.Add($"{id} 有 _alt 却解不出来");
                    else if (ReferenceEquals(altImage, image)) problems.Add($"{id} 的 _alt 与主版同一张");
                }
                else if (!ReferenceEquals(VizCover.Load(id, alt: true), image))
                {
                    problems.Add($"{id} 无 _alt 却没回退主版");
                }
            }

            // 反向核对：程序集里的素材集合必须与预期**一模一样**（多一张少一张都说明哪里错了）
            var embedded = ListEmbeddedCovers();
            expected.Sort(StringComparer.OrdinalIgnoreCase);

            var extra = embedded.Except(expected, StringComparer.OrdinalIgnoreCase).ToList();
            var lack = expected.Except(embedded, StringComparer.OrdinalIgnoreCase).ToList();

            // ---- 界面层：占位与图必须**互斥** ----
            // 上面那些只证明了"资源在"，不能证明"画面上换得动"。这里直接拿真控件驱动一遍：
            // 有图 → 占位折叠；没图 → 占位露出来。**可离屏验，不需要 Show 窗口**。
            var win = new VizWindow(null);
            var surface = win.Surface;

            bool uiOk;
            string uiNote;

            if (surface.FindName("CoverImage") is not System.Windows.Controls.Image coverImage ||
                surface.FindName("CoverPlaceholder") is not UIElement placeholder)
            {
                uiOk = false;
                uiNote = "找不到 CoverImage / CoverPlaceholder";
            }
            else
            {
                var sample = VizCover.Load("th06", alt: false);

                surface.SetCover(sample);
                bool withImage = coverImage.Source is not null &&
                                 placeholder.Visibility == Visibility.Collapsed;

                surface.SetCover(null);
                bool without = coverImage.Source is null &&
                               placeholder.Visibility == Visibility.Visible;

                uiOk = withImage && without;
                uiNote = uiOk
                    ? "有图→占位折叠 / 没图→占位露出（都不需要 Show 窗口）"
                    : $"✗ 占位切换不对（有图 {withImage} / 没图 {without}）";
            }

            bool ok = missing.Count == 0 && tooBig.Count == 0 && problems.Count == 0 &&
                      extra.Count == 0 && lack.Count == 0 && uiOk;

            string detail =
                $"可解析 {ids.Length - missing.Count}/{ids.Length}" +
                $"（缺：{(missing.Count == 0 ? "无" : string.Join("、", missing))}）；" +
                $"超 {VizCover.MaxEdge}px：{(tooBig.Count == 0 ? "无" : string.Join("、", tooBig))}；" +
                $"带副版 {altCount} 个（th06nc 应为 1）；" +
                $"程序集里 {embedded.Count} 张（预期 {expected.Count}" +
                $"{(extra.Count == 0 ? "" : "，多 " + string.Join("、", extra))}" +
                $"{(lack.Count == 0 ? "" : "，少 " + string.Join("、", lack))}）" +
                "；" + uiNote +
                (problems.Count == 0 ? "" : "；⚠ " + string.Join("；", problems));

            return new Result(Title, ok, detail);
        }
        catch (Exception ex)
        {
            return new Result(Title, false, ex.GetType().Name + "：" + ex.Message);
        }
    }

    /// <summary>
    /// 列出程序集里 <c>resources/cover/</c> 下的素材名。
    ///
    /// ⚠️ 不能用 <c>GetManifestResourceNames()</c> —— `Resource`-build 的项**不在**那里，
    /// 它们被打进 <c>&lt;程序集名&gt;.g.resources</c> 这一个清单资源里，得用
    /// <see cref="System.Resources.ResourceReader"/> 读。当初以为「能解析出来就算对」，
    /// 但那只证明**某个名字**能查到；只有枚举出来才能发现"多了一张 / 名字改错了"。
    /// </summary>
    private static List<string> ListEmbeddedCovers()
    {
        var names = new List<string>();

        var asm = typeof(VizCover).Assembly;

        using var stream = asm.GetManifestResourceStream(asm.GetName().Name + ".g.resources");
        if (stream is null) return names;

        using var reader = new System.Resources.ResourceReader(stream);

        foreach (System.Collections.DictionaryEntry entry in reader)
        {
            if (entry.Key is string key &&
                key.StartsWith("resources/cover/", StringComparison.OrdinalIgnoreCase))
                names.Add(key);
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    /// <summary>
    /// 节拍的退订策略（方案 §3.5 / R2「绝不让它常驻空转」）。
    ///
    /// <c>CompositionTarget.Rendering</c> 在自检里根本不会触发（没有渲染循环），
    /// 所以判定被抽成了纯函数 <see cref="VizPump.ShouldStop"/>，这里扫它的各态。
    /// </summary>
    private static Result CheckVizIdlePolicy()
    {
        const string Title = "节拍退订策略（播放中不退订 / 落定即退订 / 1.5s 硬上限）";

        try
        {
            const double Limit = 1.5;
            var rows = new List<string>();
            bool ok = true;

            bool Row(string label, bool active, bool settled, double idle, bool want)
            {
                bool got = VizPump.ShouldStop(active, settled, idle, Limit);
                ok &= got == want;
                rows.Add(got == want
                    ? $"{label}={(got ? "退订" : "继续")}"
                    : $"{label} ✗期望{(want ? "退订" : "继续")} 实际{(got ? "退订" : "继续")}");
                return got == want;
            }

            Row("播放中·即便判定已落定", true, true, 99, false);      // 在播就该一直画
            Row("刚不播·未落定", false, false, 0.01, false);
            Row("刚不播·已落定", false, true, 0.01, true);            // 收敛到位，立刻放掉
            Row("未落定·差一点到限", false, false, 1.499, false);
            Row("未落定·正好到限", false, false, 1.500, true);
            Row("未落定·超限", false, false, 5.0, true);

            // 帧距统计的两条边界（诊断用，见 docs/2026-09-21-viz-frame-pacing-pending.md）。
            // 错了不会报错、只会让**峰值变成垃圾**，进而把整次分诊带偏 —— 所以锁住。
            bool intervalOk =
                !VizPump.CountInterval(false, 16.7) &&      // 没有上一帧可比（刚 Start）→ 不收
                VizPump.CountInterval(true, 16.7) &&        // 正常间隔 → 收
                !VizPump.CountInterval(true, 0) &&          // 非正（时钟回退/重启边界）→ 不收
                !VizPump.CountInterval(true, -5);

            ok &= intervalOk;
            rows.Add(intervalOk
                ? "帧距边界：无上一帧/非正间隔都不计入"
                : "✗ 帧距边界判据不对（会把退订时长或时钟回退算成峰值）");

            return new Result(Title, ok,
                $"上限 {Limit}s｜" + string.Join("；", rows));
        }
        catch (Exception ex)
        {
            return new Result(Title, false, ex.GetType().Name + "：" + ex.Message);
        }
    }

    /// <summary>
    /// 延迟对齐的取窗数学（方案 §2 的「读约 100ms 前的样本」）。
    ///
    /// **画面上看不出 100ms 的错位** —— 频谱/波形晚个几十毫秒，人眼分不出来 ——
    /// 所以这类"错了也不报警"的算术必须直接扫。顺带把三种边界一起锁住：
    /// 正常取窗 / 写指针跨环尾（调用方取模）/ 数据还不够（前面留白读静音，
    /// 而不是把环里的旧数据当新数据读出来）。
    /// </summary>
    private static Result CheckVizRingWindow()
    {
        const string Title = "延迟对齐取窗（偏移 / 数据不足留白 / 偏移过大取不到）";

        try
        {
            var rows = new List<string>();
            bool ok = true;

            bool Row(string label, long written, int frames, int offset,
                     long wantFrom, int wantHead, int wantCount)
            {
                var (from, head, count) = VizRing.Window(written, frames, offset);
                bool good = from == wantFrom && head == wantHead && count == wantCount;
                ok &= good;
                rows.Add(good
                    ? $"{label}=(from {from} head {head} count {count})"
                    : $"{label} ✗期望 (from {wantFrom} head {wantHead} count {wantCount}) " +
                      $"实际 (from {from} head {head} count {count})");
                return good;
            }

            Row("无偏移", 1000, 100, 0, 900, 0, 100);
            Row("偏移50帧", 1000, 100, 50, 850, 0, 100);

            // 方案默认的 100ms @44100 = 4410 帧 —— 窗口整体后退 4410 帧，一格不差
            Row("默认100ms", 20000, 4096, 4410, 20000 - 4410 - 4096, 0, 4096);

            Row("数据恰好够", 100, 100, 0, 0, 0, 100);
            Row("数据不足→留白", 50, 100, 0, 0, 50, 50);        // 前 50 帧保持静音
            Row("偏移比已写入还大", 30, 100, 50, 0, 100, 0);     // 一格都别取
            Row("frames=0", 1000, 0, 0, 0, 0, 0);
            Row("负偏移按 0 处理", 1000, 100, -50, 900, 0, 100);  // 绝不能算出"写到未来"的窗口

            return new Result(Title, ok, string.Join("；", rows));
        }
        catch (Exception ex)
        {
            return new Result(Title, false, ex.GetType().Name + "：" + ex.Message);
        }
    }

    /// <summary>相对容差比较（星号分配 + 整像素取整会有零点几个百分点的偏差）。</summary>
    private static bool Near(double value, double target, double tolerance = 0.03) =>
        target > 0 && Math.Abs(value - target) <= target * tolerance;

    /// <summary>
    /// 把一块面板离屏画一遍，返回 BGRA 像素缓冲（失败返回 <c>null</c> 并向 <paramref name="error"/> 回填错误串）。
    /// 用 <c>RenderTargetBitmap</c> 而不是真窗口 —— 自检不开可见窗口
    /// （<c>ShutdownMode=OnLastWindowClose</c>，乱开窗口会把进程提前关掉）。
    /// </summary>
    private static byte[]? RenderPixels(IVizRenderer renderer, VizFrame frame, int w, int h, out string error)
    {
        error = "";
        try
        {
            var panel = new VizPanel { Renderer = renderer, Frame = frame };
            panel.Measure(new Size(w, h));
            panel.Arrange(new Rect(0, 0, w, h));
            panel.Redraw();

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(panel);

            int stride = w * 4;
            var px = new byte[stride * h];
            rtb.CopyPixels(px, stride, 0);
            return px;
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + "：" + ex.Message;
            return null;
        }
    }

    /// <summary>
    /// 左上角是不是底色 —— 「离屏位图真的被渲染过」的证据。
    /// （若位图全空，通道值会是 0x00，与底色不符，会在这里被抓住而不是误判成「画了很多」。）
    /// </summary>
    private static bool BackgroundPainted(byte[] px)
    {
        var bg = BackgroundColor;
        return px[0] == bg.B && px[1] == bg.G && px[2] == bg.R;
    }

    /// <summary>与底色不同的像素数。</summary>
    private static int CountNonBackground(byte[] px)
    {
        var bg = BackgroundColor;
        int n = 0;
        for (int i = 0; i < px.Length; i += 4)
            if (px[i] != bg.B || px[i + 1] != bg.G || px[i + 2] != bg.R) n++;
        return n;
    }

    /// <summary>
    /// 数一条**竖条带**里的非底色像素（x ∈ [x0, x1)）。
    ///
    /// 为什么不数全图：左上角有 L/R 标签、中间有分割线与两半的中线 —— 那些是**常量**，
    /// 会把「波形到底有没有变」淹掉。挑一条只会画到波形的竖条最干净。
    /// </summary>
    private static int InkInStrip(byte[] px, int w, int h, int x0, int x1)
    {
        if (x0 < 0) x0 = 0;
        if (x1 > w) x1 = w;

        var bg = BackgroundColor;
        int n = 0;

        for (int y = 0; y < h; y++)
        {
            int row = y * w * 4;
            for (int x = x0; x < x1; x++)
            {
                int i = row + x * 4;
                if (px[i] != bg.B || px[i + 1] != bg.G || px[i + 2] != bg.R) n++;
            }
        }

        return n;
    }

    /// <summary>两幅同尺寸图之间颜色不同的像素数（只比 BGR —— alpha 两幅都是 FF）。</summary>
    private static int CountChanged(byte[] a, byte[] b)
    {
        int n = 0;
        for (int i = 0; i < a.Length; i += 4)
            if (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2]) n++;
        return n;
    }

    /// <summary>面板底色（<c>VizStyle.Background</c> ← 主题 <c>BgDeep</c>）。从主题取而不是写死。</summary>
    private static Color BackgroundColor =>
        VizStyle.Create().Background is SolidColorBrush b ? b.Color : Color.FromRgb(0x17, 0x17, 0x17);

    /// <summary>
    /// 调试声源的**编码路由**：<c>.ogg</c> 里可能是 Vorbis 也可能是 Opus（扩展名一样）。
    /// 这条判定错了，表现就是 NVorbis 抛出「Could not find Vorbis data to decode.」，
    /// 用户看不出所以然 —— 实测踩过（新典 demo 期的 <c>th06nc_16.ogg</c> 其实是 Ogg Opus）。
    ///
    /// 用**内存里现造的最小 Ogg 页**锁住判定，不碰磁盘（运行期禁止写临时目录）。
    /// </summary>
    private static Result CheckVizCodecRoute()
    {
        // 最小合法 Ogg 页：27 字节页头 + 1 字节段表 + 8 字节首包签名 = 36 字节
        static byte[] Page(ReadOnlySpan<byte> sig)
        {
            var buf = new byte[36];
            "OggS"u8.CopyTo(buf);
            buf[5] = 0x02;                // BOS
            buf[26] = 1;                  // 段数
            buf[27] = (byte)sig.Length;   // 段长（8 < 255，单段即一包）
            sig.CopyTo(buf.AsSpan(28));
            return buf;
        }

        byte[] opus = Page("OpusHead"u8);
        byte[] vorbis = Page(new byte[] { 0x01, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s', 0 });
        byte[] notOgg = new byte[64];                     // 长度够但无 OggS 魔数
        byte[] truncated = new byte[30];                  // 有 OggS、段表在，但正文被截断
        "OggS"u8.CopyTo(truncated);
        truncated[26] = 1;

        string a = VizDebugFeed.SniffOggCodec(opus);
        string b = VizDebugFeed.SniffOggCodec(vorbis);
        string c = VizDebugFeed.SniffOggCodec(notOgg);
        string d = VizDebugFeed.SniffOggCodec(truncated);

        bool ok = a == "Opus" && b == "Vorbis"
                  && c == "非 Ogg 容器" && d == "Ogg 页不完整";

        return new Result("调试源编码路由（Ogg 内 Vorbis / Opus 辨别）", ok,
            $"OpusHead→{a}；vorbis→{b}；非 Ogg→{c}；截断页→{d}");
    }

    /// <summary>
    /// 自检专用的假声源：直接吐一路正弦，不碰文件、不碰声卡。
    /// 相位连续（<c>Phase</c> 一直往前推），所以多次读取拼起来是一个连续波形 ——
    /// 相关度、RMS 这些统计量才有意义。
    /// </summary>
    private sealed class FakeFeed : IVizFeed
    {
        public int SampleRate { get; init; } = 44100;

        public int Channels => 2;

        public bool IsPlaying { get; set; } = true;

        public float Amplitude { get; set; } = 1f;

        public double Frequency { get; set; } = 1000;

        /// <summary>右声道取反 → 用于验证相关度符号。</summary>
        public bool InvertRight { get; set; }

        private long _phase;

        public int ReadLatest(Span<float> dstL, Span<float> dstR, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                double t = (_phase + i) / (double)SampleRate;
                float v = Amplitude * MathF.Sin((float)(2.0 * Math.PI * Frequency * t));
                dstL[i] = v;
                dstR[i] = InvertRight ? -v : v;
            }
            _phase += frames;
            return frames;
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
