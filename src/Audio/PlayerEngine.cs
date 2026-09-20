using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using ThbgmPlayer.Core;
using ThbgmPlayer.Data;
using ThbgmPlayer.Viz;

namespace ThbgmPlayer.Audio;

/// <summary>
/// 播放引擎。多源调度：每首曲子开自己的音频源（不同作品在不同 dat，采样率也可能不同），
/// 切曲时关掉上一个（DESIGN_v3.md §6）。
///
/// <code>
///   音频源(源采样率) ─→ [重采样到 44100，灵界版 22050 时需要] ─┐
///                                                              ├─→ 交叉淡化混音器 ─→ 音量
///                                                              ┘        ─→ float 转 16bit ─→ WASAPI 共享模式
/// </code>
///
/// 输出固定 44100/2ch（WASAPI 共享模式）。灵界版是 22050Hz，由 WdlResamplingSampleProvider
/// 重采样上来 —— 纯托管实现，不依赖 Media Foundation。
/// </summary>
public sealed class PlayerEngine : IDisposable
{
    private const int OutputRate = 44100;
    private const int OutputChannels = 2;
    private const int LatencyMs = 100;

    /// <summary>灵界版切换的交叉淡化时长。</summary>
    public static readonly TimeSpan AltCrossfade = TimeSpan.FromMilliseconds(20);

    private readonly CrossfadeMixer _mixer;
    private readonly VolumeSampleProvider _volume;
    private WasapiPlayer? _out;
    private string? _initError;
    private string? _currentDeviceId;   // 当前输出绑定的是哪台设备（null = 跟随系统默认）
    private readonly List<(DateTime At, IAudioSource Source)> _retiring = new();

    private LoopSampleProvider? _loop;
    private IAudioSource? _source;
    private float _volumeLevel = 1f;
    private System.Threading.Timer? _fadeTimer;

    /// <summary>
    /// 可视化分接节点（方案 §3.2）。**长期持有**，插在音量之后、16bit 转换之前 ——
    /// 「取样点在音量后」是已拍板的：所见即所听，音量拉到 0 画面跟着归零。
    ///
    /// ⚠️ 它**不随输出设备重建**：切设备时 <see cref="BuildOutput"/> 会重建 WasapiPlayer，
    /// 但分接节点必须留着，否则环里攒的数据与可视化的订阅方会一起断掉。
    /// </summary>
    private readonly VizTap _vizTap;

    public PlayerEngine()
    {
        var fmt = WaveFormat.CreateIeeeFloatWaveFormat(OutputRate, OutputChannels);
        _mixer = new CrossfadeMixer(fmt);
        _volume = new VolumeSampleProvider(_mixer);
        _vizTap = new VizTap(_volume, AppSettings.Current.Viz.LatencyOffsetMs);

        try
        {
            _out = BuildOutput();
        }
        catch (Exception ex)
        {
            // 没有音频设备时不让程序起不来，等到真正播放时再报错
            _initError = ex.Message;
            _out = null;
        }
    }

    /// <summary>
    /// 可视化读侧（只读）。窗口只依赖 <see cref="IVizFeed"/>，不认引擎 ——
    /// 这也是隔离期能用 <c>VizDebugFeed</c> 顶替它的原因。
    /// </summary>
    public IVizFeed VizFeed => _vizTap;

    /// <summary>设置里改了「与听觉对齐」的偏移就调一下（幂等，立即生效）。</summary>
    public void SetVizLatency(double ms) => _vizTap.SetLatencyOffset(ms);

    /// <summary>
    /// 按当前设置建输出。指定了设备且找得到就绑上去；否则用系统默认 + 自动流路由
    /// （WithDefaultDeviceStreamRouting：系统默认设备变化时 Windows 无缝把流挪过去，
    /// 不需要应用层做任何事 —— 这就是「系统里切了输出要重启才生效」的根治）。
    /// </summary>
    private WasapiPlayer BuildOutput()
    {
        var builder = new WasapiPlayerBuilder()
            .WithSharedMode()
            .WithEventSync()
            .WithMmcssThreadPriority("Pro Audio")   // 播放线程提优先级，高负载下不易欠载
            .WithLatency(LatencyMs);

        var deviceId = AppSettings.Current.Playback.OutputDeviceId;
        MMDevice? device = OutputDevices.FindById(deviceId);

        builder = device is not null
            ? builder.WithDevice(device)
            : builder.WithDefaultDeviceStreamRouting();

        // 流路由（跟随系统默认）只能异步激活：NAudio 规定开了 routing 必须 BuildAsync，
        // 同步 Build() 会抛「call BuildAsync() instead」。这个异常此前被构造函数 catch
        // 吞成了 InitError —— 系统默认这条路径的播放从引入 routing 起就是坏的。
        // 这里 ctor 必须是同步的，GetAwaiter().GetResult() 等这一次性的初始化即可。
        var player = builder.BuildAsync().GetAwaiter().GetResult();

        // 分接节点插在**音量之后、16bit 转换之前**（方案 §10 的 1 行改动）：
        // 于是可视化看到的是「用户实际听到的」那一份电平。
        player.Init(new SampleToWaveProvider16(_vizTap));

        // 指定设备找不到时已经悄悄退回系统默认，把设置也掰回来，免得界面显示和实际不符
        _currentDeviceId = device is not null ? deviceId : null;
        if (device is null && !string.IsNullOrEmpty(deviceId))
            AppSettings.Current.Playback.OutputDeviceId = null;

        return player;
    }

    /// <summary>
    /// 切换输出设备，**立即生效**：停掉当前输出，在新设备上重建并续播同一条混音链。
    /// 混音器没动，所以播放位置不丢；正在播的话只有重建那一下的短暂间隙，不用停、不用重启。
    /// deviceId 传 null = 跟随系统默认。与当前设备相同则什么都不做。
    /// </summary>
    public void SetOutputDevice(string? deviceId)
    {
        if (deviceId == _currentDeviceId && _out is not null) return;

        bool wasPlaying = IsPlaying;

        if (_out is not null)
        {
            try { _out.Stop(); } catch { /* 设备已丢 */ }
            try { _out.Dispose(); } catch { /* 同上 */ }
            _out = null;
        }

        try
        {
            _out = BuildOutput();
            _initError = null;
        }
        catch (Exception ex)
        {
            _initError = ex.Message;
            throw new InvalidOperationException($"切换输出设备失败：{ex.Message}");
        }

        if (wasPlaying) _out.Play();
    }

    /// <summary>输出设备初始化失败的原因；正常为 null。</summary>
    public string? InitError => _initError;

    /// <summary>正在播的曲目。</summary>
    public TrackRef? Current { get; private set; }

    /// <summary>是否正在使用灵界版。</summary>
    public bool UsingAlt { get; private set; }

    public bool IsPlaying => _out is not null && _out.PlaybackState == PlaybackState.Playing;

    /// <summary>有限模式的当前曲目已播完（由外层轮询，用来自动切下一首）。</summary>
    public bool IsFinished => _loop is not null && _loop.IsFinished;

    public TimeSpan CurrentTime => _loop?.CurrentTime ?? TimeSpan.Zero;

    /// <summary>进度条量程。无限循环按整轨（loop=1），普通 / 随机按 N / X / F。</summary>
    public TimeSpan TotalTime => _loop?.TotalTime ?? TimeSpan.Zero;

    /// <summary>进度条当前位置。</summary>
    public TimeSpan ProgressPosition => _loop?.ProgressPosition ?? TimeSpan.Zero;

    public TimeSpan LoopPosition => _loop?.LoopPosition ?? TimeSpan.Zero;

    public TimeSpan LoopDuration => _loop?.LoopDuration ?? TimeSpan.Zero;

    public bool Infinite => _loop?.Infinite ?? false;

    public float Volume
    {
        get => _volumeLevel;
        set
        {
            _volumeLevel = Math.Clamp(value, 0f, 1f);
            _volume.Volume = _volumeLevel;
        }
    }

    // ---------- 播放控制 ----------

    /// <summary>开始播放。useAlt 为 true 且该曲有灵界版时播灵界版。</summary>
    public void Play(GameDef game, TrackDef track, bool useAlt)
    {
        Ready();
        DisposeFadeTimer();   // 取消可能正在进行的停止淡出，否则它到点会把新曲子一起停掉
        _volume.Volume = _volumeLevel;   // 淡出被打断时音量可能停在半途，这里还原

        Reap(TimeSpan.FromMilliseconds(500));
        RetireCurrent();

        var td = Pick(game, track, useAlt);
        // 预读命中就是零等待；没命中走同步读取（UI 线程付一次性读入的老代价）
        _source = PreloadCache.TryTake(game, track, useAlt) ?? AudioSourceFactory.Create(game, td);
        _loop = new LoopSampleProvider(_source, AppSettings.Current.Playback, oneShot: td.IsTfOneShot);

        // 淡化交给混音器在采样层面做，不用音量节点：
        // 音量节点靠 8ms 定时器改增益，而音频回调是按缓冲区（约 100ms）取用的，
        // 几十毫秒的渐变压根落不进缓冲区，等于硬切。混音器是逐帧算增益的。
        _mixer.SwitchTo(BuildChain(_loop), SwitchFade);

        Current = new TrackRef(game.Id, track.No);
        UsingAlt = useAlt && track.HasAlt;

        // 已经在播就不用再 Play 一次，重复调用会重启音频客户端
        if (!IsPlaying) _out!.Play();
    }

    /// <summary>切曲 / seek 的淡化时长，取设置里的 30ms 默认。</summary>
    private static TimeSpan SwitchFade =>
        TimeSpan.FromSeconds(Math.Max(0, AppSettings.Current.Playback.FadeOnSeekSeconds));

    /// <summary>
    /// 就地改时间线参数（换循环模式 / 改 N X F 时用）。
    /// 不重建音源、不重新定位，因此音频**完全不中断** ——
    /// 这是「切模式有跳转或卡滞」的根治办法。
    /// </summary>
    public void Reconfigure() => _loop?.Configure(AppSettings.Current.Playback);

    /// <summary>
    /// 在主版 / 灵界版之间切换。按当前时间 1:1 对齐（两者时长完全相同），交叉淡化。
    /// </summary>
    public void SwitchAlt(GameDef game, TrackDef track, bool useAlt)
    {
        Ready();
        if (_loop is null)
        {
            Play(game, track, useAlt);
            return;
        }

        Reap(TimeSpan.FromMilliseconds(500));

        var td = Pick(game, track, useAlt);
        var src = PreloadCache.TryTake(game, track, useAlt) ?? AudioSourceFactory.Create(game, td);
        var lp = new LoopSampleProvider(src, AppSettings.Current.Playback, oneShot: td.IsTfOneShot);

        // 对齐要等真正换上去的那一刻再做：从现在到换链之间，旧链还会被读走
        // 最多一个缓冲区（100ms 延迟）。提前 seek 的话两条链错开这么多，
        // 交叉淡化两个错开的相同旋律就是梳状滤波。
        // 捕获旧链本身而不是 _loop 字段 —— 字段这几行之后就被换成新链了。
        var old = _loop;
        _mixer.SwitchTo(BuildChain(lp), AltCrossfade, () => lp.SeekToTime(old.CurrentTime));

        RetireCurrent();
        _source = src;
        _loop = lp;
        UsingAlt = useAlt && track.HasAlt;

        // 输出状态保持不变：正在播就继续播，暂停着的仍保持暂停，不去动音频客户端
    }

    public void Pause() => _out?.Pause();

    public void Resume() => _out?.Play();

    /// <summary>定位到时间线上的某处。</summary>
    public void Seek(TimeSpan t) => _loop?.SeekToTime(t);

    /// <summary>定位到 loop 段内的某处。无限循环模式下进度条走这个。</summary>
    public void SeekLoop(TimeSpan t) => _loop?.SeekToLoopPosition(t);

    /// <summary>停止。fade &gt; 0 时先把音量线性降到 0 再停，避免爆音。</summary>
    public void Stop(TimeSpan fade)
    {
        DisposeFadeTimer();

        if (_out is null || fade <= TimeSpan.Zero || !IsPlaying)
        {
            HardStop();
            return;
        }

        float from = _volume.Volume;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _fadeTimer = new System.Threading.Timer(_ =>
        {
            double k = sw.Elapsed.TotalSeconds / fade.TotalSeconds;
            if (k >= 1)
            {
                DisposeFadeTimer();
                HardStop();
                return;
            }
            _volume.Volume = from * (float)(1 - k);
        }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(8));
    }

    private void HardStop()
    {
        try { _out?.Stop(); } catch { /* 设备已丢失时忽略 */ }

        _mixer.Clear();
        RetireCurrent();

        // _out.Stop() 返回后音频回调不会再跑，且混音器已清空，此时可以立刻释放句柄
        Reap(TimeSpan.Zero);

        _volume.Volume = _volumeLevel;   // 淡出把音量压到了 0，这里还原
        Current = null;
        UsingAlt = false;
    }

    // ---------- 内部 ----------

    private void Ready()
    {
        if (_out is null)
            throw new InvalidOperationException($"音频输出设备初始化失败：{_initError ?? "未知原因"}");
    }

    /// <summary>采样率不是 44100 就挂一个重采样节点（灵界版 22050 走这条路）。</summary>
    private static ISampleProvider BuildChain(LoopSampleProvider loop) =>
        loop.WaveFormat.SampleRate == OutputRate
            ? loop
            : new WdlResamplingSampleProvider(loop, OutputRate);

    private static TrackDef Pick(GameDef game, TrackDef track, bool useAlt) =>
        useAlt && track.Alt is not null ? track.Alt : track;

    /// <summary>
    /// 把当前音源转入待释放队列，等一会儿再 Dispose。
    /// WASAPI 缓冲里可能还压着引用它的音频，立刻释放会炸。
    /// </summary>
    private void RetireCurrent()
    {
        if (_source is not null)
        {
            _retiring.Add((DateTime.UtcNow, _source));
            _source = null;
        }
        _loop = null;
    }

    private void Reap(TimeSpan grace)
    {
        var now = DateTime.UtcNow;
        for (int i = _retiring.Count - 1; i >= 0; i--)
        {
            if (now - _retiring[i].At < grace) continue;
            try { _retiring[i].Source.Dispose(); } catch { /* 关不掉就算了 */ }
            _retiring.RemoveAt(i);
        }
    }

    private void DisposeFadeTimer()
    {
        _fadeTimer?.Dispose();
        _fadeTimer = null;
    }

    /// <summary>把总音量在 duration 内从 from 渐变到 to。</summary>
    private void RampVolume(float from, float to, TimeSpan duration)
    {
        DisposeFadeTimer();

        if (duration <= TimeSpan.Zero || Math.Abs(to - from) < 1e-4f)
        {
            _volume.Volume = to;
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        _fadeTimer = new System.Threading.Timer(_ =>
        {
            double k = Math.Min(1, sw.Elapsed.TotalSeconds / duration.TotalSeconds);
            _volume.Volume = from + (to - from) * (float)k;
            if (k >= 1) DisposeFadeTimer();
        }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(8));
    }

    public void Dispose()
    {
        DisposeFadeTimer();
        HardStop();
        Reap(TimeSpan.Zero);
        try { _out?.Dispose(); } catch { /* 忽略 */ }
        _vizTap.Dispose();   // 释放后 IsPlaying 恒 false，可视化那边自然走归零
    }
}
