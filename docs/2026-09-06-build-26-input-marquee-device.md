# build 26 —— 回车播放 + 多媒体键 / 框选 + 拖动排序 / 输出设备选择

2026-09-06

## 1. 回车播放 + 多媒体键

- **回车 = 播放选中行**：`TrackGrid_PreviewKeyDown` 里拦 Enter，走 `PlaySelectedRow()`（与双击共用，原默认行为是光标下移，和 ↓ 没区别）。
- **播放控制抽无参方法**：`PlayPause() / PlayPrev() / PlayNext()`，按钮、多媒体键、全局热键三处共用。
- **全局多媒体键**：新文件 `Core/MediaKeysHotkey.cs`，RegisterHotKey 拦 VK_MEDIA_PLAY_PAUSE / PREV / NEXT（MOD_NOREPEAT）。三个键**全有或全无**——任何一个被别的程序占了就整体放弃，退回「仅聚焦时响应」（窗口级 `Window_PreviewKeyDown` 兜底）。注册成功后系统把按键拦成 WM_HOTKEY，WPF 键事件收不到，不会双触发。
- 设置开关：`PlaybackSettings.GlobalMediaKeys`（默认开），设置→播放参数 里的复选框。`OnSourceInitialized` 创建助手，`Window_Closing` 注销，`OpenSettings` 后 `ApplyGlobalMediaKeys()` 幂等重挂。

## 2. 框选 + 拖动排序

- DataGrid 外包一层 `GridHost`，覆盖两个元素：`MarqueeRect`（框选矩形，半透明 Accent）与 `InsertLine`（排序指示线）。
- **手势路由**（左键拖过 4px 阈值时决定）：
  - 按在行上 + 可排序列表（自定义/收藏）+ 无 Ctrl → 拖动排序
  - 其余 → 框选（按下时带 Ctrl 则追加，基线是按下前的选择集——Preview 隧道事件先于 DataGrid 选中处理，读到的是旧选择）
- 框选实时改选择集：`SelectedItems.Clear()` + 按 Rows 顺序 Add，期间 `_suppressSelectionEvent` 压着面板刷新（try/finally），松手补一次 `OnTrackSelectionChanged()`。
- 排序落点：指示线按目标行上/下半判定插入位；`PlaylistStore.MoveInList / MoveFavorite`（to 是原列表插入位 0..Count，to==from 或 from+1 不动），持久化后 `RefreshPlaylistView()` 并把选中跟到被挪行。
- 拖动靠近上下边缘 28px 自动滚动一行（ScrollIntoView）。
- 作品列表锁定：手势路由 + CommitReorder 双保险。

## 3. 输出设备选择

- 设置→播放参数 新增「输出设备」下拉：系统默认（跟随系统切换）+ 枚举的活跃渲染端点（`Audio/OutputDevices.cs`，ID 持久化、FriendlyName 显示）。
- **跟随系统的根治**：`WasapiPlayerBuilder.WithDefaultDeviceStreamRouting()`（Win10 1607+），系统默认设备变化时 Windows 无缝迁移流，零应用层代码——之前「系统里切了要重启才生效」就是因为建输出时绑死了当时的默认设备。
- **指定设备**：`WithDevice(MMDevice)`；`PlayerEngine.SetOutputDevice(id)` 停旧输出→同一条混音链在新设备重建→续播，**位置不丢**（混音器没动，链是拉取式），只有重建瞬间的短间隙。设备相同则 no-op。
- 指定的设备被拔掉：`OutputDevices.FindById` 找不到 → 退回系统默认路由，同时把设置掰回 null，界面与实际一致。
- `_out` / `_initError` 去掉 readonly；构造与切换共用 `BuildOutput()`。切换失败保留 `_initError` 并抛给主窗口弹提示。

## 验证

check_src.py 7 项全过（命名控件 MainWindow 20 / SettingsWindow 19）。
待实机：回车播放、聚焦与未聚焦的多媒体键、三种列表里的框选、收藏/自定义列表拖动排序及重启后顺序保持、设置里切声卡立即生效、系统默认切换跟随。
