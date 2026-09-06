# 构建日志 · 第 4 轮：播放内核（第二步）

（按红线不改写已有文件，另开新文件记录。）

## 新增文件

| 文件 | 作用 |
|---|---|
| `Data/TrackRef.cs` | 曲目的`(作品代号, 曲序)`二元组。列表 / 收藏只存它，**不存文件路径**，改了路径设置后依然有效 |
| `Audio/IAudioSource.cs` | 音频源抽象：开流 / 定位 / 读裸 PCM。tf 侧的扩展点 |
| `Audio/PcmFileSource.cs` | 唯一实现。ZWAV 的 thbgm.dat 与 TH06 的 wav 在读取层完全一样 |
| `Audio/AudioSourceFactory.cs` | 按索引字节偏移定位文件。路径来自 settings.json，无默认值、不扫描 |
| `Audio/LoopSampleProvider.cs` | 时间线 + 循环 + 淡出 + 定位（核心） |
| `Audio/CrossfadeMixer.cs` | 两路输入带增益斜坡的混音器，用于 TH13 灵界版切换 |
| `Audio/PlayerEngine.cs` | 多源调度、WASAPI 输出、音量、切曲淡化、灵界版切换 |

改动：`MainWindow.xaml.cs`（接线）、`MainWindow.xaml`（补 4 个事件绑定）。

## NAudio 3.0.1 的重要 API 变更（3.x 与 2.x 不兼容）

1. **`ISampleProvider.Read` 改成了 `int Read(Span<float> buffer)`**，
   不再是 `Read(float[] buffer, int offset, int count)`。所有自定义 provider 都得按 Span 写。
2. **`WasapiOut.Init` 只收 `IWaveProvider`**，没有 `ISampleProvider` 重载。
   末端必须挂 `SampleToWaveProvider16` 把 float 转回 16bit。
3. 构造器：`new WasapiOut(AudioClientShareMode.Shared, bool useEventCallback, int latencyMs)`。
4. 可用的关键类型：`WdlResamplingSampleProvider`（纯托管重采样，不依赖 Media Foundation）、
   `VolumeSampleProvider`、`SampleToWaveProvider16`、`WaveFormat.CreateIeeeFloatWaveFormat(rate, ch)`。

## 播放管线

```
音频源(44100 或 22050, 16bit/2ch)
  → [WdlResamplingSampleProvider → 44100]     灵界版 22050 才需要
  → CrossfadeMixer(44100 / IEEE float / 2ch)  灵界版切换时两路并存
  → VolumeSampleProvider                      总音量 + 停止淡出 + 切曲淡入
  → SampleToWaveProvider16                    float 转 16bit
  → WasapiOut 共享模式
```

全库格式统一 **16bit / 2ch**（已用脚本核对 346 条），采样率只有 44100（333）与 22050（13 首灵界版）。

## 时间线模型（LoopSampleProvider）

```
|-- intro --|-- loop × N 次 --|-- 额外 X 秒（续播 loop 内容）--|-- 淡出 F 秒（边播边淡出）--|
```

- 总长 = intro + N×loop + X + F；淡出段是**额外加在最后**的一段，播的仍是 loop 内容
- 无限循环模式没有终点，N / X / F 全部忽略
- 源位置与时间线位置用 `MapToSource()` 换算：`intro` 内 1:1，之后按 `(t - intro) % loop` 折返
- 进度条：无限循环显示 **loop 段内位置**（走 `SeekToLoopPosition`），
  有限模式显示整条时间线（走 `SeekToTime`）—— 两者语义不同，不能混用

## 自查修掉的 5 个问题

1. **进度条回环**：`UpdateProgress` 回写 Slider.Value 会触发 `ValueChanged` → 又去 Seek，
   而 Seek 会清掉"已播完"标志，导致自动切曲永远触发不了。加 `_updatingSlider` 标志挡掉。
2. **滑块语义错位**：无限循环下进度条是 loop 内位置，但 `Seek()` 走整条时间线。
   补 `SeekToLoopPosition()` / `SeekLoop()`。
3. **灵界版前缀叠加**：原写法 `("霊界版 · " + Text.TrimStart())` 反复切换会不断累积。
   改为 `_nowDetail` 存原文 + `RefreshNowPlaying()` 重新拼。
4. **`SwitchAlt` 会在暂停时启动播放**：去掉多余的 `_out.Play()`，输出状态保持不变。
5. **格式描述错误**：`LoopSampleProvider` 原先对外宣称源的 16bit PCM，但它输出的是 float，
   会让下游重采样器 / 转波形节点误判。改为 `CreateIeeeFloatWaveFormat`。

另外两处收尾：`HardStop` 里 `_out.Stop()` 已返回、混音器已清空，可以立刻 `Reap(0)` 释放句柄，
不必再等 500ms；`Play()` 开头 `DisposeFadeTimer()`，否则进行中的"停止淡出"到点会把新曲子一起停掉。

## 静态自检（无法编译，只能这样兜底）

- 16 个 .cs 文件括号全部配平
- XAML 里所有事件处理器在代码里都有对应方法，参数类型抽查无误

## 待用户验收

1. `dotnet build`
2. 双击曲目播放；TH13 的 13 首灵界版按钮可切换，其余 5 首灰显
3. 三种循环模式：无限 / 普通 / 随机
4. 普通模式末尾回到第一首
