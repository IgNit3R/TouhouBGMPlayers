namespace ThbgmPlayer.Viz;

/// <summary>一次放置判定的结果。<see cref="ShowAttachedWindow"/> 为 false 时几何无意义（窗口该收起来）。</summary>
public readonly record struct VizPlacement(
    VizPlacementMode Mode,
    double Left,
    double Top,
    double Width,
    double Height,
    bool ShowAttachedWindow);

/// <summary>
/// 一次放置判定之后**该对窗口做什么**。
///
/// 为什么单开一个类型、而不在判定里直接操作窗口：自检**起不了窗口**
/// （`ShutdownMode=OnLastWindowClose`，Show 了一个就把进程带下去），而
/// 「窗口最后可见吗」这种断言在未 Show 的窗口上**恒为 false** —— 验了等于没验。
/// 所以把决定做成**纯数据**：自测断言的是「**决定让它隐藏**」，不是「它现在隐藏着」。
/// </summary>
public readonly record struct VizWindowAction(
    VizPlacementMode Mode,
    bool Show,
    bool Hide,
    bool SetBounds,
    double Left,
    double Top,
    double Width,
    double Height,
    bool Attached,
    bool Degraded);

/// <summary>
/// 放置判定（方案 §3.6）。**纯函数、零窗口依赖** → 可以被 `--viz-selftest` 直接驱动，
/// 也能在隔离期就把「与宿主等高」「最大化内嵌」两条需求写完并回归。
///
/// 判定表（＝自检用例表，逐行都有断言）：
/// <list type="table">
/// <item><description><c>IsAttached=false</c>（自由模式） → <c>Free</c>，几何原样返回</description></item>
/// <item><description><c>Normal</c>，右侧空间 ≥ desired → <c>AttachedNormal</c>；<c>Left=host.Right</c>、<c>Top=host.Top</c>、<c>Height=host.Height</c></description></item>
/// <item><description><c>Normal</c>，<c>MinWidth ≤ 空间 &lt; desired</c> → <c>AttachedNormal</c>；<c>Width=空间</c></description></item>
/// <item><description><c>Normal</c>，空间 &lt; <c>MinWidth</c> → <c>Width=MinWidth</c>，<c>Left</c> 回夹到 <c>workArea.Right - MinWidth</c>（<b>宁可压住宿主，绝不跑出屏幕</b>）</description></item>
/// <item><description><c>Maximized</c> 且内嵌 → <c>AttachedEmbedded</c>，<c>ShowAttachedWindow=false</c></description></item>
/// <item><description><c>Maximized</c> 且不内嵌 → 退化为贴右侧（预留开关）</description></item>
/// <item><description><c>Minimized</c> → <c>ShowAttachedWindow=false</c>，几何不参与计算</description></item>
/// </list>
///
/// ⚠️ <b>「与宿主等高」只体现在 Normal 分支</b>：<c>Height = host.Bounds.Height</c>、<c>Top = host.Bounds.Top</c>。
/// 这正是当年给附件窗口去掉标题栏的理由 —— 标题栏会白吃掉约 30px 的「等高」预算。
/// </summary>
public static class VizPlacementLogic
{
    /// <summary>附件窗口的最小宽度（DIP）。与 `VizWindow.xaml` 的 <c>MinWidth</c> 同值。</summary>
    public const double MinWidth = 360;

    /// <summary>
    /// 算一次放置。
    /// </summary>
    /// <param name="host">宿主几何。隔离期是 <see cref="FreeHost"/>。</param>
    /// <param name="desiredWidth">用户期望的附件窗口宽度（DIP，一般来自上次记住的值）。
    /// 小于 <see cref="MinWidth"/> 时按 <see cref="MinWidth"/> 处理 —— 期望值不合法不该让窗口变得不可用。</param>
    /// <param name="embedWhenMaximized">宿主最大化时是否改为内嵌（<c>AppSettings.Viz.EmbedWhenMaximized</c>）。</param>
    public static VizPlacement Compute(IVizHost host, double desiredWidth, bool embedWhenMaximized)
    {
        ArgumentNullException.ThrowIfNull(host);

        var b = host.Bounds;

        // 期望宽度不合法就按最小宽度办：宁可宽一点，也不要给出一个放不下内容的窗口
        if (!double.IsFinite(desiredWidth) || desiredWidth < MinWidth) desiredWidth = MinWidth;

        // ① 自由模式：几何由窗口自己保管，**原样返回**（调用方据此知道「不要动我」）
        if (!host.IsAttached)
            return new VizPlacement(VizPlacementMode.Free, b.Left, b.Top, b.Width, b.Height, true);

        // ② 最小化：不显示，也不参与计算
        if (host.State == VizHostState.Minimized)
            return new VizPlacement(VizPlacementMode.AttachedNormal, b.Left, b.Top, b.Width, b.Height, false);

        // ③ 最大化 + 内嵌：附件窗口隐藏，画面搬进宿主
        if (host.State == VizHostState.Maximized && embedWhenMaximized)
            return new VizPlacement(VizPlacementMode.AttachedEmbedded, b.Left, b.Top, b.Width, b.Height, false);

        // ④ 贴右侧（Normal；Maximized 且不内嵌时也退化到这里）
        var wa = host.WorkArea;
        double avail = wa.Right - b.Right;      // 宿主右边缘到工作区右边缘的剩余空间

        if (avail < MinWidth)
        {
            // 连最小宽度都放不下：**宁可压住宿主，绝不跑出屏幕**
            return new VizPlacement(VizPlacementMode.AttachedNormal,
                                    wa.Right - MinWidth, b.Top, MinWidth, b.Height, true);
        }

        // 放不下期望宽度就用满可用空间
        double width = avail < desiredWidth ? avail : desiredWidth;

        return new VizPlacement(VizPlacementMode.AttachedNormal, b.Right, b.Top, width, b.Height, true);
    }

    // ------------------------------------------------------------------ 该对窗口做什么（M5）

    /// <summary>
    /// 由宿主状态推出窗口动作（纯函数）。分支覆盖：
    /// <list type="bullet">
    /// <item><b>Free</b>：几何归窗口自己保管 —— 既不动位置、也不动显隐，只保证没被贴上
    ///   （<c>Attached=false</c> → 拖动与缩放都回到自由窗口那套）。</item>
    /// <item><b>AttachedNormal</b>：贴附 + 写几何 + 必要时 <c>Show</c>
    ///   （宿主从最大化还原回来时，窗口得回来）。</item>
    /// <item><b>AttachedEmbedded / Minimized</b>：**隐藏**（内嵌时画面搬进宿主，附件窗口让位）。</item>
    /// <item><b>兜底</b>：几何还没就绪。⚠️ 这不是假想 —— <b>未 Show 的 WPF 窗口
    ///   <c>Left/Top</c> 就是 <c>NaN</c></b>，而宿主可能刚建好就被读了一次。
    ///   此时**什么都不动**，让窗口保持原样可见：宁可位置不完美，也不要往窗口里写 NaN。</item>
    /// </list>
    /// </summary>
    public static VizWindowAction Decide(IVizHost host, double desiredWidth, bool embedWhenMaximized)
    {
        var p = Compute(host, desiredWidth, embedWhenMaximized);

        // 自由模式：边界、显隐都不插手
        if (p.Mode == VizPlacementMode.Free)
            return new VizWindowAction(p.Mode, false, false, false, 0, 0, 0, 0, false, false);

        // 该收起窗口的两个分支：内嵌 / 最小化
        if (!p.ShowAttachedWindow)
            return new VizWindowAction(p.Mode, false, true, false, 0, 0, 0, 0, true, false);

        // 兜底：几何不可用 → 什么都不动（保持可见）
        if (!Usable(p))
            return new VizWindowAction(p.Mode, true, false, false, 0, 0, 0, 0, true, true);

        return new VizWindowAction(p.Mode, true, false, true, p.Left, p.Top, p.Width, p.Height, true, false);
    }

    /// <summary>几何是否可用：有限、且不是退化尺寸。兜底分支的判据。</summary>
    public static bool Usable(in VizPlacement p) =>
        double.IsFinite(p.Left) && double.IsFinite(p.Top) &&
        double.IsFinite(p.Width) && double.IsFinite(p.Height) &&
        p.Width >= 1 && p.Height >= 1;

    /// <summary>
    /// 该不该把当前几何回写进设置 —— 也就是「**这份几何属于谁**」。
    ///
    /// <list type="bullet">
    /// <item><b>贴附时不写</b>：那时的 <c>Left/Top/Width/Height</c> 是**宿主算出来的**，
    ///   写回去会把用户自己记下的自由窗口位置覆盖掉 —— 下次他在自由模式打开，
    ///   窗口会跑到上一任宿主旁边。</item>
    /// <item><b>没 Show 过也不写</b>：那时 <c>Left/Top</c> 还是 <c>NaN</c>、
    ///   <c>Width/Height</c> 是从设置里读出来的，回写等于把「期望值」当成「实际值」固化。</item>
    /// </list>
    ///
    /// 抽成纯函数是为了能被断言 —— 它是这条所有权规则的唯一判据，
    /// 散在 `Closing` 里就只能靠读代码。
    /// </summary>
    public static bool ShouldPersistGeometry(bool wasShown, bool attached) => wasShown && !attached;
}
