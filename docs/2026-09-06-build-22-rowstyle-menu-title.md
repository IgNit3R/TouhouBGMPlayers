# 构建日志 22：播放高亮被压掉 / 菜单残留 / 标题空格 / 下拉去计数

日期：2026-09-06
阶段：外观改进第三轮（验收后提出）

## 1. 播放高亮点别的行就消失 —— 根因不是逻辑，是 WPF 值优先级

逻辑查过多遍没问题（`IsPlaying` 只在换曲时才变，点别的行不该动它）。
真正的原因在 XAML：

**`DataGrid.RowBackground` / `AlternatingRowBackground` 是 DataGrid 用
`SetValue` 写到行容器上的 —— 那是「本地值」，优先级高于 Style 触发器。**

所以 RowStyle 里那些 `Background` 触发器能不能生效，取决于行的状态变化
有没有碰巧让 DataGrid 重新写一次本地值。表现出来的现象就很诡异：
初始能亮，一动选中就掉。

### 改法：行底色一律走触发器

去掉 DataGrid 上的 `RowBackground` / `AlternatingRowBackground`，
改成 `AlternationCount="2"` + RowStyle 里的 `ItemsControl.AlternationIndex` 触发器。
四档底色全部由触发器决定，靠「后命中的生效」排优先级：

1. 基础 `BgDeep`
2. `AlternationIndex=1` → `BgAltRow`（斑马纹）
3. `IsAvailable=False` → 文字转暗 + 透明度 0.55（底色不动）
4. `IsPlaying=True` → `RowPlaying`
5. `IsSelected=True` → `Accent`（放最后，压过前面所有）

这样就不存在本地值和触发器打架的问题了。

**教训**：在 WPF 里给 `DataGridRow.Background` 做状态色，
先确认 DataGrid 上没设 RowBackground —— 否则触发器是在跟本地值抢。

## 2. 菜单右侧深色条 + 没画完整的白线

- **深色条**：弹层里那个 `ScrollViewer` 的竖滚动条轨道（`ScrollBg` `#1B1B1D`）。
  菜单最多四、五项，根本用不着滚动，直接去掉 ScrollViewer。
- **白线**：`Menu` 默认模板带一圈系统色描边。补了 `Menu` 样式，
  `BorderThickness=0`、`BorderBrush=Transparent`。

## 3. 窗口标题的空格

「东方 Project BGM 播放器」→ **「东方Project BGM播放器」**。
去掉中文与拉丁字母之间的空格，Project 和 BGM 之间那个保留（两个都是拉丁词）。

改了两处并核对一致：MainWindow.xaml 的 `Title` 属性和
`UpdateStatus()` 里运行时赋的 `Title` —— 这两处不一致的话，
启动后标题会跳一下。

## 4. 下拉列表不再显示曲目数

`TH06` / `★ 收藏` / `♪ 我的列表 1`，不再跟 `(17 首)`。

顺带删掉了 `RefreshPlaylistCounts()` —— 它存在的唯一目的就是就地更新
下拉里的计数，计数没了它也就没用了。四处调用一并清掉。

曲目数没丢，还在下面的汇总栏里。

## 自检

7 项全部通过；标题两处一致。
