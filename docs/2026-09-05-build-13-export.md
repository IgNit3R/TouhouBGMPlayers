# 构建日志 13：导出音频（第三步最后一块）

日期：2026-09-05（跨零点续做）
阶段：第三步 B

## 新增文件

| 文件 | 说明 |
|---|---|
| `Audio/WavExporter.cs` | 按普通模式时间线渲染 wav |
| `UI/ExportDialog.cs` | 导出对话框，代码搭建（与 `PromptDialog` 一致） |
| `dependence/03_tools/check_src.py` | C# / XAML 静态自检脚本，见文末 |

## 导出渲染器

复用播放的 `LoopSampleProvider` —— 导出与播放走**同一套代码**，
所以导出的时间线和听到的一致，不存在两套实现慢慢跑偏的问题。

```
IAudioSource(内存) → LoopSampleProvider(普通模式, N/X/F) → SampleToWaveProvider16 → WaveFileWriter
```

导出**一律按普通模式**，与当前处于哪种循环模式无关
（无限循环没有终点，导出设置里的 N / X / F 就是终点）。

输出 16bit PCM wav，沿用源采样率（灵界版 22050）。
文件名 `{作品代号}_{曲号}_{曲名}.wav`，Windows 非法字符替换为 `_`。

### 一个必须注意的地方

`LoopSampleProvider.Read` **永远返回满长度**（短了就补静音），
所以不能靠 `WaveFileWriter.CreateWaveFile` 那种「读到 0 就停」的写法 —— 它会永远不停。
必须自己算总帧数再按量读：

```csharp
long wantFrames = (long)Math.Round(loop.TotalTime.TotalSeconds * td.Rate);
long wantBytes  = wantFrames * bytesPerFrame;
while (done < wantBytes) { ... }
```

## 导出对话框

用代码搭建而非 XAML，理由和 `PromptDialog` 一样：内容固定、不需要样式复用，
还能避开「灵界版选项按曲目有无决定是否显示」这种动态部分要写的触发器。

三个数值字段**各自独立**地「跟随 / 覆盖」：

- 勾上跟随 → 存 `null`，运行时实时取播放参数
- 取消勾选并填值 → 固定下来，播放参数再变也不跟着变，反过来也不影响播放参数

取消「跟随」时会把播放参数的当前值填进输入框当起点，省得用户去记。

灵界版选项只在**选中的曲目里有带灵界版的**时才显示；
如果部分曲目没有，旁边会提示「其中 N 首没有霊界版，将导出主版」。

导出途中禁止直接关窗（先中止），有进度条和当前文件名。

## 两个入口

| 入口 | 行为 |
|---|---|
| 导出…（右键 / 设置页） | 打开对话框，可改 N/X/F、选目录、选主版/霊界版 |
| 快速导出（右键） | 不弹窗，按当前导出设置直接出；灵界版取正在播放的版本 |

两个都支持多选批量导出。

## 设置窗口「导出」页

三个数值 + 跟随复选框 + 输出目录。留空则用程序目录下的 `export\`。

## check_src.py —— 本轮顺手固化的自检脚本

之前每轮都是临时写一段 Python 检查，这次固化成文件：

```
python dependence/03_tools/check_src.py [已删除的符号名...]
```

六项检查：

1. XAML 能否通过 XML 解析（上一轮就是靠这个发现 XAML 被改坏的）
2. C# 括号配平
3. **构造函数实参个数**（WPF `Thickness` 只有 1 参或 4 参）
4. XAML 事件处理器是否都有对应方法
5. 逐文件核对 `x:Name` 与代码引用（全局核对会有假阳性，必须按文件配对查）
6. 已删除/改名的符号是否有残留（命令行传入）

**它第一轮就抓到了 5 个错**：新写的 `ExportDialog.cs` 里我又用了
`Padding = new Thickness(5, 4)` —— 这是把 WinForms `Padding` 的两参习惯带过来了，
WPF 的 `Thickness`（`Margin` 和 `Padding` 都是它）没有两参构造。
这正是第 3 项检查存在的理由。

## 自检结果

全部通过（XAML 3 份、括号配平、构造签名、事件绑定、17 个命名控件、4 个已删符号零残留）
