# build 35 —— 双字体：界面字体（中文 UI）与曲名字体（日文内容）拆分

2026-09-06

## 设计

用户观察：播放列表与播放信息是日文，其余 UI 是中文 —— 拆成两种字体各选各的。
中文字体（雅黑系）与日文字体（Yu Gothic 系）擅长区不同。

- **界面字体**（中文 UI：菜单、按钮、设置页、对话框）→ 沿用 `Ui.FontFamily`
- **曲名字体**（日文内容）→ 新增 `Ui.ContentFontFamily`，默认同为 DefaultFontChain
  —— 默认值不变，对现有用户**零外观变化**

## 曲名字体套用点

| 位置 | 控件 |
|---|---|
| 曲目表 | `TrackGrid` 整体（作品/曲序/曲名/时长/循环列） |
| 正在播放 | `NowPlayingText`、`NowPlayingGame`（参数行保持 Consolas） |
| 导出对话框 | 曲目清单 ListBox（回退顺序：曲名字体 → 界面字体） |

`Theme` 新增 `UserContentFontFamily` 与 `ApplyContentFont(el)`，与界面字体共用
`Resolve`（无效设置返回 null = 不套，用默认）。

## 设置→外观

两个下拉（共用 `FillFontCombo` 填充逻辑）：
- 界面字体（中文 UI）：选中即在设置窗口本体预览
- 曲名字体（日文内容）：新增日文样张行
  `亡き王女の為のセプテット / ネクロファンタジア / 上海紅茶館`，选中即时预览

主窗口在设置应用/关窗后经 `ApplySettingsSideEffects → ApplyFont` 同时刷新两种字体。

## 验证

check_src.py 7 项全过。待实机：两个下拉互不影响；选纯中文字体（如雅黑）做界面、
Yu Gothic 做曲名时，菜单是雅黑、曲目表是 Yu Gothic。
