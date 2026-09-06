# 构建日志 6：播放内核编译通过

日期：2026-09-04
阶段：第二步播放内核 · 第 3 次编译

```
还原完成(0.2)
ThbgmPlayer net10.0-windows 已成功 (0.5 秒) → bin\Debug\net10.0-windows\ThbgmPlayer.dll
在 1.0 秒内生成 已成功
```

**零错误、零警告** —— 上一轮的 3 条 CS0618 过时警告也随 `WasapiPlayer` 迁移一并消失。

## 至此落地的第二步产物

```
Audio/
  IAudioSource.cs         音频源抽象；tf 侧留 TfpkSource / OggSource 口子
  PcmFileSource.cs        裸 PCM 片段源（ZWAV：seek 到 start 直读；TH06：wav 内偏移）
  AudioSourceFactory.cs   按 GameDef.Source 选择实现（zwav / wav）
  LoopSampleProvider.cs   三种循环模式时间线 + N/X/F 参数
  CrossfadeMixer.cs       灵界版切换的交叉淡化混音器
  PlayerEngine.cs         多源调度 + WasapiPlayer 输出 + 淡入淡出
Data/TrackRef.cs          (作品代号, 曲序) 二元组，不存路径
```

构建链：

```
音频源(44100 或 22050)
  → [WdlResamplingSampleProvider 重采样到 44100，仅灵界版需要]
  → CrossfadeMixer（交叉淡化，灵界版切换用）
  → VolumeSampleProvider
  → SampleToWaveProvider16（float 转 16bit）
  → WasapiPlayer（共享模式，100ms 延迟）
```

## 待运行时验收（我这边跑不了 dotnet，也无法听音）

1. 双击曲目能出声，进度条 / 时间 / 暂停继续正常
2. TH13 有灵界版的曲子（如 02 死霊の夜桜）点「霊界版」—— 位置不跳、20ms 交叉淡化无缝
3. TH13 无灵界版的 5 首（欲深き霊魂、聖徳伝説、神社の新しい風、デザイアドリーム、
   プレイヤーズスコア）按钮灰显
4. 普通模式：loop 播满 N 次后自动切下一首，末首回到首首
5. 随机模式：当前列表内随机跳
6. 跨作品切曲（自定义列表还没做，先手动切不同作品）不出错、原有句柄正常释放

## 下一步（第三步）

播放参数窗口（N / X / F、淡出）、自定义列表与收藏、导出。
播放参数目前走 `settings.json` 的默认值（loopCount=2、fadeSeconds=3）。
