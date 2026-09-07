# 黄昏作接入实现记录（2026-09-06 开工日）

> 本文件是当日实现的工作记录（当日 .workbuddy 日报已存在，按红线约定改记于产物目录）。

## 结果

**主工程构建 0 警告 0 错误；P4 端到端冒烟 36 PASS / 0 FAIL（7 作全绿）。**

## 改动清单

### 用户授权白名单内（已先镜像备份至 src_privious/，逐一 diff 校验）
1. `src/Data/TrackIndex.cs` — TrackDef 加秒字段（ls/le/d/comp/theme）；GameDef 加 IsTfSource/Containers；启动时主系列 346 条 + 黄昏作 230 条按编号序混排（GameOrder：th07→7，th075→7.5，th128→12.8）
2. `src/ThbgmPlayer.csproj` — NVorbis 0.10.5（MIT）；内嵌 tracks.tf.json.gz / TfFileslist.txt / TfFileslist.js；ProjectReference 解析器库
3. `src/Audio/AudioSourceFactory.cs` — tf 分支（IsTfSource → TfContainerSet → 魔数路由），zwav/wav 路径一字未动
4. `src/Core/PathValidator.cs` — ValidateTf 分支（主包缺失 Fail/补包缺失 Warn/加密容器注明仅校验存在性）

### 新增文件
- `src/Audio/Tf/TfContainerSet.cs` — 容器组封装：覆盖优先序实例化解析器、四档宽松查找（含 .sfl 旁挂排除修复）、fileslist 三级回退（游戏目录→程序目录→内嵌）
- `src/Audio/Tf/OggMemorySource.cs` / `WavEntrySource.cs` / `TfMemory.cs`（tf-audio 产出，NVorbis 解码 / RIFF 遍历 / 内存池共享循环语义）
- `TfParsers/TfParsers.Lib.csproj` — 解析器类库桥接（解析器源码零改动；绕开 WPF 工程 ImplicitUsings 差异）
- `tools/csv_to_tfjson.py` — 索引生成（输出 src/Resources/tracks.tf.json.gz，230 首）
- `tf_prep/selftest_audio/`、`tf_prep/selftest_wiring/`（含 probe/）、`tf_prep/p4_smoke/`、`tf_prep/p4_probe/` — 各级自测工程
- `tf_prep/export/th135|th145_tracklist_final.csv` — xlsx 容器定稿的纯文本导出（openpyxl）

## 过程要点（复用价值）
1. **沙箱 shell 缺环境变量**：dotnet 命令必须带 env 前缀（APPDATA/ProgramFiles/ProgramFiles(x86)/ProgramW6432/ProgramData/LOCALAPPDATA），否则 restore 报 path1 null。
2. **编辑通道的 return_csv 丢空单元格**：xlsx 容器数据导出要用 openpyxl（临时复制成 .xlsx 绕扩展名限制）。
3. **前 3 份曲表 loop 列是「循环/不循环」**，后 4 份是 Y/N——生成脚本两者都收。
4. **.sfl 旁挂文件与音频同容器并存**（XOR 与 TFPK 都是）：宽松查找若不排除旁挂扩展名，op.ogg 会被 op.sfl（RIFF 头）顶替 → 送错解码器。已修：同扩展名优先、旁挂永不命中、歧义不猜。
5. TFPK v1/cga 条目真名靠 fileslist 哈希反查——已内嵌社区名单（TfFileslist.txt 30431 行 / TfFileslist.js）。

## 验证矩阵
- 解析器：75/75（Suica 34、XOR 11、TFPK 30）
- 音频源自测：全 PASS（th155/175/075 真数据，循环点帧对齐 ±0）
- 接线自测：22/22（含索引合并冒烟：28 部游戏、编号序插入点、内嵌名单断言）
- **P4 端到端：36/36（7 作 × [首曲 + 不循环曲]，全部走真实内嵌索引 + 真实容器）**；tf-wiring 复核自测 40/40（含 7 作遍历 16 项 + TFWA 行为锁定 2 项）
- **TFWA 实证结论（tf-wiring 上报，采纳）**：「TFWA=8 字节头+OGG」的规格与实测不符——TFWA 实为 27B 头 + 裸 PCM/OGG 的 SE 音效（th145 全部 897 个 TFWA 载荷中 OggS 出现 0 次），且索引 7 作无任何 TFWA 轨。现行实现：TFWA → NotSupportedException（带诊断），与用户「SE 音效不收录」的拍板一致。日后若要播 SE，单开任务做 TfwPcmSource（先拿转换后 wav 锁定头布局）。
- 遗留：用户听感验收（循环点无感折返）待用户亲自过一遍。

## 待用户
- 运行播放器，设置页给 7 部黄昏作填路径（=游戏根目录，如 tf/th105），逐作听首曲+不循环曲。
- git 未提交，验收后由用户决定提交方式。

## 2026-09-07 用户验收 bug 修复（3 项）

1. **不循环曲被循环**：根因 = 内核把「intro=0、loop=整曲」当普通循环展开（N 次折返）。
   修复：确立 one-shot 语义 —— tf 源对不循环曲输出 IntroBytes==TotalBytes（无循环段信号），
   `LoopSampleProvider` 识别后不折返、不套 N/X/F，整曲播一遍即停（无限模式下同样一遍停）；**普通/随机模式下一 shot 曲同样不做淡出**（用户 09:44 指正，GainAt/Decode16 已加 _oneShot 守卫）。
   涉及：LoopSampleProvider.cs（6 处守卫，已按备份协议先备份）、TfMemory.CalcIntroTotal。
2. **th123 编号错**：TH12.5 → **TH12.3**（用户指正；官方编号 12.3，此前误写）。
   涉及：csv_to_tfjson.py、重新生成的索引、tf_games_meta.md。排序键本就是 12.3，顺序一直正确。
3. **黄昏作列表「时长/循环」两列全 0**：根因 = 两列从字节字段换算，tf 索引存秒、字节为 0。
   修复：TrackDef.IntroTime/LengthTime/LoopTime 加 tf 分支（按秒）；LoopText 对 tf 显示
   「循环 00:06 起 · 至 01:35」/「循环 xx 起 · 至曲末」/「不循环」，主系列格式不变。
   涉及：TrackIndex.cs、MainWindow.xaml.cs（均已按备份协议先备份）。

验证：P4 冒烟 36/36（不循环曲断言改为 Intro==Total）、selftest_wiring 40/40、主工程 0 警告 0 错误。

## 2026-09-07 第二批验收修复（4 项）

1. **循环列风格统一**：黄昏作循环曲改回与弹幕作相同的 `intro X / loop Y` 格式（此前自创「循环 xx 起 · 至 xx」）。
2. **th075/105/123 不循环曲时长为 0**：根因 = 定稿 CSV 里这三行的时长值误写进 loop_end_sec 列、duration_sec 缺失（少一个逗号）。
   已修正（83.58/84.46/100.066 回位）。
   ⚠️ 过程事故：th105 CSV 曾被中途失败的回填脚本截断（31 行剩 17），已从会话 transcript 恢复全文并核实（含双 BOM 清理）。
3. **th135/145 循环曲 loop_end 缺失**（显示「至曲末」）：用 SflReader 批量解析 52 个 sfl（th135 主包 19 + 补包 3 + th145 30），
   回填 49 行真实循环终点到 export CSV 与 xlsx 定稿原件。此后 th135/145 的循环段不再「至曲末」，播放循环点与游戏一致。
   —— 这也可能是第 4 条「循环衔接听感问题」的诱因之一（此前 th135/145 折返点=曲末而非 sfl 终点），听感验收时重点复核这两作。
4. **循环衔接听感**：待复核（可能在 #3 修复后自然改善）。

验证：索引重生成（d/le 全部就位）、主工程 0 警告 0 错误、P4 36/36、selftest_wiring 40/40。

## 2026-09-07 根因修复：OGG 解码器更换（NVorbis → VorbisPizza）

### 根因
黄昏作 OGG 的页结构很碎（每页 22~29 个包，且几乎每页以跨页包结尾）。NVorbis（0.10.5 与 1.0.0-rc.2 均如此）
解码时会**丢 1024 样本块**，造成解码流累积漂移：th175 op.ogg 实测
- 0~16s 对齐、24~48s 偏 1024 样本(-23ms)、56~80s 偏 2048 样本(-46ms)
- 总长 3919936 vs 容器末页 granule / libsndfile 的 3922834（少 2898）
循环点本身正确（与游戏自带 sfl/cue/ini 整数样本 208/209 误差 ≤0.68ms），但因解码流错位，
折返落在错误的音乐位置 → 听感上「最后的鼓重复 / 乐器声音不完整」。

### 修复
- `ThbgmPlayer.csproj`：NVorbis 0.10.5 → **VorbisPizza 1.4.2**（NVorbis 现代分支，命名空间仍为 NVorbis，纯托管，MIT）
- `src/Audio/Tf/OggMemorySource.cs`：加 `vorbis.Initialize()`；`ReadSamples(Span<float>)` 返回的是**帧数**
  （旧 NVorbis 返回浮点个数），按 `floats = frames * channels` 换算后再转 PCM16
参考项目 musicroom（`E://GitWorkspace//musicroom`）的播放实现已核对：`Streamer` 线程 + DirectSound 环形缓冲，
逐块 `ov_read_bgm()`（libvorbis，到 End 即 seek 回 Loop、丢弃超出样本，无限循环），循环次数/淡出只用于提取；
其播放模型与我们完全同构，差异只在解码器（libvorbis vs 我们的 NVorbis）。

### 验证
- 主工程 0 警告 0 错误
- P4 冒烟 36/36、接线自测 40/40
- **解码回归**（新增 `tf_prep/p4_regress/`）：抽样 7 作，解码长度 vs 容器末页 granule **0 首不符**（th175 op = 3922834 ✓），
  中段采样点漂移全 0（talk_reactor 的 59 为两样本判据误匹配，64 样本窗口复核偏移 0 匹配 100%）
- 不循环曲尾部丢失随之消失（th175 staff_roll 24775424 → 24801176）

## 2026-09-07 续：索引改为整数样本（v2）

- `TrackDef`：循环点/时长由秒（`ls`/`le`/`d`）改为**整数样本**（`lss`/`les`/`ds`）；
  对外的 `LoopStartSec/LoopEndSec/DurationSec` 变成按 Rate 换算的只读计算属性，调用方零改动。
- `tools/csv_to_tfjson.py`：循环点**优先取游戏自带权威源**（复用 `tools/loop_delta.py` 的解析：
  th075 = WAV cue+ltxt、105/123/135/145/155 = sfl、175 = `.ogg.ini`），取不到才回落 CSV 秒值×44100；
  整曲长度优先取资源真实长度（OGG 末页 granule / WAV data 块）。
- `tools/loop_delta.py`：新增 `build_asset_index/asset_total_samples` 等辅助；比对改为直接读样本字段。
- 自测工程（selftest_wiring）手工构造 TrackDef 处改用样本字段。
- 回归工具加固：漂移判据先验证偏移 0，消除两样本误命中造成的假警报。

### 验证
主工程 0 警告 0 错误；P4 冒烟 36/36；接线自测 40/40；
**循环点权威比对 209 首全部 Δ=0（样本级完全一致）**；解码回归 0 长度不符 / 0 漂移。
副作用：先前遗留的 th135 `win.ogg`（起点 -165 样本）因改取权威样本而自动修正。
