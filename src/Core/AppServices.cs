using ThbgmPlayer.Audio;

namespace ThbgmPlayer.Core;

/// <summary>
/// 跨窗口存活的进程级单例。
///
/// 存在的理由：换主题要重建主窗口，而正在播的音频不该跟着断：
/// 播放器内核本身与窗口毫无耦合（<see cref="PlayerEngine"/> 的构造函数
/// 一个 UI 参数都没有），把它的生命周期挂在主窗口上是历史包袱。
///
/// ⚠️ 除了 <see cref="Dispose"/> 之外任何地方都不要释放引擎；
/// 进程退场时由 App.OnExit 统一收，主窗口重建（换主题）绝不走这里。
/// </summary>
public static class AppServices
{
    private static PlayerEngine? _engine;

    /// <summary>播放引擎。第一次访问时创建，此后跨窗口存活。</summary>
    public static PlayerEngine Engine => _engine ??= new PlayerEngine();

    /// <summary>
    /// 释放全部进程级单例。只允许 App.OnExit 调用：
    /// 主窗口重建（换主题）时走这里的话，正在播的音频当场断流。
    /// </summary>
    public static void Dispose()
    {
        _engine?.Dispose();
        _engine = null;
    }
}
