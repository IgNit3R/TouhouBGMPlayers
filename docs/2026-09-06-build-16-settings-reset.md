# 构建日志 16：设置「重启后回到默认值」

日期：2026-09-06
阶段：缺陷修复

## 现象

> 设置的路径、循环参数，关掉程序再启动时又变成默认的了

## 先确认：不是没保存

`bin\Debug\net10.0-windows\settings.json` 内容完好：

```json
"paths": { "th17": "F:\\SteamLibrary\\steamapps\\common\\th17" },
"export": { "loopCount": 2, "extraSeconds": 10, "fadeSeconds": 5, ... },
"ui": { "left": 493, "top": 150, ... }
```

路径、导出参数、窗口位置都写进去了。所以问题在**启动阶段被覆盖回默认**，
不在保存阶段。

## 根因：XAML 的控件默认值会在构造期间触发一次变更事件

WPF 的 `InitializeComponent()` 会按 XAML 给控件赋初值，
而赋值就会触发 `SelectionChanged` / `ValueChanged`：

```xml
<ComboBox x:Name="LoopModeCombo" SelectedIndex="0" ... />   <!-- 触发一次，mode = 无限循环 -->
<Slider   x:Name="VolumeSlider"  Value="0.8"      ... />    <!-- 触发一次，volume = 0.8 -->
```

而构造函数里应用已保存设置的语句**在这些事件之后**才执行：

```csharp
InitializeComponent();        // ← 这里两个 handler 已经把 Playback 写成了默认值
...
LoopModeCombo.SelectedIndex = (int)pb.LoopMode;   // ← 读到的 pb 已经被改成默认了
VolumeSlider.Value = pb.Volume;
```

于是每次启动都必然发生：

1. 载入设置（比如 loopMode = 普通）
2. XAML 默认值把 loopMode 冲成「无限循环」
3. 关闭时把这个被冲掉的值存回去
4. 下次启动重复 —— **设置永久性地退化成默认值**

音量同理（无论设成多少，下次都回到 0.8）。

这也解释了文件里为什么 `loopMode` 永远是 0、`volume` 永远是 0.8，
而**没有对应 XAML 默认控件的字段（paths / export / ui 几何）都好好的**。

## 修法

### 1. 加 `_ready` 开关

```csharp
/// <summary>
/// 构造是否已走完。XAML 里给控件写的默认值会在 InitializeComponent() 期间
/// 就触发一次变更事件，而那时设置还没被应用上去 —— 放任它们写进设置，
/// 配置就会在每次启动时被覆盖回默认值。
/// </summary>
private bool _ready;
```

构造函数末尾置 `true`；`LoopModeCombo_SelectionChanged` 与
`VolumeSlider_ValueChanged` 开头 `if (!_ready) return;`。

这是个**通用防御**：以后再加任何会写设置的控件，加上这一句就不会重蹈覆辙。

### 2. 改了循环模式立刻落盘

原来只在关窗时统一保存。现在换模式当下就 `Save()`，
程序异常退出也不会丢这次改动。

### 3. 写文件改成原子替换

```csharp
public static bool WriteTextAtomic(string path, string contents)
{
    string tmp = path + ".tmp";
    File.WriteAllText(tmp, contents);
    File.Move(tmp, path, overwrite: true);   // 整体替换，没有中间态
}
```

直接 `WriteAllText` 时若进程中途死掉，会留下一个**截断的文件**；
下次启动 `Load()` 解析失败就静默退回默认 —— 用户看到的是
「配置莫名其妙全没了」，而现场已经消失，极难排查。

`AppSettings.Save()` 和 `PlaylistStore.Save()` 都改成走这个。

## 关于「路径也回默认」

从磁盘文件看，路径是**正常持久化**的（`paths.th17` 在）。
已确认的覆盖只影响有 XAML 默认值的两个控件（循环模式、音量）。

如果重启后确实看到路径没了，需要确认一下操作顺序：
设置窗口里必须点「应用」或「确定」才会写入，「取消」不保存。

## 自检

6 项全部通过。
