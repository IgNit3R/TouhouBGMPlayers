# 构建日志 21：菜单白边 / 标签页区分度 / 正在播放高亮 / 关于对话框

日期：2026-09-06
阶段：外观改进第二轮（验收后提出）

## 1. 顶部菜单打开时有很厚的白色框线

同 ComboBox 一类问题：**默认 MenuItem 模板的弹层用系统浅色背景 + 浅色描边**。

重写整份模板，另加：

- `ContextMenu` 样式（右键菜单是代码建的，走自己的模板，不写这份就还是浅色）
- `Separator` 样式（菜单分隔线原本也是系统色）
- 顶部项与下拉项**分开处理**：顶部悬停只轻微提亮（`BgHover`），
  下拉项才用强调色 —— 否则整条菜单栏会被蓝色盖住

## 2. 设置窗口四个标签页底色与文字区分度不够

同样是默认模板的锅：只设 `Background` 不顶用，选中/未选中的状态色在模板内部。

重写 TabItem 模板：

| 状态 | 底色 | 文字 | 其他 |
|---|---|---|---|
| 未选中 | 透明 | TextDim | — |
| 悬停 | BgHover | Text | — |
| **选中** | **BgElevated** | **Text** | **2px 强调色下划线** |

选中态比未选中抬了两档明度还加了下划线，不会再分不清。

同时把 SettingsWindow 里那份局部 TabItem 样式删了 —— 窗口级会盖掉应用级的。

## 3. 正在播放的曲目常驻底色（之前没做）

用户问起才确认：**上一轮我列在「加分项」里没做**，这轮补上。

- `TrackRow.IsPlaying`（带变更通知）
- `MainWindow.UpdatePlayingRow()`：换曲 / 换列表 / 切灵界版后刷一遍
- RowStyle 加 `DataTrigger`，底色 `RowPlaying` `#FF233A4D`

两个细节：

- **与选中态无关**。选中行可以切到别的曲目，正在播放的那首仍然亮着
- 触发器顺序：`IsPlaying` 放在 `IsSelected` **之前** ——
  WPF 里后命中的触发器生效，这样两个都命中时选中色优先，不会打架
- 灵界版不单独成行（挂在 alt 字段），所以切灵界版时这一行照样亮，符合要求

## 4. 关于对话框

原来是 `MessageBox` —— 系统浅色，和播放器整体不搭。改成正式对话框：

- **删掉**「程序目录：…」及下面那几行
- **新增**构建时间、依赖表（可滚动）
- 风格与播放器一致（走 `Theme.Get` 取画刷）

### 构建时间怎么拿（三个坑）

1. **不能用 PE 头时间戳**：SDK 默认确定性编译，那字段是内容哈希不是时间
2. **不能读 exe 修改时间**：可能是复制时间
3. 所以走 **MSBuild 注入**：

```xml
<AssemblyMetadata Include="BuildTimestamp"
                  Value="$([System.DateTime]::Now.ToString('yyyy-MM-dd HH:mm:ss'))" />
```

运行时用 `AssemblyMetadataAttribute` 读出来；读不到才回退到文件修改时间。

依赖表列了 .NET / NAudio / NAudio.Core / WPF / WinForms，
后两个版本用 `Assembly.Load(name).GetName().Version` 现取，不会写死后过期。

## 又踩了一次 Thickness

`AboutDialog.cs` 里 `Padding = new Thickness(12, 4)` —— 两参。
**这是第三次犯同一个错**（PromptDialog、ExportDialog、AboutDialog）。
自检脚本第 3 项每次都拦住了。肌肉记忆靠不住，检查必须有。

## 清理

- 删掉 MainWindow / SettingsWindow 里会盖掉主题的窗口级样式
  （Button / ComboBox / ComboBoxItem / TabItem / MenuItem）
- 删掉主题里的死条目 `AccentHover`（定义了没用到）

## 自检

7 项全部通过。
