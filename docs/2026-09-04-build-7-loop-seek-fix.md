# 构建日志 7：修「逻辑位置与文件读指针脱节」

日期：2026-09-04
阶段：第二步播放内核 · 运行时缺陷修复

## 用户报告的三个症状

1. 切灵界版 → 从头播放（应该是 1:1 对齐、位置不跳）
2. 切循环模式 → 同样从头播放
3. 进度条拖不动
4. 播放有卡滞感

**三个症状同一个根因。**

## 根因

`LoopSampleProvider` 维护 `_srcFrame`（源内帧号）作为**逻辑**位置，
`PcmFileSource` 内部另有 `_position` + `FileStream.Position` 作为**物理**读指针。
前者改了从不通知后者，于是：

| 场景 | 现象 |
|---|---|
| `SeekToTime` / `SeekToLoopPosition` | 只改 `_emitFrame`/`_srcFrame`，文件读指针不动 → 数字跳了、声音没动（症状 3） |
| 灵界版切换 `lp.SeekToTime(cur)` | 新建的 source 读指针在音轨开头 → 从头播（症状 1） |
| 切循环模式 `PlayTrack + Seek` | 同上（症状 2） |
| **loop 折返** `_srcFrame = _loopStartFrame` | 文件读指针停在音轨末尾，`Read` 持续返回 0 → zeroReads 累积 → `_finished` → 补静音（症状 4） |

也就是说，**loop 从来没有真正循环过**——每次播完一遍整轨就卡住。

## 修复

### 1. LoopSampleProvider：补上「逻辑位置 → 文件读指针」的同步

新增 `_srcPosBytes` 缓存底层已定位的字节位置，只在真的不一致时才 seek
（seek 是系统调用，loop 折返约 90 秒一次，开销可忽略）：

```csharp
private void EnsureSourcePosition()
{
    long want = _srcFrame * _blockAlign;
    if (_srcPosBytes == want) return;
    _source.Seek(want);
    _srcPosBytes = want;
}
```

调用点共 5 处，全部覆盖：

- `SeekToFrame`（一切定位的公共出口）
- Read 里 3 个折返分支（`toEnd<=0`、`allowed<=0`、读不到数据）
- 读完一段后 `_srcFrame >= _totalFrames` 的折返

读完同步推进：`_srcPosBytes += gotFrames * _blockAlign`。

### 2. PcmFileSource：`RandomAccess` → `SequentialScan`

`FileOptions.RandomAccess` 会提示系统**关闭预读**（FILE_FLAG_RANDOM_ACCESS）。
而我们 99.9% 是顺序读，只在 loop 折返和用户拖动时跳一次 —— 这个提示完全是反的，
每次回调都直接打到磁盘，是卡滞的第二个来源。

改成 `FileOptions.SequentialScan` + 256 KB 缓冲（≈1.5 秒音频），
系统激进预读，一次系统调用能顶很久。顺带给 `Seek` 加了同位置短路。

### 3. Decode16 快路径

淡出段以外（99% 的时间）不做 `GainAt` 计算，每帧省一次除法：

```csharp
long fadeStart = (_infinite || _fadeFrames <= 0) ? long.MaxValue : _emitFrames - _fadeFrames;
if (emitStart + frames <= fadeStart) { /* 无增益转换 */ return; }
```

### 4. 播放线程挂 MMCSS

`.WithMmcssThreadPriority("Pro Audio")` —— 把渲染线程注册到多媒体类调度服务，
高系统负载下不易欠载爆音。

## 顺带修掉的两个相关缺陷

**a) 切循环模式会丢灵界版状态**
`PlayTrack` 硬编码 `useAlt: false`。改成同一首曲子重入时保留：

```csharp
bool keepAlt = _engine.UsingAlt && _engine.Current is TrackRef prev && prev == r;
_engine.Play(game, track, keepAlt);
```

**b) 切循环模式用总时间定位会被夹到末尾**
无限循环模式下 `_emitFrame` 一直增长，直接拿它去定位有限时间线会被 `Clamp` 到
`_emitFrames`（表现为一切换就跳到快播完的地方）。
改用 **`LoopPosition`（loop 段内位置）作为两种模式的共同锚点**——
两种时间线的 intro + loop 结构相同，这个锚点通用。

## 教训

**「逻辑位置」和「底层读指针」是两套状态，改了前者必须同步后者。**
这种 bug 编译器抓不到、静态检查也抓不到，只有真跑起来才暴露。

本轮未做运行时验证（我这边跑不了 dotnet，也听不了声音），等用户验收。
