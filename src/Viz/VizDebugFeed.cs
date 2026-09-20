using System.IO;
using NAudio.Wave;
using NVorbis;
using ThbgmPlayer.Audio;

namespace ThbgmPlayer.Viz;

/// <summary>
/// 隔离期的调试声源：直接播一个音频文件，边播边把样本灌进环形缓冲。
///
/// <b>它不是接入方案的一部分</b>（方案 §3.1）：接入期换成引擎输出链上的分接节点
/// （<c>VizTap</c>），窗口与渲染器一行都不用改 —— 这正是 <see cref="IVizFeed"/>
/// 存在的意义。之所以现在就要它，是因为「M1 结束后画布与取数链路能自己跑起来」
/// 必须有个不依赖播放器主体的声源。
///
/// 线程模型（唯一一处跨线程代码）：
/// <list type="bullet">
/// <item><b>写</b>：NAudio 的输出线程在 <c>TapProvider.Read</c> 里解码、写环、推进写指针。</item>
/// <item><b>读</b>：UI 线程在渲染节拍上 <see cref="ReadLatest"/>。</item>
/// <item>单写单读，靠「单调递增的写指针 + <see cref="Volatile"/>」同步，<b>不加锁</b>。</item>
/// </list>
///
/// 延迟对齐（已拍板「对齐」）：环里存的是**已交给声卡**的样本，而声卡还有约一个
/// 缓冲的存量没播出去。所以 <see cref="ReadLatest"/> 读的不是最新样本，而是
/// <c>写指针 - LatencyOffsetMs</c> 处 —— 屏幕上跳动的时刻与耳朵听到的时刻对齐。
/// </summary>
public sealed class VizDebugFeed : IVizFeed, IDisposable
{
    /// <summary>解码暂存区。要≥ NAudio 单次请求的帧数（DesiredLatency 100ms ≈ 4410 帧，留足余量）。</summary>
    private const int ScratchFrames = 32768;

    private readonly IDecoder _decoder;
    private readonly WaveOut _output;
    private readonly TapProvider _provider;
    private readonly float[] _scratch = new float[ScratchFrames * 2];

    /// <summary>环缓冲。与接入期的 <see cref="VizTap"/> **共用同一个实现**（见 <see cref="VizRing"/>）。</summary>
    private readonly VizRing _ring = new();

    /// <summary>延迟对齐的帧数偏移。</summary>
    private readonly int _latencyFrames;

    /// <summary>解码到头了。音频线程置位，UI 线程读 → volatile。</summary>
    private volatile bool _ended;

    private bool _disposed;

    /// <param name="path">音频文件路径（.wav / .ogg / .opus）。</param>
    /// <param name="latencyOffsetMs">延迟对齐偏移（毫秒）。取值见 <c>AppSettings.Viz.LatencyOffsetMs</c>。</param>
    public VizDebugFeed(string path, double latencyOffsetMs)
    {
        _decoder = CreateDecoder(path);

        _latencyFrames = VizRing.MsToFrames(latencyOffsetMs, _decoder.SampleRate);

        var format = new WaveFormat(_decoder.SampleRate, 16, _decoder.Channels);
        _provider = new TapProvider(this, format);

        // NAudio 3.0 把 WaveOutEvent 改名为 WaveOut，并且**去掉了 DesiredLatency** ——
        // 换成了 BufferMilliseconds × NumberOfBuffers（旧的那个属性本来就是这两者的乘积：
        // 默认 numberOfBuffers=2，desiredLatency=300 → bufferMilliseconds=150）。
        // 2 × 50ms = 100ms，与旧写法 `DesiredLatency = 100` 完全等价。
        _output = new WaveOut { NumberOfBuffers = 2, BufferMilliseconds = 50 };
        _output.Init(_provider);
    }

    // ------------------------------------------------------------------ IVizFeed

    public int SampleRate => _decoder.SampleRate;

    public int Channels => _decoder.Channels;

    /// <summary>是否正在出声。暂停 / 播完 / 还没开始都是 false。</summary>
    public bool IsPlaying => !_disposed && !_ended && _output.PlaybackState == PlaybackState.Playing;

    public int ReadLatest(Span<float> dstL, Span<float> dstR, int frames) =>
        _ring.ReadLatest(dstL, dstR, frames, _latencyFrames);

    // ------------------------------------------------------------------ 播放控制

    public void Start() => _output.Play();

    public void Stop() => _output.Stop();

    /// <summary>
    /// 暂停（保留播放位置）。**恢复用 <see cref="Resume"/>**。
    ///
    /// 暂停与「播完 / 停止」在方案 §2 里是**两种不同语义**（淡影 vs 归零），
    /// 区分靠 <see cref="HasEnded"/>：暂停时它为 false，播完时才是 true。
    /// </summary>
    public void Pause() => _output.Pause();

    /// <summary>从暂停处继续。</summary>
    public void Resume() => _output.Play();

    /// <summary>
    /// 是不是**播完了**（而不是暂停）。归零语义靠它触发 —— 见 <see cref="Pause"/>。
    /// </summary>
    public bool HasEnded => _ended;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 先停输出（会等播放线程退出），再放解码器 —— 反了就是「边解码边释放」
        try { _output.Stop(); } catch { }
        try { _output.Dispose(); } catch { }
        try { _decoder.Dispose(); } catch { }
    }

    // ------------------------------------------------------------------ 解码器

    private interface IDecoder : IDisposable
    {
        int SampleRate { get; }
        int Channels { get; }

        /// <summary>读一批交错 float 样本，返回<b>帧数</b>（不是浮点个数）。0 = 到头了。</summary>
        int Read(float[] dst, int frames);
    }

    /// <summary>
    /// 按扩展名路由解码器。
    ///
    /// ⚠️ <c>.ogg</c> **不能只看扩展名**：Ogg 容器里可能装 Vorbis 也可能装 Opus，
    /// 而 VorbisPizza 只认 Vorbis。实测踩过 —— 新典 demo 期的 <c>th06nc_16.ogg</c>
    /// 其实是 Ogg Opus（从 <c>.opus</c> 复制改名而来），直接丢给 VorbisPizza 只会抛
    /// 一句 NVorbis 的黑话「Could not find Vorbis data to decode.」。
    /// 所以 .ogg 走 <see cref="CreateOggDecoder"/> 先嗅编码。
    /// </summary>
    private static IDecoder CreateDecoder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("音频路径为空。", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException("找不到音频文件。", path);

        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".wav" => new WavDecoder(path),
            ".ogg" => CreateOggDecoder(path),
            // 新典（TH06NC）原生 .opus 是**自定义容器**（不是 Ogg），复用引擎现成的解码器。
            ".opus" => CreateOpusDecoder(path),
            _ => throw new NotSupportedException(
                $"调试声源不支持 {ext}（目前只有 .wav / .ogg / .opus）。"),
        };
    }

    /// <summary>
    /// .ogg 的两步走：先读第一个 Ogg 页的标识头判断编码，再决定用哪个解码器。
    /// Opus 目前没有解码路径（缺 Ogg 页解析器），所以**明确拒绝并说清怎么办**，
    /// 而不是让它掉进 VorbisPizza 里报一句外人看不懂的话。
    /// </summary>
    private static IDecoder CreateOggDecoder(string path)
    {
        string codec = SniffOggCodec(path);

        if (codec == "Opus")
            throw new NotSupportedException(
                "这个 .ogg 里装的是 Ogg Opus，不是 Vorbis，当前调试源只解 Vorbis。" +
                "请换真 Vorbis 的 .ogg（例如 tf/th175 的 op.ogg），" +
                "或直接给新典原生 .opus（那个已支持，走的是自定义容器）。");

        if (codec != "Vorbis")
            throw new NotSupportedException($"这个 .ogg 的首包不是 Vorbis 标识头：{codec}。");

        return new OggDecoder(path);
    }

    /// <summary>
    /// .opus 的分叉：**标准 Ogg Opus** 与**新典自定义容器**都用这个扩展名，
    /// 但前者我们解不了（没有 Ogg 页解析器）。先辨认再给准话 ——
    /// 否则会掉进 <see cref="OpusMemorySource"/> 里报「容器长度异常」，
    /// 那是在描述症状而不是原因。
    /// </summary>
    private static IDecoder CreateOpusDecoder(string path)
    {
        if (SniffOggCodec(path) == "Opus")
            throw new NotSupportedException(
                "这个 .opus 是标准 Ogg Opus（OggS 容器），当前只解新典的自定义 Opus 容器" +
                "（data/bgm/th06_NN.opus 那种：40 字节头 + N×488 字节记录）。");

        return new NcOpusDecoder(path);
    }

    /// <summary>
    /// 读 → 256 字节，解第一个 Ogg 页的**首包签名**判断编码。
    /// 只做识别，不做容器校验（CRC / 续包都不看）：这里要回答的只是「是什么编码」。
    /// 返回 "Vorbis" / "Opus"，其余情况返回 <c>未知(hex)</c> 让错误信息可行动。
    /// </summary>
    private static string SniffOggCodec(string path)
    {
        byte[] head = new byte[256];
        int read = 0;
        using (var fs = File.OpenRead(path))
        {
            while (read < head.Length)
            {
                int n = fs.Read(head, read, head.Length - read);
                if (n <= 0) break;
                read += n;
            }
        }
        return SniffOggCodec(head.AsSpan(0, read));
    }

    /// <summary>
    /// 嗅探本体，只看字节（无 I/O）—— 这样自检可以直接喂构造出来的 Ogg 页，
    /// 不必往磁盘写测试文件（运行期禁止写临时目录，<c>AppPaths.cs</c> 硬约束）。
    /// </summary>
    internal static string SniffOggCodec(ReadOnlySpan<byte> head)
    {
        if (head.Length < 28 || !head.Slice(0, 4).SequenceEqual("OggS"u8))
            return "非 Ogg 容器";

        // 页头 27 字节 + 段表 nsegs 字节 → 首包正文起点
        int pkt = 27 + head[26];
        if (pkt + 8 > head.Length) return "Ogg 页不完整";

        ReadOnlySpan<byte> sig = head.Slice(pkt, 8);
        if (sig.SequenceEqual("OpusHead"u8)) return "Opus";
        // Vorbis 标识头 = 0x01 + "vorbis"
        if (sig[0] == 0x01 && sig.Slice(1, 6).SequenceEqual("vorbis"u8)) return "Vorbis";
        return "未知(" + Convert.ToHexString(sig) + ")";
    }

    /// <summary>.wav：NAudio 的 <see cref="WaveFileReader"/> 转 float 采样。</summary>
    private sealed class WavDecoder : IDecoder
    {
        private readonly WaveFileReader _reader;
        private readonly ISampleProvider _samples;

        public WavDecoder(string path)
        {
            _reader = new WaveFileReader(path);
            _samples = _reader.ToSampleProvider();
        }

        public int SampleRate => _samples.WaveFormat.SampleRate;

        public int Channels => _samples.WaveFormat.Channels;

        // ⚠️ NAudio 3.0 的 ISampleProvider 也只有 Read(Span<float>) 一个成员 —— 三参数组重载已不在接口上。
        //    返回值语义没变，仍是「写入的浮点个数」，所以除以声道数才是帧数。
        public int Read(float[] dst, int frames) =>
            _samples.Read(dst.AsSpan(0, frames * Channels)) / Channels;

        public void Dispose() => _reader.Dispose();
    }

    /// <summary>.ogg：VorbisPizza。⚠️ 必须显式 <c>Initialize()</c>（旧 NVorbis 是构造时自动初始化）。</summary>
    private sealed class OggDecoder : IDecoder
    {
        private readonly VorbisReader _vorbis;

        public OggDecoder(string path)
        {
            FileStream fs = File.OpenRead(path);
            VorbisReader vr;
            try
            {
                vr = new VorbisReader(fs, true);   // closeOnDispose：流交给它管
                vr.Initialize();
            }
            catch
            {
                fs.Dispose();
                throw;
            }
            _vorbis = vr;
        }

        public int SampleRate => _vorbis.SampleRate;

        public int Channels => _vorbis.Channels;

        // ⚠️ VorbisPizza 的 ReadSamples(Span) 返回【帧数】，不是浮点个数（与旧 NVorbis 相反）
        public int Read(float[] dst, int frames) =>
            _vorbis.ReadSamples(dst.AsSpan(0, frames * Channels));

        public void Dispose() => _vorbis.Dispose();
    }

    /// <summary>
    /// 新典（TH06NC）的原生 <c>.opus</c>：**自定义容器**（40 字节头 + N×488 字节定长记录，
    /// 每记录 <c>[8:488]</c> 是 480 字节裸 Opus 包，960 帧/包 @48kHz），不是 Ogg。
    ///
    /// 容器解析 + Concentus 解码引擎里已经有了（<see cref="OpusMemorySource"/>），
    /// 这里**刻意不重写**，只做一层「16bit PCM 字节 → float 帧」的形态转换：
    /// 调试源不需要循环，loop 点传 <c>null</c>（= 不循环，IntroBytes=0、TotalBytes=整曲）。
    ///
    /// 与其它解码器的差异要记住：<see cref="OpusMemorySource"/> 在构造时就把整轨解进内存
    /// （约 22MB / 2 分钟曲），所以 <see cref="Read"/> 只是内存拷贝 —— 不省内存，但零解码开销。
    /// </summary>
    private sealed class NcOpusDecoder : IDecoder
    {
        private readonly OpusMemorySource _src;

        /// <summary>16bit 交错 PCM 暂存。按「一次最多被要 ScratchFrames 帧」预分配，之后零分配。</summary>
        private readonly byte[] _pcm;

        public NcOpusDecoder(string path)
        {
            byte[] raw = File.ReadAllBytes(path);
            _src = new OpusMemorySource(raw, null, null);

            if (_src.Format.BitsPerSample != 16)
                throw new NotSupportedException(
                    $"新典 Opus 解出的是 {_src.Format.BitsPerSample}bit，本适配器只处理 16bit。");

            _pcm = new byte[ScratchFrames * _src.Format.Channels * 2];
        }

        public int SampleRate => _src.Format.SampleRate;

        public int Channels => _src.Format.Channels;

        public int Read(float[] dst, int frames)
        {
            int want = frames * Channels * 2;    // 16bit × 声道数
            if (want > _pcm.Length) want = _pcm.Length;

            int got = _src.Read(_pcm, 0, want);   // 到头返回 0
            int n = got / 2;                      // 浮点个数
            for (int i = 0; i < n; i++)
            {
                short v = (short)(_pcm[2 * i] | (_pcm[2 * i + 1] << 8));
                dst[i] = v / 32768f;
            }

            return n / Channels;                  // 帧数
        }

        public void Dispose() => _src.Dispose();
    }

    // ------------------------------------------------------------------ 输出适配

    /// <summary>
    /// 挂在音频输出链上的适配器：一边把样本灌进环形缓冲，一边按 16bit 交错交给 NAudio。
    /// 「分接」这件事在隔离期就发生在这一层 —— 接入期换成引擎链上的同款节点。
    /// </summary>
    private sealed class TapProvider : IWaveProvider
    {
        private readonly VizDebugFeed _feed;

        public TapProvider(VizDebugFeed feed, WaveFormat format)
        {
            _feed = feed;
            WaveFormat = format;
        }

        public WaveFormat WaveFormat { get; }

        /// <summary>
        /// NAudio 3.0 的 <see cref="IWaveProvider"/> <b>只声明了 <c>Read(Span&lt;byte&gt;)</c></b>
        /// 这一个成员 —— 旧版的 <c>Read(byte[], int, int)</c> 已经不在接口上了，
        /// 只写数组版会直接报「不实现接口成员」。所以这里实现 Span 版。
        ///
        /// 返回策略：<b>永远填满并返回整个长度</b>。解码到头（<c>got == 0</c>）时尾部填静音，
        /// 而不是返回 0 —— 返回 0 会让 WaveOut 立刻判定流结束并触发 PlaybackStopped，
        /// 而我们希望窗口继续开着、画面自然衰减到静止。结束状态由 <see cref="VizDebugFeed.IsPlaying"/>
        /// （读 <c>_ended</c>）对外表达，不靠这个返回值。
        /// </summary>
        public int Read(Span<byte> buffer)
        {
            int channels = _feed._decoder.Channels;
            int blockAlign = WaveFormat.BlockAlign;
            if (blockAlign <= 0 || channels <= 0) return 0;

            int frames = buffer.Length / blockAlign;
            if (frames > ScratchFrames) frames = ScratchFrames;   // 防御：绝不越过暂存区

            int got = _feed._decoder.Read(_feed._scratch, frames);
            if (got > 0)
                _feed._ring.Write(_feed._scratch.AsSpan(), got, channels);
            else
                _feed._ended = true;

            // float → 16bit LE 交错
            int n = got * channels;
            for (int i = 0; i < n; i++)
            {
                float f = _feed._scratch[i];
                if (f > 1f) f = 1f;
                else if (f < -1f) f = -1f;
                short s = (short)MathF.Round(f * 32767f);
                buffer[2 * i] = (byte)s;
                buffer[2 * i + 1] = (byte)(s >> 8);
            }

            // 尾部补静音（含「frames 取整后被切掉的不到一帧的尾巴」）
            for (int i = n * 2; i < buffer.Length; i++) buffer[i] = 0;

            return buffer.Length;
        }
    }
}
