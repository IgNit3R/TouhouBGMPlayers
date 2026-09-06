# 构建日志 8：列表体系 + 设置页（第三步 A）

日期：2026-09-04
阶段：第三步第一批

## 新增文件

| 文件 | 说明 |
|---|---|
| `Data/PlaylistStore.cs` | 收藏与自定义列表的持久化（`favorites.json` / `playlists.json`） |
| `UI/PromptDialog.cs` | 单行输入对话框（新建 / 重命名列表用），代码搭建省一个 XAML |

## 改动的既有文件

`Data/TrackRef.cs`、`Core/AppSettings.cs`、`MainWindow.xaml(.cs)`、
`UI/SettingsWindow.xaml(.cs)`

## 关键设计

### 列表项只存二元组，且序列化成短串

```json
// playlists.json
[{ "name": "战斗曲", "items": ["th13:16", "th06:03", "th20:11"] }]
// favorites.json
["th13:02", "th08:16"]
```

存 `"th13:16"` 而不是 `{"game":"th13","no":16}` 的理由：
- 紧凑（收藏几百首也只有几 KB）
- **用户能用记事本直接改**，`TrackRef.Parse` 对格式错误返回 `Empty` 而不是抛异常
- 天然不存文件路径 —— 改了路径设置后列表依然有效

### 收藏计数就地更新，不重建下拉

第一版写的是「增删收藏 → `BuildPlaylists()` 重建整个下拉」，
但重建会连带刷新曲目表，**把用户当前选中的行弄丢**。

改成 `PlaylistItem : ViewModelBase`，`DisplayName` 可写并带通知，
增删后只改文字（ `RefreshPlaylistCounts()` ），Combo 就地更新，曲目表不动。
只有「正在看收藏列表」时才真正重画曲目表。

### 播放参数改了要重建时间线，位置用 loop 段内位置做锚点

时间线（循环模式 / N / X / F）是 `LoopSampleProvider` 建链那一刻定死的，
改了只能重建。锚点用 **`LoopPosition`（loop 段内位置）** 而不是总时间：

- 无限循环没有终点，累计时间会一直增长
- 拿总时间去定位有限时间线会被 `Clamp` 到 `_emitFrames`
  → 表现为「一切换就跳到快播完的地方」

两种时间线的 intro+loop 结构相同，`LoopPosition` 是通用锚点。

**注意一个坑**：循环模式在设置窗口里改了之后，
主界面 `LoopModeCombo.SelectedIndex = idx` 触发的 `SelectionChanged` 会因为
「`AppSettings.Current.Playback.LoopMode == mode`」而**提前返回**、不重建。
所以 `OpenSettings` 里对两种情形统一调一次 `RebuildCurrent()`，正好只做一次。

### 窗口几何恢复

设计文档要求「尺寸与位置存 settings.json」，`UiSettings` 早有字段但一直没用上。
现在启动时恢复，并对坐标做屏幕范围校验 —— 显示器配置变了之后，
旧坐标可能把所有窗口都推到屏幕外，那样程序就"失踪"了。

## 过程中的坑

**Edit 工具把 XAML 改坏了。** 为了替换「播放参数」标签页的内容，
我用了两行的 `old_string`，结果把标签页的闭合标签一起吃掉了，XML 结构断掉。

**教训：XAML 必须用 XML 解析器校验。** 之前只校验 C# 的括号配平，
XAML 一直靠肉眼。现在加了一条硬检查：

```python
xml.etree.ElementTree.parse(xaml)   # 三份 XAML 全部通过才算过
```

这个检查成本极低，早该加。

## 自检清单（脚本化）

1. 三份 XAML 通过 XML 解析
2. 所有 `.cs` 括号配平
3. XAML 事件处理器在代码里都有对应方法
4. 已移除的标识符（`IsPlaceholder` / `NotYet`）无残留引用
5. 逐文件核对 `x:Name` 与代码的引用关系（全局核对会有假阳性，必须按文件配对查）

## 待办（第三步 B）

导出功能：设置窗口「导出」页目前仍是占位。
