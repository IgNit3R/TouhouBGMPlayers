# 构建日志 20：深色主题配色 + 滚动条 + 按钮状态 + 下拉框

日期：2026-09-06
阶段：外观改进第一轮

## 用户诉求

> 深色模式下颜色区分度不够，但不希望太花。
> 下拉表（ComboBox）那几处文字和背景融为一体。
> 不要改动 UI 布局。改进 1（滚动条）和 5（按钮状态），
> 2/3/4/6 说明具体位置，7 先不动。

## 先查根因：下拉框为什么看不清

不是配色没调好，是**样式根本没生效**。

WPF 默认 ComboBox 模板的弹层背景取自 `SystemColors.WindowBrush`（浅色），
而 `Foreground` 会**继承窗口的 `#FFE8E8E8`**（浅灰）。
浅灰字压在浅色底上 —— 直接看不见。

更麻烦的是：MainWindow 里虽然写了 ComboBox 的隐式样式，
但 SettingsWindow **一个 ComboBox 样式都没有**，所以那边最明显。

而且窗口级隐式样式会盖掉应用级的，
所以主题做在 App.xaml 还不够，必须把窗口里那几份重复的删掉 —— 否则白做。

## 做法：把主题抽到应用级

新增 `Themes/DarkTheme.xaml`，在 `App.xaml` 合并。

放应用级而不是各窗口 Resources，有三个理由：

1. **Popup 在另一棵可视化树上**，窗口级资源不一定继承得到 —— 下拉弹层正是这个场景
2. 设置窗口不用再抄一份
3. 代码搭的对话框（PromptDialog / ExportDialog）能通过 `Application.Current.Resources` 取到

顺带加了 `UI/Theme.cs`（`Theme.Get(key, fallbackHex)`），
让代码搭的对话框也用同一套画刷，取不到时按备用色值构造，不炸也不瞎。

`PathRow.cs` 里写死的校验状态颜色也改成从主题取了。

## 配色

取向：**只有一套中性灰阶 + 一个强调色（蓝），不引入第二种彩色**。
层次靠明度阶梯拉开，不靠描边和色相。

### 表面（关键：把阶梯拉开了）

| | 原来 | 现在 | 用途 |
|---|---|---|---|
| BgDeep | `1E1E1E` | **`171717`** | 窗口底 |
| BgAltRow | `232324` | **`1C1C1E`** | 斑马纹交替行 |
| BgPanel | `252526` | **`222224`** | 条带、面板、下拉弹层 |
| BgElevated | `333333` | **`2E2E31`** | 输入控件、按钮 |
| BgHover | — | **`393940`** | 悬停 |
| BgPressed | — | **`43434A`** | 按下 |

原来 `1E → 25 → 33` 三档间隔 7/14，现在 `17 → 22 → 2E → 39/43`
间隔 5/12/11/10，而且多了一档交替行，层次更清楚。

### 描边 / 文字 / 强调

| | 原来 | 现在 |
|---|---|---|
| Border | `3E3E42` | `3F3F45`（另加 Hover `52525A` / Focus `6E6E7A`） |
| Text | `E8E8E8` | `EDEDED` |
| TextDim | `A0A0A0` | `ADADB4` |
| TextFaint | `6E7681` | `71717A` |
| Accent | `0E639C` | 保持（这是唯一的彩色，不做第二套） |

### 语义色（压低饱和度）

Ok `4EC9B0` / Warn `CEA86A` / Fail `D4696B` —— 只在路径校验状态出现。
另有 `Alt` `B07FD0`（灵界版）**暂未接线**，留作备用。

## 改进 1：滚动条

整份模板重写，去掉两端箭头按钮（现代风格、也更省地方）：

- 轨道 `1B1B1D`，滑块 `4A4A52`（悬停 `5E5E68`），圆角 3px
- 轨道空白处是透明 `RepeatButton`，点击翻页的默认交互保留
- 竖/横两套模板（`IsDirectionReversed` 相反、命令分别是 PageUp/Down 与 PageLeft/Right）

影响面：曲目表、设置页各标签页、下拉列表 —— 之前全是浅灰的 Windows 滚动条。

## 改进 5：按钮状态

加了 `IsMouseOver` / `IsPressed` / `IsEnabled=False` 三态：

- 常规 `BgElevated` → 悬停 `BgHover` + 边框提亮 → 按下 `BgPressed`
- 禁用：底色降到 `BgPanel`、文字 `TextFaint`（原来禁用按钮几乎看不出区别）

## 未纳入（用户要求说明位置）

| # | 控件 | 出现在哪 |
|---|---|---|
| 2 | Slider | 底部**进度条**、右下角**音量滑块** |
| 3 | ToolTip | 设置页每行路径输入框的提示、曲目表循环列的提示、播放面板「霊界版」按钮的提示 |
| 4 | CheckBox / RadioButton | 设置页**导出**页的三个「跟随播放参数」；导出对话框里的**主版 / 霊界版**选择 |
| 6 | ListBox / ProgressBar | 只在**导出对话框**里：曲目清单是 ListBox，导出进度是 ProgressBar |

另外**顶部菜单的下拉弹层**也是系统浅色（和 ComboBox 同类问题），
这次一起没动，等第二轮再说。

## 自检

7 项全部通过（含新加入的 Themes/DarkTheme.xaml 解析）。
旧键名 `DarkMenuItem` / `PanelBg` / `CtrlBg` / `OkBrush` 等残留引用全部清零。
