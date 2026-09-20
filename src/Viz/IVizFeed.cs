namespace ThbgmPlayer.Viz;

/// <summary>
/// 可视化读侧的唯一接口。
///
/// **窗口 / 渲染器只依赖这个**，绝不依赖 <c>PlayerEngine</c> —— 隔离期由调试声源实现，
/// 接入期换成引擎里的分接节点（<see cref="VizTap"/>），窗口侧一行不用改。
///
/// 实现必须是「单写单读 + 无锁」：调用方在 UI 线程按帧读，写入方在音频线程。
/// </summary>
public interface IVizFeed
{
    /// <summary>采样率。渲染器要靠它把「12ms」「20Hz–20kHz」换算成样本数与 bin。</summary>
    int SampleRate { get; }

    /// <summary>声道数（本项目恒为 2）。</summary>
    int Channels { get; }

    /// <summary>是否正在出声。暂停 / 结束 / 未开始都为 false。</summary>
    bool IsPlaying { get; }

    /// <summary>
    /// 把「最近 frames 帧」拷进调用方缓冲，L/R 分离写入。
    /// 返回实际写出的帧数，不足时以静音（0）补齐 —— 调用方不需要判空。
    /// 允许单帧撕裂（频谱看不见、利萨如一个噪点），不值得为此加锁。
    /// </summary>
    int ReadLatest(Span<float> dstL, Span<float> dstR, int frames);
}
