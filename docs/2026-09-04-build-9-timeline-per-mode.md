# 构建日志 9：进度条时间轴按模式区分

日期：2026-09-04
阶段：第三步 · 用户反馈调整

## 需求

> 普通播放模式的 loop 是 2，所以进度条的时间三个模式都变成了 loop=2 的长度。
> 无限循环模式应该是 loop=1 情况下的时间轴，另外两个模式看播放设置。

即进度条的量程：

| 模式 | 量程 |
|---|---|
| **无限循环** | 整轨（intro + 一遍 loop），等价于 loop=1，**不看 N / X / F** |
| 普通 / 随机 | intro + N×loop + X + F，完全按播放设置 |

## 改动

### 1. `LoopSampleProvider._emitFrames`

```csharp
// 之前
_emitFrames = _infinite ? 0 : intro + N*loop + X + F;

// 现在
_emitFrames = _infinite ? _totalFrames   // 整轨 = intro + loop
                        : intro + N*loop + X + F;
```

`_totalFrames` 正好等于 `intro + loopFrames`（`_loopFrames = total - intro`），
所以「loop=1 的时间轴」就是整轨长度。

**安全性**：`_emitFrames` 在无限模式下原本是 0，现在有了值，
所有用到它的地方都必须确认已被 `_infinite` 挡开，逐一核对过：

| 用处 | 是否受 _emitFrames 变化影响 |
|---|---|
| `Read` 判定播完 | `if (!_infinite && _emitFrame >= _emitFrames)` ✓ 已挡 |
| `GainAt` 判定淡出 | `if (_infinite \|\| _fadeFrames <= 0) return 1f;` ✓ 已挡 |
| `Decode16` 快路径 | `fadeStart = (_infinite …) ? long.MaxValue : …` ✓ 已挡 |
| `allowed` 计算 | `_infinite ? toEnd : …` ✓ 已挡 |

**所以无限循环依旧没有终点、没有淡出**，N / X / F 对它完全无效。

### 2. 新增 `ProgressPosition`

无限循环下 `_emitFrame` 是一直累加的（用来显示「总 xx:xx」），
不能直接当进度条位置。取**源内位置** `_srcFrame` 更合适 ——
它播到整轨末尾会自动折返到循环起点，于是进度条：

- 第一遍从 0 走到满（整轨长度）
- 之后每次从**循环起点**跳回去，而不是回到 0

正好对得上听感，顺带还能直观看出循环点在哪。

```csharp
public TimeSpan ProgressPosition => FramesToTime(_infinite ? _srcFrame : _emitFrame);
```

### 3. `TotalTime` 不再可空

`TimeSpan?` → `TimeSpan`。三种模式都有确定的量程了，UI 不再需要判空。

### 4. `SeekToFrame` 两种模式统一夹取

```csharp
long f = Math.Clamp(frame, 0, Math.Max(0, _emitFrames));
```

原来无限模式只挡了下界（`Math.Max(0, frame)`），现在两边一致。

### 5. UI 不再分辨模式

`UpdateProgress` 从「if (Infinite) … else …」两套逻辑合成一套，
量程和位置都直接问内核要。拖动进度条也统一走 `_engine.Seek(...)`。

时间文本保留了一个小差别：无限循环额外显示累计时长（`总 xx:xx`），
因为它会一直重复，光看量程内的位置不知道听了多久。

## 关于「看到淡出效果」

无限循环模式下**不应有淡出**，代码里 `GainAt` / `Decode16` 都用 `_infinite` 挡开了。
用户上一版看到的淡出应该是在**普通模式**下（默认 N=2、F=3，
时间线末尾确实有 3 秒淡出）—— 那正是设计里的行为。

设置里若把 `loopMode` 存成了 1（普通），下次启动就不是无限循环了，
这一点在切换过模式之后值得留意。

## 换模式时的位置锚点仍用 loop 段内相对位置

`RebuildCurrent` 依旧走 `SeekLoop(loopPos)`：
两种模式的时间轴长度不同，拿绝对时间去定位会被夹到末尾
（表现为「一切换就跳到快播完的地方」），
而「在循环段内的相对位置」两边通用。这个逻辑本次没动。
