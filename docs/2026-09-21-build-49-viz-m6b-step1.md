# build-49：M6b 第一步 —— 抽出 `VizSurfaceHost`

日期：2026-09-21
关联：`docs/2026-09-21-build-45-viz-m6a.md`（接入）、
已批准方案 §四（文件清单里的 `VizSurfaceHost.xaml/.cs`）、§十（内嵌改动预览）

**范围**：M6b 的第一步，**纯搬家**：把五块区域的布局从 `VizWindow.xaml` 抽成可复用的 UserControl。
两步走的原因见文末 —— 这一步**不许掺任何观感变化**，否则下一步（主窗口加列）出问题就分不清是谁的。

**基线**：用户已提交（`596fd9d visualizer plug addon v1`，`git status` 干净）。

---

## 一、结论

| 项 | 结果 |
| --- | --- |
| 新增文件 | **2 个**：`src/Viz/VizSurfaceHost.xaml` / `.xaml.cs` |
| 修改文件 | **4 个**：`VizWindow.xaml(.cs)` / `DarkTheme.xaml` / `VizSelfTest.cs` |
| `dotnet build` | **成功**，0 警告 / 0 错误 |
| `--viz-selftest` | **20 / 20 全绿** |
| XAML 良构 | `check_xaml.py` **6 / 6**（新文件入库） |
| **等价性** | 布局比、渲染像素差、每帧分配**三个数字逐位相同** ✓ |

---

## 二、做了什么

`VizSurfaceHost` = **状态条以下的那两行**（`GroupFirst` 2.3* + `GroupSecond` 1*）。
状态条**留在窗口里** —— 那是附件窗口特有的（调试声源报错用），内嵌时没有它的位置。

对外只有两个方法：

```csharp
public void ApplyPanelVisibility();      // 按设置里的面板开关决定哪几块真的画（幂等）
public void Render(VizFrame frame);      // 把一帧发给四块面板并重画
```

配套改动：

| 位置 | 改动 |
| --- | --- |
| `VizWindow.xaml` | 两行 → 一行 `<viz:VizSurfaceHost x:Name="Surface" Grid.Row="1"/>`；根 Grid 行定义 `Auto/2.3*/1*` → `Auto/*`（比例挪进 surface，**不变**：状态条是 Auto，隐藏时高度 0） |
| `VizWindow.xaml.cs` | 删 `_panels` 字段；`OnVizFrame` → `Surface.Render(frame)`；`ApplyPanelVisibility` 转发给 Surface |
| `VizSelfTest.CheckVizLayout` | 三个容器与四个面板改用 **`surface.FindName(...)`** —— 见 §三 |
| `DarkTheme.xaml` | 收留 `VizCell` —— 见 §四 |

---

## 三、`FindName` 不穿透子命名域（自检侧）

`CheckVizLayout` 本来就是 `FindName` 取元素的（当初写得对），但它在**窗口**上找 ✗：
搬进 UserControl 后那几个 `x:Name` 属于**它自己的命名域**，
`Window.FindName` 不穿透子命名域 → 返回 `null` → 那条断言会以
「x:Name 被改了？」的面目失败，**把人往错方向带** ✗。

改成在 `surface` 上找，并在注释里写明原因。

---

## 四、⚠️ 踩到的坑：`VizCell` 放在宿主窗口的 `Resources` 里

`VizCell`（区域外框样式）原来定义在 `VizWindow.xaml` 的 `<Window.Resources>` 里。
搬家后 UserControl 自己解析时**够不着宿主的 Resources**：

```
XamlParseException：“在 StaticResourceExtension 上提供值时引发了异常。” 行号 48
```

（48 行就是 `<Border Style="{StaticResource VizCell}">`。）

**修法**：把 `VizCell` 搬进 `DarkTheme.xaml`（应用级）——
五块区域要被**两个宿主**共用，这个样式本来就该在那儿。

> 🔑 **规矩**：凡是要被**多个宿主**共用的 XAML 资源，必须放**应用级主题**，
> 不能放在任一宿主窗口里。
> `StaticResource` 是**解析期**按 XAML 文档**自己的祖先作用域**找的，
> 不跟着运行时的逻辑树走 —— 所以「窗口能解析到」并不蕴含「它的子控件也能」。

---

## 五、等价性证据（这一步唯一该看的东西）

| 指标 | 搬家前 | 搬家后 |
| --- | --- | --- |
| 组比 / 列比 | 2.299 / 2.341 | **2.299 / 2.341** |
| A:B / D:封面 | 1.006 / 1 | **1.006 / 1** |
| 面板尺寸（默认 560×480） | A 385×152 B 385×151 C+J 161×309 D 273×131 | **同** |
| 渲染像素差（A/B/C+J/D） | 1505 / 4826 / 5982 / 346 | **同** |
| 每帧分配 | 0.9 / 17.7 / 9.1 / 22.4KB | **同** |

**一个数没变** ✓ → 纯搬家、零观感变化。

---

## 六、为什么先停在这里

面试代码里最贵的错误是「一次改两件事」—— 出了症状不知道是谁引起的。
这一步是**结构性搬家**（动的是自检赖以工作的命名域），下一步是**加列改布局**（动的是主窗口）。
分开做，各自有一个干净的对照。

**待用户验**：`--viz` 打开附件窗口，画面应与之前**完全一样**（含拖放、空格暂停、拖右边缘缩放）。

---

## 七、下一步（第二步，未开始）

1. 主窗口根 Grid 加 `* / Auto / 0` 列 + 5 行加 `ColumnSpan="3"`
   （`:37/59/176/196/241`；**`:84` 的 `GridHost` 一动不动**）+
   在 `GridHost` **之后**放 `GridSplitter` + `ContentControl x:Name="VizHost"`；
2. 第二套 `VizSurfaceHost` 实例 + 共享分析器，**D 实例两个宿主共享同一个**（用户已定）；
3. `StateChanged` → 最大化时解锁内嵌列（宽度写回 `EmbeddedWidth`）、还原时收回 0；
4. `VizWindow.EmbedWhenMaximized` 改回读设置（现在恒为 false，见 build-46 §五）+ 设置页放出该开关。
