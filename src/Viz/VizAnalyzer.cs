using System.Diagnostics;
using System.Numerics;
using NAudio.Dsp;
using DspComplex = NAudio.Dsp.Complex;

namespace ThbgmPlayer.Viz;

/// <summary>
/// 取数 + 分析：把 <see cref="IVizFeed"/> 的原始样本变成一帧 <see cref="VizFrame"/>。
///
/// 职责边界（方案 §3.3）：**本类是唯一持有平滑状态的地方**，渲染器一律无状态 ——
/// 它们只读帧里的成品值（含已平滑的柱高 / 电平 / 相关度），不自己再滤一遍。
///
/// ⚠️ 两条硬性纪律：
/// <list type="number">
/// <item><b>零分配。</b>所有缓冲都是构造期分配、之后原地复用。<see cref="Update"/> 活在
///    UI 线程上、每秒 60 次，任何一处 <c>new</c> 都会让 GC 在播放时持续抖动。</item>
/// <item><b>限频 60Hz。</b>渲染节拍跟 vsync（可能是 144Hz），但 FFT 没必要跟着跑 144 次/秒。
///   <see cref="Update"/> 内部按 <see cref="AnalyzeInterval"/> 自我节流；被节流掉的调用
///   什么都不做并返回 <c>false</c>，调用方照旧渲染上一帧 —— 肉眼看不出，
///   而**平滑弹道被钉在 60Hz 上**，换显示器不会改变手感。</item>
/// </list>
///
/// 全部数学逐项对齐验证页 <c>.workbuddy/viz-demo/index.html</c>，每处都标了出处。
/// </summary>
public sealed class VizAnalyzer
{
    /// <summary>分析帧率上限。渲染可以更快，分析不必。</summary>
    public const int AnalyzeHz = 60;

    private const double AnalyzeInterval = 1.0 / AnalyzeHz;

    /// <summary>单帧最大步长（参照页 <c>dt=Math.min(0.1, …)</c>）。卡顿/断点后不让弹道一步跳完。</summary>
    private const double MaxDt = 0.1;

    // ------------------------------------------------------------------ 常量（来源：参照页）

    /// <summary>A 的 dB → 柱高映射下界（参照页 <c>tg=(db+75)/70</c>，取负得 -75dB 起）。</summary>
    private const float SpecFloorDb = -75f;

    /// <summary>A 的 dB → 柱高映射跨度（参照页 <c>/70</c>：-75dB→0，-5dB→1）。</summary>
    private const float SpecSpanDb = 70f;

    /// <summary>柱高上冲系数（参照页 <c>tg>v[b]?0.55</c>）。</summary>
    private const float BarAttack = 0.55f;

    /// <summary>柱高回落系数下限（参照页 <c>Math.max(0.12, dt*8)</c>）。</summary>
    private const float BarReleaseMin = 0.12f;

    /// <summary>柱高回落的时间系数（参照页 <c>dt*8</c>）。</summary>
    private const float BarReleasePerSec = 8f;

    /// <summary>RMS 上冲系数（参照页 <c>rmsV>rms[ch]?0.6</c>）。</summary>
    private const float RmsAttack = 0.6f;

    /// <summary>RMS 回落系数（参照页 <c>:0.08</c>）。</summary>
    private const float RmsRelease = 0.08f;

    /// <summary>相关度平滑系数（参照页 <c>corr+=(cj-corr)*0.15</c>）。</summary>
    private const float CorrSmoothing = 0.15f;

    /// <summary>
    /// 相关度初值：**0（中位 = 无数据）**。
    ///
    /// ⚠️ 参照页写的是 <c>var corr = 0.8</c>（起始偏「假定同相」，避免指针从 -1 甩上来），
    /// **但那条不能照搬**：参照页只在出声时跑，那个初值根本看不见；
    /// 而我们的分析器**空闲时也在跑**（窗口一打开就要画一帧静止态），
    /// 于是 0.8 会明晃晃停在屏幕上，再慢慢滑向真实值 —— 观感就是「相位表从 +1 那边滑过来」。
    ///
    /// 0 = 指针停在中位，读作「还没测到」，这才是"无数据"该有的样子。
    /// </summary>
    private const float CorrInitial = 0f;

    /// <summary>求 dB 时的地板，防止 <c>log10(0)</c>。</summary>
    private const float DbFloor = 1e-9f;

    /// <summary>相关度分母的地板（参照页 <c>Math.max(1e-6, …)</c>）：静音时不去做 0/0。</summary>
    private const float CorrDenomFloor = 1e-6f;

    // ------------------------------------------------------------------ 语义收口（方案 §2）

    // ------------------------------------------------------------------ 淡影与归零（方案 §2）
    //
    // ⚠️ 2026-09-21 改过。原文是「暂停 → 向暂停瞬间显示值的 **15%** 收敛后停住」，实机下来两个问题：
    //   ① **与 D 不一致**：D 的余辉是按帧递推的，自己一路烧到零 → 同一次暂停，D 衰减干净、
    //      其余四块停在 15%，五块面板两种收尾（用户实机截图报上来的）；
    //   ② **读数会撒谎**：停在 15% 时 C 显示 -27.2 这种低电平 —— 明明没有声音，
    //      却读出一个存在的声音。而参照页本来就是衰减到 0。
    //
    // 现在的语义：**暂停 = 慢慢衰减到干净（约 1 秒）；停止 / 切曲 / 播完 = 立刻清零**（Reset）。
    // 两者仍分得开 —— 差别从「终点不同」变成「**速度不同**」。
    //
    // 实现上不再有「目标比例」这个量：各显示量的目标就是 0，靠它们各自的跨帧平滑慢慢走；
    // 唯一需要自己动画的是**波形幅度**（见 _fadeScale / ApplyFade）。

    /// <summary>判定「已经收敛到位」的阈值。指数逼近永远不会精确等于目标，只能给容差。</summary>
    private const float FadeEpsilon = 0.003f;

    /// <summary>
    /// 对数频带边界，53 个（52 柱）。参照页 <c>eLog.push(20*Math.pow(20000/20, i/NB))</c> ——
    /// <b>定稿为对数版</b>，线性版已否决。
    /// </summary>
    private static readonly float[] BarEdges = BuildLogEdges();

    // ------------------------------------------------------------------ 状态

    /// <summary>
    /// 读侧。**不是 readonly**：换源（拖放音频 / 切曲）走 <see cref="Rebind"/> —— 只换喂数据的，
    /// 不换分析器本身。原因见 <see cref="Rebind"/>。
    /// </summary>
    private IVizFeed _feed;

    private readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>FFT 输入（实部填样本×窗，虚部恒 0）。原址变换，每帧复用。</summary>
    private readonly DspComplex[] _fft = new DspComplex[VizFrame.FftSize];

    /// <summary>降混后的单声道样本（A 走 FFT 用）。</summary>
    private readonly float[] _mono = new float[VizFrame.FftSize];

    /// <summary>预计算的 Hann 窗乘数。参照页靠 Web Audio 内部加窗，这里得自己乘。</summary>
    private readonly float[] _hann = new float[VizFrame.FftSize];

    private double _lastAnalyze = -1;

    // 平滑状态（跨帧递推，只在这里存在）
    private float _rmsL, _rmsR;
    private float _corr = CorrInitial;

    // 淡影状态：只有「波形幅度」这一个量需要自己动画系数（见 ApplyFade）。
    // 柱高 / 电平 / 相关度本身就有跨帧平滑 —— 把目标设成 0 它们自己会慢慢走。
    /// <summary>
    /// 波形幅度缩放：播放中恒为 1（不缩放）；暂停后向 0 慢慢收敛；归零态直接是 0。
    /// **它同时也是「淡化中」的状态位** —— <c>&gt; 0</c> 就是还在淡出（见 <see cref="IsSettled"/>）。
    /// </summary>
    private float _fadeScale;

    private bool _lastActive;

    /// <summary>输出帧。<b>同一个对象被反复填充</b>，调用方不要缓存引用去做跨帧比较。</summary>
    public VizFrame Frame { get; } = new();

    /// <summary>本分析器被节流跳过（未重算）的次数。诊断用，不参与渲染。</summary>
    public long ThrottledCount { get; private set; }

    public VizAnalyzer(IVizFeed feed)
    {
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));

        // 窗表预算。NAudio 的 HannWindow 返回的是「乘数」而不是「加窗后的值」，
        // 正好可以提前算好存起来 —— 每帧省 4096 次 cos。
        // ⚠️ 它返回的是 **double**（同族的 Hamming/BlackmanHarris 也是），要显式收敛到 float。
        for (int i = 0; i < _hann.Length; i++)
            _hann[i] = (float)FastFourierTransform.HannWindow(i, VizFrame.FftSize);
    }

    /// <summary>
    /// 推进一帧。由渲染节拍每 vsync 调用一次。
    /// </summary>
    /// <returns>本帧真的重算了返回 <c>true</c>；被 60Hz 限频跳过返回 <c>false</c>。</returns>
    public bool Update()
    {
        double now = _clock.Elapsed.TotalSeconds;

        // 限频：距上次分析不足一个分析周期就什么都不做。
        // 注意是拿「上次分析时刻」比，不是「上次调用时刻」——
        // 这样 dt 才是两次分析之间的真实间隔，弹道不会因为多调了几次而变快。
        if (_lastAnalyze >= 0 && now - _lastAnalyze < AnalyzeInterval)
        {
            ThrottledCount++;
            return false;
        }

        double dt = _lastAnalyze < 0 ? 0 : now - _lastAnalyze;
        if (dt > MaxDt) dt = MaxDt;
        _lastAnalyze = now;

        var f = Frame;
        f.SampleRate = _feed.SampleRate;
        f.Dt = dt;
        f.Active = _feed.IsPlaying;
        f.Revision++;   // 只有走到这里才算「新的一帧」；被上面限频挡掉的调用不动它（见 VizFrame.Revision）

        // 「在播 → 不播」的那一帧：只置一下波形的缩放系数（见 BeginFade）。
        // 柱高/电平/相关度不需要预备什么 —— 它们的目标本来就是 0，从当前值平滑走过去即可。
        if (!f.Active && _lastActive) BeginFade();

        // 取数（4096 帧）。契约：不足时以静音补齐，所以这里不需要自己清缓冲。
        _feed.ReadLatest(f.TimeL, f.TimeR, VizFrame.TimeFrames);

        // 波形窗 = 时域缓冲的末尾一段。B/C/D 用 2048，FFT 用全部 4096 —— 参照页也是如此
        // （anMix 4096 / anL·anR 2048）。一次取数、拷贝出两种视图，比让 feed 读两遍干净。
        Array.Copy(f.TimeL, VizFrame.TimeFrames - VizFrame.WaveFrames, f.WaveL, 0, VizFrame.WaveFrames);
        Array.Copy(f.TimeR, VizFrame.TimeFrames - VizFrame.WaveFrames, f.WaveR, 0, VizFrame.WaveFrames);

        UpdateSpectrum(f);   // 不播时它内部跳过 FFT（目标已由淡影基准决定，重算纯属白烧 CPU）
        UpdateLevels(f);
        ApplyFade(f);        // 波形整体缩放：唯一没有现成平滑可借用的量

        _lastActive = f.Active;
        return true;
    }

    // ------------------------------------------------------------------ 频谱（A）

    /// <summary>
    /// A 频谱条：降混单声道 → 加 Hann 窗 → FFT → 按对数频带取最大值 → dB → 平滑。
    ///
    /// 与参照页的一处**刻意差异**：参照页的 <c>anMix</c> 是未 split 的源，
    /// Web Audio 自己会下混，这里手工取 <c>(L+R)*0.5</c>。
    ///
    /// ⚠️ 另一处差异要记住（M2 并排调参时会撞上）：Web Audio 的 <c>AnalyserNode</c>
    /// **默认加 Blackman 窗**且带它自己的一套归一化，我们用的是 Hann。所以这里按 Hann 的
    /// 相干增益（<c>sum(w)/N</c> = 0.5）折算回 dBFS 语义（满幅正弦 ≈ 0dB），
    /// 但两边的窗泄漏形状本来就不同 —— 频柱高度会有几个 dB 的系统偏差，
    /// 属于「换窗」的正常代价，M2 直接按观感对齐，不要试图逐 bin 复刻。
    ///
    /// ⚠️ 还有一个与 NAudio **版本**绑定的坑（见下方标度处的长注释）：3.0 的
    /// <c>FastFourierTransform.FFT</c> 输出已含 1/N 归一化，别再按 2.x 推系数。
    /// </summary>
    private void UpdateSpectrum(VizFrame f)
    {
        const int n = VizFrame.FftSize;
        const float HannCoherentGain = 0.5f;   // Hann 窗 sum(w)/N

        var spec = f.SpectrumDb;

        // ⚠️ 不播时**整段跳过 FFT**：目标就是 0，不需要谱来算柱高的目标 ——
        // 拿冻结的样本再算一遍纯属白烧 CPU（R2 / §8.1#4 明确要求暂停后 CPU 回落）。
        // SpectrumDb 此时保持上一份值；它不直接上屏（上屏的是 Bars），所以无所谓。
        if (f.Active)
        {
            // 降混单声道 → 加窗。输入就是 4096 个**真实**样本，不补零。
            // ⚠️ 窗的周期必须跟着信号长度（= FftSize）走。反过来若只喂 2048 个样本再补零，
            // 按 4096 周期算出来的窗就只覆盖半截信号 —— 那是个从 1 直落 0 的斜坡而不是 Hann 窗，
            // 旁瓣会明显变差、柱高整体偏低。这里踩过一次，别再合并这两条路径。
            for (int i = 0; i < n; i++)
                _mono[i] = (f.TimeL[i] + f.TimeR[i]) * 0.5f;

            for (int i = 0; i < n; i++)
            {
                _fft[i].X = _mono[i] * _hann[i];
                _fft[i].Y = 0f;
            }

            FastFourierTransform.FFT(true, BitOperations.Log2((uint)n), _fft);

            // ⚠️ NAudio 3.0 的 FastFourierTransform.FFT **输出已含 1/N 归一化**。
            // 这一点 XML 文档里没写，是自检用满幅 1kHz 正弦反解出来的 —— 实测谱峰比理论低
            // 整整 72dB ≈ 20·log10(4096)，不多不少一个 N。（2.x 时代的 lore 是「未归一化、
            // 满幅正弦模 ≈ N/2」，照那条推就会在这里多除一个 N，柱高会整体趴到看不见。）
            //
            // 于是实测模 = A·(N/2)·增益·(1/N) = A·增益/2。乘下面这个系数把 A 还原成 1.0：
            //   ×2 —— 补回实信号正负频谱各占一半的折半；
            //   ÷HannCoherentGain —— 去掉加窗造成的幅度衰减。
            // 合起来满幅正弦 → 1.0 → 0dBFS，后面 (db+75)/70 的映射才有意义。
            float scale = 2f / HannCoherentGain;
            for (int k = 0; k < spec.Length; k++)
            {
                float re = _fft[k].X;
                float im = _fft[k].Y;
                float mag = MathF.Sqrt(re * re + im * im);
                spec[k] = 20f * MathF.Log10(MathF.Max(mag * scale, DbFloor));
            }
        }

        // 对数频带 → 柱高。ny 必须是 float：参照页靠 JS 的浮点除法，
        // 写成整数除法会把所有高频柱压到同一个 bin。
        float ny = f.SampleRate * 0.5f > 0 ? f.SampleRate * 0.5f : 22050f;
        float release = MathF.Max(BarReleaseMin, (float)(f.Dt * BarReleasePerSec));

        for (int b = 0; b < VizFrame.BarCount; b++)
        {
            float target;
            if (f.Active)
            {
                // 参照页：bi 至少为 1（跳过 DC），bj 取整并夹在谱长内，然后扫这段的最大值
                int bi = Math.Max(1, (int)MathF.Floor(BarEdges[b] / ny * spec.Length));
                int bj = Math.Min(spec.Length - 1, (int)MathF.Round(BarEdges[b + 1] / ny * spec.Length));
                if (bj < bi) bj = bi;

                float db = -100f;
                for (int k = bi; k <= bj; k++)
                    if (spec[k] > db) db = spec[k];

                target = Math.Clamp((db - SpecFloorDb) / SpecSpanDb, 0f, 1f);
            }
            else
            {
                // 不播时目标就是 0 —— 暂停与终止的差别只在**走得多快**（见类首那段说明），
                // 到这里都一样：靠下面那行平滑慢慢落下去。
                target = 0f;
            }

            float cur = f.Bars[b];
            f.Bars[b] = cur + (target - cur) * (target > cur ? BarAttack : release);
        }
    }

    // ------------------------------------------------------------------ 电平 / 相关（C / J）

    /// <summary>
    /// C 的 RMS 与 J 的相关度。
    ///
    /// ⚠️ <b>两者用的都是全部 2048 个时域样本</b>，不是 B 绘制时那个 12ms（529 样本）的子窗口 ——
    /// 参照页的 <c>tdL.length</c> 就是 2048。别被 B 的 <c>win</c> 带偏。
    ///
    /// 相关度在参照页里被算了**两遍**（累加语句写在 <c>ch2</c> 循环体内），
    /// 于是 slr/sl2/sr2 同倍放大 —— 比值不变，所以这里算一遍。
    /// </summary>
    private void UpdateLevels(VizFrame f)
    {
        // ⚠️ 用波形窗（2048），不是给 FFT 的 4096 —— 参照页的 tdL.length 就是 2048
        int n = VizFrame.WaveFrames;
        var tl = f.WaveL;
        var tr = f.WaveR;

        float sumL = 0f, sumR = 0f, peakL = 0f, peakR = 0f;
        float dotLR = 0f, sqL = 0f, sqR = 0f;

        if (f.Active)
        {
            for (int i = 0; i < n; i++)
            {
                float l = tl[i];
                float r = tr[i];

                sumL += l * l;
                sumR += r * r;
                dotLR += l * r;
                sqL += l * l;
                sqR += r * r;

                float al = MathF.Abs(l);
                float ar = MathF.Abs(r);
                if (al > peakL) peakL = al;
                if (ar > peakR) peakR = ar;
            }
        }

        float rmsMeasL = MathF.Sqrt(sumL / n);
        float rmsMeasR = MathF.Sqrt(sumR / n);

        // 不播时目标一律 0（参照页也是这么做的）。曾经的「停在 15%」有个坏处：
        // C 的读数会显示 -27.2 这种低电平 —— 明明没有声音，却读出一个存在的声音。
        float targetRmsL = f.Active ? rmsMeasL : 0f;
        float targetRmsR = f.Active ? rmsMeasR : 0f;

        _rmsL += (targetRmsL - _rmsL) * (targetRmsL > _rmsL ? RmsAttack : RmsRelease);
        _rmsR += (targetRmsR - _rmsR) * (targetRmsR > _rmsR ? RmsAttack : RmsRelease);

        // 同理：不播时目标 0，指针缓缓回到中位（与其余几块一致，见类首那段说明）。
        float corrTarget = f.Active
            ? dotLR / MathF.Max(CorrDenomFloor, MathF.Sqrt(sqL * sqR))
            : 0f;
        _corr += (corrTarget - _corr) * CorrSmoothing;

        f.RmsL = _rmsL;
        f.RmsR = _rmsR;
        f.PeakL = peakL;
        f.PeakR = peakR;
        f.Correlation = _corr;
    }

    // ------------------------------------------------------------------ 暂停淡影 / 归零（方案 §2）

    /// <summary>
    /// 换源（拖放音频 / 切曲）：只换读侧，**分析器实例保持不变**，并顺带归零。
    ///
    /// 为什么不干脆 new 一个分析器：<see cref="VizFrame.ResetRevision"/> 是**跟着帧对象**计数的
    /// —— 换一份新的 <c>VizFrame</c> 会让计数从 0 重来，而带跨帧状态的渲染器（D）记住的是旧值，
    /// 「归零」这个信号就会**悄悄丢掉**（上一首的余辉留在画面上）。复用同一个实例最省事也最稳。
    /// </summary>
    public void Rebind(IVizFeed feed)
    {
        _feed = feed ?? throw new ArgumentNullException(nameof(feed));
        _lastAnalyze = -1;      // 换源后第一次 Update 立刻生效，不必再等一个限频窗口
        Reset();
    }

    /// <summary>
    /// 归零（方案 §2）：**停止 / 切曲 / 播完**用它 —— 比暂停彻底，画面清干净、不留淡影。
    ///
    /// ⚠️ 除了清掉自己的平滑状态，还得靠 <see cref="VizFrame.Clear"/> 顶一格
    /// <see cref="VizFrame.ResetRevision"/>：D 的余辉是**渲染器侧**状态，本类够不着它；
    /// 不通知它就会按自己那套慢衰减淡出去，留一小段残影 —— 那正是「归零」要避免的。
    ///
    /// 幂等：重复调用只是把已经是零的状态再清一遍。
    /// </summary>
    public void Reset()
    {
        Frame.Clear();

        _rmsL = _rmsR = 0f;
        _corr = 0f;

        _fadeScale = 0f;          // 波形直接清空（不走淡出）
        _lastActive = false;
    }

    /// <summary>
    /// 已经「落定」：没在播放，且各显示量都已经收敛到 0 —— 此时可以退订节拍（方案 R2）。
    ///
    /// 平滑是指数逼近，永远不等于 0（虽然肉眼看不出），所以只能给容差；
    /// 容差比一个像素对应的量级小得多，收敛判定不会早于画面稳定。
    ///
    /// **归零态（<see cref="Reset"/> 之后）本来就是全 0，所以立刻算落定** —— 不需要单独的标志位：
    /// 「淡化中」与「已清零」的差别天然体现在这些数值上。
    /// </summary>
    public bool IsSettled
    {
        get
        {
            if (_lastActive) return false;                   // 还在播，谈不上落定

            // 波形幅度（它单独动画，不在这几个量里）
            if (_fadeScale > FadeEpsilon) return false;

            if (MathF.Abs(_rmsL) > FadeEpsilon) return false;
            if (MathF.Abs(_rmsR) > FadeEpsilon) return false;
            if (MathF.Abs(_corr) > FadeEpsilon) return false;

            var bars = Frame.Bars;
            for (int b = 0; b < bars.Length; b++)
                if (MathF.Abs(bars[b]) > FadeEpsilon) return false;

            return true;
        }
    }

    /// <summary>
    /// 开始淡出。**只在「在播 → 不播」的那一帧调一次。**
    ///
    /// 只把 <see cref="_fadeScale"/> 置 1（波形从满幅开始缩）；柱高/电平/相关度不需要预备什么 ——
    /// 它们的目标本来就是 0，从当前值平滑走过去就行。
    /// </summary>
    private void BeginFade()
    {
        _fadeScale = 1f;
    }

    /// <summary>
    /// 波形的整体缩放。
    ///
    /// 为什么只有它需要自己动画：柱高 / 电平 / 相关度本身就有跨帧平滑，目标设 0 就自动慢慢走；
    /// 而波形是每帧从环里拷来的原始数据，没有平滑可言，所以得单独维护一个系数乘上去。
    ///
    /// 播放中恒为 1（不缩放）；暂停后向 0 收敛；归零态直接是 0（波形清空）。
    /// </summary>
    private void ApplyFade(VizFrame f)
    {
        if (f.Active)
        {
            _fadeScale = 1f;
            return;
        }

        // 暂停后：向 0 慢慢收敛。速率借用柱高的回落系数，与其余几块大致同步（约 1 秒）。
        if (_fadeScale > 0f)
        {
            _fadeScale += (0f - _fadeScale) * BarReleaseMin;
            if (_fadeScale < FadeEpsilon) _fadeScale = 0f;   // 收敛到位就归 0，别留个永不为 0 的尾巴
        }

        if (_fadeScale >= 1f) return;                        // 播放中，省掉两轮乘法
        if (_fadeScale <= 0f)
        {
            Array.Clear(f.WaveL);
            Array.Clear(f.WaveR);
            return;
        }

        for (int i = 0; i < VizFrame.WaveFrames; i++)
        {
            f.WaveL[i] *= _fadeScale;
            f.WaveR[i] *= _fadeScale;
        }
    }

    // ------------------------------------------------------------------ 小工具

    private static float[] BuildLogEdges()
    {
        var e = new float[VizFrame.BarCount + 1];
        for (int i = 0; i <= VizFrame.BarCount; i++)
            e[i] = 20f * MathF.Pow(20000f / 20f, (float)i / VizFrame.BarCount);
        return e;
    }
}
