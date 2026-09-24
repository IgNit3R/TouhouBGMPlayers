namespace ThbgmPlayer.UI;

/// <summary>波形**可见的时间区间**（单位：秒）。<see cref="Span"/> 就是缩放级别（1× 时 = 整轨长度）。</summary>
public readonly record struct ViewRange(double Start, double Span)
{
    public double End => Start + Span;
}

/// <summary>
/// 波形面板的全部**纯算术**：视图↔像素↔桶的换算、缩放、播放头跟随、循环入口标记的判定。
///
/// 单独放一个文件、不碰任何 WPF 对象，是为了**能离屏逐例断言** ——
/// 项目里那些出过事的都是算术（错一位整轨错位那种），而不是渲染。
/// 面板本身只负责把这些结果画出来。
/// </summary>
public static class WaveformMath
{
    /// <summary>缩放下限：1×（整轨）。不能再往外缩，否则两侧露白。</summary>
    public const double MinZoom = 1.0;

    /// <summary>滚轮一格（120）对应的缩放系数。1.25 ⇒ 四格约 2.4 倍，手感不至于一步跳太远。</summary>
    public const double ZoomStep = 1.25;

    /// <summary>
    /// 最小可见跨度 = 一屏正好铺满 <paramref name="width"/> 个桶。
    /// 这就是**缩放的物理上限**：再放大一屏的桶数就不够铺了，属于空放大（画不出更多细节）。
    /// ⚠️ 短曲子（整轨还不到一屏桶数）要夹回整轨，否则会出现"能放大到超过整轨"的怪状态。
    /// </summary>
    public static double MinSpan(double width, double secondsPerBucket, double total) =>
        Math.Min(Math.Max(1, width) * secondsPerBucket, Math.Max(0, total));

    /// <summary>夹进合法范围：跨度不超整轨、不窄于 <see cref="MinSpan"/>，起点不越出曲首曲尾。</summary>
    public static ViewRange Clamp(ViewRange v, double width, double secondsPerBucket, double total)
    {
        if (total <= 0) return new ViewRange(0, 0);

        double span = Math.Clamp(v.Span, MinSpan(width, secondsPerBucket, total), total);
        double start = Math.Clamp(v.Start, 0, Math.Max(0, total - span));
        return new ViewRange(start, span);
    }

    /// <summary>
    /// 滚轮缩放一轮。**锚点 = 鼠标位置**：光标下那一刻的时间在缩放前后停在同一像素上
    /// （除非已经贴到曲首/曲尾被夹住 —— 那时锚点会跟着偏，这是必然的，别当 bug）。
    /// </summary>
    public static ViewRange ZoomAt(ViewRange v, int wheelDelta, double anchorX, double width,
                                   double secondsPerBucket, double total)
    {
        if (width <= 1 || total <= 0 || wheelDelta == 0) return Clamp(v, width, secondsPerBucket, total);

        // 滚轮向上（正） = 放大 = 跨度变小
        double notches = wheelDelta / 120.0;
        double factor = Math.Pow(ZoomStep, -notches);

        double anchorRatio = Math.Clamp(anchorX / width, 0, 1);
        double anchorTime = v.Start + anchorRatio * v.Span;

        double span = v.Span * factor;
        double start = anchorTime - anchorRatio * span;

        return Clamp(new ViewRange(start, span), width, secondsPerBucket, total);
    }

    /// <summary>某个像素对应的桶号（可能越界，调用方自己夹）。</summary>
    public static int BucketAt(ViewRange v, double x, double width, double secondsPerBucket)
    {
        if (width <= 0 || secondsPerBucket <= 0) return 0;

        double t = v.Start + Math.Clamp(x / width, 0, 1) * v.Span;
        return (int)(t / secondsPerBucket);
    }

    /// <summary>
    /// 像素区间 <c>[x0, x1)</c> 覆盖到的桶范围（闭区间 <c>[From, To]</c>，已夹进 <c>[0, bucketCount-1]</c>）。
    ///
    /// ⚠️ 这个"归并"正是**缩放不重扫**的关键：1× 时一个像素可能覆盖上百个桶（取组内 min/max），
    /// 放大到底时一个像素不到一个桶（同一个桶会被相邻像素重复用）。
    /// 两端都夹住、且保证 <c>To ≥ From</c> —— 否则放大到极处会出现空区间、波形整段消失。
    /// </summary>
    public static (int From, int To) BucketSpan(ViewRange v, double x0, double x1,
                                                double width, double secondsPerBucket, int bucketCount)
    {
        if (bucketCount <= 0) return (0, 0);

        int from = BucketAt(v, x0, width, secondsPerBucket);
        int to = BucketAt(v, Math.Max(x0, x1 - 1e-9), width, secondsPerBucket);

        from = Math.Clamp(from, 0, bucketCount - 1);
        to = Math.Clamp(to, 0, bucketCount - 1);

        return to < from ? (from, from) : (from, to);
    }

    /// <summary>
    /// 缩放后视图起点：**播放头走到可视区正中就开始滚，一直滚到波形尾进入视野为止**。
    ///
    /// 三阶段全在这一条里：
    /// · 未到中点 ⇒ 夹在 0 ⇒ 视图钉在曲首、播放头从左往中间走；
    /// · 到中点后 ⇒ 视图跟着走、播放头恒在正中；
    /// · 接近尾部 ⇒ 夹在 `total − span` ⇒ 视图钉在曲尾，播放头从中间走到右缘。
    ///
    /// ✅ 1×（未缩放）时 `total − span == 0` ⇒ 自动退化为"钉在整轨、不滚动"，
    /// 所以**不需要给"没缩放"写特例**。
    /// </summary>
    public static double FollowStart(double filePos, double span, double total) =>
        Math.Clamp(filePos - span / 2, 0, Math.Max(0, total - span));

    /// <summary>
    /// 播放头的**文件位置**（秒）—— 波形的横轴是文件，不是播放时间线。
    ///
    /// 引擎的两个量（`LoopSampleProvider.cs:160/163`）：
    /// · <c>ProgressPosition</c>：无限循环取 `_srcFrame`（**文件真实位置**，所以进度条在无限模式下
    ///   本来就是折返的）；有限模式取 `_emitFrame`（时间线位置）；
    /// · <c>LoopPosition</c>：当前这一遍 loop 内已播的时长。
    ///
    /// intro 段两种模式的位置都等于时间线开头，直接用；进了循环段就用
    /// `intro + LoopPosition` 折算回文件坐标 —— 于是第 N 遍也对，
    /// 淡出段（`_srcFrame` 仍在循环体里）也会稳稳停在循环段内。
    /// ⚠️ 别自己拿 `ProgressPosition` 取模：那要另算一遍 intro/loop 的秒数，多一处会漂的算术。
    /// </summary>
    public static double FilePosition(TimeSpan progress, TimeSpan loopPosition, TimeSpan intro) =>
        progress < intro ? progress.TotalSeconds : intro.TotalSeconds + loopPosition.TotalSeconds;

    /// <summary>
    /// 要不要画**循环入口**标记。
    ///
    /// 两种"不循环"都要压掉：
    /// · 曲目本身无循环点（黄昏作 ED / Staff Roll，`TrackDef.IsTfOneShot`，由引擎传给 `LoopSampleProvider`）；
    /// · 压根没有循环段（运行时的 intro == total）。
    ///
    /// ⚠️ 必须用**运行时**的 intro/total，不能查索引：实测 tf 230 曲里有 21 曲
    /// （th135 那 21 首）索引里根本没有循环字段，循环点是运行时从 sfl 解出来的。
    /// ⚠️ 循环**末**标记不做 —— 结构上 loop 永远跑到文件末尾（loop ≡ total − intro），
    /// 那个标记只会永远贴在右边缘，零信息量。
    /// </summary>
    public static bool ShouldShowLoopMark(bool isTfOneShot, double introSeconds, double totalSeconds) =>
        !isTfOneShot && introSeconds > 0 && introSeconds < totalSeconds;

    /// <summary>
    /// 时间刻度的**间隔**：取 (1,2,5)×10^k 里第一个"像素间距不小于 targetPx"的档。
    /// 从候选序列**向上取整** ⇒ 间距天然 ≥ targetPx ✓（宁可疏一点，也不要挤成一团）。
    /// 纯算术、只吃数值 ⇒ 可离屏自检。
    /// </summary>
    public static double TickStep(double span, double width, double targetPx = 80)
    {
        if (span <= 0 || width <= 1 || targetPx <= 0) return 0;

        double desired = span * targetPx / width;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(desired)));

        foreach (double m in new[] { 1.0, 2.0, 5.0 })
            if (m * mag >= desired) return m * mag;

        return 10 * mag;
    }

    /// <summary>
    /// 时间刻度（**绝对时刻**，文件 0 起；即 0:30 / 1:00 这类整点）。
    /// ⚠️ 锚在绝对时间、不是"视图内按比例" —— 播放中视图每帧跟随滚动，
    /// 若按视图锚定，刻度会跟着**逐帧爬动** ✗。
    /// </summary>
    public static List<double> Ticks(ViewRange view, double step, int maxCount = 200)
    {
        var list = new List<double>();
        if (step <= 0 || view.Span <= 0) return list;

        double t = Math.Ceiling(view.Start / step) * step;
        for (int i = 0; i < maxCount && t <= view.End; i++, t += step)
            list.Add(t);

        return list;
    }
}
