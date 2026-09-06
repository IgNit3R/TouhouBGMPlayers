# 待办：字体支持（用户指定为「验收完之后的改进项目」）

日期：2026-09-06 记录

## 现状

已经有基础支持，不是从零做：

- **设置 → 外观 → 界面字体**，下拉四选一：
  - Yu Gothic UI（默认，推荐）
  - Meiryo UI
  - Microsoft YaHei UI
  - MS Gothic
- 存 `settings.json` 的 `ui.fontFamily`，值是一串**回退链**（逗号分隔），
  如 `Yu Gothic UI, Meiryo UI, Microsoft YaHei UI`
- 启动时 `MainWindow.ApplyFont()` 读它设到窗口上；
  代码搭的对话框（PromptDialog / ExportDialog）从 `owner.FontFamily` 继承
- 改了之后要重启才完全生效（设置里改完会立刻预览窗口自身）

## 待用户明确

「增加对字体方面的支持」可能是下面某一条，也可能都要，
**等当前主题改进验收完再问清楚**：

1. 候选字体太少？—— 加更多日文字体
2. 想分开设置？—— UI 字体 vs **曲名字体**（曲名是日语原名，可能想要别的字体）
3. 想调字号？—— 现在 FontSize 写死 13
4. 回退链想自己编辑？—— 现在只能选预设的四项

## 相关文件

- `MainWindow.xaml.cs` 的 `ApplyFont()`
- `UI/SettingsWindow.xaml(.cs)` 的 `FontCombo` / `LoadUi()` / `FontCombo_SelectionChanged`
- `Core/AppSettings.cs` 的 `UiSettings.FontFamily`

## 注意

字体改动会同时影响 MainWindow、SettingsWindow 和两个代码搭的对话框
（它们从 owner 继承），改的时候四处都要验。
