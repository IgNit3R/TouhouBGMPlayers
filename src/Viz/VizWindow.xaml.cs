using System.IO;
using System.Windows;
using ThbgmPlayer.Core;
using ThbgmPlayer.UI;
using ThbgmPlayer.Viz.Renderers;

namespace ThbgmPlayer.Viz;

/// <summary>
/// 可视化附件窗口。
///
/// 隔离期（命令行 <c>--viz [音频路径]</c>）它就是一个普通的自由窗口；
/// 接入期由 <c>IVizHost</c> 驱动几何（贴主窗口右侧、等高、最大化时内嵌）。
/// 后者本次全部实现，但挂的是 <c>FreeHost</c>，所以现在走的是「自由模式」分支。
///
/// M0 搭了窗口本身（几何持久化、字体、状态条）；M1 接上「取数 → 一帧分析 → 重绘」这条链；
/// M2 把五块区域里的四块（A 频谱条 / B 示波器 / C 电平+J 相位 / D 利萨如）都换成真渲染器，
/// 只剩「封面」是占位（图源属另一条支线，方案 §2 明确留后）；
/// M3 补上缩放钩子（贴附模式下只允许拖右边缘；自由模式完全不插手）——
/// 放置判定本身在 <c>VizPlacementLogic</c>（纯函数，隔离期由 <c>FreeHost</c> 驱动）。
///
/// ⚠️ 生命周期：<c>App.xaml</c> 未设 <c>ShutdownMode</c>（默认 OnLastWindowClose），
/// 窗口全关即退出进程 —— 隔离期只有一个窗口时正好是对的，**不要**改成 OnMainWindowClose：
/// <c>--viz</c> 路径下 MainWindow 未必如期赋值，改了会「永不退出」（方案 §7-R1）。
/// </summary>
public partial class VizWindow : Window
{
    /// <summary>当前调试声源路径（可为 null）。拖放换源会更新它。</summary>
    public string? DebugSource { get; private set; }

    /// <summary>命令行给的延迟对齐偏移（毫秒；null = 用设置里记住的值）。</summary>
    private readonly double? _delayMs;

    /// <summary>接入期注入的读侧（引擎分接节点）。**生命周期归注入方**，本窗口只读不拥有。</summary>
    private readonly IVizFeed? _injectedFeed;

    /// <summary>本轮的「播完」是否已经处理过 —— 归零只该做一次。</summary>
    private bool _endedHandled;

    /// <summary>
    /// 窗口是否真的显示过。没显示过时 <see cref="Window.Left"/> / <see cref="Window.Top"/>
    /// 是 NaN（WPF 用 NaN 表示「交给系统摆」），把这些值写进设置会把配置写脏，
    /// 下一次恢复几何就会出问题 —— 所以只在显示过之后才回写。
    /// </summary>
    private bool _wasShown;

    // ---- M1/M2：取数与渲染链路。没接上时为 null，此时面板留白，不报错。 ----
    /// <summary>当前读侧。可能是自己建的调试声源，也可能是**接入期注入的引擎分接节点**。</summary>
    private IVizFeed? _feed;

    /// <summary>
    /// **自己建的**调试声源（隔离期那套）。接入期注入引擎分接节点时它是 null ——
    /// 这个区分很重要：暂停 / 播完 / 拖放换源 / 释放，**都只对自己的声源做**
    /// （引擎那份归引擎管，窗口去动它就是把所有权搞混了）。
    /// </summary>
    private VizDebugFeed? _debugFeed;

    private VizAnalyzer? _analyzer;
    private VizPump? _pump;

    /// <summary>
    /// 四块面板的渲染器实例。**本窗口与主窗口内嵌那套共用这一份** ——
    /// 共享出去靠 <see cref="Renderers"/>，理由见 <see cref="VizRenderers"/>（D 的余辉）。
    /// </summary>
    private readonly VizRenderers _renderers = new();

    /// <summary>渲染器实例，供内嵌宿主共用（<c>MainWindow</c> 在装配第二套区域时拿去用）。</summary>
    public VizRenderers Renderers => _renderers;

    /// <summary>M3：窗口缩放钩子（贴附模式下只允许拖右边缘）。自由模式下它完全不插手。</summary>
    private VizWindowSizing? _sizing;

    /// <param name="debugSource">隔离期的调试声源路径（<c>--viz</c>）。接入期传 null。</param>
    /// <param name="delayMs">命令行给的延迟对齐偏移（毫秒）。</param>
    /// <param name="feed">
    /// **接入期注入的读侧**（引擎的 <c>VizTap</c>）。给了就用它，不再自建调试声源；
    /// 它的生命周期归注入方，本窗口只读不拥有。
    /// </param>
    public VizWindow(string? debugSource = null, double? delayMs = null, IVizFeed? feed = null)
    {
        InitializeComponent();

        // 渲染器实例交给区域用（内嵌那套会共用同一份，见 Renderers 的注释）
        Surface.Renderers = _renderers;

        DebugSource = debugSource;
        _delayMs = delayMs;
        _injectedFeed = feed;

        // 命令行给的偏移就记下来（方案 §2「听觉延迟对齐…偏移可配」）—— 下次不带参数也照旧
        if (delayMs is double d) AppSettings.Current.Viz.LatencyOffsetMs = d;

        Closing += VizWindow_Closing;
        Loaded += VizWindow_Loaded;
        PreviewKeyDown += VizWindow_PreviewKeyDown;   // 空格 = 暂停/继续（隔离期专用，见方法注释）

        RestoreGeometry();
        Theme.ApplyUserFont(this);

        // 标题挂上声源文件名。隔离期没有标题栏（WindowStyle=None），标题只出现在任务栏与
        // Alt+Tab 里 —— 而一次可能同时开着好几个 --viz 窗口，带文件名才分得清是哪个。
        // 接入期由宿主管窗口身份，届时改回固定的「可视化」。
        if (!string.IsNullOrWhiteSpace(debugSource))
            Title = $"可视化 — {Path.GetFileName(debugSource)}";

        SetupViz(debugSource);
    }

    /// <summary>
    /// 接线：调试声源 → 分析器 → 渲染节拍 → 四块面板（M1 打通的链路，M2 补齐渲染器）。
    /// **换源也走它**（拖放音频），所以不是"只跑一次"的初始化。
    ///
    /// 声源路径的优先级：命令行 / 拖入的路径 &gt; 上次记住的（<c>AppSettings.Viz.DebugSource</c>，
    /// 由 <see cref="VizWindow_Closing"/> 回写）。
    ///
    /// 两边都没有、或者打不开，都只写状态条 —— <b>窗口照常打开</b>（方案 §7-R7），
    /// 面板留白。这条路径在自检里会被反复走到，任何一次抛异常都是自检失败。
    /// </summary>
    private void SetupViz(string? source)
    {
        // 渲染器与面板的绑定和声源无关，放最前面：没有声源时面板也得是「已就位但没数据」，
        // 而不是「什么都不画」—— 后者会让「窗口打得开」这条底线看起来像坏了。
        // 幂等，重复调无妨（换源会再走一遍 SetupViz）。
        Surface.ApplyPanelVisibility();     // 哪几块真的画，由设置里的面板开关决定
        // 接入期：读侧是引擎注入的分接节点，没有「打开文件」这一步，
        // 也不该去读设置里那个调试源路径（那是隔离期的东西）。
        if (_injectedFeed is not null)
        {
            Bind(_injectedFeed);
            ClearStatus();
            Title = "可视化";
            return;
        }

        string? src = !string.IsNullOrWhiteSpace(source) ? source : AppSettings.Current.Viz.DebugSource;
        if (string.IsNullOrWhiteSpace(src))
        {
            ShowStatus("没有调试声源。用法：--viz <音频路径>（.wav / .ogg / .opus），或把音频拖到窗口里。");
            return;
        }

        // 先把新声源开起来再拆旧的 —— 换源失败时不该把正在放的那首也弄没了
        VizDebugFeed fresh;
        try
        {
            fresh = new VizDebugFeed(src!, _delayMs ?? AppSettings.Current.Viz.LatencyOffsetMs);
        }
        catch (Exception ex)
        {
            ShowStatus($"打不开调试声源：{ex.Message}");
            return;
        }

        _debugFeed?.Dispose();
        _debugFeed = fresh;
        DebugSource = src;
        _endedHandled = false;
        ClearStatus();

        if (!string.IsNullOrWhiteSpace(src)) Title = $"可视化 — {Path.GetFileName(src)}";

        Bind(fresh);
    }

    /// <summary>
    /// 把读侧接上。首次要建分析器与节拍；**换源只 <c>Rebind</c>、不重建分析器** ——
    /// 原因见 <see cref="VizAnalyzer.Rebind"/>：<c>ResetRevision</c> 跟着帧对象计数，
    /// 换一份新帧会让「归零」这个信号悄悄丢掉。
    /// </summary>
    private void Bind(IVizFeed feed)
    {
        _feed = feed;

        if (_analyzer is null)
        {
            _analyzer = new VizAnalyzer(feed);
            _pump = new VizPump(_analyzer, OnVizFrame);
        }
        else
        {
            _analyzer.Rebind(feed);
        }
    }

    /// <summary>
    /// 拆掉当前的声源链路（关窗口）。顺序：**先停节拍再停声源** ——
    /// 反过来的话，渲染回调会去读一个已经释放的声源。
    /// </summary>
    private void TearDownSource()
    {
        _pump?.Dispose();
        _pump = null;
        _analyzer = null;
        _feed = null;

        // ⚠️ 只释放**自己建的**那一个。引擎注入的那份归引擎管 ——
        // 窗口替它 Dispose 等于越权销毁别人的资产，而那个 tap 还要活到进程结束。
        _debugFeed?.Dispose();
        _debugFeed = null;
    }

    /// <summary>
    /// 让节拍跟上播放状态：在播就起搏。**停搏不归这里管** —— 那是
    /// <see cref="VizPump"/> 自己的退订策略（淡影收敛或撞上 1.5s 上限后自退订，方案 §3.5/R2）。
    ///
    /// 凡是「画面该重新动起来」的动作（起播 / 换源 / 取消暂停）都要调一下它。
    /// </summary>
    private void SyncPump()
    {
        if (_pump is null || _feed is null) return;
        if (_feed.IsPlaying) _pump.Start();
    }

    /// <summary>
    /// 渲染节拍回调（UI 线程，跟 vsync）。把当前帧交给四块面板重画。
    ///
    /// 四块共用**同一个帧对象**（方案 §3.3：重活每帧只算一次），所以这里不做任何拷贝；
    /// 面板各自只读自己要的那几个字段。D 的余辉靠 <see cref="VizFrame.Revision"/> 分辨
    /// 「这帧是新的吗」，与画几次无关 —— 见 <c>LissajousRenderer</c> 的说明。
    /// </summary>
    /// <summary>收到一帧就发给五块区域。**只有正在显示的那一套该被调**（见 VizSurfaceHost.Render）。</summary>
    private void OnVizFrame()
    {
        if (_analyzer is null) return;

        // 播完 → **归零**（方案 §2：比暂停彻底，清干净不留淡影）。
        // 只做一次 —— 否则每帧都清一遍，画面会永远停在"空"的样子上。
        // ⚠️ 只有调试声源会「播完」；引擎那条链的终止由 <see cref="NotifyTerminated"/> 说。
        if (!_endedHandled && _debugFeed is not null && _debugFeed.HasEnded)
        {
            _endedHandled = true;
            _analyzer.Reset();
        }

        // ⚠️ **不可见就别画**：主窗口最大化时画面搬进了内嵌宿主（那一套在画），
        // 这时再给隐藏窗口里的面板录一遍绘制指令纯属白费（每帧约 50KB 的分配，实测过）。
        if (IsVisible) Surface.Render(_analyzer.Frame);

        // 不管可见与否都把帧推出去 —— 内嵌宿主靠它拿同一帧。
        FrameReady?.Invoke(_analyzer.Frame);
    }

    /// <summary>
    /// 每渲染一帧就抛一次（**不管本窗口可见与否**）。
    ///
    /// 为什么要把帧推出去：分析器在这个窗口里，而另一块宿主（主窗口内嵌那套）需要**同一帧**。
    /// 与其让两边各持一个分析器（那就是两份平滑状态、两个真相源），不如把帧推出去。
    /// </summary>
    public event Action<VizFrame>? FrameReady;

    /// <summary>当前该由谁显示（<see cref="RefreshPlacement"/> 算出来的）。主窗口据此开关内嵌列。</summary>
    public VizPlacementMode PlacementMode { get; private set; } = VizPlacementMode.Free;

    /// <summary>放置方式变了（自由 ↔ 贴附 ↔ 内嵌）。主窗口据此开关内嵌那一列。</summary>
    public event Action? PlacementChanged;

    /// <summary>当前这一帧（还没接上读侧时为 null）。切到内嵌时会用它立刻画一帧。</summary>
    public VizFrame? Frame => _analyzer?.Frame;

    private void VizWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _wasShown = true;

        // 起声源放在这里而不是构造里：构造期（自检路径）连窗口句柄都还没有，
        // 开声源毫无意义，还会把自检拖成「听得到声音的自检」。
        if (_feed is null) return;

        // 只有自己建的调试声源需要「起播」（它自带 WaveOut）；引擎那份由播放器驱动
        _debugFeed?.Start();
        RenderOnce();    // ⚠️ 先画一帧：未播放时节拍不会起搏，没有这一帧就是空白窗口
        SyncPump();      // 在播才起搏；停搏由 VizPump 的退订策略自己负责
    }

    /// <summary>
    /// 顶部状态条（方案 §7-R7）：设备缺失、解码失败之类不致命的问题走这里，
    /// 窗口照常打开、渲染器保持静止态，绝不弹异常。</summary>
    public void ShowStatus(string text)
    {
        StatusText.Text = text;
        StatusBar.Visibility = Visibility.Visible;
    }

    /// <summary>清掉状态条。</summary>
    public void ClearStatus()
    {
        StatusText.Text = "";
        StatusBar.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 窗口拿到 HWND 之后装缩放钩子（方案 §3.7；P/Invoke 范式照 <c>MediaKeysHotkey</c>）。
    ///
    /// ⚠️ 用 <c>OnSourceInitialized</c> 而不是 <c>Loaded</c>：钩子要尽早装，
    /// 装晚了用户在装上之前那一小段时间里是会拖出花样的。摘钩则在 <c>Closing</c>（那时 HWND 还在）。
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _sizing = new VizWindowSizing(this) { Attached = _attachedMode };
        _sizing.Attach();
    }

    /// <summary>
    /// 贴附模式（接入期由 <c>IVizHost</c> / M5 置位）。贴附时位置由宿主算出来，
    /// 用户拖它没有意义，还会和宿主的定位互相打架 → 拖动必须关掉；
    /// 缩放也同样收窄成「只能拖右边缘」（见 <see cref="VizWindowSizing"/>）。
    ///
    /// 刻意**不从 <c>AppSettings.Viz.Attached</c> 初始化**：隔离期没有主体，
    /// 就算设置里勾了「贴附」也得按自由窗口处理，否则会变成一个既拖不动、
    /// 又没人给它算位置的死窗口。判定归宿主，窗口只认这个开关。
    /// </summary>
    public bool AttachedMode
    {
        get => _attachedMode;
        set
        {
            _attachedMode = value;
            if (_sizing is not null) _sizing.Attached = value;
        }
    }

    private bool _attachedMode;

    /// <summary>
    /// 宿主（接入期由主窗口注入）。**隔离期恒为 null** —— 没有宿主时窗口就按自由窗口活：
    /// 不算位置、不理显隐，与 M0～M4 的行为完全一致。
    ///
    /// 置 null = **解除贴附**（关掉可视化开关时主窗口就这么做）：窗口回到自由模式，
    /// 并把几何恢复成**用户自己记下的那一份**。
    /// </summary>
    public IVizHost? Host
    {
        get => _host;
        set
        {
            if (ReferenceEquals(_host, value)) return;

            if (_host is not null) _host.Changed -= OnHostChanged;
            _host = value;
            if (_host is not null) _host.Changed += OnHostChanged;

            if (_host is null)
            {
                // 解除贴附。⚠️ 别用 RestoreGeometry 之外的手段：贴附期间的几何是**宿主算出来的**，
                // 留着它会得到一个"看起来贴过"的窗口（尺寸位置都还停在宿主旁边）。
                // 回到自由态。主窗口那边要据此把内嵌那一列收掉 —— 所以也要通知一次。
                AttachedMode = false;
                PlacementMode = VizPlacementMode.Free;
                RestoreGeometry();
                PlacementChanged?.Invoke();
            }
            else
            {
                RefreshPlacement();
            }
        }
    }

    private IVizHost? _host;

    /// <summary>期望宽度（DIP）：用户记住的那一份。宿主右侧空间不够时判定会往下夹。</summary>
    private static double DesiredWidth =>
        AppSettings.Current.Viz.Width > 0 ? AppSettings.Current.Viz.Width : VizPlacementLogic.MinWidth;

    /// <summary>
    /// 最大化时是否改为**内嵌**（画面搬进主窗口里，曲目表被挤窄）。
    ///
    /// M6a 阶段这里曾恒为 <c>false</c>（那时还没有内嵌宿主，传 true 会「隐藏了却没人接手显示」）。
    /// **M6b 已把内嵌宿主做出来**（`VizSurfaceHost` + 主窗口的那一列），所以改回读设置项。
    /// </summary>
    private static bool EmbedWhenMaximized => AppSettings.Current.Viz.EmbedWhenMaximized;

    private void OnHostChanged() => RefreshPlacement();

    /// <summary>
    /// 重算放置。宿主几何一变（移动 / 缩放 / 最大化 / 最小化）就调一次。
    /// 判定本身在 <see cref="VizPlacementLogic.Decide"/>（纯函数），这里只负责把它落到窗口上。
    /// </summary>
    /// <summary>
    /// 立刻画一帧（不走节拍）。
    ///
    /// ⚠️ **窗口一打开就得调一次**：面板没有「帧」就什么都不画，而引擎侧未播放时
    /// <c>IsPlaying</c> 为 false —— 节拍不会起搏（就算起搏也会立刻自我退订），
    /// 于是窗口里会是一片空白，连底槽与刻度都看不见。实测就是这个症状。
    /// 拨了面板开关之后也要调（新开的那块否则要等下一次起搏才有内容）。
    ///
    /// 幂等、且不依赖播放状态；分析器还没建时是空操作（构造期会被调一次）。
    /// </summary>
    public void RenderOnce()
    {
        _analyzer?.Update();
        OnVizFrame();
    }

    /// <summary>
    /// 宿主通知「这次是**终止**（停止 / 切曲 / 播完），不是暂停」→ 归零（方案 §2）。
    ///
    /// 为什么必须由宿主来说：引擎的读侧只能表达「还在出声吗」，**分不出暂停与播完**
    /// （两者都不再出声），而方案 §2 要求前者走淡影、后者走归零。
    /// 隔离期的调试声源能自己判断（<c>HasEnded</c>），引擎这条链上只有知道内情的一方说得出。
    /// </summary>
    public void NotifyTerminated()
    {
        _analyzer?.Reset();
        RenderOnce();
    }

    /// <summary>
    /// 由宿主定期调（主窗口那个 100ms 的 tick）：**在播就把节拍起起来**。
    ///
    /// 为什么需要它：引擎**没有播放状态事件**，而节拍停着的时候没人去拉数据 ——
    /// 不轮询的话「点播放 → 画面不动」。停搏仍然归 <see cref="VizPump"/> 自己
    /// （淡影收敛或 1.5s 上限后自退订），这里只管起。
    /// 代价是**恢复播放的画面最多晚 100ms 起来**，可接受。
    /// </summary>
    public void RefreshFeedState()
    {
        if (_pump is null || _feed is null) return;
        if (_feed.IsPlaying && !_pump.IsRunning) _pump.Start();
    }

    public void RefreshPlacement()
    {
        if (_host is null) return;
        ApplyPlacement(VizPlacementLogic.Decide(_host, DesiredWidth, EmbedWhenMaximized));
    }

    /// <summary>
    /// 按设置里的**面板开关**决定哪几块真的画。
    ///
    /// 做法是「有渲染器就画、没有就空白」—— <see cref="VizPanel.Renderer"/> 为 null 时
    /// 面板什么都不画（当初就是这么设计的），于是不必去动 XAML 的可见性，
    /// 也就**不会牵动 2.3:1 / 7:3 那套分区比例**。
    ///
    /// ⚠️ 这是**刻意的**：关掉一块只是不画、不重排布局 ——
    /// 否则每开关一次整块画面都会跳一下。封面是纯 XAML 装饰，那个才切可见性。
    ///
    /// 设置窗口关掉后由主窗口再调一次（改了要立刻生效）。
    /// </summary>
    public void ApplyPanelVisibility()
    {
        Surface.ApplyPanelVisibility();

        // 刚打开的某一块也得立刻有内容 —— 否则要等下一次节拍起搏才看得见
        RenderOnce();
    }

    /// <summary>
    /// 把一次判定落到窗口上。**只做三件事**：开关贴附、写几何、显隐。
    /// 顺序有讲究：**先开关贴附再写几何** —— 贴附会连带改缩放钩子的策略
    /// （只允许拖右边缘），写几何时钩子已经在正确状态上了。
    /// </summary>
    private void ApplyPlacement(VizWindowAction a)
    {
        PlacementMode = a.Mode;
        AttachedMode = a.Attached;

        if (a.SetBounds)
        {
            Left = a.Left;
            Top = a.Top;
            Width = a.Width;
            Height = a.Height;
        }

        if (a.Hide)
        {
            if (IsVisible) Hide();
        }
        else if (a.Show && !IsVisible)
        {
            Show();
        }

        // 每次落地都通知一次（订阅方幂等）。⚠️ 不只在"方式变了"时才通知 ——
        // 宿主被摘掉再装回来、或者尺寸变了都可能需要对方重算，多通知一次不花钱。
        PlacementChanged?.Invoke();
    }

    /// <summary>
    /// 拖动窗口。<c>WindowStyle=None</c> + <c>CaptionHeight=0</c> 之后没有可拖的标题栏，
    /// 只能由内容区接管；整块画布都能拖 —— 里面没有可点控件，不会误触。
    /// （边缘的缩放由 WindowChrome 在 WM_NCHITTEST 那层处理，不走这里。）
    /// </summary>
    private void Root_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (AttachedMode) return;

        // DragMove 在左键并非按下时会抛 InvalidOperationException
        if (e.ButtonState != System.Windows.Input.MouseButtonState.Pressed) return;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // 极少见的按键状态竞态。拖不起来就算了，不能因此把窗口搞崩。
        }
    }

    /// <summary>
    /// 空格 = 暂停 / 继续。**隔离期专用的可观测手段**：方案 §8.1#4 要求「暂停 → 各块衰减」
    /// 在隔离期就能验，而隔离期的调试声源没有任何播放控件。
    ///
    /// 刻意不做窗口内按钮：附件窗口没有标题栏、也不放控件（M0 决策），
    /// 一个键既测得了、又不破坏「窗口里什么都点不到」。**接入期该删掉** ——
    /// 那时暂停由引擎的播放状态驱动，这个键会变成第二个真相源。
    /// </summary>
    private void VizWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // 只对自己建的调试声源管用：接入期暂停由播放器控制，窗口不该有第二个暂停入口
        if (e.Key != System.Windows.Input.Key.Space || _debugFeed is null) return;

        if (_debugFeed.IsPlaying)
        {
            // 暂停：**不立刻停节拍** —— 让画面把淡影衰减跑完，pump 自己会退订（方案 §3.5）
            _debugFeed.Pause();
        }
        else if (!_debugFeed.HasEnded)
        {
            _debugFeed.Resume();
            SyncPump();
        }
        else
        {
            return;   // 播完了，空格没有意义（要重听得拖进来或重开）
        }

        e.Handled = true;
    }

    /// <summary>
    /// 拖入音频文件即换源（方案 §四「拖放」、§8.1#3「拖入音频 → 出声 + 五块同时活动」）。
    /// 换源 = 切曲 → 归零（<see cref="VizAnalyzer.Rebind"/> 内部已经做了）。
    /// </summary>
    private void Root_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;

        // 拖放换源只对隔离期的调试声源有意义：接入期声音是播放器放出来的，
        // 往窗口里丢个文件不应该把它换掉。
        if (_injectedFeed is not null) return;

        string? path = TryGetDropPath(e.Data);
        if (path is null) return;

        SetupViz(path);
        if (_debugFeed is null) return;      // 打不开（状态条已报错），保持原样

        _debugFeed.Start();
        SyncPump();
    }

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        bool accept = _injectedFeed is null && TryGetDropPath(e.Data) is not null;
        e.Effects = accept ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>从拖放数据里取第一个文件路径。不是文件拖放就返回 null（调用方据此拒绝）。</summary>
    private static string? TryGetDropPath(System.Windows.IDataObject? data)
    {
        if (data is null || !data.GetDataPresent(DataFormats.FileDrop)) return null;
        if (data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return null;
        return files[0];
    }

    /// <summary>
    /// 恢复上次的窗口几何。越界（屏幕拔掉了 / 分辨率变了）就只留默认位置，
    /// 与 <c>MainWindow.RestoreWindowGeometry</c> 同一套判据，避免窗口跑到看不见的地方。
    /// </summary>
    private void RestoreGeometry()
    {
        var viz = AppSettings.Current.Viz;

        if (viz.Width > 0) Width = viz.Width;
        if (viz.Height > 0) Height = viz.Height;

        if (viz.Left is double l && viz.Top is double t)
        {
            bool onScreen =
                l > -SystemParameters.VirtualScreenWidth + 120 && l < SystemParameters.VirtualScreenWidth - 120 &&
                t > -SystemParameters.VirtualScreenHeight + 120 && t < SystemParameters.VirtualScreenHeight - 120;
            if (onScreen)
            {
                Left = l;
                Top = t;
            }
        }
    }

    private void VizWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // 收尾与「是否回写几何」无关，所以放在最前面。
        // 顺序要紧：① 先摘消息钩子 —— 必须在 HWND 还在的时候摘（Closing 里还在，Closed 就没了）；
        // ② 再停节拍；③ 最后停声源（顺序在 TearDownSource 里）——
        // 反过来的话，渲染回调会去读一个已经释放的声源。
        _sizing?.Dispose();
        _sizing = null;

        // 摘宿主订阅：窗口都要关了，再有人推 Changed 就是白发一次重算
        if (_host is not null)
        {
            _host.Changed -= OnHostChanged;
            _host = null;
        }

        TearDownSource();

        // 没显示过就不回写。自检路径会构造一个不 Show 的窗口，
        // 那时 Left/Top 还是 NaN、Width/Height 是从设置里读出来的，
        // 回写一遍等于把「期望值」当成「实际值」固化下来，没有意义还容易写脏。
        // 未 Show 过不回写（那时 Left/Top 还是 NaN）；**贴附下也不回写**
        // —— 那时的几何是宿主算出来的，属于宿主、不属于这个窗口。
        // 判据在 VizPlacementLogic.ShouldPersistGeometry（纯函数，自检有断言）。
        if (!VizPlacementLogic.ShouldPersistGeometry(_wasShown, AttachedMode)) return;

        var viz = AppSettings.Current.Viz;

        // 最大化 / 最小化时用 RestoreBounds（还原后的尺寸），与主窗口同一套处理思路
        if (WindowState == WindowState.Normal)
        {
            if (IsPositiveFinite(Width)) viz.Width = Width;
            if (IsPositiveFinite(Height)) viz.Height = Height;
            if (double.IsFinite(Left)) viz.Left = Left;
            if (double.IsFinite(Top)) viz.Top = Top;
        }
        else
        {
            var b = RestoreBounds;
            if (!b.IsEmpty)
            {
                if (IsPositiveFinite(b.Width)) viz.Width = b.Width;
                if (IsPositiveFinite(b.Height)) viz.Height = b.Height;
                if (double.IsFinite(b.Left)) viz.Left = b.Left;
                if (double.IsFinite(b.Top)) viz.Top = b.Top;
            }
        }

        // 命令行给的路径记下来，下次 --viz 不带参数也能接着听（调试期便利）
        if (!string.IsNullOrWhiteSpace(DebugSource)) viz.DebugSource = DebugSource!;

        AppSettings.Current.Save();
    }

    private static bool IsPositiveFinite(double v) => double.IsFinite(v) && v > 0;
}
