namespace ThbgmPlayer.Viz;

/// <summary>
/// 单写单读环形缓冲：**音频线程写、UI 线程读**，靠「单调递增的写指针 + <see cref="Volatile"/>」同步，
/// **不加锁** —— 音频线程上锁竞争的代价远大于收益（方案 §3.2）。
///
/// 原先长在 <see cref="VizDebugFeed"/> 里。M6 接入时 <see cref="VizTap"/> 要用**同一套**，
/// 于是抽出来：这段是**音频线程上唯一会跑的代码**，两份拷贝迟早会分叉，
/// 而这种分叉的后果（半帧错位、读到半截脏数据）在画面上很难查。
///
/// 环长取 2 的幂 → 取模退化成按位与。**不能取小**：延迟对齐要读到约 100ms 前（4410 帧），
/// 而 FFT 窗口本身还要 4096 帧，加起来 8506 帧，4096 的环根本不够。
///
/// ⚠️ 数组在构造期**自动预清零** → 启动初期读到的是静音，不是垃圾。
/// ⚠️ 明确接受**单帧撕裂**（读侧可能跨过一次写入）：频谱上一个噪点、波形上一处毛刺、
/// 相关系数偏差 &lt;1e-2 —— 换来音频线程完全无锁。方案 R3 已拍板接受。
/// </summary>
internal sealed class VizRing
{
    /// <summary>环长（帧）。2 的幂，靠位与取模。65536 帧 ≈ 1.49 秒 @44100。</summary>
    public const int Frames = 1 << 16;

    private const int Mask = Frames - 1;

    /// <summary>L/R 两条**独立**环 —— 一条交错环会有跨声道撕裂（方案 §3.2）。</summary>
    private readonly float[] _l = new float[Frames];
    private readonly float[] _r = new float[Frames];

    /// <summary>累计写入的帧数（**单调递增，不取模**）。只在音频线程写，用 Volatile 发布。</summary>
    private long _written;

    /// <summary>毫秒 → 帧，并把上限夹住（别把缓冲吃穿）。</summary>
    public static int MsToFrames(double ms, int sampleRate)
    {
        double positive = ms > 0 ? ms : 0;
        int frames = (int)Math.Round(positive / 1000.0 * sampleRate);
        return frames > Frames / 2 ? Frames / 2 : frames;
    }

    /// <summary>
    /// 追加一批**交错**样本。<b>只在音频线程调用。</b>
    ///
    /// 写指针最后**一次性推进**：读到旧指针的 UI 线程至多看到「少了一段」，
    /// 不会读到「写了一半」的中间状态。
    /// </summary>
    public void Write(ReadOnlySpan<float> interleaved, int frames, int channels)
    {
        if (frames <= 0) return;

        long w = _written;
        bool stereo = channels > 1;

        for (int i = 0; i < frames; i++)
        {
            int idx = (int)((w + i) & Mask);
            int src = i * channels;
            _l[idx] = interleaved[src];
            _r[idx] = stereo ? interleaved[src + 1] : interleaved[src];
        }

        Volatile.Write(ref _written, w + frames);
    }

    /// <summary>
    /// 读「延迟 <paramref name="offsetFrames"/> 帧之后、最近 <paramref name="frames"/> 帧」。
    /// **契约：不足即以静音补齐**（先整体清零，再把能取到的盖上去），返回实际取到的帧数。
    /// </summary>
    public int ReadLatest(Span<float> dstL, Span<float> dstR, int frames, int offsetFrames)
    {
        if (frames <= 0) return 0;

        dstL.Slice(0, frames).Clear();
        dstR.Slice(0, frames).Clear();

        // 一次读入写指针（不重读：撕裂可接受，见类型注释）
        long w = Volatile.Read(ref _written);

        var (from, head, count) = Window(w, frames, offsetFrames);
        if (count <= 0) return 0;

        for (int i = 0; i < count; i++)
        {
            int idx = (int)((from + i) & Mask);
            dstL[head + i] = _l[idx];
            dstR[head + i] = _r[idx];
        }

        return count;
    }

    /// <summary>
    /// 取窗计算。<b>纯函数</b>（只吃一个写指针）→ 自检能直接驱动它。
    ///
    /// 为什么值得单独抽：**延迟对齐差几十毫秒在画面上根本看不出来** ——
    /// 频谱/波形晚个 100ms，人眼分不出。这类"错了也不报警"的算术只能靠扫。
    ///
    /// 三种边界都由它一处兜住：
    /// <list type="bullet">
    /// <item>数据还不够（启动初期 / 刚清过）：<c>Head</c> 记下前面要留白的长度，
    ///   调用方那部分保持静音 —— 读到的必须是静音而不是环里的旧数据。</item>
    /// <item>延迟偏移比已写入的还大：返回 <c>Count = 0</c>。</item>
    /// <item>写指针跨过环尾：取模交给调用方（这里只出绝对帧号）。</item>
    /// </list>
    /// </summary>
    internal static (long From, int Head, int Count) Window(long written, int frames, int offsetFrames)
    {
        if (frames <= 0) return (0, 0, 0);
        if (offsetFrames < 0) offsetFrames = 0;   // 负偏移会算出"写到未来"的窗口，宁可当没偏移

        long end = written - offsetFrames;        // 「现在听到的」那一点
        if (end <= 0) return (0, frames, 0);

        long start = end - frames;
        long from = Math.Max(0, start);
        int head = (int)(from - start);
        int count = (int)Math.Min(end - from, frames - head);

        return (from, head, count > 0 ? count : 0);
    }
}
