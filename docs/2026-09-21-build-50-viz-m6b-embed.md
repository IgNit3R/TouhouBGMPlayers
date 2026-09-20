# build-50：M6b 内嵌（方案 §十 的落地）

日期：2026-09-21
关联：`docs/2026-09-21-build-49-viz-m6b-step1.md`（第一步：抽 `VizSurfaceHost`）、
已批准方案 §3.6（判定表）、§十（接入期改动预览）、§8.2（接入期判据）

**范围**：M6b 的 2～4 步 —— 主窗口加列、第二套区域、最大化↔还原切换。**M6b 到此功能完整**。

| 项 | 结果 |
| --- | --- |
| 新增文件 | **1 个**：`src/Viz/VizRenderers.cs` |
| 修改文件 | **5 个**：`MainWindow.xaml(.cs)` / `VizWindow.xaml.cs` / `VizSurfaceHost.xaml.cs` / `SettingsWindow.xaml(.cs)` |
| `dotnet build` | **成功**，0 警告 / 0 错误 |
| `--viz-selftest` | **20 / 20 全绿** |
| XAML 良构 | `check_xaml.py` **6 / 6** |

---

## 一、主窗口加列（方案 §十）

```xml
<Grid.ColumnDefinitions>
    <ColumnDefinition Width="*" MinWidth="260"/>   <!-- 主体 -->
    <ColumnDefinition Width="Auto"/>               <!-- 分隔条 -->
    <ColumnDefinition x:Name="VizColumn" Width="0"/>  <!-- 内嵌可视化 -->
</Grid.ColumnDefinitions>
```

- 五行加 `Grid.ColumnSpan="3"`（菜单 / 播放列表行 / 进度条 / 播放控件 / 曲目信息）；
  **`GridHost` 那一行不加** —— 它就该只占主体列，于是内嵌列一开，曲目表被挤窄（方案 §2 要的效果）。
- `GridSplitter` + `ContentControl x:Name="VizHost"` 插在 `GridHost` **之后**、与它**平级**：

> ⚠️ 方案 §十 特别标了这条：**绝不能放进 `GridHost` 内部** —— 那是框选 / 拖拽排序的坐标系，
> 多一个子元素就把 `MarqueeRect` / `InsertLine` 那套相对坐标搞乱。

⚠️ **一个容易漏的细节**：内嵌列的 `MinWidth` **只在开着时给**。
关掉时列宽必须是**真的 0** —— 否则 `MinWidth` 会让它留一条缝，
而「**功能关掉时主窗口逐像素不变**」是这条设计的硬要求。

---

## 二、两处宿主：帧怎么给、渲染器怎么共享

### 帧：从附件窗口**推**出去，不是各持一个分析器

`VizWindow` 新增 `event Action<VizFrame>? FrameReady`，每渲染一帧推一次；
主窗口的 `OnVizFrameReady` 把它转给内嵌那套。

为什么不让内嵌侧自己建一个分析器：那就是**两份平滑状态、两个真相源** ——
音量/淡出/余辉各算各的，两边画面会不一样。

### 渲染器：**共享同一份实例**（含 D 的余辉）

新增 `VizRenderers`（四块的实例），`VizWindow` 持有一份、主窗口装配内嵌区域时把它递给对方。

> **D 的余辉是渲染器侧状态**（最近 20 帧的轨迹）。两处各持一份的话，
> 「窗口化 ↔ 最大化」每切一次团雾就从零重来 —— 表现为「一最大化，画面就空一下」。
> A/B/C 本身无状态，共享它们没有副作用 —— 所以四块一起共享，**少一条特例**。

### 帧的浪费也省掉了

`VizWindow.OnVizFrame` 里：**不可见就跳过自己那套的绘制**。
`Hide()` 之后它虽然还在收帧，但没必要给看不见的面板录一遍绘制指令（每帧约 50KB 的分配，实测过）。

---

## 三、切换与分隔条

| 机制 | 做法 |
| --- | --- |
| 谁该显示 | `VizWindow.PlacementMode`（`ApplyPlacement` 算出来的）+ `event PlacementChanged` |
| 主窗口反应 | `SetEmbedded(mode == AttachedEmbedded)`（**幂等**）：开关列宽 / 分隔条 / 区域可见性 |
| 立刻补一帧 | 切进内嵌时用 `VizWindow.Frame` 补画一次 —— 暂停时节拍可能已退订，不补就是一片空白 |
| 分隔条拖动 | `DragCompleted` → 宽度写回 `AppSettings.Viz.EmbeddedWidth` 并保存（方案 §8.2；下限 200，不许拖没） |
| 关可视化 | 退订两个信号 → 摘宿主 → 关窗 → `SetEmbedded(false)` 收列 |

### ⚠️ 自查出的一个「闪一下」

开可视化时若主窗口**已经是最大化**，原来的顺序是「先 `Show()` 再被判定 `Hide()`」——
窗口会实打实闪一下 ✗。改成 **只在自由模式下自己 `Show()`**：

- 贴附模式：`ApplyPlacement` 已经按判定显示过它了（`a.Show` 分支）；
- 内嵌模式：判定要的就是「别显示」，从头到尾不该出现。

---

## 四、设置页

`可视化` 页新增「**主窗口最大化时改为内嵌**」（`EmbedWhenMaximized`），
并把 `VizWindow.EmbedWhenMaximized` 从 M6a 的「恒为 false」**改回读设置**。

（M6a 那时恒为 false 是权宜：内嵌宿主还没做，传 true 会「隐藏了却没人接手显示」——
见 build-46 §五。现在两边都到位了。）

---

## 五、未验证 / 待用户复验

⚠️ **主窗口的布局自检覆盖不到**：构造 `MainWindow` 会连带建引擎、加载播放列表、起定时器 ——
自检刻意不碰它。所以主窗口这一侧只能人工验。

1. **关掉可视化**：主窗口应与从前**逐像素一致**（不该多出任何一条缝）。
2. **打开 + 窗口化**：附件窗口贴主窗口右侧、顶对齐、等高（同 M6a）。
3. **打开 + 最大化**：附件窗口**消失**，五块画面出现在主窗口**右侧一列**，曲目表被挤窄。
4. **拖那条分隔条**：能调宽窄，且**关掉再开/重启后宽度被记住**。
5. **还原窗口**：内嵌列收回、附件窗口回来（画面接着动，不重新开始 —— 因为渲染器实例是共享的）。
6. **最小化主窗口**：两个都不显示。
7. **设置里取消勾选「最大化时内嵌」**：最大化变成 M6a 那种「退化为贴右侧」。
