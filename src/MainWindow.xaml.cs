using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ThbgmPlayer.Audio;
using ThbgmPlayer.Core;
using ThbgmPlayer.Data;
using ThbgmPlayer.UI;

namespace ThbgmPlayer;

/// <summary>曲目表的一行。显示格式：[作品代号] - [曲序] - [曲名]。</summary>
public sealed class TrackRow : ViewModelBase
{
    public TrackRow(GameDef game, TrackDef track, bool available)
    {
        GameId = game.Id;
        GameCode = game.Code;
        GameName = game.ShortName;
        No = track.No;
        Title = track.Title;
        IsAvailable = available;
        LengthText = FormatTime(track.LengthTime);
        // 统一风格：循环曲（主系列与黄昏作）都是 intro/loop 两段时长；黄昏作不循环曲标「不循环」。
        // 黄昏作的三个时间属性已按秒计算（TrackDef.IntroTime/LoopTime），格式与主系列完全一致。
        LoopText = track.DurationSec is not null && !track.HasLoopSeconds
            ? "不循环"
            : $"intro {FormatTime(track.IntroTime)} / loop {FormatTime(track.LoopTime)}";
    }

    public string GameId { get; }
    public string GameCode { get; }
    public string GameName { get; }
    public int No { get; }
    public string Title { get; }
    public bool IsAvailable { get; }

    /// <summary>曲序补零显示（01、02 …）。</summary>
    public string DisplayNo => No.ToString("00");

    public string LengthText { get; }
    public string LoopText { get; }

    private bool _isPlaying;

    /// <summary>
    /// 是否是正在播放的那一首。与「选中」无关 ——
    /// 选中行可以切到别的曲目，但仍要能一眼看出正在播的是哪首。
    /// 灵界版不单独成行，所以切到灵界版时这一行照样亮着。
    /// </summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        set => SetField(ref _isPlaying, value);
    }

    /// <summary>显示在「正在播放」那一栏。作品用名称而非代号（用户选择）。</summary>
    public string DisplayText => $"{GameName} - {DisplayNo} - {Title}";

    public TrackRef Ref => new(GameId, No);

    public static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
            : $"{t.Minutes:D2}:{t.Seconds:D2}";
}

/// <summary>
/// 播放列表下拉框里的一项：作品 / 收藏 / 自定义列表。
/// DisplayName 可变 —— 增删条目后只改文字就能就地更新，不必重建整个下拉
/// （重建会连带刷新曲目表，把用户当前选中的行弄丢）。
/// </summary>
public sealed class PlaylistItem : ViewModelBase
{
    private string _displayName = "";

    public string DisplayName
    {
        get => _displayName;
        set => SetField(ref _displayName, value);
    }

    /// <summary>作品代号；收藏与自定义列表为 null。</summary>
    public string? GameId { get; init; }

    /// <summary>自定义列表；其余类型为 null。</summary>
    public CustomPlaylist? Custom { get; init; }

    public bool IsFavorites { get; init; }

    public bool IsAvailable { get; init; } = true;

    public override string ToString() => DisplayName;
}

public partial class MainWindow : Window
{
    private PlayerEngine? _engine;
    private readonly DispatcherTimer _tick;
    private bool _seeking;          // 用户正在拖动进度条
    private bool _updatingSlider;   // 代码正在回写进度条（此时不能触发 seek）

    /// <summary>
    /// 构造是否已走完。XAML 里给控件写的默认值（SelectedIndex="0"、Value="0.8" 之类）
    /// 会在 InitializeComponent() 期间就触发一次变更事件，而那时设置还没被应用上去 ——
    /// 放任它们写进设置，配置就会在每次启动时被覆盖回默认值。
    /// 所以真正代表「用户操作」的写设置动作，都要先过这个开关。
    /// </summary>
    private bool _ready;

    private string _nowDetail = ""; // 曲目信息原文（灵界版状态前缀另加，避免反复叠加）
    private readonly Random _rng = new();
    private MediaKeysHotkey? _mediaKeys;   // 全局多媒体键；句柄可用后才创建
    private DispatcherTimer? _selPreloadTimer;   // ③ 选中即预读的防抖

    public MainWindow()
    {
        InitializeComponent();

        Closing += Window_Closing;

        RestoreWindowGeometry();
        ApplyFont();

        _engine = new PlayerEngine();
        if (_engine.InitError is not null)
            PlaylistSummary.Text = $"音频设备初始化失败：{_engine.InitError}";

        var pb = AppSettings.Current.Playback;
        _engine.Volume = (float)pb.Volume;
        VolumeSlider.Value = pb.Volume;
        LoopModeCombo.SelectedIndex = (int)pb.LoopMode;

        PlaylistStore.Load();
        BuildPlaylists();
        UpdateStatus();

        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _tick.Tick += Tick;
        _tick.Start();

        // ③ 选中即预读的防抖计时器：选中变化就重置，停留满 250ms 才真正开读
        _selPreloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _selPreloadTimer.Tick += SelectionPreload_Tick;

        // 排序延迟计时器：可排序列表里按在行上，按住满 300ms 才进入可拖排序状态。
        // 到时换个十字箭头光标，提示「现在拖动会移动这一行」
        _reorderDelayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ReorderDelayMs) };
        _reorderDelayTimer.Tick += (_, _) =>
        {
            _reorderDelayTimer.Stop();
            _reorderDelayElapsed = true;
            if (_dragArmed && !_marqueeActive && !_reorderActive)
                TrackGrid.Cursor = System.Windows.Input.Cursors.SizeAll;
        };

        UpdateTransportEnabled();
        UpdateAltButton();
        UpdateFavoriteButton();
        UpdatePlaylistMenus();

        // 到此设置才真正应用到界面上，此后控件变化才算用户操作
        _ready = true;
    }

    /// <summary>恢复上次的窗口尺寸与位置。位置若已跑到所有屏幕之外则忽略。</summary>
    private void RestoreWindowGeometry()
    {
        var ui = AppSettings.Current.Ui;
        if (ui.Width > 0) Width = ui.Width;
        if (ui.Height > 0) Height = ui.Height;

        if (ui.Left is double l && ui.Top is double t)
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

        if (ui.Maximized) WindowState = WindowState.Maximized;
    }

    // ---------- 多媒体键 ----------

    /// <summary>窗口句柄一可用就创建热键助手并按设置注册。</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _mediaKeys = new MediaKeysHotkey(this, OnMediaKey);
        ApplyGlobalMediaKeys();
    }

    /// <summary>按设置注册 / 注销全局多媒体键。幂等，设置窗口关闭后也会调一次。</summary>
    private void ApplyGlobalMediaKeys()
    {
        if (_mediaKeys is null) return;
        if (AppSettings.Current.Playback.GlobalMediaKeys) _mediaKeys.Register();
        else _mediaKeys.Unregister();
    }

    /// <summary>全局多媒体键回调（UI 线程）。</summary>
    private void OnMediaKey(int id)
    {
        switch (id)
        {
            case MediaKeysHotkey.PlayPauseId: PlayPause(); break;
            case MediaKeysHotkey.PrevId: PlayPrev(); break;
            case MediaKeysHotkey.NextId: PlayNext(); break;
        }
    }

    /// <summary>
    /// 窗口聚焦时的多媒体键兜底。全局热键注册成功时按键被系统拦成 WM_HOTKEY，
    /// 走不到这里；只有注册失败（被别的程序占用）时这里才接得住。
    /// </summary>
    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case System.Windows.Input.Key.MediaPlayPause: PlayPause(); e.Handled = true; break;
            case System.Windows.Input.Key.MediaPreviousTrack: PlayPrev(); e.Handled = true; break;
            case System.Windows.Input.Key.MediaNextTrack: PlayNext(); e.Handled = true; break;
        }
    }

    /// <summary>
    /// 界面字体（中文 UI）套整个窗口；曲名字体（日文内容）只套三处：
    /// 曲目表 + 正在播放的两行日文。参数行保持 Consolas 不动。
    /// </summary>
    private void ApplyFont()
    {
        Theme.ApplyUserFont(this);
        Theme.ApplyContentFont(TrackGrid);
        Theme.ApplyContentFont(NowPlayingText);
        Theme.ApplyContentFont(NowPlayingGame);
    }

    // ---------- 列表 ----------

    /// <summary>构建播放列表下拉框：21 部作品 + 收藏 + 自定义列表。</summary>
    private void BuildPlaylists()
    {
        var items = new List<PlaylistItem>();

        foreach (var g in TrackIndex.Games)
        {
            bool configured = AppSettings.Current.GetPath(g.Id) is not null;
            items.Add(new PlaylistItem
            {
                // 只显示代号，不跟曲目数 —— 曲目数在下面的汇总栏里已经有了
                DisplayName = g.Code,
                GameId = g.Id,
                IsAvailable = configured,
            });
        }

        items.Add(new PlaylistItem
        {
            DisplayName = "★ 收藏",
            IsFavorites = true,
        });

        foreach (var list in PlaylistStore.Lists)
        {
            items.Add(new PlaylistItem
            {
                DisplayName = $"♪ {list.Name}",
                Custom = list,
            });
        }

        PlaylistCombo.ItemsSource = items;

        // 默认落在最近播放的作品上，没记录就选第一个
        var last = AppSettings.Current.LastPlayed.Game;
        var target = items.FirstOrDefault(i => i.GameId == last && i.IsAvailable)
                     ?? items.FirstOrDefault(i => i.GameId is not null)
                     ?? items[0];
        PlaylistCombo.SelectedItem = target;
    }

    /// <summary>重建下拉框，并把原来选中的那一项重新选回来。</summary>
    private void RebuildPlaylistsKeepSelection()
    {
        var cur = PlaylistCombo.SelectedItem as PlaylistItem;
        string? keepGame = cur?.GameId;
        bool keepFav = cur?.IsFavorites ?? false;
        CustomPlaylist? keepCustom = cur?.Custom;

        BuildPlaylists();

        var again = PlaylistCombo.Items.Cast<PlaylistItem>().FirstOrDefault(i =>
                        (keepGame is not null && i.GameId == keepGame) ||
                        (keepFav && i.IsFavorites) ||
                        (keepCustom is not null && ReferenceEquals(i.Custom, keepCustom)))
                    ?? PlaylistCombo.Items.Cast<PlaylistItem>().FirstOrDefault();

        PlaylistCombo.SelectedItem = again;
    }

    private void PlaylistCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        LoadPlaylist(PlaylistCombo.SelectedItem as PlaylistItem);

    /// <summary>按选中的列表重建曲目表。增删条目后也用它刷新。</summary>
    private void LoadPlaylist(PlaylistItem? item)
    {
        if (item is null)
        {
            TrackGrid.ItemsSource = null;
            PlaylistSummary.Text = "";
        }
        else if (item.Custom is CustomPlaylist list)
        {
            TrackGrid.ItemsSource = TrackRowsOf(list.Items.Select(TrackRef.Parse));

            // 只显示曲目数：列表类型在下拉框里已经写着，没必要重复一遍
            PlaylistSummary.Text = $"{list.Count} 首";
        }
        else if (item.IsFavorites)
        {
            TrackGrid.ItemsSource = TrackRowsOf(PlaylistStore.Favorites);
            PlaylistSummary.Text = $"{PlaylistStore.Favorites.Count} 首";
        }
        else if (item.GameId is string gid && TrackIndex.ById.TryGetValue(gid, out var game))
        {
            bool configured = AppSettings.Current.GetPath(game.Id) is not null;

            TrackGrid.ItemsSource = game.Tracks
                .Select(t => new TrackRow(game, t, configured))
                .ToList();

            // 未设置路径的信息在别处都能看到（下拉灰显、曲目行灰显、双击时的提示），
            // 这里不重复占地方
            PlaylistSummary.Text = $"{game.TrackCount} 首";
        }
        else
        {
            TrackGrid.ItemsSource = null;
            PlaylistSummary.Text = "";
        }

        UpdateTransportEnabled();
        UpdatePlaylistMenus();
        UpdateFavoriteButton();
        UpdatePlayingRow();
    }

    /// <summary>把「正在播放」标记刷到曲目表上。换曲、换列表、切灵界版后都要调。</summary>
    private void UpdatePlayingRow()
    {
        var cur = _engine?.Current ?? default;
        foreach (var r in Rows)
            r.IsPlaying = !cur.IsEmpty && r.Ref == cur;
    }

    /// <summary>把一串 (作品, 曲序) 还原成曲目行，找不到的直接丢掉。</summary>
    private static List<TrackRow> TrackRowsOf(IEnumerable<TrackRef> refs) =>
        refs.Select(RowFromRef).Where(r => r is not null).Select(r => r!).ToList();

    private static TrackRow? RowFromRef(TrackRef r)
    {
        if (!TrackIndex.ById.TryGetValue(r.GameId, out var game)) return null;

        var track = game.Tracks.FirstOrDefault(t => t.No == r.TrackNo);
        if (track is null) return null;

        return new TrackRow(game, track, AppSettings.Current.GetPath(game.Id) is not null);
    }

    /// <summary>列表内容变了（增删条目）时重画曲目表，不改变当前选中的列表。</summary>
    private void RefreshPlaylistView() => LoadPlaylist(PlaylistCombo.SelectedItem as PlaylistItem);

    /// <summary>当前选中的自定义列表；选中的是作品或收藏时为 null。</summary>
    private CustomPlaylist? CurrentCustomList => (PlaylistCombo.SelectedItem as PlaylistItem)?.Custom;

    /// <summary>
    /// 右键菜单与收藏按钮的作用对象：曲目表的选中行（支持多选）；
    /// 一行都没选中时退回正在播放的那首（DESIGN_v3.md §5.4）。
    /// </summary>
    private List<TrackRef> SelectedRefs()
    {
        var rows = TrackGrid.SelectedItems
            .Cast<TrackRow>()
            .Select(r => r.Ref)
            .Where(r => !r.IsEmpty)
            .ToList();

        if (rows.Count > 0) return rows;

        return _engine?.Current is TrackRef cur && !cur.IsEmpty
            ? new List<TrackRef> { cur }
            : new List<TrackRef>();
    }

    private List<TrackRow> Rows =>
        TrackGrid.ItemsSource as List<TrackRow> ?? new List<TrackRow>();

    /// <summary>框选批量改选择集时置位：压着 SelectionChanged，免得面板跟着每行闪。</summary>
    private bool _suppressSelectionEvent;

    private void TrackGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionEvent) return;
        OnTrackSelectionChanged();
    }

    private void OnTrackSelectionChanged()
    {
        // ③ 选中即预读：每次选中变化都重置防抖，停留满 250ms 才真正开读
        _selPreloadTimer?.Stop();
        _selPreloadTimer?.Start();

        if (TrackGrid.SelectedItem is not TrackRow row)
        {
            if (_engine?.Current is null)
            {
                NowPlayingText.Text = "未播放";
                NowPlayingDetail.Text = "—";
            }
            AltButton.IsEnabled = false;
            UpdateFavoriteButton();
            return;
        }

        var game = TrackIndex.ById[row.GameId];
        var track = game.Tracks.First(t => t.No == row.No);

        // 已经选中的就是正在播的那首时不覆盖面板，避免选中态把播放信息冲掉
        if (_engine?.Current != row.Ref)
        {
            NowPlayingText.Text = row.DisplayText;
            NowPlayingGame.Text = game.Name;
            _nowDetail = Describe(track);
            // 选中的不是正在播的那首，所以不带灵界版前缀
            NowPlayingDetail.Text = _nowDetail;
        }

        // 灵界版按钮：仅当前正在播的这首有灵界版时可用
        AltButton.IsEnabled = track.HasAlt && row.IsAvailable && _engine?.Current == row.Ref;

        UpdateFavoriteButton();
    }

    private void TrackGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        PlaySelectedRow();

    /// <summary>播放曲目表当前选中行（双击与回车共用）。</summary>
    private void PlaySelectedRow()
    {
        if (TrackGrid.SelectedItem is not TrackRow row) return;

        if (!row.IsAvailable)
        {
            MessageBox.Show(this, $"{row.GameCode} 还没设置路径，先到「设置 → 路径」里指定。",
                            "无法播放", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        PlayTrack(row.Ref);
    }

    // ---------- 播放 ----------

    /// <summary>播放指定曲目。找不到或没配路径就提示，不抛异常。</summary>
    private void PlayTrack(TrackRef r)
    {
        if (_engine is null) return;

        if (!TrackIndex.ById.TryGetValue(r.GameId, out var game))
            return;

        var track = game.Tracks.FirstOrDefault(t => t.No == r.TrackNo);
        if (track is null) return;

        // 同一首曲子重入时（切循环模式走的就是这条路）保留灵界版状态，换曲则回到主版
        bool keepAlt = _engine.UsingAlt && _engine.Current is TrackRef prev && prev == r;

        try
        {
            _engine.Play(game, track, keepAlt);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "无法播放", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        NowPlayingText.Text = $"{game.ShortName} - {track.No:00} - {track.Title}";
        NowPlayingGame.Text = game.Name;   // 全名（含副标题），主标题那行已经用过 ShortName
        _nowDetail = Describe(track);
        RefreshNowPlaying();

        AppSettings.Current.LastPlayed.Game = game.Id;
        AppSettings.Current.LastPlayed.TrackNo = track.No;

        UpdateTransportEnabled();
        UpdatePlayButton();
        UpdateAltButton();
        UpdateFavoriteButton();
        UpdatePlayingRow();

        // 让曲目表选中行跟着走
        var row = Rows.FirstOrDefault(x => x.Ref == r);
        if (row is not null && !ReferenceEquals(TrackGrid.SelectedItem, row))
            TrackGrid.SelectedItem = row;

        SchedulePreloads(game, track);
    }

    /// <summary>
    /// 播放一首之后安排预读（docs/2026-09-06-checkpoint-preload-plan.md 的三预测源之二）：
    /// ② 灵界版配对：有灵界版就预读没在播的那个版本，第一次切灵界版零等待
    /// ① 列表顺序：非随机模式预读当前列表的下一首，自动连播零等待
    /// ③ 选中即预读在 OnTrackSelectionChanged 的防抖里。
    /// </summary>
    private void SchedulePreloads(GameDef game, TrackDef track)
    {
        // ② 灵界版配对（仅 TH13 部分曲目；预读「没在播的那个版本」）
        if (track.HasAlt && _engine is not null)
            PreloadCache.Preload(game, track, !_engine.UsingAlt);

        // ① 列表顺序（随机模式下一首不可预测，不浪费这次读盘）
        if (AppSettings.Current.Playback.LoopMode == LoopMode.Shuffle) return;

        if (NextRef() is not TrackRef nr) return;
        if (!TrackIndex.ById.TryGetValue(nr.GameId, out var ng)) return;
        var nt = ng.Tracks.FirstOrDefault(t => t.No == nr.TrackNo);
        if (nt is null) return;
        PreloadCache.Preload(ng, nt, false);
    }

    /// <summary>③ 选中即预读：防抖到时才真正开读，乱扫与框选扫过都不会触发。</summary>
    private void SelectionPreload_Tick(object? sender, EventArgs e)
    {
        _selPreloadTimer?.Stop();

        if (TrackGrid.SelectedItem is not TrackRow row || !row.IsAvailable) return;
        if (_engine?.Current == row.Ref) return;   // 正在播的那首不需要预读

        if (!TrackIndex.ById.TryGetValue(row.GameId, out var g)) return;
        var t = g.Tracks.FirstOrDefault(x => x.No == row.No);
        if (t is null) return;

        PreloadCache.Preload(g, t, false);
    }

    /// <summary>当前列表里的下一首。普通模式到末尾回到第一首。</summary>
    private TrackRef? NextRef()
    {
        var rows = Rows.Where(r => r.IsAvailable).ToList();
        if (rows.Count == 0) return null;

        var cur = _engine?.Current;
        if (cur is null) return rows[0].Ref;

        int i = rows.FindIndex(r => r.Ref == cur.Value);
        return i < 0 ? rows[0].Ref : rows[(i + 1) % rows.Count].Ref;
    }

    private TrackRef? PrevRef()
    {
        var rows = Rows.Where(r => r.IsAvailable).ToList();
        if (rows.Count == 0) return null;

        var cur = _engine?.Current;
        if (cur is null) return rows[0].Ref;

        int i = rows.FindIndex(r => r.Ref == cur.Value);
        return i <= 0 ? rows[^1].Ref : rows[i - 1].Ref;
    }

    private TrackRef? RandomRef()
    {
        var rows = Rows.Where(r => r.IsAvailable).ToList();
        if (rows.Count == 0) return null;
        if (rows.Count == 1) return rows[0].Ref;

        var cur = _engine?.Current;
        int i;
        do { i = _rng.Next(rows.Count); }
        while (cur is not null && rows[i].Ref == cur.Value);

        return rows[i].Ref;
    }

    // ---------- 100ms 轮询：进度 + 自动切曲 ----------

    private void Tick(object? sender, EventArgs e)
    {
        if (_engine is null) return;

        if (!_engine.IsPlaying)
        {
            UpdatePlayButton();
            return;
        }

        UpdateProgress();

        // 普通 / 随机模式播完自动切下一首；无限循环不会自己结束
        if (_engine.IsFinished && AppSettings.Current.Playback.LoopMode != LoopMode.Infinite)
        {
            var next = AppSettings.Current.Playback.LoopMode == LoopMode.Shuffle
                ? RandomRef()
                : NextRef();
            if (next is not null) PlayTrack(next.Value);
        }
    }

    private void UpdateProgress()
    {
        if (_engine is null) return;

        // 回写滑块本身会触发 ValueChanged，必须挡掉，否则会变成 100ms 一次的 seek 回环，
        // 而且 Seek 会清掉"已播完"标志，自动切曲就永远触发不了
        _updatingSlider = true;
        try
        {
            // 三种模式共用一套：量程和位置都由内核按模式算好
            // （无限循环 = 整轨，普通 / 随机 = N / X / F），UI 不需要分辨差别
            double len = Math.Max(0.001, _engine.TotalTime.TotalSeconds);
            SeekBar.Maximum = len;
            if (!_seeking) SeekBar.Value = Math.Min(_engine.ProgressPosition.TotalSeconds, len);

            TimeText.Text = $"{TrackRow.FormatTime(_engine.ProgressPosition)} / " +
                            TrackRow.FormatTime(_engine.TotalTime);

            // 无限循环会一直重复，额外把累计时长显示出来
            if (_engine.Infinite)
                TimeText.Text += $"   总 {TrackRow.FormatTime(_engine.CurrentTime)}";
        }
        finally
        {
            _updatingSlider = false;
        }
    }

    private void SeekBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingSlider || _seeking || _engine is null || !_engine.IsPlaying) return;

        // 滑块的含义由内核统一：拖到哪就是时间线上的哪一秒，两种模式都一样
        _engine.Seek(TimeSpan.FromSeconds(SeekBar.Value));
    }

    private void SeekBar_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e) =>
        _seeking = true;

    private void SeekBar_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _seeking = false;
        if (_engine is null || !_engine.IsPlaying) return;

        _engine.Seek(TimeSpan.FromSeconds(SeekBar.Value));
    }

    // ---------- 播放控件 ----------

    private void Prev_Click(object sender, RoutedEventArgs e) => PlayPrev();

    private void Play_Click(object sender, RoutedEventArgs e) => PlayPause();

    private void Next_Click(object sender, RoutedEventArgs e) => PlayNext();

    /// <summary>播放 / 暂停切换。没在播也没暂停就从当前列表挑一首开始（按钮与多媒体键共用）。</summary>
    private void PlayPause()
    {
        if (_engine is null) return;

        if (_engine.IsPlaying)
        {
            _engine.Pause();
        }
        else if (_engine.Current is TrackRef cur && !cur.IsEmpty)
        {
            _engine.Resume();
        }
        else
        {
            var r = NextRef();
            if (r is not null) PlayTrack(r.Value);
        }

        UpdatePlayButton();
    }

    private void PlayPrev()
    {
        var r = PrevRef();
        if (r is not null) PlayTrack(r.Value);
    }

    private void PlayNext()
    {
        var r = AppSettings.Current.Playback.LoopMode == LoopMode.Shuffle ? RandomRef() : NextRef();
        if (r is not null) PlayTrack(r.Value);
    }

    private void Favorite_Click(object sender, RoutedEventArgs e) =>
        ToggleFavorite(SelectedRefs());

    /// <summary>整批切换收藏状态。只要有一个没收藏就整批收藏，全收藏了才取消。</summary>
    private void ToggleFavorite(List<TrackRef> targets)
    {
        if (targets.Count == 0) return;

        bool toFavorite = targets.Any(r => !PlaylistStore.IsFavorite(r));
        PlaylistStore.SetFavorite(targets, toFavorite);

        // 正看着收藏列表时，取消收藏要立刻从表里消失
        if ((PlaylistCombo.SelectedItem as PlaylistItem)?.IsFavorites == true)
            RefreshPlaylistView();

        UpdateFavoriteButton();
    }

    private void UpdateFavoriteButton()
    {
        var targets = SelectedRefs();
        if (targets.Count == 0)
        {
            FavoriteButton.IsEnabled = false;
            FavoriteButton.Content = "♥ 收藏";
            return;
        }

        FavoriteButton.IsEnabled = true;
        FavoriteButton.Content = targets.All(r => PlaylistStore.IsFavorite(r)) ? "♥ 已收藏" : "♡ 收藏";
    }

    // ---------- 右键菜单 ----------

    /// <summary>曲目表右键菜单是动态的（列表项会变），每次打开前重建。</summary>
    private void TrackGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e) =>
        TrackGrid.ContextMenu = BuildTrackContextMenu();

    private ContextMenu? BuildTrackContextMenu()
    {
        var targets = SelectedRefs();
        if (targets.Count == 0) return null;

        var menu = new ContextMenu();

        // 添加到列表 ▸
        var addItem = new MenuItem { Header = "添加到列表" };
        foreach (var list in PlaylistStore.Lists)
        {
            var captured = list;   // 闭包必须捕获副本，否则所有子项都指向最后一个列表
            var sub = new MenuItem { Header = list.Name };
            sub.Click += (_, _) =>
            {
                PlaylistStore.AddToList(captured, targets);
                if (ReferenceEquals(CurrentCustomList, captured)) RefreshPlaylistView();
            };
            addItem.Items.Add(sub);
        }
        addItem.Items.Add(new Separator());

        var newList = new MenuItem { Header = "＋ 新建列表…" };
        newList.Click += (_, _) =>
        {
            var name = PromptDialog.Show(this, "新建播放列表", "列表名称：", NextListName());
            if (name is null) return;

            var list = PlaylistStore.CreateList(name);
            PlaylistStore.AddToList(list, targets);
            BuildPlaylists();
            SelectPlaylist(list);
        };
        addItem.Items.Add(newList);
        menu.Items.Add(addItem);

        // 收藏 / 取消收藏：按这批曲目的当前状态决定显示哪个
        bool allFav = targets.All(r => PlaylistStore.IsFavorite(r));
        var favItem = new MenuItem { Header = allFav ? "取消收藏" : "收藏" };
        favItem.Click += (_, _) => ToggleFavorite(targets);
        menu.Items.Add(favItem);

        menu.Items.Add(new Separator());

        var expItem = new MenuItem { Header = "导出…" };
        expItem.Click += ExportSelected_Click;
        menu.Items.Add(expItem);

        var quickItem = new MenuItem { Header = "快速导出" };
        quickItem.Click += QuickExport_Click;
        menu.Items.Add(quickItem);

        menu.Items.Add(new Separator());

        // 只有自定义列表是「可编辑」的，作品列表与收藏不能删条目
        if (CurrentCustomList is CustomPlaylist cur)
        {
            var rm = new MenuItem { Header = "从列表中删除" };
            rm.Click += (_, _) =>
            {
                PlaylistStore.RemoveFromList(cur, targets);
                RefreshPlaylistView();
            };
            menu.Items.Add(rm);
        }

        return menu;
    }

    /// <summary>DELETE 键：从当前自定义列表里移除选中项（DESIGN_v3.md §5.4）。</summary>
    private void TrackGrid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // 回车 = 播放选中行。默认行为是把光标挪到下一行，和 ↓ 没区别，不是用户要的
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            PlaySelectedRow();
            e.Handled = true;
            return;
        }

        if (e.Key != System.Windows.Input.Key.Delete) return;
        if (CurrentCustomList is not CustomPlaylist list) return;

        var targets = SelectedRefs();
        if (targets.Count == 0) return;

        PlaylistStore.RemoveFromList(list, targets);
        RefreshPlaylistView();
        e.Handled = true;
    }

    // ---------- 框选与拖动排序 ----------

    /*
    手势路由（左键按下后）：
      · 可排序列表（自定义 / 收藏）里按在行上：先等 300ms ——
        按住满 300ms 再拖 → 拖动排序（光标变成十字箭头提示）；
        不到 300ms 就拖 → 框选（防止想框选时误触发排序）
      · 其余一切（锁定列表 / 空白处 / 按 Ctrl）→ 框选，按下时带 Ctrl 则追加
    可排序列表里想框选：快速起拖、从空白处起拖，或按住 Ctrl 从行上起拖。
    */

    private const double DragThreshold = 4;      // 超过这个位移才算「拖」
    private const double EdgeScrollZone = 28;    // 拖动靠近上下边缘这么多就自动滚动
    private const int ReorderDelayMs = 300;      // 按在行上按住多久才进入可拖排序状态

    private bool _dragArmed;        // 左键已按下，等位移
    private bool _marqueeActive;
    private bool _reorderActive;
    private Point _dragStart;       // GridHost 坐标
    private bool _pressWithCtrl;    // 按下瞬间的 Ctrl
    private int _pressRowIndex = -1;
    private int _dropIndex = -1;    // 拖动排序的落点（原列表插入位）
    private bool _reorderDelayElapsed;   // 排序延迟已到：再拖就是排序而不是框选
    private DispatcherTimer? _reorderDelayTimer;
    private readonly List<TrackRow> _marqueeBase = new();   // 追加框选的基线选择

    /// <summary>当前列表是否允许拖动换顺序：自定义列表与收藏可以，作品列表锁定。</summary>
    private bool CurrentListReorderable =>
        CurrentCustomList is not null ||
        (PlaylistCombo.SelectedItem as PlaylistItem)?.IsFavorites == true;

    /// <summary>
    /// 事件的原始来源是否落在滚动条或列标题上。沿可视树往上找，
    /// 遇到 ScrollBar / 列标题 = true，遇到 DataGrid = false。
    /// </summary>
    private static bool IsOnScrollbarOrHeader(object? originalSource)
    {
        for (var v = originalSource as DependencyObject; v is not null;
             v = System.Windows.Media.VisualTreeHelper.GetParent(v))
        {
            if (v is System.Windows.Controls.Primitives.ScrollBar) return true;
            if (v is System.Windows.Controls.Primitives.DataGridColumnHeader) return true;
            if (v is DataGrid) return false;
        }
        return false;
    }

    private void TrackGrid_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // 滚动条 / 列标题在 DataGrid 可视树内部，隧道事件会先经过我们这里 ——
        // 但它们要自己处理拖动（滚条拖拽翻页）。不挡掉的话，框选手势会捕获鼠标、
        // 把滚动条的拖动抢走（表现为「拖滚动条要么进框选、要么只动一点点」）。
        if (IsOnScrollbarOrHeader(e.OriginalSource)) return;

        // 这是隧道事件，先于 DataGrid 自己的选中处理 —— 此刻读到的选择集还是按下前的
        _dragStart = e.GetPosition(GridHost);
        _pressWithCtrl = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control);
        _pressRowIndex = RowIndexAt(_dragStart);
        _dragArmed = true;
        _marqueeActive = _reorderActive = false;
        _reorderDelayElapsed = false;

        _marqueeBase.Clear();
        if (_pressWithCtrl)
            _marqueeBase.AddRange(TrackGrid.SelectedItems.Cast<TrackRow>());

        // 按在行上且列表可排序：启动排序延迟。按住满 300ms 才进入可拖排序状态；
        // 不到时间就拖动会被 MouseMove 判成框选
        if (_pressRowIndex >= 0 && !_pressWithCtrl && CurrentListReorderable)
            _reorderDelayTimer?.Start();
    }

    private void TrackGrid_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_dragArmed || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;

        var pos = e.GetPosition(GridHost);

        if (!_marqueeActive && !_reorderActive)
        {
            if (Math.Abs(pos.X - _dragStart.X) < DragThreshold &&
                Math.Abs(pos.Y - _dragStart.Y) < DragThreshold) return;

            bool onRow = _pressRowIndex >= 0;
            if (onRow && !_pressWithCtrl && CurrentListReorderable && _reorderDelayElapsed)
            {
                BeginReorder();
            }
            else
            {
                // 排序延迟还没过就拖了（或压根不满足排序条件）→ 框选，
                // 并废掉这次的排序资格，免得框选中途延迟到了变排序
                _reorderDelayTimer?.Stop();
                BeginMarquee();
            }
        }

        EdgeScroll(pos);
        if (_marqueeActive) UpdateMarquee(pos);
        else if (_reorderActive) UpdateReorder(pos);
    }

    private void TrackGrid_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _reorderDelayTimer?.Stop();
        _reorderDelayElapsed = false;
        TrackGrid.ClearValue(CursorProperty);   // 摘掉排序待命时的十字箭头

        if (_marqueeActive)
        {
            _marqueeActive = false;
            MarqueeRect.Visibility = Visibility.Collapsed;
            OnTrackSelectionChanged();   // 拖动期间压着没刷面板，松手补一次
        }
        else if (_reorderActive)
        {
            _reorderActive = false;
            InsertLine.Visibility = Visibility.Collapsed;
            CommitReorder();
        }

        _dragArmed = false;
        if (TrackGrid.IsMouseCaptured) TrackGrid.ReleaseMouseCapture();
    }

    // ----- 框选 -----

    private void BeginMarquee()
    {
        _marqueeActive = true;
        MarqueeRect.Visibility = Visibility.Visible;
        TrackGrid.CaptureMouse();
    }

    private void UpdateMarquee(Point pos)
    {
        var rect = new Rect(
            Math.Min(_dragStart.X, pos.X), Math.Min(_dragStart.Y, pos.Y),
            Math.Abs(pos.X - _dragStart.X), Math.Abs(pos.Y - _dragStart.Y));

        MarqueeRect.Margin = new Thickness(rect.X, rect.Y, 0, 0);
        MarqueeRect.Width = rect.Width;
        MarqueeRect.Height = rect.Height;

        var target = new HashSet<TrackRow>(_marqueeBase);
        var rows = Rows;
        for (int i = 0; i < rows.Count; i++)
        {
            if (RowBounds(i) is Rect b && b.IntersectsWith(rect))
                target.Add(rows[i]);
        }

        var current = TrackGrid.SelectedItems.Cast<TrackRow>().ToHashSet();
        if (current.SetEquals(target)) return;

        _suppressSelectionEvent = true;
        try
        {
            TrackGrid.SelectedItems.Clear();
            foreach (var r in rows.Where(target.Contains))
                TrackGrid.SelectedItems.Add(r);
        }
        finally
        {
            _suppressSelectionEvent = false;
        }
    }

    // ----- 拖动排序 -----

    private void BeginReorder()
    {
        _reorderActive = true;
        InsertLine.Width = GridHost.ActualWidth;
        InsertLine.Visibility = Visibility.Visible;
        TrackGrid.CaptureMouse();
    }

    private void UpdateReorder(Point pos)
    {
        var (idx, y) = InsertionAt(pos);
        InsertLine.Margin = new Thickness(0, Math.Max(0, y - 1), 0, 0);
        _dropIndex = idx;
    }

    private void CommitReorder()
    {
        int from = _pressRowIndex;
        int to = _dropIndex;
        _dropIndex = -1;
        if (from < 0 || to == from || to == from + 1) return;

        var rows = Rows;
        if (from >= rows.Count) return;
        var moved = rows[from].Ref;

        if (CurrentCustomList is CustomPlaylist list)
            PlaylistStore.MoveInList(list, from, to);
        else if ((PlaylistCombo.SelectedItem as PlaylistItem)?.IsFavorites == true)
            PlaylistStore.MoveFavorite(from, to);
        else
            return;   // 作品列表理论上来不到这里（手势路由挡了），双保险

        RefreshPlaylistView();

        // 选中跟到被挪的那一行
        var row = Rows.FirstOrDefault(r => r.Ref == moved);
        if (row is not null) TrackGrid.SelectedItem = row;
    }

    // ----- 共用的命中与滚动 -----

    /// <summary>某行在 GridHost 坐标系里的外框；虚拟化导致容器没生成时为 null。</summary>
    private Rect? RowBounds(int index)
    {
        if (TrackGrid.ItemContainerGenerator.ContainerFromIndex(index) is not DataGridRow el)
            return null;
        return el.TransformToAncestor(GridHost)
                 .TransformBounds(new Rect(0, 0, el.ActualWidth, el.ActualHeight));
    }

    /// <summary>GridHost 坐标点落在哪一行；空白处返回 -1。</summary>
    private int RowIndexAt(Point pos)
    {
        var rows = Rows;
        for (int i = 0; i < rows.Count; i++)
        {
            if (RowBounds(i) is Rect b && b.Contains(pos)) return i;
        }
        return -1;
    }

    /// <summary>拖动排序的插入位：目标索引 + 指示线的 Y。</summary>
    private (int Index, double Y) InsertionAt(Point pos)
    {
        var rows = Rows;
        double lastBottom = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            if (RowBounds(i) is not Rect b) continue;
            if (pos.Y < b.Top + b.Height / 2) return (i, b.Top);
            lastBottom = b.Bottom;
        }
        return (rows.Count, lastBottom);
    }

    /// <summary>拖动到上下边缘附近时自动滚动一行。</summary>
    private void EdgeScroll(Point pos)
    {
        var rows = Rows;
        if (rows.Count == 0) return;

        int first = -1, last = -1;
        for (int i = 0; i < rows.Count; i++)
        {
            if (RowBounds(i) is not Rect b) continue;
            if (b.Bottom < 0 || b.Top > GridHost.ActualHeight) continue;
            if (first < 0) first = i;
            last = i;
        }
        if (first < 0) return;

        if (pos.Y < EdgeScrollZone && first > 0)
            TrackGrid.ScrollIntoView(rows[first - 1]);
        else if (pos.Y > GridHost.ActualHeight - EdgeScrollZone && last < rows.Count - 1)
            TrackGrid.ScrollIntoView(rows[last + 1]);
    }

    private void Alt_Click(object sender, RoutedEventArgs e)
    {
        if (_engine?.Current is not TrackRef cur || cur.IsEmpty) return;
        if (!TrackIndex.ById.TryGetValue(cur.GameId, out var game)) return;

        var track = game.Tracks.FirstOrDefault(t => t.No == cur.TrackNo);
        if (track is null || !track.HasAlt) return;

        try
        {
            _engine.SwitchAlt(game, track, !_engine.UsingAlt);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "切换失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 曲名保持主版不变（用户定：列表与面板都不跳字），只在按钮和详情前缀上体现状态
        UpdateAltButton();
        RefreshNowPlaying();

        // 切过去之后把「另一版本」预读上，来回 A/B 对比也是零等待
        PreloadCache.Preload(game, track, !_engine.UsingAlt);
    }

    /// <summary>曲目信息的原文（不含灵界版前缀）。</summary>
    private static string Describe(TrackDef track) =>
        $"{track.Rate}Hz / {track.Channels}ch / {track.Bits}bit · " +
        $"intro {TrackRow.FormatTime(track.IntroTime)} · loop {TrackRow.FormatTime(track.LoopTime)} · " +
        $"总长 {TrackRow.FormatTime(track.LengthTime)}";

    /// <summary>按当前灵界版状态重拼曲目信息。前缀是算出来的，不会叠加。</summary>
    private void RefreshNowPlaying() =>
        NowPlayingDetail.Text = _engine?.UsingAlt == true ? "霊界版 · " + _nowDetail : _nowDetail;

    private void UpdateAltButton()
    {
        if (_engine?.Current is not TrackRef cur || cur.IsEmpty)
        {
            AltButton.IsEnabled = false;
            AltButton.Content = "霊界版";
            AltButton.ClearValue(StyleProperty);   // 落回应用级隐式 Button 样式
            return;
        }

        bool hasAlt = TrackIndex.ById.TryGetValue(cur.GameId, out var game) &&
                      game.Tracks.FirstOrDefault(t => t.No == cur.TrackNo)?.HasAlt == true;

        AltButton.IsEnabled = hasAlt;
        AltButton.Content = _engine.UsingAlt ? "霊界版 ✓" : "霊界版";

        // 切到灵界版：换上紫色激活样式（DarkTheme.xaml 里的 AltActiveButton）。
        // 换回主版：清掉本地 Style，重新走应用级隐式 Button 样式。
        if (_engine.UsingAlt)
            AltButton.Style = (Style)FindResource("AltActiveButton");
        else
            AltButton.ClearValue(StyleProperty);
    }

    private void LoopModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;   // XAML 的 SelectedIndex="0" 在构造期间也会触发一次，别让它覆盖设置

        var mode = (LoopMode)LoopModeCombo.SelectedIndex;
        if (AppSettings.Current.Playback.LoopMode == mode) return;
        AppSettings.Current.Playback.LoopMode = mode;

        // 立刻落盘，免得程序异常退出时丢掉这次改动
        AppSettings.Current.Save();

        // 时间线参数是在建链时定下来的，换模式要让内核按新参数重算，
        // 就地改即可，音频不会中断
        ApplyTimelineToCurrent();
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;   // 同上：XAML 的 Value="0.8" 不该把已保存的音量冲掉

        AppSettings.Current.Playback.Volume = e.NewValue;
        if (_engine is not null) _engine.Volume = (float)e.NewValue;
    }

    private void UpdatePlayButton() =>
        PlayButton.Content = _engine is not null && _engine.IsPlaying ? "⏸ 暂停" : "▶ 播放";

    private void UpdateTransportEnabled()
    {
        bool any = Rows.Any(r => r.IsAvailable);
        PlayButton.IsEnabled = any;
        PrevButton.IsEnabled = any;
        NextButton.IsEnabled = any;
        LoopModeCombo.IsEnabled = any;
        SeekBar.IsEnabled = any;
    }

    // ---------- 菜单 ----------

    private void SettingsPaths_Click(object sender, RoutedEventArgs e) => OpenSettings(0);
    private void SettingsPlayback_Click(object sender, RoutedEventArgs e) => OpenSettings(1);
    private void SettingsExport_Click(object sender, RoutedEventArgs e) => OpenSettings(2);
    private void SettingsUi_Click(object sender, RoutedEventArgs e) => OpenSettings(3);

    /// <summary>打开设置窗口并定位到指定标签页：0 路径 / 1 播放参数 / 2 导出 / 3 外观。</summary>
    private void OpenSettings(int tab)
    {
        var dlg = new SettingsWindow { Owner = this, InitialTab = tab };
        dlg.Applied += () => ApplySettingsSideEffects(dlg);   // 点「应用」立即生效（对话框还开着）
        dlg.ShowDialog();
        ApplySettingsSideEffects(dlg);   // 关窗后再过一次（幂等）
    }

    /// <summary>
    /// 设置变化后的副作用统一走这里：点「应用」（对话框还开着）和关窗后都会调到，
    /// 全部幂等。顺序上先清缓存重建列表，再处理播放相关。
    /// </summary>
    private void ApplySettingsSideEffects(SettingsWindow dlg)
    {
        PreloadCache.Clear();   // 路径可能改了，缓存里的音源指向旧文件，必须丢

        // 路径可能变了，重建下拉（连收藏与自定义列表一起）
        RebuildPlaylistsKeepSelection();

        ApplyFont();
        UpdateStatus();
        UpdateTransportEnabled();
        ApplyGlobalMediaKeys();   // 全局多媒体键的开关可能改了，重挂一次（幂等）

        if (!dlg.PlaybackChanged) return;

        // 输出设备可能改了：立即切换（引擎内部会比对，没变就什么都不做）
        try
        {
            _engine?.SetOutputDevice(AppSettings.Current.Playback.OutputDeviceId);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "切换输出设备失败",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // 循环模式可能是在设置里改的，同步主界面那个下拉。
        // 赋值若触发了 SelectionChanged，它自己就会应用一次；
        // Reconfigure 是幂等的，下面再调一次也无害（N / X / F 变了要走这里）。
        int idx = (int)AppSettings.Current.Playback.LoopMode;
        if (LoopModeCombo.SelectedIndex != idx) LoopModeCombo.SelectedIndex = idx;

        ApplyTimelineToCurrent();
    }

    /// <summary>
    /// 把改过的播放参数（循环模式 / N / X / F）应用到正在播放的曲子上。
    /// 走内核的就地改参数，不重建音源 —— 音频不中断，位置也不跳。
    /// 没有在播的曲子就什么都不用做，下次 Play 自然会用上新参数。
    /// </summary>
    private void ApplyTimelineToCurrent()
    {
        if (_engine?.Current is null || _engine.Current.Value.IsEmpty) return;

        _engine.Reconfigure();
        UpdateProgress();
    }

    private void PlaylistNew_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptDialog.Show(this, "新建播放列表", "列表名称：", NextListName());
        if (name is null) return;

        var list = PlaylistStore.CreateList(name);
        BuildPlaylists();
        SelectPlaylist(list);
    }

    private void PlaylistRename_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentCustomList is not CustomPlaylist list) return;

        var name = PromptDialog.Show(this, "重命名列表", "列表名称：", list.Name);
        if (name is null) return;

        PlaylistStore.RenameList(list, name);
        RebuildPlaylistsKeepSelection();
    }

    private void PlaylistDelete_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentCustomList is not CustomPlaylist list) return;

        if (MessageBox.Show(this, $"确定删除列表「{list.Name}」？此操作不可撤销。",
                            "删除列表", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        PlaylistStore.DeleteList(list);
        BuildPlaylists();
        PlaylistCombo.SelectedItem = PlaylistCombo.Items.Cast<PlaylistItem>().FirstOrDefault();
    }

    /// <summary>把下拉框切到指定自定义列表。</summary>
    private void SelectPlaylist(CustomPlaylist list)
    {
        var item = PlaylistCombo.Items.Cast<PlaylistItem>()
            .FirstOrDefault(i => ReferenceEquals(i.Custom, list));
        if (item is not null) PlaylistCombo.SelectedItem = item;
    }

    /// <summary>生成一个不重名的默认列表名。</summary>
    private static string NextListName()
    {
        for (int i = 1; ; i++)
        {
            var name = $"我的列表 {i}";
            if (PlaylistStore.Lists.All(l => l.Name != name)) return name;
        }
    }

    /// <summary>重命名 / 删除只对自定义列表可用。</summary>
    private void UpdatePlaylistMenus()
    {
        bool custom = CurrentCustomList is not null;
        PlaylistRenameItem.IsEnabled = custom;
        PlaylistDeleteItem.IsEnabled = custom;
    }

    private void About_Click(object sender, RoutedEventArgs e) => AboutDialog.Show(this);

    // ---------- 导出 ----------

    private static (GameDef Game, TrackDef Track)? Resolve(TrackRef r)
    {
        if (!TrackIndex.ById.TryGetValue(r.GameId, out var game)) return null;

        var track = game.Tracks.FirstOrDefault(t => t.No == r.TrackNo);
        return track is null ? null : (game, track);
    }

    /// <summary>把选中的曲目解析成可导出的列表，索引里找不到的直接丢掉。</summary>
    private List<(GameDef Game, TrackDef Track)> ExportTargets() =>
        SelectedRefs().Select(Resolve).Where(x => x is not null).Select(x => x!.Value).ToList();

    private void ExportSelected_Click(object sender, RoutedEventArgs e)
    {
        var items = ExportTargets();
        if (items.Count == 0)
        {
            MessageBox.Show(this, "没有可导出的曲目。", "导出", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 灵界版的初始勾选跟随当前播放状态
        ExportDialog.Show(this, items, _engine?.UsingAlt == true);
    }

    /// <summary>
    /// 快速导出：不弹窗，按当前导出设置直接出文件。
    /// 灵界版取正在播放的版本（DESIGN_v3.md §8.2）。
    /// </summary>
    private async void QuickExport_Click(object sender, RoutedEventArgs e)
    {
        var items = ExportTargets();
        if (items.Count == 0) return;

        var p = WavExporter.ResolveParams();
        bool useAlt = _engine?.UsingAlt == true;

        int done = 0;
        string? error = null;

        try
        {
            foreach (var (g, t) in items)
            {
                await WavExporter.ExportAsync(g, t, useAlt, p);
                done++;
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        if (error is not null)
            MessageBox.Show(this, error, "导出失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            MessageBox.Show(this,
                $"已导出 {done} 个文件到：\n{WavExporter.OutputDirectory}",
                "导出完成", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ---------- 辅助 ----------

    private void UpdateStatus()
    {
        // 标题栏只留标题（已配置数量在下拉灰显、设置页汇总里都看得到）。
        // 全角半角之间不留空格。
        Title = "东方ProjectBGM播放器";

        if (!AppPaths.IsWritable && _engine?.InitError is null)
            PlaylistSummary.Text = "程序目录不可写，设置不会被保存";
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _tick?.Stop();
        _mediaKeys?.Dispose();   // 热键不注销的话，窗口没了系统还会往里投递

        var ui = AppSettings.Current.Ui;
        ui.Width = Width;
        ui.Height = Height;
        ui.Left = Left;
        ui.Top = Top;
        ui.Maximized = WindowState == WindowState.Maximized;

        AppSettings.Current.Save();

        _engine?.Dispose();
        _engine = null;
    }
}