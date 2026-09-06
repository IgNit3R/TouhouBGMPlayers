# 构建日志 10：换链交叉淡化失效（切换模式时卡滞 / 混乱）

日期：2026-09-04
阶段：第三步 · 用户反馈修复

## 症状

切换循环模式时音频卡滞、混乱。（普通播放正常）

## 根因：两个 bug 叠在一起

### Bug 1：新链满增益进入

```csharp
// 旧代码：换曲走这条
public void SetSource(ISampleProvider source) =>
    _pending = new Lane { Source = source, From = 1f, To = 1f, RampFrames = 0 };
```

新链的 `From = 1f`，也就是**一进来就是满增益**，没有任何淡入。
而旧链被转成 outgoing 时 `RampFrames = Math.Max(1, _pending.RampFrames) = 1`。

### Bug 2：增益每个缓冲区才算一次

```csharp
// 旧代码
float g = lane.Gain;                       // Pos 取的是缓冲区开头
for (int i = 0; i < n; i++) dest[i] += span[i] * g;
```

WASAPI 延迟 100ms，一个缓冲区就是 **四千多帧**，却只取一个增益值。
outgoing 那条链在缓冲区开头 `Pos = 0`，`Gain = From = 1` —— 于是
**整整 100ms 里旧链维持满增益**，和新链的满增益叠加。

### 叠加后果

```
+6 dB（1.0 + 1.0）→ 削顶失真 → 听感「混乱」
电平在缓冲区边界突变      → 听感「卡滞」
```

`RampFrames = 1` 本意是「立刻淡掉旧链」，但因为增益是按缓冲区取的，
这个 1 帧的渐变被拉满到整个缓冲区，反而让旧链以最大音量播了 100ms。

## 为什么灵界版切换没这问题

`SwitchAlt` 走的是 `SwitchTo`（`From = 0`，真正的 20ms 交叉淡化），
而且两条链内容相同、位置对齐，即使增益按缓冲区取，
粗略的交叉淡化听起来也还是顺的 —— 所以问题被掩盖了。

**换曲 / 换模式走的是 `SetSource`，两个 bug 全中。**

## 修复

### 1. `CrossfadeMixer.Mix` 逐帧算增益

```csharp
if (lane.RampFrames <= 0 || lane.Pos >= lane.RampFrames)
{
    float g = lane.To;                                   // 不在渐变中，快路径
    for (int i = 0; i < n; i++) dest[i] += span[i] * g;
}
else
{
    long use = Math.Min(frames, lane.RampFrames - lane.Pos);
    for (int f = 0; f < frames; f++)
    {
        float g = f < use
            ? (float)(lane.From + (lane.To - lane.From) * ((double)(lane.Pos + f) / lane.RampFrames))
            : lane.To;
        ...
    }
}
```

只在渐变期间走慢路径，其余时间仍是整块乘常数。

### 2. 删除 `SetSource`，换曲统一走 `SwitchTo`

`SwitchTo` 的 `From = 0`、两条链增益**线性互补**（0→1 / 1→0），
叠加后的总电平恒定为 1 —— 换曲时音量不跳。

切循环模式时新旧链是**同一首、位置逐帧相同**，
互补叠加正好还原成原信号，不会产生梳状滤波（梳状滤波正是「混乱」的另一种来源）。

### 3. 去掉音量节点那条淡化路径

原来换曲是「把 `_volume.Volume` 压到 0，再用 8ms 定时器爬回来」。
这条路根本不成立：**音量节点按定时器改增益，音频回调按缓冲区（约 100ms）取用**，
30ms 的渐变压根落不进缓冲区，等于硬切。

现在淡化全部交给混音器在采样层面做，音量节点只保留用户设的总音量。

## 顺带修：暂停时切换模式会把声音放出来

`RebuildCurrent` 调 `PlayTrack` → `Play()` → `if (!IsPlaying) _out.Play()`，
于是暂停状态下改参数会开始播放。改成记录之前的状态，重建后按回去：

```csharp
if (!wasPlaying && _engine?.IsPlaying == true) _engine.Pause();
```

加 `IsPlaying` 判断是因为设备可能从未启动过，
对一个没在播的 `WasapiPlayer` 调 `Pause()` 行为不确定。

## 自检

- 三份 XAML 通过 XML 解析
- 所有 `.cs` 括号配平
- `SetSource` 引用归零（定义已删，无残留调用）
