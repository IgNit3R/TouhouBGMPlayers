# build 25 —— 霊界版按钮挪位 + 激活态紫色

2026-09-06

## 改动

1. **按钮挪位**：霊界版按钮从底部信息区（右下角）移到播放控制栏「♥ 收藏」右边。
   底部信息区简化为纯文字三行（曲名 16 / 作品全名 11 TextFaint / 参数行 Consolas）。
   只动 `MainWindow.xaml`，代码全部按 `x:Name` 引用，零改动。

2. **激活态紫色**：切到灵界版时按钮换紫色样式。主题里预留的 `Alt`（#B07FD0 淡紫）正式接线。
   - 新增画刷：`AltBg` #3A2A4E / `AltBgHover` #4A365F / `AltBgPressed` #563F6E（暗紫调，思路同 RowPlaying 暗蓝调）
   - 新增 keyed 样式 `AltActiveButton`：暗紫底 + 淡紫文字与描边，自带悬停/按下/禁用三态
   - `UpdateAltButton()`：`_engine.UsingAlt` 为真时 `AltButton.Style = FindResource("AltActiveButton")`，否则 `ClearValue(StyleProperty)` 落回应用级隐式 Button 样式

## 关键决策：为什么不能 BasedOn 继承隐式 Button 样式

隐式 Button 样式的悬停/按下是**模板级触发器**，直接给模板内的 Border 设**固定画刷**
（BgHover / BgPressed 灰色）。BasedOn 继承会把这套模板一起继承过来，
悬停时紫色底会被灰色盖掉。所以 `AltActiveButton` 整份模板自带，状态色全部换成紫色系。

同理，也不能只在代码里设 `AltButton.Background = AltBg` —— 那是本地值，
模板触发器照常把悬停态画成灰色，按钮会一半紫一半灰。

## 配色（激活态）

| 状态 | 底 | 文字/描边 |
|---|---|---|
| 正常 | #3A2A4E | #B07FD0 |
| 悬停 | #4A365F | 同上 |
| 按下 | #563F6E | 同上 |
| 禁用 | BgPanel | TextFaint（与普通按钮一致） |

## 验证

check_src.py 7 项全过。待编译实机确认：TH13 带灵界版的曲子切换时按钮变紫，换回主版恢复普通样式；无灵界版的曲子按钮灰显且无紫色残留。
