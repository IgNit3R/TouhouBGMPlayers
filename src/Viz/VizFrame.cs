namespace ThbgmPlayer.Viz;

/// <summary>
/// 一帧的分析结果。**重活每帧只算一次，四个渲染器共享**（方案 §3.3）。
///
/// 缓冲全部在构造期分配好、之后原地复用：这个对象每秒被读 60 次，
/// 每帧 new 一个数组会让 GC 在播放时持续抖动。
/// </summary>
public sealed class VizFrame
{
    /// <summary>FFT 点数。44100 下 bin 宽 10.77Hz —— 频带下探到 20Hz，用 2048 会把前两三格挤满。</summary>
    public const int FftSize = 4096;

    /// <summary>
    /// 一次取数的时域窗长，同时就是 FFT 的输入长度（4096，<b>不做补零</b>）≈ 93ms @44100。
    ///
    /// 与 <see cref="FftSize"/> 同值不是巧合：补零到更长的 FFT 只会插值、不增加真实分辨率，
    /// 而把 4096 个真实样本喂给 4096 点 FFT 才是参照页 <c>anMix.fftSize = 4096</c> 的做法。
    /// （最初写成「2048 样本 + 补零」，那会让窗只覆盖半截信号 —— 见 VizAnalyzer 里的警示。）
    /// </summary>
    public const int TimeFrames = FftSize;

    /// <summary>
    /// 波形窗：<see cref="TimeL"/> 的**末尾** 2048 帧，B 示波器 / C 电平 / D 利萨如用。
    /// 与参照页对齐 —— 那边的 <c>anL/anR</c> 时域分析器 <c>fftSize = 2048</c>，
    /// 而频谱分析器 <c>anMix</c> 是 4096，两者本来就不同长。别把它们混成一个数。
    /// </summary>
    public const int WaveFrames = 2048;

    /// <summary>
    /// 采样率。<b>方案 §3.3 的字段表里没有这一条，是实现时补的</b> ——
    /// B 要算 <c>12ms × SampleRate</c> 的窗口长度、A 要把 bin 换算成频率，
    /// 这两个都离不开它。放在 VizStyle 里语义不对（那是纯观感），所以挂在帧上。
    /// </summary>
    public int SampleRate { get; set; } = 44100;

    /// <summary>时域 L（<see cref="TimeFrames"/> 帧，供 FFT）。索引 0 最旧、末尾最新。</summary>
    public float[] TimeL { get; } = new float[TimeFrames];

    /// <summary>时域 R（<see cref="TimeFrames"/> 帧，供 FFT）。</summary>
    public float[] TimeR { get; } = new float[TimeFrames];

    /// <summary>波形窗 L：<see cref="TimeL"/> 的末尾 <see cref="WaveFrames"/> 帧。</summary>
    public float[] WaveL { get; } = new float[WaveFrames];

    /// <summary>波形窗 R：<see cref="TimeR"/> 的末尾 <see cref="WaveFrames"/> 帧。</summary>
    public float[] WaveR { get; } = new float[WaveFrames];

    /// <summary>FFT 幅度谱（dBFS，长度 = FftSize/2，四渲染器共享）。</summary>
    public float[] SpectrumDb { get; } = new float[FftSize / 2];

    /// <summary>
    /// A 频谱条的柱高（0…1，<b>已平滑，含暂停时的释放衰减</b>）。
    ///
    /// <b>方案 §3.3 的字段表里同样没有这一条</b>，理由和 <see cref="SampleRate"/> 一样是
    /// 「必须有地方放」：参照页里 <c>v[b] += (tg - v[b]) * …</c> 是**跨帧递推**，
    /// 纯函数画不出来。它既不是渲染器该持有的状态（渲染器要求无状态），
    /// 也不适合留给 <see cref="VizAnalyzer"/> 私有 —— A 渲染器得拿到平滑后的值。
    /// 放进帧里，语义正好：柱高就是分析输出的一部分。
    /// </summary>
    public float[] Bars { get; } = new float[BarCount];

    /// <summary>频谱柱数（定稿：52 根对数频柱）。</summary>
    public const int BarCount = 52;

    /// <summary>
    /// RMS 电平（0…1 线性值，已做升降不同速的平滑）。渲染器直接取，不要自己再平滑
    /// —— 平滑状态归 <see cref="VizAnalyzer"/>，渲染器必须无状态（方案 §3.4）。
    /// </summary>
    public float RmsL { get; set; }
    public float RmsR { get; set; }

    /// <summary>峰值（0…1）。目前没有面板用它（峰值帽/保持线都已否决），先算着备用。</summary>
    public float PeakL { get; set; }
    public float PeakR { get; set; }

    /// <summary>L/R 相位相关度（-1…+1，已平滑）。+1 = 完全同相，-1 = 反相。</summary>
    public float Correlation { get; set; }

    /// <summary>距上一帧的秒数。平滑系数要按它折算，否则帧率一变弹道就变。</summary>
    public double Dt { get; set; }

    /// <summary>本帧是否有效（正在出声）。false 时渲染器应画静止/归零态。</summary>
    public bool Active { get; set; }

    /// <summary>
    /// 内容版本号：**每真的重算一次分析就 +1**，被 60Hz 限频跳过的调用不动它。
    ///
    /// 为什么必须有它：渲染节拍跟 vsync（`VizPump`），而分析钉在 60Hz
    /// （`VizAnalyzer.AnalyzeHz`）。在 144Hz 屏上，**同一个帧对象会被画 2～3 次** ——
    /// 对无状态渲染器（A/B/C）这无所谓，但 D 的余辉是**按帧递推**的跨帧状态
    /// （参照页每帧拿 <c>rgba(23,23,23,0.16)</c> 覆盖一次），跟着 vsync 递推会让余辉长度
    /// 随刷新率变化。拿版本号一比就知道「这帧我推过没有」，把递推重新钉回 60Hz。
    ///
    /// 单看 <see cref="Active"/> 或 <see cref="Dt"/> 都判不出来 —— 那两者在一次新分析之后
    /// 的连续多次绘制里是同一个值。
    /// </summary>
    public long Revision { get; set; }

    /// <summary>
    /// 归零计数：<see cref="Clear"/> 每调一次 +1。
    ///
    /// 为什么不复用 <see cref="Revision"/>：那个**每次重算都变**，分不出「这帧是新数据」与
    /// 「这帧是归零后的第一帧」。而带跨帧状态的渲染器（D 的余辉）必须分辨清楚 ——
    /// 方案 §2 要求「停止 / 切曲 / 播完」是**归零**（画面清干净、不留淡影）；
    /// 不通知它的话，余辉会按自己那套慢衰减（约 1 秒）淡出去，那正是「留了一小段残影」。
    /// </summary>
    public long ResetRevision { get; set; }

    /// <summary>把时域与频域清零。停止 / 切曲用它做「归零」（方案 §2）。</summary>
    public void Clear()
    {
        Array.Clear(TimeL);
        Array.Clear(TimeR);
        Array.Clear(WaveL);
        Array.Clear(WaveR);
        Array.Clear(SpectrumDb);
        Array.Clear(Bars);
        RmsL = RmsR = PeakL = PeakR = Correlation = 0f;
        Active = false;
        ResetRevision++;   // 让 D 之类的跨帧状态方把自己的历史一并丢掉
    }
}
