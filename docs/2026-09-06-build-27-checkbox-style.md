# build 27 —— CheckBox / RadioButton 深色样式

2026-09-06

## 问题

设置→播放参数 里「全局多媒体键」那行字是黑色的，与深色主题不符。

## 根因

和 ComboBox / MenuItem 同一类：**WPF 默认 CheckBox 样式的 Foreground Setter 取自
系统 ControlText（黑色）**。样式 Setter 的优先级高于从窗口继承的 Foreground，
所以窗口上设了 `Foreground="{StaticResource Text}"` 也不顶用。

这也是之前 B 清单第 4 项（CheckBox / RadioButton）当时跳过没做的遗留。
受影响的其实不止这一处：设置→导出 页的三个「跟随播放参数」CheckBox、
导出对话框里的「主版 / 霊界版」RadioButton，文字全是黑的。

## 改动（Themes/DarkTheme.xaml）

- **CheckBox 样式**：14×14 方框（BgElevated 底 + Border 描边），勾是 Path
  （M 1,5.5 L 4,8.5 L 9,1.5，Text 色 1.6 粗），IsChecked={x:Null} 时显示短横线；
  悬停 BorderHover、按下 BgPressed、禁用 TextFaint + 框 0.5 透明。
- **RadioButton 样式**：14×14 圆框 + 6×6 内圆点，状态色同上。
- 文字 Foreground 显式设为 Text，不再吃系统 ControlText。

至此 B 清单第 4 项完成。剩余未做：ToolTip（3）、ListBox/ProgressBar（6，仅导出对话框）。

## 验证

check_src.py 7 项全过。待实机看：设置→播放参数 的多媒体键行、导出页三个跟随框、
导出对话框的主版/霊界版单选，文字应为亮色，勾选标记清晰可见。
