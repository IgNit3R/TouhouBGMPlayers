using NAudio.Wave;
using ThbgmPlayer.Core;

namespace ThbgmPlayer.Audio;

/// <summary>
/// 把一条音轨按 intro / loop 模型展开，并实现 DESIGN_v3.md §5 的时间线：
///
/// <code>
///   |-- intro --|-- loop × N 次 --|-- 额外 X 秒（续播 loop 内容）--|-- 淡出 F 秒（边播边淡出）--|
/// </code>
///
/// 「额外 X 秒」和「淡出 F 秒」播的都是 loop 段的内容，只是后者边播边把音量线性降到 0。
/// 总长 = intro + N×loop + X + F。
///
/// 无限循环模式没有终点，N / X / F 全部忽略。
/// </summary>
public sealed class LoopSampleProvider : ISampleProvider
{
    private readonly IAudioSource _source;
    private readonly int _channels;
    private readonly int _blockAlign;
    private readonly long _introFrames;
    private readonly long _totalFrames;
    private readonly long _loopFrames;
    private readonly long _loopStartFrame;
    private readonly bool _oneShotTrack;  // 曲目本身无循环点（黄昏作 ED/Staff Roll，由调用方声明）
    private bool _oneShot;                // 当前生效的一次性语义 = 无循环点 且 当前是普通/随机模式
                                          // （无限循环模式下不循环曲照常循环 —— 用户约定）
    // ↓ 以下四项由 Configure 设置，换循环模式 / 改 N X F 时会变，所以不能是 readonly
    private long _extraFrames;
    private long _fadeFrames;
    private bool _infinite;
    private long _emitFrames;
    private readonly byte[] _pcm;

    private long _srcFrame;     // 源内位置（loop 段内自动折返）
    private long _emitFrame;    // 时间线位置：已输出的帧数
    private long _srcPosBytes = -1;   // 底层文件已定位到的字节位置；-1 = 尚未定位
    private PlaybackSettings? _pendingConfig;   // 待应用的时间线参数（由 UI 线程投递）
    private bool _finished;

    /// <summary>
    /// one-shot：一次性曲（黄昏作 ED/Staff Roll 等无循环点曲目）。
    /// 由调用方传入 —— 播放传 true（一遍停、无 N/X/F），导出传 false（整曲作为循环段，
    /// 正常吃 N/X/F，见 WavExporter）。源以 intro=0、loop=整曲表达「无循环点」。
    /// </summary>
    public LoopSampleProvider(IAudioSource source, PlaybackSettings pb, bool oneShot = false)
    {
        _source = source;
        _channels = source.Format.Channels;
        _blockAlign = source.Format.BlockAlign;

        if (source.Format.BitsPerSample != 16)
            throw new NotSupportedException($"目前只支持 16bit PCM，实际是 {source.Format.BitsPerSample}bit。");
        if (_blockAlign <= 0)
            throw new NotSupportedException("音频格式异常：BlockAlign 为 0。");

        // 对外宣称 IEEE float：本类型输出的本来就是 [-1,1] 的 float。
        // 这里如果沿用源的「16bit PCM」描述，下游的重采样器和转波形节点会按错格式理解。
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.Format.SampleRate, _channels);

        _introFrames = Math.Max(0, source.IntroBytes / _blockAlign);
        _totalFrames = source.TotalBytes / _blockAlign;
        if (_totalFrames <= 0)
            throw new NotSupportedException("音频格式异常：整轨长度为 0。");

        // one-shot 由调用方声明（播放=无循环点曲；导出不传=false）。注意它只对普通/随机生效，
        // 无限循环模式下不循环曲照常循环 —— 所以 loop 段按真实长度算，不归零。
        _oneShotTrack = oneShot && _introFrames < _totalFrames;
        _loopFrames = Math.Max(1, _totalFrames - _introFrames);
        _loopStartFrame = _introFrames < _totalFrames ? _introFrames : 0;

        // 8K 帧 ≈ 186ms @44100：一次回调基本一次读完，减少文件 IO 次数
        _pcm = new byte[_blockAlign * 8192];

        Configure(pb);
        ApplyPendingConfig();   // 还没交给音频线程，这里直接应用即可
        SeekToFrame(0);
    }

    /// <summary>
    /// 请求设置（或**就地更新**）时间线参数。构造时、换循环模式、改 N / X / F 都走这里。
    ///
    /// 就地更新是刻意的：重建音源的话，新链会从「我们读到位置」那一刻开始，
    /// 而音频线程在这期间又播走了一个缓冲区（WASAPI 延迟 100ms），两条链错开
    /// 最多 100ms —— 交叉淡化两个错开的相同信号就是梳状滤波，听感是卡滞；
    /// 事后再 seek 又会被听到一次跳回开头。就地改参数则完全不碰音源，音频不中断。
    ///
    /// 真正的写入推迟到音频回调（Read）里做：本方法从 UI 线程调用，直接改这些字段
    /// 会和正在读它们的音频线程撞上 —— 哪怕单个 long / bool 的读写是原子的，
    /// 也可能读到「新 _emitFrames 配旧 _infinite」这种混搭组合。
    /// </summary>
    public void Configure(PlaybackSettings pb) => Volatile.Write(ref _pendingConfig, pb);

    private void ApplyPendingConfig()
    {
        var pb = Volatile.Read(ref _pendingConfig);
        if (pb is null) return;
        Volatile.Write(ref _pendingConfig, null);

        _infinite = pb.LoopMode == LoopMode.Infinite;
        // 一次性语义只对普通/随机生效；无限循环模式下不循环曲照常循环（用户约定）
        _oneShot = _oneShotTrack && !_infinite;
        _extraFrames = SecondsToFrames(pb.ExtraSeconds);
        _fadeFrames = SecondsToFrames(pb.FadeSeconds);

        // 时间轴长度（也就是进度条的量程）：
        //   无限循环 → 整轨（intro + 一遍 loop），等价于 loop=1，不使用 N / X / F
        //   普通 / 随机 → intro + N×loop + X + F，完全按播放设置
        // _emitFrames 在无限模式下只用于显示：Read 判定结束、GainAt 判定淡出
        // 都已经用 _infinite 挡开了，不会因为它不再是 0 而多出一个终点。
        _emitFrames = _oneShot
            ? _totalFrames
            : _infinite
              ? _totalFrames
              : _introFrames
                + (long)Math.Max(0, pb.LoopCount) * _loopFrames
                + _extraFrames
                + _fadeFrames;

        // 长度变了，位置跟着重映射。保留「在循环段内的相对位置」，
        // 丢掉「已经循环了几遍」—— 同一首曲子换模式，接着当前这一遍往下播才自然。
        if (_oneShot)
        {
            _emitFrame = Math.Clamp(_srcFrame, 0, Math.Max(0, _emitFrames));
            _finished = false;
            return;
        }
        long offset = _srcFrame - _loopStartFrame;
        _emitFrame = offset < 0
            ? Math.Max(0, _srcFrame)                                  // 还在 intro 里，原样保留
            : Math.Min(_introFrames + (offset % _loopFrames), Math.Max(0, _emitFrames));

        _finished = false;
    }

    public WaveFormat WaveFormat { get; }

    /// <summary>无限循环模式（没有终点）。</summary>
    public bool Infinite => _infinite;

    /// <summary>有限模式下已经播完。由外层轮询，用来触发自动切下一首。</summary>
    public bool IsFinished => _finished;

    /// <summary>累计播放时长（无限循环下会一直增长）。</summary>
    public TimeSpan CurrentTime => FramesToTime(_emitFrame);

    /// <summary>
    /// 进度条的量程。无限循环按整轨算（= loop=1），普通 / 随机按 N / X / F。
    /// </summary>
    public TimeSpan TotalTime => FramesToTime(_emitFrames);

    /// <summary>
    /// 进度条的位置。
    /// 无限循环下 _emitFrame 是一直累加的，所以改取源内位置 —— 它播到整轨末尾
    /// 会自动折返到循环起点，于是进度条在一遍之内从 0 走到满，然后跳回循环起点，
    /// 正好对得上听感。有限模式直接用时间线位置。
    /// </summary>
    public TimeSpan ProgressPosition => FramesToTime(_infinite ? _srcFrame : _emitFrame);

    /// <summary>当前这一遍 loop 内已播的时长（进度条用）。</summary>
    public TimeSpan LoopPosition => FramesToTime(Math.Max(0, _srcFrame - _loopStartFrame));

    public TimeSpan LoopDuration => FramesToTime(_loopFrames);

    /// <summary>已经播完几遍 loop。</summary>
    public int LoopsDone =>
        _emitFrame <= _introFrames ? 0 : (int)((_emitFrame - _introFrames) / _loopFrames);

    public long SecondsToFrames(double seconds) =>
        seconds <= 0 ? 0 : (long)Math.Round(seconds * WaveFormat.SampleRate);

    private TimeSpan FramesToTime(long frames) =>
        TimeSpan.FromSeconds((double)frames / WaveFormat.SampleRate);

    public void SeekToTime(TimeSpan t) => SeekToFrame(SecondsToFrames(t.TotalSeconds));

    /// <summary>
    /// 定位到 loop 段内的某处（相对循环起点算）。
    /// 换循环模式时用这个做锚点 —— 两种模式的时间轴长度不同，
    /// 拿绝对时间去定位会被夹到末尾，而「在循环段内的相对位置」两边通用。
    /// </summary>
    public void SeekToLoopPosition(TimeSpan t)
    {
        if (_oneShot) { SeekToFrame(SecondsToFrames(t.TotalSeconds)); return; }   // 无循环段：退化为普通定位
        long f = Math.Clamp(SecondsToFrames(t.TotalSeconds), 0, _loopFrames - 1);
        SeekToFrame(_introFrames + f);
    }

    /// <summary>按时间线定位。源内位置由时间线位置换算，loop 段自动折返。</summary>
    public void SeekToFrame(long frame)
    {
        // 两种模式统一夹在 [0, _emitFrames]：无限循环的量程就是整轨
        long f = Math.Clamp(frame, 0, Math.Max(0, _emitFrames));
        _emitFrame = f;
        _srcFrame = MapToSource(f);
        _finished = false;
        EnsureSourcePosition();
    }

    /// <summary>
    /// 把底层文件的读指针对齐到 _srcFrame。
    ///
    /// 这是整个播放内核最容易漏的一环：_srcFrame 只是本类型维护的**逻辑**位置，
    /// 底层 IAudioSource 有自己的读指针，两者互不知情。只改 _srcFrame 而不 seek 底层，
    /// 声音会继续从文件当前位置播下去 —— 表现为 seek、灵界版 1:1 对齐、loop 折返
    /// 全部失效。seek 是系统调用，所以只在位置真的不一致时才做。
    /// </summary>
    private void EnsureSourcePosition()
    {
        long want = _srcFrame * _blockAlign;
        if (_srcPosBytes == want) return;
        _source.Seek(want);
        _srcPosBytes = want;
    }

    private long MapToSource(long emitFrame)
    {
        if (_oneShot) return Math.Clamp(emitFrame, 0, _totalFrames);
        if (emitFrame < _introFrames) return emitFrame;
        return _loopStartFrame + ((emitFrame - _introFrames) % _loopFrames);
    }

    /// <summary>时间线某处的增益。只有最后的淡出段小于 1。</summary>
    private float GainAt(long emitFrame)
    {
        if (_infinite || _oneShot || _fadeFrames <= 0) return 1f;
        long fadeStart = _emitFrames - _fadeFrames;
        if (emitFrame < fadeStart) return 1f;
        return (float)(1.0 - (double)(emitFrame - fadeStart) / _fadeFrames);
    }

    public int Read(Span<float> buffer)
    {
        ApplyPendingConfig();   // 在音频线程上应用，与读取串行，不会有数据竞争

        int framesWanted = buffer.Length / _channels;
        int framesDone = 0;
        int maxChunk = _pcm.Length / _blockAlign;
        int zeroReads = 0;

        while (framesDone < framesWanted)
        {
            if (!_infinite && _emitFrame >= _emitFrames)
            {
                _finished = true;
                break;
            }

            long toEnd = _totalFrames - _srcFrame;
            if (toEnd <= 0)
            {
                if (_oneShot) { _finished = true; break; }   // 一次性曲：到曲末即结束，不折返
                _srcFrame = _loopStartFrame;
                EnsureSourcePosition();
                continue;
            }

            long allowed = _infinite ? toEnd : Math.Min(toEnd, _emitFrames - _emitFrame);
            if (allowed <= 0)
            {
                _srcFrame = _loopStartFrame;
                EnsureSourcePosition();
                continue;
            }

            int chunk = (int)Math.Min(Math.Min(framesWanted - framesDone, allowed), maxChunk);
            int gotBytes = _source.Read(_pcm, 0, chunk * _blockAlign);
            int gotFrames = gotBytes / _blockAlign;
            if (gotFrames <= 0)
            {
                // 连续读不到数据就认定结束，避免文件异常时在这里死循环
                if (++zeroReads >= 2)
                {
                    _finished = true;
                    break;
                }
                _srcFrame = _loopStartFrame;
                EnsureSourcePosition();
                continue;
            }
            zeroReads = 0;

            Decode16(gotFrames, buffer.Slice(framesDone * _channels), _emitFrame);
            framesDone += gotFrames;
            _srcFrame += gotFrames;
            _srcPosBytes += (long)gotFrames * _blockAlign;   // 文件读指针同步前进
            _emitFrame += gotFrames;
            if (_srcFrame >= _totalFrames && !_oneShot)
            {
                _srcFrame = _loopStartFrame;
                EnsureSourcePosition();
            }
        }

        if (framesDone < framesWanted)
            buffer.Slice(framesDone * _channels).Clear();

        // 始终返回满长度：短读会被下游当成流结束，这里宁可补静音
        return buffer.Length;
    }

    private void Decode16(int frames, Span<float> dest, long emitStart)
    {
        // 绝大多数时间不在淡出段，走无增益快路径：每帧省一次 GainAt（一次除法 + 分支）
        long fadeStart = (_infinite || _oneShot || _fadeFrames <= 0) ? long.MaxValue : _emitFrames - _fadeFrames;
        if (emitStart + frames <= fadeStart)
        {
            int n = frames * _channels;
            for (int i = 0; i < n; i++)
            {
                int p = i * 2;
                dest[i] = (short)(_pcm[p] | (_pcm[p + 1] << 8)) / 32768f;
            }
            return;
        }

        for (int f = 0; f < frames; f++)
        {
            float g = GainAt(emitStart + f);
            int p = f * _channels * 2;
            for (int c = 0; c < _channels; c++)
            {
                short v = (short)(_pcm[p] | (_pcm[p + 1] << 8));
                dest[f * _channels + c] = (v / 32768f) * g;
                p += 2;
            }
        }
    }
}
