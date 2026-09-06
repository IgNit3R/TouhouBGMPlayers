# 构建日志 14：导出编译错误（两个签名问题）

日期：2026-09-06
阶段：第三步 B · 第一次编译

## 报错

```
WavExporter.cs(96,30): error CS1501  “Read”方法没有采用 3 个参数的重载
SettingsWindow.xaml.cs(206~208): error CS7036  未提供所需参数“min”
```

## 错误 1：NAudio 3 把 `IWaveProvider.Read` 也 Span 化了

之前只确认了 `ISampleProvider.Read` 改成 `int Read(Span<float>)`，
**没想到 `IWaveProvider` 也一起改了**：

```csharp
// NAudio 3.0.1
int IWaveProvider.Read(Span<byte>)          // 不再有 Read(byte[], int, int)
int ISampleProvider.Read(Span<float>)       // 不再有 Read(float[], int, int)
int SampleToWaveProvider16.Read(Span<byte>)
```

改法：读的时候传 span，落盘那一侧不变 ——
`WaveFileWriter.Write(byte[], int, int)` 仍收数组。

```csharp
int got = toWave.Read(buf.AsSpan(0, chunk));   // 读：Span
writer.Write(buf, 0, got);                     // 写：数组
```

**同名方法不同层用不同参数形态，这是 NAudio 3 最容易踩的地方。**

梳理了全项目的 `.Read(` 调用，确认其余三参调用都安全：

| 位置 | 接收方 | 签名 |
|---|---|---|
| `LoopSampleProvider.cs:246` | 自家的 `IAudioSource` | `Read(byte[], int, int)` ✓ 我们自己的接口 |
| `PcmFileSource.cs:59` | `FileStream` | `Read(byte[], int, int)` ✓ BCL 没变 |
| `CrossfadeMixer.cs:121` | `ISampleProvider` | `Read(span)` ✓ |
| `WavExporter.cs:99` | `IWaveProvider` | 已改为 `Read(span)` ✓ |

## 错误 2：复用了错误的 Parse 重载

设置窗口里已有的 helper 是给播放参数写的，fallback 是**非空**类型：

```csharp
private static int ParseInt(string s, int fallback, int min, int max)
```

导出参数那三个字段是 `int?` / `double?`，**null 有含义**（表示跟随播放参数），
不能套用非空版本的回退值 —— 硬套会把 null 抹成 0，"跟随"就永久失效了。

所以另加了两个可空版本，与播放参数的 helper 分开：

```csharp
private static int? ParseExportInt(string s, int? fallback) =>
    int.TryParse(s.Trim(), out var v) ? Math.Clamp(v, 0, 999) : fallback;
```

命名上刻意带 `Export` 前缀，避免下次又顺手调错。

## 自检脚本的表现

6 项检查全部通过 —— 但**这两类错误它一项都抓不到**：
NAudio 的签名变化和可空回退值的重载选择，都只有编译器知道。

脚本能抓的是结构性问题（XML、括号、构造实参个数、事件绑定、命名控件、残留符号），
语义层面的还得靠编译。分工就是这样，别指望它替代 `dotnet build`。
