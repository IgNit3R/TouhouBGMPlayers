# 项目要点明细（从 MEMORY.md 腾出来的细节）

日期：2026-09-21
起因：`MEMORY.md` 只该放**索引与契约**（额度 ~3k 字符），细节一律搬到这里或各自的专题文档。
本文件收的是**原本只在 MEMORY.md 里、别处没有**的那些事实。

---

## 一、曲序与乐评名源

- 权威定义：`docs/design/DESIGN_v3.md` §4.2「按 `musicNo` 升序 + 无编号补号排末位（13 条）」。
  实测实现 **100% 符合**（21 作 `n` 连续 1..N、可见 333 首、副版不占 `n`）。
- 键的一致性：`musiccmt.json` 的键 ＝ `musicNo` ＝ `TrackDef.No` ＝ `TrackRef` 第二段，四者一致（设计使然）。
- ⚠️ **`docs/musiccmt/*.md` 的 `##` 段号不是曲序**：用的是 `tracklist.csv` 的 `index`（物理序），
  th07/th08 末尾各 6 首错位（共 12 首）。`README.md` 称「按播放器曲序」**不实**（用户资产，未改）。
  → **跨系统对齐一律用 `gameId+musicNo`**；不要从 md 段号提取，也不要用曲名当键（`Who done it?` vs `!` 之类差异）。
- ⚠️ 文档漂移（未修）：DESIGN_v3 §4.1/§4.2 称索引保留 `musicNo` 原值，实测 `tracks.json.gz`
  **无该字段**（字段仅 `alt,b,c,f,i,l,n,r,s,t`）→ 无法区分真号与补号。

## 二、黄昏作（tasofro）数据

- **TFCS 容器**（th135/145/155 的 bgm.csv）：`TFCS\0` + u32 complen@5 + u32 origlen@9 + zlib@13；
  解压后 = nrows(+2) / ncols / 列名 / 类型名（首个恒 `"type"`）/ 行（u32 字段数 + sstr，**整数也是 ASCII**）。
  cp932；**th135c 简中版是 GBK**。解析器 `.workbuddy/_tmp_musiccmt/parse_bgmcsv.py`。
- 乐评现状：th135 = 21 曲编曲自评（うに19/ZUN2）；**th075 = 34 曲全有**（NKZ15/U2 16/ZUN3，
  源 `pushfiles/th075/th075.dat/musicroom.dat/tracklist.csv`）；th105 = 音乐室无评论，仅 Omake（UTF-16LE）
  「曲のコメント」3 首 ZUN 新曲；th145 全空；th155 仅 credits；th123 经查证确无；th175 仅曲名。
  **th105/th135 的原汉化中文评论已弃（待换新资料）**。
- ⚠️ 找解包产物**先查 `pushfiles/` 再查 `release/`**（release 里 musicroom.dat 本体高熵未解）。
- bgm.csv 的 `track_no` ≠ 音乐室序；曲序以 `docs/th1?5_tracklist_final.csv` 的 order 列为准 join ogg 文件名。
  th135/145 那个文件实为 xlsx(inlineStr)，th155 是真 csv。

## 三、官方专辑（技术论证完成，未动手）

- 资源形态：wav/flac + cue 为主，tta 有，**ape 弃**，mp3 可能有。
- 解码路径：wav = NAudio 原生；mp3 = `Mp3FileReader`（ACM/DMO，零新 DLL）；flac = MF 或 CSCore(Ms-PL)；
  **tta 自研移植**。项目 LICENSE = GPL-3.0 → ffmpeg `tta.c`(LGPL-2.1+)、libttaR(GPL-3) 均可合法移植。
- **TTA 要点**：头 22 字节；`frame_length = 256*sr/245`（整数除法，错一位整轨错位）；
  seek table 每帧 u32 前缀和；CRC-32 IEEE LE、**帧 CRC 失败必须静音该帧**；位流 **LSB-first**；只接 16bit。
  验收 = 与 `ffmpeg -i x.tta -f s16le -` 逐样本比对。
- **两条已定策略**：
  ① **禁用导出、只做播放** → 三层拦截（`WavExporter.Export` 守卫 / `ExportTargets()` 过滤 / UI 灰显）；
  注意黄昏作 ED/StaffRoll（`IsTfOneShot`）**仍可导出**。
  ② **禁用无限循环**（不随全局循环模式）→ 新增「绝不重复」态：`LoopSampleProvider` 加 `neverLoop` 参数，
  `PlayerEngine.cs:181,222` 按 `GameDef.IsAlbumSource` 传参；`MainWindow.xaml.cs:620` 自动切曲改看内核
  `!_engine.Infinite`。判定入 `GameDef`（`Source=="album"` + `IsAlbumSource`），勿硬编码前缀。
- 连带放宽：mp3 encoder delay / cue pregap 只需**可听正确**；FLAC 走 MF 不再要求字节级确定性（仍要量化回 PCM16）。
  **不能省**：解码器必须能 seek 到任意样本、16bit、数据/UI 层改动。

## 四、音频解码源矩阵（排查「放不出声」的第一张表）

- 依赖：NAudio 3.0.1 ＋ **VorbisPizza 1.4.2**（命名空间仍是 `NVorbis`）＋ **Concentus 2.2.2**
  （BSD-3-Clause，可入 GPL-3.0）。
- 现成解码源（都在 `src/Audio/`，`loop*=null` 即不循环，**可直接拿来当调试源**）：

  | 容器/编码 | 类 | 凭据 |
  |---|---|---|
  | 标准 Ogg **Vorbis** | `Tf/OggMemorySource` | VorbisPizza（须显式 `Initialize()`；`ReadSamples` 返回**帧数**） |
  | 新典自定义容器 **Opus** | `Nc/OpusMemorySource` | Concentus；`Decode` 必须**恰好**喂 480B/包 |
  | RIFF wav | `Tf/WavEntrySource` / `PcmFileSource` | NAudio 原生 |

- ⚠️ **缺：标准 Ogg Opus**。Concentus 2.x **已移除** `Concentus.Oggfile`/`OpusOggReadStream`，
  nuspec 明写不含 Ogg 容器解析 → 要支持必须自写 Ogg 页解析（lacing / 跨页续包 / granule；只读可跳 CRC）≈180 行。
- ⚠️ `AudioSourceFactory.Create` 需要 `GameDef`+`TrackDef`（走索引）→ **不能**开任意路径。
- 排查手法：解码器报「找不到 XX 数据」时，**先验真实编码再怀疑代码** ——
  `.workbuddy/tools/ogg_probe.py <文件…>` 打印 Ogg 页结构与 `\x01vorbis`/`OpusHead` 签名 + 采样率/声道。
  实测 `tf/th175/…/op.ogg` 是真 **Vorbis**，而 `.workbuddy/viz-demo/th06nc_16.ogg` 实为 **Ogg Opus**
  （demo 期改名，故 VorbisPizza 报错属正确行为）。
- 新典原生 `.opus` = **自定义容器**（40B 头 + N×488B 记录；记录 [0:4] = BE 包长 480、[8:488] = 480B 裸包；
  960 帧/包 @48k；无 OpusHead → preskip=0）。重包脚本 `.workbuddy/viz-demo/nc_to_ogg.py`。

## 五、多语言（ja / zh-Hans；调研完成，**用户明确暂不动手**）

- 文档：`docs/2026-09-19-research-localization.md`（已盘点 248 条文案）。
- 已定：内嵌 JSON + `lang\` 覆盖；设置页手动选 + **立即热切换**；结构层二分法（Core/Audio 先降为「状态码+参数」）。
- **双入口**：**界面语言**（外壳 → `lang/*.json`）与**内容语言**（曲名/作品名/乐评 → 数据层，键 `gameId+musicNo`）。
  **内容绝不进 `lang/`**。`contentLanguage` 三态 `auto|ja|zh-Hans`，默认 auto；配置放新开 `AppSettings.Content`
  （勿塞 `UiSettings`）。
- 骑墙归置：副版标签→内容语言；`LoopText`「不循环」→界面语言；`Describe()` 技术串要拆；
  导出文件名／用户自起列表名／已落盘默认名→**两边都不切**。
- 漏项/待拍板：作品名零中文名源（`musiccmt.json` 作品级只有 `title_ja`）；其余 7 项见文档 §7.2。
  **乐评区是新功能，不是本地化项**。

## 六、接入期触点（写窗口相关代码时的既有先例）

- `Theme.Get(key, fallbackHex)` 是**代码取主题的唯一入口**。
- `MediaKeysHotkey.cs` 是 `HwndSource.AddHook` P/Invoke 的**唯一先例**（`WM_SIZING` 照抄）。
- 全项目仅 `MainWindow.xaml.cs` 用过 `VisualTreeHelper`。
- ⚠️ `App.xaml` **未设** `ShutdownMode`（默认 `OnLastWindowClose`，**保持默认**）——
  改 `OnMainWindowClose` 会让 `--viz` 永不退出；接入期给附件窗口设 `Owner` 即可（自然随属主关闭）。
