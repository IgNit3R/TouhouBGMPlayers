# build 34 —— 字体支持：系统已安装字体任选 + 文件字体预留入口

2026-09-06

## 实现（本地已安装字体）

- 设置→外观「界面字体」从 4 个硬编码预设改为：**默认回退链 + 系统全部已安装字体**
  （`Fonts.SystemFontFamilies`，按名称排序去重），键盘输入可跳转
  （IsTextSearchEnabled）。条目统一用界面字体显示名称（防止图标字体把名字画成符号）。
- **迁移**：旧设置存的是预设回退链。整条等于默认链 → 默认项；否则取链的第一个
  字体名匹配已安装字体（旧预设自动迁移成单字体名，选中即生效）。
- 选中立即预览（设置窗口本身）+ 写入内存设置（沿用原行为，随下次保存落盘）。

## 全窗口覆盖

新增 `Theme.UserFontFamily`（无效设置返回 null = 用默认，不缓存）与
`Theme.ApplyUserFont(Window)`：
- MainWindow.ApplyFont、SettingsWindow 构造 → ApplyUserFont
- PromptDialog / ExportDialog / AboutDialog → 初始化器里 `FontFamily = Theme.UserFontFamily`

此前只有主窗口吃用户字体，对话框全是系统默认。

## 字体文件加载（.ttf/.otf/.ttc）：预留入口，未实现

按用户意见留入口以后做：`UiSettings.FontFile`（string?，非空时优先于 FontFamily），
实现要点写在字段注释里（GlyphTypeface 读家族名、file URI+#家族名、ttc 多家族、
文件丢失静默回退）。

## 其他

- `UiSettings.DefaultFontChain` 提为常量（原字面量散落两处）
- check_src.py 7 项全过
- 版本未动（1.0c 已提交）；本特性建议定版 1.0d，待用户确认

## 待实机

设置→外观选任意已安装字体：设置窗口立即预览；关窗后主窗口、菜单、曲目表、
对话框字体一致；日文曲名在选的字体下无缺字（缺字形系统自动回退）。
