namespace ThbgmPlayer.Viz;

/// <summary>
/// 放置判定与窗口缩放的用例表（方案 §3.6 判定表 + §3.7 WM_SIZING）。
///
/// 为什么单独一个文件：这两件事都是**纯函数**，没有窗口也能验 —— 而隔离期恰恰没有主体
/// （`FreeHost.IsAttached` 恒 false），不抽出来就只能在接入期才能第一次跑到，等于把风险全压到 M6。
///
/// 由 <see cref="VizSelfTest"/> 统一调用，产出的是同一张平表里的条目。
/// </summary>
internal static class VizPlacementSelfTest
{
    public static List<VizSelfTest.Result> Run() => new()
    {
        CheckPlacement(),
        CheckPlacementAction(),
        CheckSizing(),
    };

    // ------------------------------------------------------------------ §3.6 放置判定

    /// <summary>自检用的假宿主：几何随便摆，不需要真窗口（方案 §九 M5 里叫 `SelfTestHost`）。</summary>
    private sealed class SelfTestHost : IVizHost
    {
        /// <summary>可写：同一份宿主要在「自由 / 贴附」两个分支之间来回切，不用重建。</summary>
        public bool IsAttached { get; set; }

        public VizHostState State { get; set; } = VizHostState.Normal;

        public VizHostBounds Bounds { get; set; }

        public VizWorkArea WorkArea { get; set; }

        public event Action? Changed;

        /// <summary>主动通知。这里主要为了让 <see cref="Changed"/> 真的是"被用到的"（也顺带验一遍契约）。</summary>
        public void Notify() => Changed?.Invoke();
    }

    /// <summary>
    /// 逐行过方案 §3.6 的判定表。宿主与工作区的数值是刻意挑的，
    /// 让每一行都落在不同的分支上（空间充足 / 只能给部分 / 连最小宽度都不够）。
    /// </summary>
    private static VizSelfTest.Result CheckPlacement()
    {
        const string Title = "放置判定（§3.6 判定表：自由 / 贴右 / 空间不足 / 内嵌 / 最小化）";

        try
        {
            // 宿主：右边缘 1300（100+1200），工作区右 1920 → 右侧空间 620
            var host = new SelfTestHost
            {
                Bounds = new VizHostBounds(100, 80, 1200, 800),
                WorkArea = new VizWorkArea(0, 0, 1920, 1040),
            };

            var rows = new List<string>();
            bool ok = true;

            bool Row(string label, VizPlacement got, VizPlacement want)
            {
                bool good = got == want;   // record struct：逐字段比较，double 也按位比
                ok &= good;
                rows.Add(good ? $"{label}={Show(got)}" : $"{label} ✗期望 {Show(want)} 实际 {Show(got)}");
                return good;
            }

            // 行 1：自由模式 —— 几何原样返回，窗口自己保管
            host.IsAttached = false;
            Row("自由", VizPlacementLogic.Compute(host, 420, true),
                new VizPlacement(VizPlacementMode.Free, 100, 80, 1200, 800, true));

            host.IsAttached = true;

            // 行 2：Normal、右侧空间 620 ≥ 期望 420 → 用期望宽度，等高、顶齐
            Row("空间足", VizPlacementLogic.Compute(host, 420, true),
                new VizPlacement(VizPlacementMode.AttachedNormal, 1300, 80, 420, 800, true));

            // 行 3：右侧只剩 400 → MinWidth(360) ≤ 400 < 420 → 宽度用满 400
            host.WorkArea = new VizWorkArea(0, 0, 1700, 1040);
            Row("空间不足", VizPlacementLogic.Compute(host, 420, true),
                new VizPlacement(VizPlacementMode.AttachedNormal, 1300, 80, 400, 800, true));

            // 行 4：右侧只剩 200 < MinWidth → **宁可压住宿主**，Left 回夹到 1500-360
            host.WorkArea = new VizWorkArea(0, 0, 1500, 1040);
            Row("放不下最小宽", VizPlacementLogic.Compute(host, 420, true),
                new VizPlacement(VizPlacementMode.AttachedNormal, 1140, 80, 360, 800, true));

            // 行 5：最大化 + 内嵌 → 附件窗口隐藏
            host.State = VizHostState.Maximized;
            host.Bounds = new VizHostBounds(0, 0, 1920, 1040);
            Row("最大化内嵌", VizPlacementLogic.Compute(host, 420, true),
                new VizPlacement(VizPlacementMode.AttachedEmbedded, 0, 0, 1920, 1040, false));

            // 行 6：最大化但不内嵌 → 退化为贴右侧（空间为 0 → 回夹）
            host.WorkArea = new VizWorkArea(0, 0, 1920, 1040);
            Row("最大化不内嵌", VizPlacementLogic.Compute(host, 420, false),
                new VizPlacement(VizPlacementMode.AttachedNormal, 1560, 0, 360, 1040, true));

            // 行 7：最小化 → 不显示
            host.State = VizHostState.Minimized;
            Row("最小化", VizPlacementLogic.Compute(host, 420, true),
                new VizPlacement(VizPlacementMode.AttachedNormal, 0, 0, 1920, 1040, false));

            // 行 8（判定表外，但必须锁住）：期望宽度小于 MinWidth → 按 MinWidth 办
            host.State = VizHostState.Normal;
            host.Bounds = new VizHostBounds(100, 80, 1200, 800);
            host.WorkArea = new VizWorkArea(0, 0, 1920, 1040);
            Row("期望宽过小", VizPlacementLogic.Compute(host, 100, true),
                new VizPlacement(VizPlacementMode.AttachedNormal, 1300, 80, 360, 800, true));

            // 顺带验 Changed 事件真的接得上（M5 靠它重算放置）
            bool fired = false;
            host.Changed += () => fired = true;
            host.Notify();
            if (!fired) { ok = false; rows.Add("✗ Changed 事件没触发"); }

            return new VizSelfTest.Result(Title, ok,
                $"{rows.Count - 1} 行判定 + Changed 事件｜" + string.Join("；", rows));
        }
        catch (Exception ex)
        {
            return new VizSelfTest.Result(Title, false, ex.GetType().Name + "：" + ex.Message);
        }
    }

    // ------------------------------------------------------------------ M5：该对窗口做什么

    /// <summary>
    /// 「该对窗口做什么」（M5）。方案 §九 M5 的验收就是「模拟最大化 → 窗口按预期 <c>Hide()</c>」。
    ///
    /// ⚠️ 验的是**动作**而不是「窗口最后可见吗」：自检起不了窗口
    /// （<c>ShutdownMode=OnLastWindowClose</c>，Show 一个就把进程带下去），
    /// 而未 Show 的窗口 <c>IsVisible</c> 恒为 false —— 直接断言「它应该隐藏」会**永远通过**，等于没验。
    ///
    /// 兜底那两个分支也不是假想：**未 Show 的 WPF 窗口 <c>Left/Top</c> 就是 <c>NaN</c>**，
    /// 宿主刚建好就被读一次是很正常的时序；高度为 0 则出现在宿主尚未布局完时。
    /// </summary>
    private static VizSelfTest.Result CheckPlacementAction()
    {
        const string Title = "放置动作（M5：贴附 / 内嵌 / 最小化 / 兜底 → 显示还是隐藏）";

        try
        {
            var rows = new List<string>();
            bool ok = true;

            bool Row(string label, VizWindowAction got, VizWindowAction want)
            {
                bool good = got == want;
                ok &= good;
                rows.Add(good ? $"{label}={Show(got)}" : $"{label} ✗期望 {Show(want)} 实际 {Show(got)}");
                return good;
            }

            var host = new SelfTestHost
            {
                IsAttached = true,
                Bounds = new VizHostBounds(100, 80, 1200, 800),
                WorkArea = new VizWorkArea(0, 0, 1920, 1040),
            };

            // 自由模式：位置与显隐都不插手，只保证没被贴上
            host.IsAttached = false;
            Row("自由", VizPlacementLogic.Decide(host, 420, true),
                new VizWindowAction(VizPlacementMode.Free, false, false, false, 0, 0, 0, 0, false, false));

            host.IsAttached = true;

            // 贴右侧：写几何 + 该显示（宿主从最大化还原回来时，窗口得回来）
            Row("贴右侧", VizPlacementLogic.Decide(host, 420, true),
                new VizWindowAction(VizPlacementMode.AttachedNormal,
                                    true, false, true, 1300, 80, 420, 800, true, false));

            // 最大化 + 内嵌 → **隐藏**（方案 §九 M5 那条验收）
            host.State = VizHostState.Maximized;
            host.Bounds = new VizHostBounds(0, 0, 1920, 1040);
            Row("最大化内嵌", VizPlacementLogic.Decide(host, 420, true),
                new VizWindowAction(VizPlacementMode.AttachedEmbedded,
                                    false, true, false, 0, 0, 0, 0, true, false));

            // 最大化但不内嵌 → 退化贴右侧：仍要显示 + 写几何
            Row("最大化不内嵌", VizPlacementLogic.Decide(host, 420, false),
                new VizWindowAction(VizPlacementMode.AttachedNormal,
                                    true, false, true, 1560, 0, 360, 1040, true, false));

            // 最小化 → 隐藏
            host.State = VizHostState.Minimized;
            Row("最小化", VizPlacementLogic.Decide(host, 420, true),
                new VizWindowAction(VizPlacementMode.AttachedNormal,
                                    false, true, false, 0, 0, 0, 0, true, false));

            // ---- 兜底两种：几何不可用时「什么都不动、保持可见」 ----
            host.State = VizHostState.Normal;

            host.Bounds = new VizHostBounds(double.NaN, double.NaN, double.NaN, double.NaN);
            Row("兜底·NaN", VizPlacementLogic.Decide(host, 420, true),
                new VizWindowAction(VizPlacementMode.AttachedNormal,
                                    true, false, false, 0, 0, 0, 0, true, true));

            host.Bounds = new VizHostBounds(0, 0, 1200, 0);
            Row("兜底·退化尺寸", VizPlacementLogic.Decide(host, 420, true),
                new VizWindowAction(VizPlacementMode.AttachedNormal,
                                    true, false, false, 0, 0, 0, 0, true, true));

            // ---- 几何所有权：谁能回写设置 ----
            // 贴附几何属于宿主；未 Show 过的几何是"期望值"不是"实际值"。两者都不该固化。
            bool persist =
                VizPlacementLogic.ShouldPersistGeometry(true, false) &&
                !VizPlacementLogic.ShouldPersistGeometry(true, true) &&
                !VizPlacementLogic.ShouldPersistGeometry(false, false) &&
                !VizPlacementLogic.ShouldPersistGeometry(false, true);
            ok &= persist;
            rows.Add(persist
                ? "几何回写：仅「自由 + Show 过」才写"
                : "✗ 几何回写判据不对（贴附或未 Show 过的几何不该固化）");

            // ---- 兜底那条**假设本身**：未 Show 的 WPF 窗口 Left/Top 真的是 NaN 吗 ----
            // 整条兜底分支都建立在这上面，所以不能只凭"文档里这么说"。
            // ⚠️ 只读 Left/Top/Width/Height，**不碰 WorkArea**（那个会去拿 HWND，是副作用）；
            //    「拿到 NaN 之后怎么反应」由上面那两行兜底用例负责，两者分工明确。
            var unsized = new System.Windows.Window();
            using (var realHost = new WindowHost(unsized))
            {
                var b = realHost.Bounds;
                bool nan = double.IsNaN(b.Left) && double.IsNaN(b.Top)
                        && double.IsNaN(b.Width) && double.IsNaN(b.Height);
                ok &= nan;
                rows.Add(nan
                    ? "未 Show 的真实窗口：Left/Top/Width/Height 确为 NaN（兜底的前提成立）"
                    : $"✗ 兜底前提不成立：({b.Left},{b.Top},{b.Width},{b.Height})");
            }

            return new VizSelfTest.Result(Title, ok, string.Join("；", rows));
        }
        catch (Exception ex)
        {
            return new VizSelfTest.Result(Title, false, ex.GetType().Name + "：" + ex.Message);
        }
    }

    // ------------------------------------------------------------------ §3.7 窗口缩放

    /// <summary>
    /// <c>WM_SIZING</c> 的裁决逻辑。**实机行为隔离期看不到**（<c>Attached</c> 恒 false），
    /// 所以这里是它唯一的验证面 —— 方案 §九 M3 的验收要求也正是「Attached 分支可被自检覆盖」。
    /// </summary>
    private static VizSelfTest.Result CheckSizing()
    {
        const string Title = "窗口缩放（贴附=只拖右边缘+锁高度 / 自由=放行；DPI 折算；空窗口不炸）";

        try
        {
            var rows = new List<string>();
            bool ok = true;

            const int MinPx = 360;
            var cur = (Left: 1000, Top: 100, Right: 1600, Bottom: 900);

            bool Row(string label, (int Left, int Top, int Right, int Bottom) got,
                     (int Left, int Top, int Right, int Bottom) want)
            {
                bool good = got == want;
                ok &= good;
                rows.Add(good ? $"{label}={Show(got)}"
                              : $"{label} ✗期望 {Show(want)} 实际 {Show(got)}");
                return good;
            }

            // 贴附 + 拖右边缘：只放行 Right，Top/Bottom 钉死（高度就是这么锁住的）
            Row("贴附·拖右", VizWindowSizing.CorrectSize(
                    VizWindowSizing.WmszRight, cur, (1000, 100, 1800, 900), MinPx, true),
                (1000, 100, 1800, 900));

            // 贴附 + 拖右边缘 + 系统连高度一起乱提 → 高度仍被钉回当前值
            Row("贴附·乱提高度", VizWindowSizing.CorrectSize(
                    VizWindowSizing.WmszRight, cur, (1000, 60, 1700, 1100), MinPx, true),
                (1000, 100, 1700, 900));

            // 贴附 + 拖右边缘但比最小宽度还窄 → 夹到 MinWidth
            Row("贴附·夹最小宽", VizWindowSizing.CorrectSize(
                    VizWindowSizing.WmszRight, cur, (1000, 100, 900, 900), MinPx, true),
                (1000, 100, 1360, 900));

            // 贴附 + 其它边/角 → 整条驳回（位置与高度都不许变）
            Row("贴附·拖左", VizWindowSizing.CorrectSize(
                    VizWindowSizing.WmszLeft, cur, (800, 100, 1600, 900), MinPx, true), cur);
            Row("贴附·拖上", VizWindowSizing.CorrectSize(
                    VizWindowSizing.WmszTop, cur, (1000, 40, 1600, 900), MinPx, true), cur);
            Row("贴附·拖下", VizWindowSizing.CorrectSize(
                    VizWindowSizing.WmszBottom, cur, (1000, 100, 1600, 1100), MinPx, true), cur);
            Row("贴附·拖左上角", VizWindowSizing.CorrectSize(
                    VizWindowSizing.WmszTopLeft, cur, (800, 40, 1600, 900), MinPx, true), cur);
            Row("贴附·拖右下角", VizWindowSizing.CorrectSize(
                    VizWindowSizing.WmszBottomRight, cur, (1000, 100, 1800, 1100), MinPx, true), cur);

            // 自由模式 → 原样放行（隔离期就是这样，四边都能拖）
            Row("自由·拖下", VizWindowSizing.CorrectSize(
                    VizWindowSizing.WmszBottom, cur, (1000, 100, 1600, 1100), MinPx, false),
                (1000, 100, 1600, 1100));

            // DPI 折算：lParam 是物理像素，MinWidth 是 DIP
            bool dip = VizWindowSizing.DipToPx(360, 96) == 360
                    && VizWindowSizing.DipToPx(360, 144) == 540
                    && VizWindowSizing.DipToPx(360, 120) == 450
                    && VizWindowSizing.DipToPx(360, 0) == 360      // 拿不到 DPI 时按 96 兜底
                    && VizWindowSizing.DipToPx(0.4, 96) == 1;      // 至少 1 像素
            ok &= dip;
            rows.Add(dip
                ? $"DPI 折算 360dip→96:{VizWindowSizing.DipToPx(360, 96)}/144:{VizWindowSizing.DipToPx(360, 144)}/0:{VizWindowSizing.DipToPx(360, 0)}"
                : $"✗ DPI 折算 144% 期望 540 实际 {VizWindowSizing.DipToPx(360, 144)}");

            // 胶水层健壮性：自检路径下的窗口没有 HWND（没 Show 过），Attach 不许抛；
            // 契约是**幂等**（重复调用先摘旧的再来、返回值一致），不是"必须返回 false"
            // —— 未 Show 的窗口到底能不能拿到 HWND 属于 WPF 内部行为，拿它当断言太脆。
            var probe = new VizWindowSizing(new VizWindow(null));
            bool attach1 = probe.Attach();
            bool attach2 = probe.Attach();
            probe.Detach();
            probe.Detach();
            probe.Dispose();

            bool glue = attach1 == attach2;
            ok &= glue;
            rows.Add(glue
                ? $"未 Show 场景 Attach={attach1}（幂等）且不抛、Detach/Dispose 可重复调用"
                : $"✗ Attach 不幂等：第一次 {attach1}、第二次 {attach2}");

            return new VizSelfTest.Result(Title, ok, string.Join("；", rows));
        }
        catch (Exception ex)
        {
            return new VizSelfTest.Result(Title, false, ex.GetType().Name + "：" + ex.Message);
        }
    }

    // ------------------------------------------------------------------ 小工具

    private static string Show(VizPlacement p) =>
        $"{p.Mode}({p.Left:0.#},{p.Top:0.#},{p.Width:0.#},{p.Height:0.#}){(p.ShowAttachedWindow ? "显" : "隐")}";

    private static string Show((int Left, int Top, int Right, int Bottom) r) =>
        $"({r.Left},{r.Top},{r.Right},{r.Bottom})";

    private static string Show(VizWindowAction a) =>
        $"{a.Mode}{(a.Degraded ? "·降级" : "")}" +
        (a.SetBounds ? $"({a.Left:0.#},{a.Top:0.#},{a.Width:0.#},{a.Height:0.#})" : "(不动几何)") +
        (a.Hide ? "隐藏" : a.Show ? "显示" : "不显隐") +
        (a.Attached ? "·贴附" : "·自由");
}
