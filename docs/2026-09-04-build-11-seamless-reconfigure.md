# 构建日志 11：切模式的跳转与残留卡滞

日期：2026-09-04
阶段：第三步 · 用户反馈修复

## 用户反馈

> 还是会有偶发的一点点卡滞感，切模式的时候也有偶发的跳转或者卡滞的感觉

## 「偶发跳转」的根因

上一版 `RebuildCurrent` 的做法：

```csharp
var loopPos = _engine.LoopPosition;   // ① 读位置（UI 线程）
PlayTrack(cur);                       // ② 建新链、交给混音器
_engine?.SeekLoop(loopPos);           // ③ 再把新链定位过去
```

**②③ 之间隔着一次线程切换。** 链一旦交给混音器，音频线程随时可能读它 ——
如果它在 ③ 之前被读到，就会**从位置 0 播出一个缓冲区**（≈100ms），
然后才被 seek 回去。这就是「偶发的跳转」。

## 「卡滞」的根因：100ms 延迟带来的位置错开

WASAPI 延迟 100ms，意味着：

- 我们在 UI 线程读到的播放位置，是**音频线程 100ms 前**的位置
- 从「读位置」到「新链被听到」，旧链又往前播了最多一个缓冲区

于是新链和旧链**错开最多 100ms**。切循环模式时两条链是同一首曲子，
交叉淡化两个错开的相同信号 → **梳状滤波（flanging）**，听感正是「卡滞 / 混乱」。

灵界版切换有同样的问题，只是内容相同、错开感被旋律掩盖了。

## 修法：不重建音源，就地改参数

时间线参数（循环模式 / N / X / F）原本是在 `LoopSampleProvider` 构造时定死的，
所以要改就得重建整条链 —— 而重建必然带来位置错开。

现在给 `LoopSampleProvider` 加 `Configure()`，**就地重算**：

- 不动 `_srcFrame`（源内位置）→ 音频一点不中断
- 不动音源、不开新文件句柄
- 只重算 `_emitFrames`（时间轴长度）并重映射 `_emitFrame`

位置保留「在循环段内的相对位置」，丢掉「已经循环了几遍」——
同一首曲子换模式，接着当前这一遍往下播才自然。

```csharp
long offset = _srcFrame - _loopStartFrame;
_emitFrame = offset < 0
    ? Math.Max(0, _srcFrame)
    : Math.Min(_introFrames + (offset % _loopFrames), Math.Max(0, _emitFrames));
```

`_infinite` / `_extraFrames` / `_fadeFrames` / `_emitFrames` 四项去掉 `readonly`。

## 线程安全：写入推迟到音频回调

`Configure` 从 UI 线程调用，直接改这些字段会和正在读它们的音频线程撞上 ——
哪怕单个 `long` / `bool` 的读写是原子的，也可能读到
「新 `_emitFrames` 配旧 `_infinite`」这种混搭组合。

所以 `Configure` 只投递（ `Volatile.Write` 到 `_pendingConfig` ），
真正的写入放在 `Read` 开头，与读取**串行**执行，天然无竞争。

构造时还没交给音频线程，所以 `Configure(pb); ApplyPendingConfig();` 直接连着调。

## 灵界版切换：换上去那一刻才对齐

`CrossfadeMixer.SwitchTo` 增加 `onStart` 回调，在**真正换上去的那一刻**
（音频线程上，旧链刚停止输出时）才执行定位：

```csharp
var old = _loop;   // 捕获旧链本身，不能捕获字段（下面几行就被换成新链了）
_mixer.SwitchTo(BuildChain(lp), AltCrossfade, () => lp.SeekToTime(old.CurrentTime));
```

此时读 `old.CurrentTime` 得到的正是旧链的落点，新链定位到那里 → **零错开**。

## 顺带的保险

`PcmFileSource.Read` 捕获 `ObjectDisposedException` 并返回 0。
切曲后旧音源是延迟释放（500ms 宽限）的，音频线程理论上不该再读到它，
但真撞上了，让上层当作「播完」总比让异常冒进音频回调炸掉播放好。

## 关于残留的「偶发卡滞」

这一版消除了两类已定位的原因（跳转、切模式时的位置错开）。
**如果还能听到偶发卡滞，最可疑的是 loop 折返时的文件 seek** ——
那次 seek 加随后的读盘发生在音频回调内部，
`FILE_FLAG_SEQUENTIAL_SCAN` 会激进释放已读页面，折返时可能要真读一次盘。

判断方法：**听它是否有规律**。如果大约每隔一两分钟（一个循环段的长度）
出现一次，就是折返读盘；如果完全随机，就是别的原因（系统负载、GC 等）。

真要治也有办法：把当前曲目整轨读进内存后再播（约 16–50 MB / 首，
只在切曲时分配一次），折返就完全不碰磁盘了。代价是内存和切曲时的一次性读取。

## 自检

- 三份 XAML 通过 XML 解析
- 所有 `.cs` 括号配平
- `RebuildCurrent` 引用归零（已改名 `ApplyTimelineToCurrent`）
- 四项字段确认已从 `readonly` 改为可写
