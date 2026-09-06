# 构建日志 17：设置读不回来（真正的根因）

日期：2026-09-06
阶段：缺陷修复

## 上一轮判断错了

上一轮我认定是「XAML 控件默认值在构造期间触发事件，把设置覆盖回默认」。
那个问题**确实存在**（循环模式、音量会被冲掉），也修了，
但它解释不了「路径也没了」—— 路径没有任何 XAML 控件，
根本没机会被覆盖。

这轮拿到的决定性证据：

```
01:42  settings.json  703 字节  → 有 th17 路径、导出参数、窗口位置 493,150
01:55  settings.json  559 字节  → paths:{}、导出全 null、窗口位置 260,260（XAML 默认值）
```

窗口位置回到 260,260 是最关键的一条：**它只可能来自默认值**。
保存是好的，问题出在**读**。

## 根因：private 构造函数

```csharp
private AppSettings() { }        // ← 就这一行

public static AppSettings Load()
{
    try
    {
        ...
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
    }
    catch { /* 损坏就当没有 */ }   // ← 还有这个裸 catch
    return new AppSettings();
}
```

**System.Text.Json 要求目标类型有「公开」的无参构造函数。**
只有 private 时，`Deserialize` 抛 `NotSupportedException`；
外面又是个裸 `catch`，异常被静默吞掉，返回全新默认实例。

于是每次启动都必然发生：

1. `Load()` 抛异常 → 静默返回默认
2. 用户在这一次会话里配好路径、参数
3. 关窗时 `Save()` 正确写入
4. 下次启动重复第 1 步 —— **文件永远存得好好的，却永远读不回来**

这完美解释了之前那个让我困惑的细节：
为什么 01:42 那份文件里 `lastPlayed` 是空的 ——
`lastPlayed` 只在播放时写入，而那次会话只做了导出、没播放，
加上读取永远失败，上一次播放记录自然也带不过来。

## 修法

### 1. 构造函数改公开

```csharp
/// <summary>
/// 必须**公开**：System.Text.Json 反序列化要求类型有公开的无参构造函数。
/// 写成 private 的话 Deserialize 会抛 NotSupportedException…
/// </summary>
public AppSettings() { }
```

其他配置类（`PlaybackSettings` / `ExportSettings` / `UiSettings` /
`LastPlayed` / `CustomPlaylist`）都是隐式公开无参构造，只有这一个漏了。
全项目 grep `private X()` 确认无其他漏网。

### 2. 兜底 catch 必须留痕

```csharp
catch (Exception ex)
{
    // 坏了就当没有，但原因必须留痕
    AppPaths.WriteTextAtomic(
        Path.Combine(AppPaths.BaseDirectory, "settings.load-error.txt"), ...);
}
```

**只在真出错时才产生这个文件**，正常运行时不会多出任何东西。
这类「静默失败 + 现场一闪即逝」的问题，没有留痕就只能靠猜 ——
我这一轮就是靠对比文件大小才反推出来的，太险了。

### 3. 自检脚本加第 6 项

```
=== 6. 供 STJ 反序列化的类型是否有公开无参构造 ===
```

扫所有带 `[JsonPropertyName]` 的文件，找 `private ClassName()` 这种写法。

## 上一轮那个修复还留着吗

留着。`_ready` 开关防的是**另一类**问题（XAML 默认值覆盖），
虽然它不是这次的主因，但循环模式和音量确实会被它冲掉，属于真 bug。
两个问题叠加，所以上一轮改完仍然全部回默认。

## 自检

7 项全部通过。
