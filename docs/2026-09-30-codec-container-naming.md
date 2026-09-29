# 音频编码 / 容器命名定义

> 日期：2026-09-30 ｜ 性质：**现状档 + 命名规范 + 实测数据**（不是 build 日志，不含改动）
> 用途：为「当前曲目信息」面板（`src/MainWindow.xaml:370-384`，根 Grid 第 6 行）将来增加「格式 / 码率」做准备。
> 来源：2026-09-30 会话对 `src/Audio/`、`src/Data/`、`tools/` 的核对，全部结论带 `文件:行` 证据。
> §7 的码率数字是**实测**（三个一次性探针，见 §7.1 / §7.4），不是推算。

---

## 0. TL;DR

| 问题 | 答案 |
|---|---|
| 音频编码有几种 | **3 种**：PCM、Vorbis、Opus |
| 落到"可显示的格式名"几种 | **4 个**（PCM 因"有无 WAV 盒子"拆成两条） |
| 建议显示名 | `PCM Raw` · `PCM RIFF/WAVE` · `Vorbis Ogg` · `Opus (custom container)` |
| 现在索引里有这个字段吗 | **没有**。运行时靠魔数现判 ⇒ 要显示必须新增 |
| 能用 `GameDef.Source` 直接映射吗 | **不建议**。两处口径会漂移，见 §5.3 |
| 三种编码的码率都拿得到吗 | **都拿得到**。PCM = 恒定 1411.2 / 705.6 kbps；Vorbis = 头里就有（196/196 已填）；Opus = 恒定 192 kbps。见 **§7** |

---

## 1. 三层概念（必须先分清，否则命名一定混）

这三个东西**不在一个层次**，项目里的历史命名把它们混着叫，是混乱的根源。

| 层次 | 是什么 | 项目里的载体 | 值 |
|---|---|---|---|
| **① 游戏归档容器**（外层） | 游戏把全部资源打成的包 | `GameDef.Source` + `GameDef.Containers` | ZWAV `thbgm.dat` / Suica / XOR `.dat` / TFPK `.pak` / `.cga-.cgb` |
| **② 音频容器**（内层） | 单条音频自己的封装 | 由**魔数**现判 | 裸流 / RIFF-WAVE / Ogg / 自定义 Opus 容器 |
| **③ 音频编码 Codec** | 样本怎么编码 | 与解码器一一对应 | PCM16 / Vorbis / Opus |

⚠️ **`GameDef.Source` 不是格式，是"路由开关"**。它的值域混了三种命名口径（`src/Data/TrackIndex.cs:109`，注释原文 `zwav | wav | tfsuica | tfogg | ncopus`）：

- `zwav` / `wav` —— 带 wav 字样（实际一个是私有格式、一个是容器）
- `tfsuica` —— 带**容器**名（Suica）
- `tfogg` / `ncopus` —— 带**编码**名（ogg / opus）

**别拿 `Source` 当编码用。**

---

## 2. 完整矩阵（全部 29 个来源）

| 作品 | `Source` | ① 外层容器 | ② 音频容器 | ③ 编码 | 解码类 |
|---|---|---|---|---|---|
| TH07–TH20（20 作） | `zwav` | `thbgm.dat`（ZWAV 头，见 §3.4） | 无（裸流） | PCM16 | `PcmFileSource` |
| TH06 | `wav` | 散装 `bgm\*.wav` | RIFF/WAVE | PCM16 | `PcmFileSource` |
| TH07.5 th075 | `tfsuica` | Suica `th075bgm.dat` | RIFF/WAVE | PCM16 | `WavEntrySource` |
| TH10.5 th105 | `tfogg` | XOR `th105b.dat` | Ogg | Vorbis | `OggMemorySource` |
| TH12.3 th123 | `tfogg` | XOR `th123b.dat` | Ogg | Vorbis | `OggMemorySource` |
| TH13.5 th135 | `tfogg` | TFPK `th135.pak`(+`b`) | Ogg | Vorbis | `OggMemorySource` |
| TH14.5 th145 | `tfogg` | TFPK `th145.pak` | Ogg | Vorbis | `OggMemorySource` |
| TH15.5 th155 | `tfogg` | TFPK `th155.pak`(+`b`) | Ogg | Vorbis | `OggMemorySource` |
| TH17.5 th175 | `tfogg` | `data.cga` + `data.cgb` | Ogg | Vorbis | `OggMemorySource` |
| TH06NC 新典 | `ncopus` | 无（散装 `.opus`） | 自定义（40B 头 + N×488B） | Opus | `OpusMemorySource` |

来源与容器清单：`tools/csv_to_tfjson.py:32-47`（黄昏作 7 部）、`src/Data/TrackIndex.cs:109`（Source 值域）。

**注意两个"同格式不同 Source"**：

- TH06 与 th075 —— Source 不同（`wav` / `tfsuica`），**格式完全同类**（PCM in WAVE）
- th075 与 th105+ —— Source 命名只差后缀（`tfsuica` / `tfogg`），**格式是两代不同的东西**（PCM vs Vorbis）

这就是 §5.3 说"别按 Source 硬映射"的直接原因。

---

## 3. 命名定义（采纳项）

### 3.1 四个显示名

| 短码（存储用） | 显示名 | 编码 | 容器 | 覆盖 |
|---|---|---|---|---|
| `pcm_raw` | `PCM Raw` | PCM16 | 无 | TH07–TH20 |
| `pcm_wav` | `PCM RIFF/WAVE` | PCM16 | RIFF/WAVE | TH06、th075 |
| `vorbis_ogg` | `Vorbis Ogg` | Vorbis | Ogg | th105/123/135/145/155/175 |
| `opus_custom` | `Opus (custom container)` | Opus | 自定义 | TH06NC |

### 3.2 被否掉的写法及理由

| 曾用写法 | 判定 | 理由 |
|---|---|---|
| `PCM` | ✅ 保留（作为 `PCM Raw`） | 只有编码名，无法区分有无 WAV 盒子 |
| `Opus Custom` | ❌ **硬伤** | **撞 libopus 的官方专有名词** `OPUS_CUSTOM` / `opus_custom_mode_create()` —— 那是"改采样率/帧长、标准解码器解不了"的特殊模式。而且**修饰错了对象**：本项目里的 Opus 是**标准包**（用标准 Concentus 解码，`src/Audio/Nc/OpusMemorySource.cs`），"自定义"的是**外面的容器**，不是编码 |
| `ncOpus` | ❌ | `nc` 是**作品名前缀**（New Classic 新典，`tools/csv_to_ncjson.py:4`），不是格式名；换作品这套命名就崩 |
| `OggVorbis` | ❌ | 容器+编码**连写**，且顺序与同组其他项（`PCM Raw`）相反 |
| `PCM Riff/Wave` | ⚠️ 改大小写 | `RIFF` / `WAVE` 是 **FourCC，规范全大写**（代码里就是 `"RIFF"u8` / `"WAVE"u8`，`src/Audio/Tf/WavEntrySource.cs:87-88`）；`Riff/Wave` 与代码不一致，将来 grep 对不上 |
| `Riff/Wave` 里的 `/` | ⚠️ 语义 | `/` 会被读成"或"，但两者是**父子关系**：WAVE 是 RIFF 的一种 form，文件里写作 `RIFF....WAVE` |
| `wav` / `zwav` 当格式名 | ❌ | `wav` 是容器名（里面装的还是 PCM）；`zwav` 是项目私有来源标识 |

### 3.3 一致性规则（写代码时照这条）

1. **顺序**：`编码 [容器]`，编码**永远在前**（四项已统一）。
2. **容器性质可以不对称**：`Raw`=没有、`RIFF/WAVE` 与 `Ogg`=标准名、`custom container`=描述。这是**合理的**，不强行四词词形对称。
3. **大小写**：容器**名**写规范大小写（`RIFF/WAVE`、`Ogg`）；文件**魔数**全大写（`RIFF` / `WAVE` / `OggS`）。
4. **存储用短码**：`pcm_raw` / `pcm_wav` / `vorbis_ogg` / `opus_custom` —— **无空格、无斜杠**，便于判等、序列化、本地化。显示名只在 UI 层拼。
5. **不做封闭枚举**：留 `unknown` 兜底，理由见 §6。

### 3.4 附：ZWAV 头的实际布局（顺带纠正一处旧表述）

`thbgm.dat` 的前 16 字节不是"无名头"，是带魔数的签名头（`tools/verify_report.py:29-41`）：

```
[0:4]   "ZWAV"              魔数
[4:8]   u32 version == 1
[8]     zwavid 低字节（作品专属）
[9]     作品编号（0x07=th07, 0x13=th13, 0x20=th20）
[10:16] 全零
```

首轨数据从 `0x10`（=16）开始，正好跳过这个头。**参数不在 `thbgm.dat` 里，在同目录的 `thbgm.fmt`**：每首 52 字节 =

```
name[16] start[4] unk[4] intro[4] total[4] WAVEFORMATEX[18] pad[2]
```

其中那 18 字节 WAVEFORMATEX 与 WAV 的 `fmt ` 块**同构**（`tools/gen_tracklist_v2.py:105,115` 解出 `tag, ch, rate, avg, align, bits, cbs`）。

> ⚠️ 但**同构 ≠ 派生**：`ZWAV` 有版本号、作品 ID、还有 Wiki 列为 `???` 的未定义字段（`docs/design/SOURCES.md:36`），是**独立设计的私有格式**，不是"从 WAV 拆出来的"。准确说法是两者都遵循「裸 PCM + 格式描述表」这个思路。

---

## 4. 当前实现现状（为什么"现在拿不到"）

### 4.1 索引里没有 codec 字段

`DESIGN_v3.md:115` 草案里写过 `"codec": "pcm"  // pcm | wav | ogg`，但**从未落地**：

| 资源 | 实测字段 | 证据 |
|---|---|---|
| `tracks.json.gz` | `alt, b, c, f, i, l, n, r, s, t` | `docs/2026-09-19-research-localization.md:492` |
| `tracks.tf.json.gz` | `n, t, f, lss, les, ds, comp, r, c, b, theme` | 同文档 `:540` |
| `tracks.nc.json.gz` | 同 tf 结构 | `tools/csv_to_ncjson.py:12` |

而且 tf 索引的 `r/c/b` 是**硬编码** `44100 / 2 / 16`（`tools/csv_to_tfjson.py:85-87`）—— 连采样率都不是读出来的。

### 4.2 运行时判定点已经存在

`src/Audio/AudioSourceFactory.cs:52-65`（`CreateTf`）已经做了一次**魔数路由**：

```csharp
if (bytes[0..4] == "OggS")  return new OggMemorySource(...);   // → Vorbis
if (bytes[0..4] == "RIFF")  return new WavEntrySource(...);    // → PCM
if (bytes[0..4] == "TFWA")  throw new NotSupportedException(...);  // 明确拒绝
```

**关键**：`tfsuica` 和 `tfogg` **共用这一条分支**（`AudioSourceFactory.cs:22-23`），所以魔数判定一次就同时覆盖了 th075（RIFF）和 th105+（OggS）—— **不需要为 th075 单写规则**。

其余三条路在 `Create` 里是**结构上就知道**的：

- `game.IsWavSource`（TH06）→ WAVE
- `ResolveDatPath`（zwav）→ 裸流
- `IsNcSource`（TH06NC）→ 自定义 Opus 容器

### 4.3 UI 现状（2026-09-30 已落地容器/码率显示，见 §5）

「当前曲目信息」面板 = `src/MainWindow.xaml:370-385`（根 Grid **第 6 行**，`Height="Auto"`），文本由 `NowPlayingDetail` 承载。**旧版** `Describe()`（只有 r/c/b + intro/loop/总长）已被 `SetDetail` 状态机取代，现行格式：

```
<容器名> / <bps> / <采样率>Hz / <深度>bit / <通道> / <单轨时长>
Vorbis Ogg / 12-251 kbps VBR / 44100Hz / 16bit / 2ch stereo / 02:14
```

（拼装函数 `UI/TrackInfoText.Format`；四类容器真实示例见 §5.3。）

---

## 5. 落地实现（2026-09-30 已动工，方案与实况）

> 用户拍板的口径（2026-09-30）：Ogg 码率显示 **min~max 真实极值**；**选中即显示**（后台只读探针，
> 不解码）；容器名**从 `GameDef.Source` 纯函数映射**；时长只留总长；bps 取整；`NowPlayingDetail`
> 加 `TextTrimming` 兜底。方案文件：`C:\Users\Mika\.workbuddy\plans\electric-vortex-lovelace-PzoxwpJK.md`。
>
> ⚠️ 早期版本的 §5 写的是"给 `IAudioSource` 加属性 + 四个源各填一次"，**已废弃** —— 那条路要动
> 接口与四个音源，与用户"其他都不用动"的要求相悖；且 `PcmFileSource` 被 zwav/wav 共用
> （§4.2），短码没法写死在源里。现行方案**一个音源都不碰**。

### 5.1 总体结构：音源层零改动，显示自成一条探针链

| 新文件 | 职责 |
|---|---|
| `src/Audio/BitrateStats.cs` | 纯数据类：min/p05/p25/p50/p75/p95/max/avg/pages/IsVbr。**一次算全量** —— UI 现在只用 min~max，将来改 p05~p95 稳定带是"换个字段读"，不是"重新探一遍" |
| `src/Audio/VorbisPages.cs` | **纯算术**：逐 Ogg 页瞬时 bps（页字节×8÷(页样本÷采样率)），无 IO 无解码 ⇒ 可离屏自检。移植自 `.workbuddy/tools/vorbis_page_bps_probe.py`，含线性插值分位 |
| `src/Audio/BitrateProbe.cs` | IO 入口：`Instant()`（PCM=Rate×ch×bit、Opus=192k 常数，零 IO）+ `Async()`（tfogg 独立开 `TfContainerSet` 只读条目、**不解码**）。结构照 `TrackScanner`：后台、失败静默、绝不碰 `PreloadCache` |
| `src/UI/TrackInfoCache.cs` | 纯内存 LRU（镜像 `WaveformCache`）：键复用 `PreloadCache.KeyOf`，容量 64，只缓存 Vorbis 探针结果，路径一改整表清 |
| `src/UI/TrackInfoText.cs` | **纯函数**：容器名映射（§3.1）+ 拼串 + kbps 取整（AwayFromZero）+ mono/stereo 判定 |
| `src/UI/BitrateSelfTest.cs` | 自检（注册在 `VizSelfTest`）：合成 Ogg 手算断言、5 条串逐字比对、真文件抽查 |

`MainWindow` 侧：`Describe` → `SetDetail(game, track, useAlt, withPrefix)`（三处调用：选中未播 /
播放 / 切副版），代际号 `_detailGen` + 取消令牌 `_detailCts` 照波形那套；`ApplySettingsSideEffects`
与 reset 分支都会作废在途探针。**顺带修掉** `Alt_Click` 只重加前缀不重算内容的既有瑕疵（TH13
主 1411k@44100 / 霊界 706k@22050，切版后码率采样率都会跟着变）。

### 5.2 关键取舍

- **容器名从 `Source` 映射，不用魔数现判**：纯函数、零 IO、选中即得。代价是显示与实际解码
  不再同一事实来源 —— 但 §4.2 的魔数路由是播放侧权威，若未来 `tfogg` 混进非 Ogg 条目，
  表现是"拒绝播放"而不是"播错"，风险可接受（当时 196/196 全为 OggS，三方印证）。
- **独立探针而非"顺路发布"**：曾评估在 `OggMemorySource` 构造里算完发布到静态表（零额外容器开），
  但要把 game/track 传进音源构造签名 ⇒ 连带改 `AudioSourceFactory` 与音源，违背"不改四个音源"，
  且缓存生命周期与播放耦合。代价是 tfogg 每次未缓存命中都要重开一次 `TfContainerSet`
  （无缓存、重解目录表）—— 后台线程、毫秒级、结果进 LRU，同曲不反复探。
- **占位符 `… kbps`**：tfogg 首探未归时其余字段照常显示，探针回来整串重拼，不闪空。

### 5.3 四类容器的真实显示串（实测数字）

| 容器 | 显示串 |
|---|---|
| `PCM Raw`（th07 等 20 作） | `PCM Raw / 1411 kbps / 44100Hz / 16bit / 2ch stereo / 01:33` |
| `PCM RIFF/WAVE`（th06 / th075） | `PCM RIFF/WAVE / 1411 kbps / 44100Hz / 16bit / 2ch stereo / 02:10` |
| `Vorbis Ogg`（tf 6 作） | `Vorbis Ogg / 12-251 kbps VBR / 44100Hz / 16bit / 2ch stereo / 02:14` |
| `Opus (custom container)`（th06nc） | `Opus (custom container) / 192 kbps / 48000Hz / 16bit / 2ch stereo / 02:00` |
| TH13 霊界版（副版前缀照旧） | `霊界版 · PCM Raw / 706 kbps / 22050Hz / 16bit / 2ch stereo / 01:30` |

### 5.4 验证记录（2026-09-30）

- `dotnet build` 0 警告 0 错误；`--viz-selftest` **48 项全绿**（新增 7 项）。
- **C# 探针 vs Python 参考实现在真数据上 6/6 吻合**（每部 tfogg 抽第一首，min/max 精确到
  取整、页数逐一相同）：th105 `op.ogg` 100-271/698 页、th123 `op2.ogg` 151-292/595、
  th135 `reimu1.ogg` 77-248/668、th145 与 th155 `reimu1` 同为 136-264/832（md5 复用曲）、
  th175 `op.ogg` 144-263/501。对照表：`.workbuddy/vorbis_page_bps_196.csv`。

---

## 6. 风险 / 待决

| # | 项 | 说明 |
|---|---|---|
| 1 | **别做封闭枚举** | 至少要留 `unknown`：(a) `TFWA` 条目会被拒播（`AudioSourceFactory.cs:60-62`），诊断路径可能遇到；(b) 官方专辑线规划过 `wav / flac / tta / mp3`（`docs/2026-09-21-project-notes.md:33-48`），将来会多出 `Opus` 之外的编码 |
| 2 | **本地化归属待定** | `PCM` / `Ogg` / `Opus` 这类格式术语属**界面语言**（且通常不翻译）。拼串现已集中在 `UI/TrackInfoText`，多语言支线（`docs/2026-09-21-project-notes.md:79` 的拆串待办）落地时从这里拆，不再散落 |
| 3 | **`thbgm.fmt` 只解了部分字段** | 构建期 `tools/gen_tracklist_v2.py:115` 其实解出了 7 个字段，但只有 `ch/rate/bits` 进了索引，`nAvgBytesPerSec` / `nBlockAlign` / `wFormatTag` / `cbSize` **全被丢弃** —— 如果之后想显示比特率，PCM 这一半的数据在**生成期**就已经能拿到了 |
| 4 | **比特率已落地（2026-09-30，§5）** | PCM/Opus 走格式常数，Vorbis 走页级 min~max 探针（后台只读、不解码）；口径 = **压缩存储码率**（§7.6 拍板） |
| 5 | **文档索引已登记** | 2026-09-30 已登记进 `docs/README.md` §2 |

---

## 7. 码率（bps）实测

> 2026-09-30 追加。三个一次性探针（**只读**，不写任何游戏文件）：
> `.workbuddy/tools/vorbis_bps_probe.py`（Vorbis 6 作 196 首·整曲平均）、
> `.workbuddy/tools/vorbis_page_bps_probe.py`（同 196 首·逐页瞬时，§7.4）、
> `.workbuddy/tools/nc_opus_bps_probe.py`（新典 36 文件）。

### 7.1 结论总表

| 编码 | 头里读到的标称 | 实测**存储**码率 | 解码后 **PCM** 码率 |
|---|---|---|---|
| PCM 44100/2/16（333 首） | — | **1411.2 kbps**（与右列同一数） | 1411.2 kbps |
| PCM 22050/2/16（13 首，TH13 霊界版） | — | **705.6 kbps**（同一数） | 705.6 kbps |
| Vorbis `tfogg`（196 首） | th105 = `160003`；其余 = `192000` | 153.2 ~ 199.4 kbps，均值 **189.3 kbps** | 1411.2 kbps |
| Opus `ncopus`（36 文件） | **无此字段** | **恒定 192.0 kbps**（含容器开销 195.2，+1.67%） | 1536 kbps |

### 7.2 PCM：由格式定义直接确定，精确

算法：`Rate × Channels × Bits`（等价于 `TrackDef.BytesPerSecond × 8`，`src/Data/TrackIndex.cs:62,65`）。

统计 `docs/tracklist.csv` 全 **346** 行：

| rate/ch/bits | 曲数 | 字节率 | 比特率 |
|---|---|---|---|
| 44100 / 2 / 16 | 333 | 176,400 B/s | **1,411,200 bps = 1411.2 kbps** |
| 22050 / 2 / 16 | 13 | 88,200 B/s | **705,600 bps = 705.6 kbps** |
| | **346** | | |

口径：`,44100,2,16,` 命中 333、`,22050,2,16,` 命中 13，**333 + 13 = 346 = 全表行数** ⇒ 全库只有这两种组合，`ch` / `bits` 无变化。13 条 22050 与 `tools/verify_report.py:71` 的期望值一致（TH13 霊界版）。

**交叉验证**（用 CSV 自己的字节数 ÷ 秒数反推）：

| 轨 | total_bytes | total_sec | 反推字节率 |
|---|---|---|---|
| th07 #1 | 16,545,568 | 93.796 | 176,400 B/s ✓ |
| th13 #3（霊界） | 9,586,104 | 108.686 | 88,200 B/s ✓ |

两行都严丝合缝 ⇒ PCM 的 bps **不需要实测**，是格式决定的常数。

> 注：`thbgm.fmt` 里本来就有 `nAvgBytesPerSec`（§6 第 3 条），只是生成期被丢弃了。既然它是 `rate × ch × bits` 的必然结果，**不补那个字段也不损失信息**。

### 7.3 Vorbis：头里就有三个字段，而且本项目 196/196 全部已填

**Vorbis identification header 的完整布局**（`i` = `"\x01vorbis"` 之后的位置）：

| 偏移 | 字段 | 类型 | 说明 |
|---|---|---|---|
| i+0 | vorbis_version | u32 | |
| i+4 | audio_channels | u8 | |
| i+5 | audio_sample_rate | u32 | ← `.workbuddy/tools/ogg_probe.py:64-69` **读到这里就停了** |
| **i+9** | **bitrate_maximum** | i32 | |
| **i+13** | **bitrate_nominal** | i32 | ← **要的就是这个** |
| **i+17** | **bitrate_minimum** | i32 | |
| i+21 | blocksize_0 / 1（半字节各一个） | u8 | |
| i+22 | framing flag | u8 | |

⇒ **现成探针少读了 3 个字段，加 3 行就能拿到。**

**196 首实测结果**（逐曲明细已落盘 `.workbuddy/vorbis_bps_196.csv`，196 行）：

| 作 | 曲数 | `bitrate_nominal` | 实测范围 kbps | **跨度** | 均值 | 离标称 |
|---|---|---|---|---|---|---|
| th105 | 31 | **160003**（全部） | 153.2 ~ 180.3 | **27.1** | 166.7 | +4.2% |
| th123 | 18 | **192000**（全部） | 194.4 ~ 195.0 | **0.6** | 194.6 | +1.4% |
| th135 | 33 | **192000**（全部） | 189.8 ~ 196.3 | 6.5 | 192.9 | +0.5% |
| th145 | 30 | **192000**（全部） | 190.5 ~ 198.8 | 8.3 | 193.9 | +1.0% |
| th155 | 58 | **192000**（全部） | 189.9 ~ 199.4 | 9.5 | 193.7 | +0.9% |
| th175 | 26 | **192000**（全部） | 185.3 ~ 197.7 | 12.5 | 192.9 | +0.5% |
| **合计** | **196** | | 153.2 ~ 199.4 | | **189.3** | |

**「逐曲浮动」还是「作品统一」——两层答案**：

- **`nominal` 是作品统一的**（作者批量编码的目标值），同作内**一个数字都不差**。
- **实测存储码率是逐曲浮动的**，且**离散宽度因作而异**：th123 只有 **0.6 kbps** 跨度（18 首几乎完全一致，
  提示同批同质素材），th105 则达 **27.1 kbps**。
- ⚠️ 上表 189.3 那个均值是**全库并集**，**不是**某一作的统一范围；每作自己的区间都更窄。

**各作极值曲（实测最低 / 最高）**：

| 作 | 最低曲 | 最高曲 |
|---|---|---|
| th105 | `st11.ogg` **153.2** | `st18.ogg` 180.3 |
| th123 | `st35.ogg` 194.4 | `st99.ogg` 195.0 |
| th135 | `mamizou1.ogg` 189.8 | `title.ogg` 196.3 |
| th145 | `select.ogg` 190.5 | `st01_1.ogg` 198.8 |
| th155 | `story01.ogg` 189.9 | `story09.ogg` **199.4** |
| th175 | `talk_door.ogg` 185.3 | `talk_scarlet.ogg` 197.7 |

**全库极值**：最低 = th105 `st11.ogg` **153.2 kbps**；最高 = th155 `story09.ogg` **199.4 kbps**。

**字段可用性**：

- `bitrate_nominal` **196/196 = 100% 已设置**。⚠️ 本条**推翻了本文件早期版本的保守说法**（"VBR 编码器经常不填 nominal"）—— 至少在这 6 作上，每一首都填了。
- `bitrate_maximum` / `bitrate_minimum` **全部未设置**，但**两种"未设置"写法都出现了**：th105 用 `0,0`，th123 及之后用 `-1,-1`。⇒ 判断"有没有值"要**同时排除 0 和 -1**。
- 采样率取值只有 `[44100]`，声道只有 `[2]`。

**浮动的来源**（四条）：

1. Vorbis 本质是 **VBR** —— 同一质量档下，内容复杂度不同，实际码率就不同；
2. `nominal` 只是**目标值**不是实际值，单曲偏差实测达 **−4.2% ~ +12.7%**（th105 最宽，其余 5 作约 −3.5% ~ +3.9%）；
3. **Ogg 页头开销占比随曲子长短变**（每页约 `27 + nsegs` 字节），短曲占比更高；
4. **长静音 / 极安静段落会被极小包编码**，把整曲平均拉低。

**th105 为什么明显比其余 5 作散**（两条**实测**旁证，指向另一套工具链/参数）：

- 标称值形态不同：`160003`（非整千） vs 其余全部 `192000`；
- `(bitrate_maximum, bitrate_minimum)` 写法不同：th105 = `(0, 0)`，th123 及其后 = `(-1, -1)`。

⇒ **对 UI 的建议**：显示 **`nominal`（作品统一值）比显示实测值友好** —— 切曲子时数字不会乱跳。
要显示实测值就取整到千位，或只在详情里给。

**真实存储码率算法**：`文件字节数 ÷ (Ogg 末页 granule ÷ 采样率)`。
末页 granule = 流的有效样本总数，读法见 `tools/loop_delta.py:214 ogg_last_granule()`（现成实现，但它读的是独立文件；容器条目要走 `TfContainerSet.GetEntry` 的字节数组）。

### 7.4 Vorbis 单曲内部：码率**确实是范围**（页级实测）

⚠️ §7.3 表里的 `actual_bps` 是**整曲平均**（`文件字节数 × 8 ÷ 总时长`）—— 按定义只有一个标量。
Vorbis 是 VBR，**单曲内部**的码率是逐页浮动的。逐页实测（`.workbuddy/tools/vorbis_page_bps_probe.py`：
`页字节 ÷ 页样本`，分辨率 = **一个 Ogg 页** ≈ 0.1~0.5 秒，不是逐样本；
逐曲统计已落盘 `.workbuddy/vorbis_page_bps_196.csv`）：

**示例（kbps）**：

| 曲 | 整曲均值 | p05 | 中位 | p95 | 最高 | 极差倍数 |
|---|---|---|---|---|---|---|
| th105 `op.ogg` | 167.8 | 148.0 | 165.3 | 194.2 | 270.8 | 2.7× |
| th135 `miko1.ogg` | 191.7 | 167.3 | 182.3 | 224.0 | 253.6 | 1.7× |
| th135 `futo2.ogg` | 191.5 | 159.6 | 194.3 | 224.8 | 250.9 | **21.4×** |
| th145 `miko1.ogg` | 192.2 | 165.5 | 188.5 | 225.4 | **894.6** | 6.2× |
| th175 `talk_door.ogg` | 185.3 | 164.4 | 182.8 | 234.7 | 394.2 | 7.3× |

**196 首的统计形态**：

| 指标 | 范围 | 中位 |
|---|---|---|
| 单曲内极差倍数（max ÷ min） | 1.4 ~ **21.4×** | 1.8× |
| 峰值 ÷ 整曲均值 | 1.17 ~ 4.65× | 1.39× |
| 全库瞬时最低 | **11.7 kbps**（th135 `futo2.ogg`，近静音页） | |
| 全库瞬时最高 | **894.6 kbps**（th145 / th155 `miko1.ogg`） | |

🔑 **关键形态：中位带很窄，尾部很宽。**
`p05 ~ p95` 通常只有 `1.2 ~ 1.5×` 的宽度（绝大多数页都贴着均值走），但 `min ~ max` 能到 **21×**。
⇒ **"范围"几乎全部由极少数离群页造成**，不是整体宽幅波动。

**对 UI 的建议（若真要显示范围）**：用 **p05 ~ p95**（稳定带），
**不要用 min ~ max** —— 后者会被一两个静音页/爆音页彻底带偏（如 `futo2.ogg` 的 11.7 kbps）。

**附带发现**：th145 `miko1.ogg` 与 th155 `155miko1.ogg` 的 **md5 完全相同**
（`bb46d2e9ce9f4264be84a7912e6e9d29`）⇒ 两作之间存在**逐字节复用的音频**。
将来若要做去重 / 缓存可以留意。

### 7.5 Opus（新典）：头里没有，但容器定长 ⇒ 是个精确常数

自定义容器：`40B 头 + N × 488B 记录`，记录 = `[0:4] 大端 u32 包长` + `[4:8] 杂散` + `[8:488] 480B 裸 Opus 包`，每包恒 **960 帧 @ 48kHz = 20 ms**（`src/Audio/Nc/OpusMemorySource.cs:31-45`）。

```
载荷码率 = 480 B × 8 ÷ 0.02 s = 192,000 bps = 192.0 kbps
含开销   = 488 B × 8 ÷ 0.02 s = 195,200 bps = 195.2 kbps   (+1.668%)
```

**36 个文件实测（`D:\SteamLibrary\steamapps\common\th06nc\data\{bgm,bgm2}`）**：

| 检查项 | 结果 |
|---|---|
| 文件数 | 36（bgm 18 + bgm2 18） |
| `(len - 40) % 488` | 全部为 0 ✓ |
| 所有记录声明的包长 | **只有一个取值：480** ✓ |
| 异常条数合计 | **0** |
| 载荷码率 | **192.0 ~ 192.0 kbps**（均值 192.00，36 首完全一致） |
| 含开销码率 | 195.2 ~ 195.2 kbps（均值 195.20） |
| 首 8 字节 | 全部 `0100008018000000`（与 `docs/2026-09-20-build-39-viz-m1.md:172` 一致） |

⇒ **hard-CBR 192 kbps**。正因为包长恒定，容器才敢用定长记录。`.workbuddy/viz-demo/nc_to_ogg.py:51` 里那句 `assert declared == PSIZE` 也是同一个事实的旁证。

### 7.6 🔑 对 UI 的意义：三种码率都是**免费**可得的

不需要额外读文件 —— 各自的音源构造函数里，算码率要的两个量**都已经在手上**：

| 源 | 手上已有的量 | 码率算法 |
|---|---|---|
| `PcmFileSource` / `WavEntrySource` | `Format` | `Rate × BlockAlign × 8` |
| `OggMemorySource` | `oggBytes.Length` + 解码出的总帧数（`vorbis.SampleRate` 也有） | `Length × 8 ÷ (frames ÷ sampleRate)` |
| `OpusMemorySource` | `opusBytes.Length` + `recordCount × 960` | 同上（结果恒为 192k） |

**⚠️ 但必须先定"显示哪个口径"** —— 这是两个不同的数，对压缩格式差一倍以上：

| 口径 | 含义 | PCM | Vorbis | Opus |
|---|---|---|---|---|
| **解码后 PCM 码率** | 喂给 WASAPI 的数据量 | 1411.2k | 1411.2k | 1536k |
| **压缩存储码率** | 磁盘上占多少 | 1411.2k（同） | ~189.3k | 192.0k |

- 想表达「文件有多省」→ 用**存储码率**（压缩格式才有意义）
- 想表达「喂给声卡多少」→ 用 **PCM 码率**（对 Vorbis/Opus 是恒定值，信息量低）
- 对 PCM 两者相同，可以不区分。

建议 UI 只显示**存储码率**，并注意它对 PCM 恰好等于 PCM 码率（无压缩）。

---

## 8. 一句话总结

> **编码 3 种（PCM / Vorbis / Opus），容器 4 类（裸流 / RIFF-WAVE / Ogg / 自定义），显示名 4 个（`PCM Raw` / `PCM RIFF/WAVE` / `Vorbis Ogg` / `Opus (custom container)`）。**
> 索引里目前**一个都没记录**，运行时靠魔数现判；判定点已存在于 `AudioSourceFactory`，落地只需把结果往上带一层 —— 但注意 `PcmFileSource` 被 `zwav` 与 `wav` 两条路共用，短码不能写死在源里。
> **码率三种都能拿到**：PCM 恒定 1411.2 / 705.6 kbps、Vorbis 头里就有（实测 153~199 kbps、均值 189.3）、Opus 恒定 192.0 kbps；且各自的音源构造函数里**算码率的量都已备齐**，不需要额外 IO。
