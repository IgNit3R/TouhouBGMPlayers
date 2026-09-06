using NAudio.Wave;

namespace ThbgmPlayer.Audio;

/// <summary>
/// 两路输入、带增益斜坡的混音器。专门为 TH13 灵界版切换准备。
///
/// 主版与灵界版时长完全相同（实测 13 组 total_sec / loop_sec 逐条 Δ=0.00%），
/// 所以切换时按当前时间 1:1 对齐即可 —— 播到第 45.3 秒切过去，灵界版正好从 45.3 秒接上。
/// 切换时旧链淡出、新链同步淡入，听感上无缝。
///
/// 换链请求只是置一个标志，真正的替换发生在音频回调里（<see cref="Read"/>），
/// 避免在读取过程中改动正在使用的链。
/// </summary>
public sealed class CrossfadeMixer : ISampleProvider
{
    private sealed class Lane
    {
        public ISampleProvider Source = null!;
        public float From;
        public float To;
        public long Pos;
        public long RampFrames;
        public bool Dead;

        /// <summary>
        /// 真正开始输出前调用（在音频线程上）。
        /// 用来做「换上去的那一刻才定位」—— 提前定位的话，旧链在这期间
        /// 又播走了最多一个缓冲区，两条链错开就会梳状滤波。
        /// </summary>
        public Action? OnStart;

        public float Gain => RampFrames <= 0
            ? To
            : (float)(From + (To - From) * ((double)Math.Min(Pos, RampFrames) / RampFrames));
    }

    private readonly int _channels;
    private Lane? _main;
    private Lane? _outgoing;
    private Lane? _pending;
    private float[] _temp = Array.Empty<float>();

    public CrossfadeMixer(WaveFormat format)
    {
        WaveFormat = format;
        _channels = Math.Max(1, format.Channels);
    }

    public WaveFormat WaveFormat { get; }

    /// <summary>
    /// 交叉淡化切到新链：旧链淡出、新链淡入，时长各 ramp。
    /// 没有旧链（首次播放）时就是一次纯淡入，不会有咔哒声。
    ///
    /// 两条链的增益是**线性互补**的（From / To 相反），叠加后的总电平恒定 ——
    /// 换曲时不会突然变响或变轻。切循环模式时新旧链是同一首、位置完全相同，
    /// 互补叠加正好还原成原信号，不会产生梳状滤波。
    /// </summary>
    /// <param name="onStart">真正换上去的那一刻才调用（在音频线程上），用来做最后的位置对齐。</param>
    public void SwitchTo(ISampleProvider source, TimeSpan ramp, Action? onStart = null)
    {
        _pending = new Lane
        {
            Source = source,
            From = 0f,
            To = 1f,
            RampFrames = ramp <= TimeSpan.Zero ? 0 : (long)Math.Round(ramp.TotalSeconds * WaveFormat.SampleRate),
            OnStart = onStart,
        };
    }

    /// <summary>停止一切输出。</summary>
    public void Clear()
    {
        _pending = null;
        _main = null;
        _outgoing = null;
    }

    public int Read(Span<float> buffer)
    {
        if (_pending is not null)
        {
            // 换链：当前主链转成淡出中的副链；没有主链（首次播放）就直接淡入
            _outgoing = _main is null
                ? null
                : new Lane
                {
                    Source = _main.Source,
                    From = _main.Gain,
                    To = 0f,
                    RampFrames = Math.Max(1, _pending.RampFrames),
                };
            _main = _pending;
            _pending = null;

            // 到这一刻旧链才停止输出，所以此刻读它的位置去对齐新链是最准的
            _main.OnStart?.Invoke();
        }

        buffer.Clear();

        if (_main is not null) Mix(_main, buffer);
        if (_outgoing is not null)
        {
            Mix(_outgoing, buffer);
            if (_outgoing.Dead) _outgoing = null;
        }

        // 始终返回满长度：短读会被下游当成流结束
        return buffer.Length;
    }

    private void Mix(Lane lane, Span<float> dest)
    {
        if (_temp.Length < dest.Length) _temp = new float[dest.Length];
        var span = _temp.AsSpan(0, dest.Length);
        span.Clear();

        int n = lane.Source.Read(span);
        if (n <= 0)
        {
            lane.Dead = true;
            return;
        }

        int frames = n / _channels;

        if (lane.RampFrames <= 0 || lane.Pos >= lane.RampFrames)
        {
            // 不在渐变中：整块乘同一个常数就行，走快路径
            float g = lane.To;
            for (int i = 0; i < n; i++) dest[i] += span[i] * g;
        }
        else
        {
            // 渐变中必须**逐帧**算增益。
            // 一个缓冲区在 100ms 延迟下是四千多帧，整块只取一个增益值的话，
            // 几十毫秒的渐变会被拉成一两级台阶 —— 短渐变等于没渐变，
            // 而且淡出的那条链会在整个缓冲区里维持起始增益（≈1），
            // 与新链满增益叠加成 +6dB 削顶，听感就是混乱加卡滞。
            long use = Math.Min(frames, lane.RampFrames - lane.Pos);
            for (int f = 0; f < frames; f++)
            {
                float g = f < use
                    ? (float)(lane.From + (lane.To - lane.From) * ((double)(lane.Pos + f) / lane.RampFrames))
                    : lane.To;

                int p = f * _channels;
                for (int c = 0; c < _channels; c++)
                    dest[p + c] += span[p + c] * g;
            }
        }

        lane.Pos += frames;
        if (lane.Pos >= lane.RampFrames)
            lane.Dead = lane.To <= 0f;   // 只有要淡到 0 的那条链才会被回收
    }
}
