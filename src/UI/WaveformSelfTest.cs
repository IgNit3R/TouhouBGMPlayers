using ThbgmPlayer.Audio;
using ThbgmPlayer.Core;
using ThbgmPlayer.Data;
using ThbgmPlayer.Viz;

namespace ThbgmPlayer.UI;

/// <summary>
/// 整轨波形的离屏自检。条目格式与 <c>VizSelfTest</c> 一致（独立文件 + 返回它的 <c>Result</c> 列表）。
///
/// 这些东西全是**算术与判定**，而出过事的都是这一类（错一位整轨错位那种），
/// 所以断言全部落在「**决定**」上 —— 一笔都不靠"看起来对"。
/// 渲染结果那条属于第 3 步（面板真画出来之后）。
/// </summary>
internal static class WaveformSelfTest
{
    public static List<VizSelfTest.Result> Run() => new()
    {
        CheckBucketing(),
        CheckZoom(),
        CheckBucketSpan(),
        CheckFollow(),
        CheckFilePosition(),
        CheckLoopMark(),
        CheckCacheKey(),
        CheckPicker(),
        CheckCancel(),
        CheckRender(),
        CheckPanelZoom(),
        CheckRealFile(),
    };

    // ---------------------------------------------------------------- 分桶

    /// <summary>
    /// 扫描分桶：桶数 = ceil(帧数/256)、尖峰落在它该在的桶里、邻桶仍是低的。
    /// ⚠️ 分桶是**每 256 帧硬切**的，所以桶号 = 帧号/256 是确定的，测试可以钉死期望值。
    /// </summary>
    private static VizSelfTest.Result CheckBucketing()
    {
        const string Title = "波形分桶（桶数 / 尖峰落位 / 尾桶）";

        try
        {
            const int Frames = 256 * 12 + 7;          // 12 整桶 + 7 帧尾巴
            const int SpikeFrame = 3000;              // 落在桶 11（必须 < Frames，否则尖峰根本没写进 PCM）
            var src = new FakeSource(MakePcm(Frames, spikeAt: SpikeFrame, spikeValue: 30000), 44100, 2);

            var peaks = TrackScanner.Scan(src);

            if (peaks is null) return new VizSelfTest.Result(Title, false, "扫描返回 null");

            int expectBuckets = (Frames + WaveformPeaks.FramesPerBucket - 1) / WaveformPeaks.FramesPerBucket;

            bool ok = true;
            var rows = new List<string>
            {
                $"桶数 {peaks.BucketCount}（期望 {expectBuckets}）",
                $"每桶秒数 {peaks.SecondsPerBucket:0.00000}（期望 {256.0 / 44100:0.00000}）",
                $"总时长 {peaks.TotalSeconds:0.000} 秒",
            };

            ok &= peaks.BucketCount == expectBuckets;
            ok &= Math.Abs(peaks.SecondsPerBucket - 256.0 / 44100) < 1e-9;

            int spikeBucket = SpikeFrame / WaveformPeaks.FramesPerBucket;
            sbyte atSpike = peaks.Max[spikeBucket];

            ok &= atSpike > 100;                                        // 尖峰被记下来了（>100/127）
            ok &= peaks.Max[spikeBucket - 1] < 40;                      // 邻桶不该被尖峰污染

            rows.Add($"尖峰桶 {spikeBucket} 的 max = {atSpike}（期望 >100）；邻桶 max = {peaks.Max[spikeBucket - 1]}（期望 <40）");

            // 尾桶：7 帧的那半桶也得有值（斜坡信号 ⇒ 非零），否则最后一小段没波形
            sbyte tail = peaks.Max[expectBuckets - 1];
            ok &= tail != 0;
            rows.Add($"尾桶 max = {tail}（期望非 0）");

            return new VizSelfTest.Result(Title, ok, string.Join("；", rows));
        }
        catch (Exception ex)
        {
            return new VizSelfTest.Result(Title, false, ex.GetType().Name + "：" + ex.Message);
        }
    }

    // ---------------------------------------------------------------- 缩放

    /// <summary>
    /// 缩放：**锚点不动**、上下限夹取、单调、放大可逆。
    /// ⚠️ 锚点只在**没被夹住**时保持不变 —— 贴到曲首/曲尾时锚点必然偏，那是设计不是 bug。
    /// </summary>
    private static VizSelfTest.Result CheckZoom()
    {
        const string Title = "波形缩放（锚点 / 上下限 / 单调 / 可逆）";

        try
        {
            const double Total = 180, Width = 400, SecPerBucket = 256.0 / 44100;
            double minSpan = WaveformMath.MinSpan(Width, SecPerBucket, Total);

            var v0 = new ViewRange(60, 40);
            double anchorX = 100;                       // 25% 处
            double anchorTimeBefore = v0.Start + anchorX / Width * v0.Span;

            var vIn = WaveformMath.ZoomAt(v0, 120, anchorX, Width, SecPerBucket, Total);
            double anchorTimeAfter = vIn.Start + anchorX / Width * vIn.Span;

            bool ok = true;
            var rows = new List<string>
            {
                $"跨度 {v0.Span:0.###} → 放大一格 {vIn.Span:0.###}（期望 {v0.Span / WaveformMath.ZoomStep:0.###}）",
                $"锚点时间 {anchorTimeBefore:0.####} → {anchorTimeAfter:0.####}",
            };

            ok &= vIn.Span < v0.Span;
            ok &= Math.Abs(vIn.Span - v0.Span / WaveformMath.ZoomStep) < 1e-9;
            ok &= Math.Abs(anchorTimeBefore - anchorTimeAfter) < 1e-9;

            // 上限：一路放大到底，跨度应停在 MinSpan 且不再变
            var vMax = v0;
            for (int i = 0; i < 200; i++) vMax = WaveformMath.ZoomAt(vMax, 120, Width / 2, Width, SecPerBucket, Total);
            var vMax2 = WaveformMath.ZoomAt(vMax, 120, Width / 2, Width, SecPerBucket, Total);

            ok &= Math.Abs(vMax.Span - minSpan) < 1e-9;
            ok &= Math.Abs(vMax2.Span - minSpan) < 1e-9;
            rows.Add($"放大到底：跨度 {vMax.Span:0.####}（MinSpan {minSpan:0.####}），再放大一格仍是 {vMax2.Span:0.####}");

            // 下限：一路缩小，应停在整轨、起点回到 0
            var vMin = v0;
            for (int i = 0; i < 200; i++) vMin = WaveformMath.ZoomAt(vMin, -120, Width / 2, Width, SecPerBucket, Total);

            ok &= Math.Abs(vMin.Span - Total) < 1e-9 && Math.Abs(vMin.Start) < 1e-9;
            rows.Add($"缩小到底：跨度 {vMin.Span:0.###}（期望整轨 {Total}），起点 {vMin.Start:0.###}");

            // 可逆：进出各 5 格回到原处（全程没被夹住）
            var vRound = v0;
            for (int i = 0; i < 5; i++) vRound = WaveformMath.ZoomAt(vRound, 120, anchorX, Width, SecPerBucket, Total);
            for (int i = 0; i < 5; i++) vRound = WaveformMath.ZoomAt(vRound, -120, anchorX, Width, SecPerBucket, Total);

            bool back = Math.Abs(vRound.Span - v0.Span) < 1e-6 && Math.Abs(vRound.Start - v0.Start) < 1e-6;
            ok &= back;
            rows.Add($"进 5 格再出 5 格：{vRound.Start:0.####}/{vRound.Span:0.####}（原始 {v0.Start}/{v0.Span}）");

            return new VizSelfTest.Result(Title, ok, string.Join("；", rows));
        }
        catch (Exception ex)
        {
            return new VizSelfTest.Result(Title, false, ex.GetType().Name + "：" + ex.Message);
        }
    }

    /// <summary>缩放后的像素→桶归并：**任何缩放下尖峰都存活**（这是"缩放到哪都能看见"的保证）。</summary>
    private static VizSelfTest.Result CheckBucketSpan()
    {
        const string Title = "波形像素归并（尖峰在任何宽度下都存活）";

        try
        {
            const int Buckets = 4096, SpikeBucket = 1234;
            var min = new sbyte[Buckets];
            var max = new sbyte[Buckets];
            max[SpikeBucket] = 120;

            const double SecPerBucket = 256.0 / 44100;
            double total = Buckets * SecPerBucket;

            // 尖峰所在的时间。⚠️ 只有**视图包含它**时才有资格要求"看得见" ——
            // 一开始我漏了这个前提，于是把"没在视野里"也算成丢失，报了 3174 组假失败。
            double spikeTime = SpikeBucket * SecPerBucket;

            var bad = new List<string>();
            int checkedCases = 0;

            // 从整轨一直扫到"一像素一桶"的极限
            for (int width = 40; width <= 1200; width += 40)
            {
                double minSpan = WaveformMath.MinSpan(width, SecPerBucket, total);

                for (double span = total; span >= minSpan - 1e-9; span /= 1.3)
                {
                    // 让尖峰出现在各种横向位置
                    for (double start = 0; start + span <= total + 1e-9; start += span / 3)
                    {
                        var v = new ViewRange(start, span);

                        // 不在视野里 ⇒ 这一组不适用，跳过
                        if (spikeTime < v.Start || spikeTime >= v.End) continue;

                        checkedCases++;
                        bool found = false;

                        for (int px = 0; px < width; px++)
                        {
                            var (f, t) = WaveformMath.BucketSpan(v, px, px + 1, width, SecPerBucket, Buckets);
                            if (f <= SpikeBucket && SpikeBucket <= t) { found = true; break; }
                        }

                        if (!found) bad.Add($"{width}px/{span:0.##}s@{start:0.##}");
                    }
                }
            }

            bool ok = bad.Count == 0;
            return new VizSelfTest.Result(Title, ok,
                ok ? $"宽度 40~1200px × 各种跨度，尖峰落在视野内的 {checkedCases} 组全部可见（其余组因不在视野而跳过）"
                   : $"视野内的 {checkedCases} 组里有 {bad.Count} 组把它弄丢了，例：{string.Join("、", bad.Take(3))}");
        }
        catch (Exception ex)
        {
            return new VizSelfTest.Result(Title, false, ex.GetType().Name + "：" + ex.Message);
        }
    }

    // ---------------------------------------------------------------- 跟随滚动

    /// <summary>播放头跟随：三阶段 + **1× 自动不滚动**。</summary>
    private static VizSelfTest.Result CheckFollow()
    {
        const string Title = "播放头跟随（三阶段 / 1× 不滚动）";

        try
        {
            const double Total = 180, Span = 40;

            double a = WaveformMath.FollowStart(5, Span, Total);      // 未到中点（5 < 20）
            double b = WaveformMath.FollowStart(100, Span, Total);    // 滚动中
            double c = WaveformMath.FollowStart(178, Span, Total);    // 接近尾部
            double d = WaveformMath.FollowStart(90, Total, Total);    // 1×：整轨

            bool ok = true;
            var rows = new List<string>
            {
                $"未到中点 filePos=5 ⇒ 起点 {a:0.###}（期望 0，钉在曲首）",
                $"滚动中 filePos=100 ⇒ 起点 {b:0.###}（期望 {100 - Span / 2:0.###}，播放头在正中）",
                $"接近尾部 filePos=178 ⇒ 起点 {c:0.###}（期望 {Total - Span:0.###}，钉在曲尾）",
                $"1× filePos=90 ⇒ 起点 {d:0.###}（期望 0）",
            };

            ok &= Math.Abs(a) < 1e-9;
            ok &= Math.Abs(b - (100 - Span / 2)) < 1e-9;
            ok &= Math.Abs(c - (Total - Span)) < 1e-9;
            ok &= Math.Abs(d) < 1e-9;

            // 单调：位置前进时起点不许倒退
            double prev = -1;
            bool mono = true;
            for (double p = 0; p <= Total; p += 0.5)
            {
                double s = WaveformMath.FollowStart(p, Span, Total);
                if (s < prev - 1e-9) { mono = false; break; }
                prev = s;
            }
            ok &= mono;
            rows.Add($"全程单调 = {mono}");

            return new VizSelfTest.Result(Title, ok, string.Join("；", rows));
        }
        catch (Exception ex)
        {
            return new VizSelfTest.Result(Title, false, ex.GetType().Name + "：" + ex.Message);
        }
    }

    // ---------------------------------------------------------------- 文件位置

    /// <summary>
    /// 播放头的文件位置：intro 段直通；循环段两种模式**算出同一个文件位置**；
    /// 淡出段（`_srcFrame` 还在循环体里）仍落在循环段内。
    /// </summary>
    private static VizSelfTest.Result CheckFilePosition()
    {
        const string Title = "播放头文件位置（两模式一致 / 淡出段仍在循环内）";

        try
        {
            var intro = TimeSpan.FromSeconds(9);
            var loop = TimeSpan.FromSeconds(74);
            var total = TimeSpan.FromSeconds(83);

            // intro 段：两种模式给出的 ProgressPosition 都是"时间线开头 = 文件开头"
            double p1 = WaveformMath.FilePosition(TimeSpan.FromSeconds(4), TimeSpan.Zero, intro);

            // 循环段第 3 遍、这一遍走了 10 秒：
            //   无限循环：ProgressPosition = _srcFrame = 9 + 10（再取模）⇒ 19；LoopPosition = 10
            //   普通模式：ProgressPosition = _emitFrame = 9 + 2*74 + 10 ⇒ 157；LoopPosition 仍是 10
            double p2 = WaveformMath.FilePosition(TimeSpan.FromSeconds(19), TimeSpan.FromSeconds(10), intro);
            double p3 = WaveformMath.FilePosition(TimeSpan.FromSeconds(157), TimeSpan.FromSeconds(10), intro);

            // 淡出段：还在循环体里转，只是音量在降
            double p4 = WaveformMath.FilePosition(TimeSpan.FromSeconds(200), TimeSpan.FromSeconds(30), intro);

            bool ok = true;
            var rows = new List<string>
            {
                $"intro 段 progress=4 ⇒ {p1:0.###} 秒（期望 4）",
                $"无限循环第 3 遍 ⇒ {p2:0.###} 秒（期望 19 = intro 9 + 本遍 10）",
                $"普通模式第 3 遍 ⇒ {p3:0.###} 秒（期望同为 19）",
                $"淡出段 ⇒ {p4:0.###} 秒（期望落在循环段 9~83 内）",
            };

            ok &= Math.Abs(p1 - 4) < 1e-9;
            ok &= Math.Abs(p2 - 19) < 1e-9;
            ok &= Math.Abs(p3 - 19) < 1e-9;
            ok &= p4 >= intro.TotalSeconds && p4 <= total.TotalSeconds;

            return new VizSelfTest.Result(Title, ok, string.Join("；", rows));
        }
        catch (Exception ex)
        {
            return new VizSelfTest.Result(Title, false, ex.GetType().Name + "：" + ex.Message);
        }
    }

    /// <summary>循环入口标记：有真循环才画；一次性曲目 / 无循环段都不画。</summary>
    private static VizSelfTest.Result CheckLoopMark()
    {
        const string Title = "循环入口标记判定（一次性 / 无循环段 都不画）";

        try
        {
            bool normal = WaveformMath.ShouldShowLoopMark(false, 100, 1000);   // 普通曲：画
            bool oneShot = WaveformMath.ShouldShowLoopMark(true, 100, 1000);   // 黄昏作 ED：不画
            bool noIntro = WaveformMath.ShouldShowLoopMark(false, 0, 1000);    // 无 intro：不画
            bool fullIntro = WaveformMath.ShouldShowLoopMark(false, 1000, 1000); // intro == total：不画

            bool ok = normal && !oneShot && !noIntro && !fullIntro;

            return new VizSelfTest.Result(Title, ok,
                $"普通 {normal}（期望 True）；一次性 {oneShot}（期望 False）；" +
                $"intro=0 {noIntro}（期望 False）；intro=total {fullIntro}（期望 False）");
        }
        catch (Exception ex)
        {
            return new VizSelfTest.Result(Title, false, ex.GetType().Name + "：" + ex.Message);
        }
    }

    // ---------------------------------------------------------------- 键与选源

    /// <summary>缓存键：与预读缓存**同一套格式**，且主版/副版必须分开。</summary>
    private static VizSelfTest.Result CheckCacheKey()
    {
        const string Title = "波形缓存键（与 PreloadCache 同格式 / alt 分开）";

        try
        {
            var game = new GameDef { Id = "th13" };
            var main = new TrackDef { No = 7 };
            var withAlt = new TrackDef { No = 7, Alt = new TrackDef { No = 7 } };

            string k1 = WaveformCache.KeyOf(game, withAlt, false);
            string k2 = WaveformCache.KeyOf(game, withAlt, true);
            string pre = PreloadCache.KeyOf(game, withAlt, false);

            bool ok = k1 == pre && k1 != k2 && k1.EndsWith(":main") && k2.EndsWith(":alt");

            return new VizSelfTest.Result(Title, ok,
                $"主版 {k1}；副版 {k2}；与 PreloadCache 一致 = {k1 == pre}");
        }
        catch (Exception ex)
        {
            return new VizSelfTest.Result(Title, false, ex.GetType().Name + "：" + ex.Message);
        }
    }

    /// <summary>选源规则与引擎一致：要副版、且真有副版时取副版，否则取主版。</summary>
    private static VizSelfTest.Result CheckPicker()
    {
        const string Title = "扫描选源（与引擎规则逐例一致）";

        try
        {
            var noAlt = new TrackDef { No = 1 };
            var withAlt = new TrackDef { No = 1, Alt = new TrackDef { No = 1 } };

            bool a = ReferenceEquals(TrackScanner.Pick(withAlt, true), withAlt.Alt);   // 要副版且存在 ⇒ 副版
            bool b = ReferenceEquals(TrackScanner.Pick(withAlt, false), withAlt);      // 不要副版 ⇒ 主版
            bool c = ReferenceEquals(TrackScanner.Pick(noAlt, true), noAlt);           // 没有副版 ⇒ 回退主版

            bool ok = a && b && c;

            return new VizSelfTest.Result(Title, ok,
                $"有副版+要副版 ⇒ 副版 {a}；有副版+不要 ⇒ 主版 {b}；无副版+要副版 ⇒ 回退主版 {c}");
        }
        catch (Exception ex)
        {
            return new VizSelfTest.Result(Title, false, ex.GetType().Name + "：" + ex.Message);
        }
    }

    /// <summary>
    /// 取消与失败：
    /// · 已取消的 token ⇒ <see cref="TrackScanner.Scan"/> 抛出（循环读那一步会查）；
    /// · 失败（这里用一个没配路径的作品）⇒ <see cref="TrackScanner.ScanAsync"/> 静默返回 null，不抛。
    /// </summary>
    private static VizSelfTest.Result CheckCancel()
    {
        const string Title = "扫描取消与失败（取消抛 / 失败静默）";

        try
        {
            var src = new FakeSource(MakePcm(256 * 4, spikeAt: 100, spikeValue: 1000), 44100, 2);

            bool threw = false;
            try
            {
                TrackScanner.Scan(src, new CancellationToken(canceled: true));
            }
            catch (OperationCanceledException)
            {
                threw = true;
            }

            // 没配路径的作品：AudioSourceFactory 会抛 ⇒ ScanAsync 必须吞掉并返回 null（不弹窗）
            var peaks = TrackScanner.ScanAsync(new GameDef { Id = "th99-nonexistent" },
                                               new TrackDef { No = 1 }, false).GetAwaiter().GetResult();

            bool ok = threw && peaks is null;

            return new VizSelfTest.Result(Title, ok,
                $"已取消 token ⇒ 抛 OperationCanceledException = {threw}；没配路径 ⇒ 返回 null = {peaks is null}");
        }
        catch (Exception ex)
        {
            return new VizSelfTest.Result(Title, false, ex.GetType().Name + "：" + ex.Message);
        }
    }

    // ---------------------------------------------------------------- 渲染结果

    /// <summary>
    /// 离屏出像素验四件事：波形真画出来了 / 播放头画在对的列 / 挪播放头**只动那一列** /
    /// 循环入口标记画在对的列。
    ///
    /// ⚠️ 四件都只用**相对比较**（跟"没有波形时"比、跟自己挪一格后比）——
    /// 因为自检跑的时候主题资源可能还没合并，绝对颜色不可依赖（会走兜底色）。
    /// </summary>
    private static VizSelfTest.Result CheckRender()
    {
        const string Title = "波形渲染结果（波形 / 播放头列 / 只动该列 / 循环标记列）";

        try
        {
            const int W = 400, H = 96;
            const double Total = 100;                 // 秒
            const double Intro = 10;
            const int HeadSec = 50, HeadMovedSec = 80;

            double spb = WaveformPeaks.FramesPerBucket / 44100.0;
            int buckets = (int)(Total / spb);

            // 幅度取 ±40（约半高的 63%）：既看得出是波形，又给顶部留出空间让循环标记露出来
            var min = new sbyte[buckets];
            var max = new sbyte[buckets];
            for (int i = 0; i < buckets; i++) { min[i] = -40; max[i] = 40; }

            var peaks = new WaveformPeaks(min, max, Total, spb, Intro);

            // ⚠️ 两个位置量必须**自洽**：循环段内 progress = intro + loopPos。
            // 第一版给了 progress=50 / loopPos=0 —— 公式正确地把它读成"循环体内偏移 0"，
            // 于是播放头落在 intro 处，渲染断言报"挪了 0 像素"（其实是我喂的数不自洽）。
            var panel = new WaveformPanel();
            panel.SetTrack(peaks, isTfOneShot: false);
            panel.SetEnginePosition(TimeSpan.FromSeconds(HeadSec), TimeSpan.FromSeconds(HeadSec - Intro));

            var withWave = Render(panel, W, H);

            // 挪播放头 → 只该动那一列（±1 列余量）
            panel.SetEnginePosition(TimeSpan.FromSeconds(HeadMovedSec), TimeSpan.FromSeconds(HeadMovedSec - Intro));
            var moved = Render(panel, W, H);
            int movedDiff = CountDiff(withWave, moved);

            // 没有波形（对照）
            panel.SetTrack(null, isTfOneShot: false);
            var blank = Render(panel, W, H);
            int blankDiff = CountDiff(withWave, blank);

            int headX = (int)(HeadSec / Total * W);
            int otherX = headX / 2;

            long headBright = ColumnBrightness(withWave, W, headX, H);
            long otherBright = ColumnBrightness(withWave, W, otherX, H);

            // 循环标记：**必须在波形之上**。
            // ⚠️ 所以专挑波形**占满的中间那一行**来验 —— 早期标记画在波形下面，满幅时整条被盖住；
            // 只去查波形上方的留白是**验不出来**的（那里本来就没东西挡）。
            // ⚠️ 不判"是不是红的"：自检跑的时候主题可能还没合并（会走兜底色），绝对颜色不可依赖。
            // 改成跟**旁边一列**比：标记只占那一两列，旁边只有均匀的波形 ⇒ 两者不同就说明线在上面。
            int markX = (int)(Intro / Total * W);
            int markRef = markX - 4;
            int midY = H / 2;

            bool markOverWave = Px(withWave, W, markX, midY) != Px(withWave, W, markRef, midY) ||
                                Px(withWave, W, markX - 1, midY) != Px(withWave, W, markRef, midY);

            // 前提校验：中间那一行**确实被波形占着**，否则上面那条断言是空过的
            bool waveAtMid = Px(withWave, W, markRef, midY) != Px(blank, W, markRef, midY);

            // 而且标记列得**真的偏红**（R > B）。
            // ⚠️ 只判"与旁边不同"是不够的：没吸附像素网格时，红色会和波形蓝各半混成灰紫 ——
            // 同样"不同"，但**看着是灰的**，实机就是这么反馈的。
            uint markPx = Px(withWave, W, markX, midY);
            bool markRed = (markPx >> 16 & 0xFF) > (markPx & 0xFF);   // R > B

            bool ok = true;
            var rows = new List<string>
            {
                $"波形覆盖（与无波形比）：{blankDiff}/{W * H} 像素不同（门槛 {W * H / 5}）",
                $"挪播放头后变化：{movedDiff} 像素（期望 ≤ {5 * H}，即只动那两列，不是整块重画）",
                $"播放头列亮度 {headBright} vs 普通列 {otherBright}（期望更亮）",
                $"中间行有波形 = {waveAtMid}（前提）；标记在中间行可见 = {markOverWave}（画在波形之上）",
                $"标记列像素 {markPx:X8}（R{(markPx >> 16 & 0xFF)} vs B{markPx & 0xFF}）" +
                $" vs 对照列 {Px(withWave, W, markRef, midY):X8}；偏红 = {markRed}（期望 True，未被混灰）",
            };

            ok &= blankDiff > W * H / 5;
            ok &= movedDiff <= 5 * H;
            ok &= headBright > otherBright;
            ok &= waveAtMid && markOverWave && markRed;

            return new VizSelfTest.Result(Title, ok, string.Join("；", rows));
        }
        catch (Exception ex)
        {
            return new VizSelfTest.Result(Title, false, ex.GetType().Name + "：" + ex.Message);
        }
    }

    /// <summary>离屏把面板渲成像素（Pbgra32：每像素 B、G、R、A）。</summary>
    private static byte[] Render(System.Windows.FrameworkElement el, int w, int h)
    {
        Arrange(el, w, h);

        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
            w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bmp.Render(el);

        var px = new byte[w * h * 4];
        bmp.CopyPixels(px, w * 4, 0);
        return px;
    }

    /// <summary>两幅图有多少像素不同（忽略 alpha）。</summary>
    private static int CountDiff(byte[] a, byte[] b)
    {
        int n = 0;
        for (int i = 0; i + 3 < a.Length; i += 4)
            if (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2]) n++;

        return n;
    }

    /// <summary>某一列的亮度总和（用来判"播放头那一列更亮"）。</summary>
    private static long ColumnBrightness(byte[] px, int w, int x, int h)
    {
        long sum = 0;
        for (int y = 0; y < h; y++)
        {
            int o = (y * w + x) * 4;
            sum += px[o] + px[o + 1] + px[o + 2];
        }

        return sum;
    }

    /// <summary>某一行上某个像素是否"偏红"（标记用的是强调色；波形是蓝的 ⇒ 红通道明显高过蓝通道）。</summary>
    private static bool IsReddish(byte[] px, int w, int x, int y)
    {
        int o = (y * w + x) * 4;
        return px[o + 2] > px[o] + 40;      // R 明显大于 B
    }

    /// <summary>取一个像素（打包成 ARGB 便于打印比较）。</summary>
    private static uint Px(byte[] px, int w, int x, int y)
    {
        int o = (y * w + x) * 4;
        return (uint)(px[o + 3] << 24 | px[o + 2] << 16 | px[o + 1] << 8 | px[o]);
    }

    // ---------------------------------------------------------------- 面板缩放与跟随

    /// <summary>
    /// **面板**上的滚轮缩放与播放头跟随（验接线：算术本身已在缩放/跟随两条里单独验过）。
    ///
    /// ⚠️ 直接调 <c>Zoom(...)</c>，不走鼠标事件 —— 离屏布局里 <c>GetPosition</c> 拿不到可靠坐标，
    /// 而这一条要验的是"面板有没有把两个纯函数接对"，不是 WPF 的事件路由。
    /// </summary>
    private static VizSelfTest.Result CheckPanelZoom()
    {
        const string Title = "面板缩放与跟随（锚点 / 播放中跟随 / 未缩放不滚）";

        try
        {
            const int W = 400, H = 96;
            const double Total = 100, Intro = 10, Head = 50;
            double spb = WaveformPeaks.FramesPerBucket / 44100.0;

            var peaks = FlatPeaks(Total, spb, Intro);

            // ① 放大一格：跨度必须变小
            var panel = new WaveformPanel();
            panel.SetTrack(peaks, isTfOneShot: false);
            Arrange(panel, W, H);
            panel.SetEnginePosition(TimeSpan.FromSeconds(Head), TimeSpan.FromSeconds(Head - Intro));

            double spanBefore = panel.View.Span;
            panel.Zoom(120, W / 2);
            double spanAfter = panel.View.Span;
            bool shrunk = spanAfter < spanBefore && Math.Abs(spanAfter - spanBefore / WaveformMath.ZoomStep) < 1e-9;

            // ② 下一拍位置更新 ⇒ 跟随把播放头对到可视区正中
            panel.SetEnginePosition(TimeSpan.FromSeconds(Head + 5), TimeSpan.FromSeconds(Head + 5 - Intro));
            var v = panel.View;
            double headX = (Head + 5 - v.Start) / v.Span * W;
            bool centered = Math.Abs(headX - W / 2.0) < 1.0;

            // ③ 未缩放（整轨）时不该滚：起点恒 0
            var whole = new WaveformPanel();
            whole.SetTrack(peaks, isTfOneShot: false);
            Arrange(whole, W, H);
            whole.SetEnginePosition(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20 - Intro));
            whole.SetEnginePosition(TimeSpan.FromSeconds(80), TimeSpan.FromSeconds(80 - Intro));

            bool noScroll = Math.Abs(whole.View.Start) < 1e-9 &&
                            Math.Abs(whole.View.Span - Total) < 1e-9;

            bool ok = shrunk && centered && noScroll;

            return new VizSelfTest.Result(Title, ok,
                $"放大一格：跨度 {spanBefore:0.###} → {spanAfter:0.###}（期望 {spanBefore / WaveformMath.ZoomStep:0.###}）；" +
                $"下一拍跟随：播放头落在 x={headX:0.#}（期望 {W / 2}）；" +
                $"整轨时起点 {whole.View.Start:0.###}（期望 0，不滚）");
        }
        catch (Exception ex)
        {
            return new VizSelfTest.Result(Title, false, ex.GetType().Name + "：" + ex.Message);
        }
    }

    /// <summary>造一份幅度恒定 ±40 的峰值（长约 96px 面板的一半高，便于用像素判读）。</summary>
    private static WaveformPeaks FlatPeaks(double totalSeconds, double secondsPerBucket, double introSeconds)
    {
        int buckets = Math.Max(1, (int)(totalSeconds / secondsPerBucket));
        var min = new sbyte[buckets];
        var max = new sbyte[buckets];

        for (int i = 0; i < buckets; i++) { min[i] = -40; max[i] = 40; }

        return new WaveformPeaks(min, max, totalSeconds, secondsPerBucket, introSeconds);
    }

    /// <summary>离屏排一次版（<c>ActualWidth/Height</c> 得有值，缩放与绘制才认）。</summary>
    private static void Arrange(System.Windows.FrameworkElement el, int w, int h)
    {
        el.Measure(new System.Windows.Size(w, h));
        el.Arrange(new System.Windows.Rect(0, 0, w, h));
    }

    // ---------------------------------------------------------------- 真文件抽查

    /// <summary>
    /// 拿一首**真实曲子**扫一遍，看包络是否**上下对称**。
    ///
    /// ⚠️ 为什么要查对称：扫描取的是 PCM16 的**高 8 位**，正常音频的正负峰量级相当。
    /// 如果一面贴着 ±127 而另一面随随便便，那基本就是字节序 / 符号读错了 ——
    /// **这种错在合成信号上测不出来**（我造的假 PCM 是自己写的字节序，写错也一致）。
    /// 实机第一张截图里波形就长得"上沿平直、下沿犬牙"，所以补上这条。
    ///
    /// 没配任何游戏路径就**跳过** —— 自检不该依赖用户环境。
    /// </summary>
    private static VizSelfTest.Result CheckRealFile()
    {
        const string Title = "真文件抽查（配了路径才跑：包络上下对称）";

        try
        {
            // 最多抽查 3 部已配路径的作品 —— 多抽几部是为了**覆盖不同容器**
            // （整数作的 zwav/wav、黄昏作的 OGG、新典的自定义 Opus 各有各的解码路径，
            // 只测一部的话，另外两条路上的字节序错就漏过去了）。
            var games = TrackIndex.Games
                .Where(g => AppSettings.Current.GetPath(g.Id) is not null && g.Tracks.Count > 0)
                .Take(10)
                .ToList();

            if (games.Count == 0)
                return new VizSelfTest.Result(Title, true, "未配置任何游戏路径 ⇒ 跳过（不算通过也不算失败）");

            bool ok = true;
            var rows = new List<string>();
            int scanned = 0;

            foreach (var game in games)
            {
                var track = game.Tracks[0];
                var peaks = TrackScanner.ScanAsync(game, track, false).GetAwaiter().GetResult();

                if (peaks is null || peaks.BucketCount == 0)
                {
                    rows.Add($"{game.Id} #{track.No}：扫不出（跳过）");
                    continue;
                }

                long sumUp = 0, sumDown = 0;
                int pegged = 0;
                int silent = 0;         // min == max == 0 的桶（数字静音）
                int run = 0, maxRun = 0;

                for (int i = 0; i < peaks.BucketCount; i++)
                {
                    sumUp += Math.Abs((int)peaks.Max[i]);
                    sumDown += Math.Abs((int)peaks.Min[i]);

                    if (peaks.Max[i] >= 120 && peaks.Min[i] <= -120) pegged++;

                    // 「整段归零」在实机上表现为波形上一块干净的空白。
                    // ⚠️ 记下它并报出来：解码出错（少喂一段）和"原曲本来就静音"都会长这样，
                    // 但前者是 bug、后者是事实 —— 有了数字才能去问人，而不是自己猜。
                    if (peaks.Min[i] == 0 && peaks.Max[i] == 0)
                    {
                        silent++;
                        if (++run > maxRun) maxRun = run;
                    }
                    else run = 0;
                }

                double up = (double)sumUp / peaks.BucketCount;
                double down = (double)sumDown / peaks.BucketCount;
                double ratio = Math.Max(up, down) / Math.Max(1e-6, Math.Min(up, down));

                // 判据放宽：两边平均幅度相差 4 倍以内都算正常（真实音乐有不对称，但不会是数量级差）
                ok &= ratio <= 4.0;
                scanned++;

                rows.Add($"{game.Id} #{track.No}（{peaks.TotalSeconds:0.0}s/{peaks.BucketCount}桶）" +
                         $" 上 {up:0.0} vs 下 {down:0.0}（比值 {ratio:0.00}）贴死桶 {pegged}" +
                         $" 静音桶 {silent}（最长连续 {maxRun} = {maxRun * peaks.SecondsPerBucket:0.00}s）");
            }

            if (scanned == 0)
                return new VizSelfTest.Result(Title, true, "配了路径但都扫不出 ⇒ 跳过");

            return new VizSelfTest.Result(Title, ok, string.Join("；", rows));
        }
        catch (Exception ex)
        {
            return new VizSelfTest.Result(Title, false, ex.GetType().Name + "：" + ex.Message);
        }
    }

    // ---------------------------------------------------------------- 测试替身

    /// <summary>
    /// 假音源：一块内存里的 PCM16。用来离屏验扫描 —— 真源要先配游戏路径、还依赖具体容器格式。
    /// </summary>
    private sealed class FakeSource : IAudioSource
    {
        private readonly byte[] _pcm;
        private int _pos;

        public FakeSource(byte[] pcm, int sampleRate, int channels)
        {
            _pcm = pcm;
            Format = new NAudio.Wave.WaveFormat(sampleRate, 16, channels);
        }

        public NAudio.Wave.WaveFormat Format { get; }

        public long IntroBytes => _pcm.Length / 2;
        public long TotalBytes => _pcm.Length;
        public long PositionBytes => _pos;

        public void Seek(long offsetFromTrackStart) =>
            _pos = (int)Math.Clamp(offsetFromTrackStart, 0, _pcm.Length);

        public int Read(byte[] buffer, int offset, int count)
        {
            int n = Math.Min(count, _pcm.Length - _pos);
            if (n <= 0) return 0;

            Array.Copy(_pcm, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }

        public void Dispose() { }
    }

    /// <summary>
    /// 造一段 PCM16 LE：整体是一个**低幅斜坡**（保证每个桶都有非零值、尾桶可验），
    /// 在 <paramref name="spikeAt"/> 那一帧塞一个尖峰（验分桶落位）。
    ///
    /// ⚠️ 斜坡的步长必须够大：扫描取的是 PCM16 的**高 8 位**（`sample >> 8`），
    /// 步长 10 那种小信号会被整段截成 0 —— 第一版就栽在这儿（尾桶断言报 0）。
    /// 这里取 400（高 8 位约 0~29，仍低于"邻桶 &lt;40"的上限），尖峰取 30000（高 8 位 117）。
    /// </summary>
    private static byte[] MakePcm(int frames, int spikeAt, short spikeValue, int channels = 2)
    {
        var pcm = new byte[frames * channels * 2];

        for (int f = 0; f < frames; f++)
        {
            short v = (short)(f % 20 * 400);         // 0..7600 ⇒ 高 8 位 0..29
            if (f == spikeAt) v = spikeValue;

            for (int c = 0; c < channels; c++)
            {
                int off = (f * channels + c) * 2;
                pcm[off] = (byte)(v & 0xFF);
                pcm[off + 1] = (byte)(v >> 8);
            }
        }

        return pcm;
    }
}
