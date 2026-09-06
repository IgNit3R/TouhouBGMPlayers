# 构建日志 23：分隔线白线（靠截图定位）

日期：2026-09-06
阶段：外观改进

## 靠截图才定位到

前三轮我都在猜「白线」在哪 —— 改了菜单弹层、去掉 ScrollViewer、加 Menu 样式，
但用户描述的位置（"收藏和快速导出的下面""设置菜单里导出…的下面"）其实是
**分隔线（Separator）** 的位置，而我一直在弹层描边上打转。

用户发来截图后一眼就对上了：右键菜单两个 Separator、设置菜单一个 Separator，
三处症状完全一致 —— **说明问题在 Separator 本身，不在容器。**

**教训：外观问题要说不清时，就该早点要截图。**
文字描述（"有白线""很厚的框"）定位不到具体控件，截图一秒就够。

## 根因：菜单里的 Separator 走专用资源键

WPF 给菜单内的 Separator 应用的是 **`MenuItem.SeparatorStyleKey`**，
优先级**高于**类型隐式样式（`TargetType="Separator"`）。

所以我写的隐式 Separator 样式对菜单内的分隔线**根本没生效**，
退回系统模板 —— 而系统模板的分隔线取的是 **`Foreground`**：

```xml
<!-- 系统模板大致这样 -->
<Rectangle Height="1" Fill="{TemplateBinding Foreground}" .../>
```

我们从 ContextMenu / MenuItem 继承下来的 Foreground 是**浅色文字色**
（`Text` `#FFEDEDED`），于是分隔线就渲染成一条白线。

## 修法

补一份带那个 key 的样式，`BasedOn` 到隐式样式，并把 Foreground 压成深色：

```xml
<Style x:Key="{x:Static MenuItem.SeparatorStyleKey}" TargetType="Separator"
       BasedOn="{StaticResource {x:Type Separator}}">
    <Setter Property="Foreground" Value="{StaticResource MenuSeparator}"/>
    <Setter Property="Background" Value="{StaticResource MenuSeparator}"/>
</Style>
```

Foreground 也要设 —— 万一哪天模板改回取 Foreground，不会再变白线。

## 顺带：弹层统一关掉透明

`AllowsTransparency="True"` 的透明弹层在两层叠加时容易出重影、
描边在亚像素位置缺角（就是之前"没画完整的白线"）。
三处弹层（ContextMenu / MenuItem 子菜单 / ComboBox 下拉）全部改成 `False`，
并加 `UseLayoutRounding="True"`。反正都是纯色底，不需要透明。

## 待验证

用户还没重新编译验证。要确认：

1. 右键菜单两个分隔线、设置菜单那个分隔线，是否变成深色细线
2. 顶部菜单打开时右侧是否还有深色竖条
3. ComboBox 下拉是否正常（这轮顺带改了它的弹层）

## 自检

7 项全部通过。
