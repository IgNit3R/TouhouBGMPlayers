# TfParsers 移植总档（PORTING_STATUS）

> 2026-09-06 · 三个子 agent 并行移植完成，验证合计 **75/75 全绿**
> 红线遵守情况：release/、pushfiles/、tools/、tsa/ 全程只读；未运行任何外部拆包 EXE；全部 C# 内存解析、零落盘解包；产物仅落在本目录。

## 总览

| 解析器 | 覆盖 | 验证 | 目录 |
|---|---|---|---|
| SuicaReader | th075（WAV） | **34/34** WAV 逐字节 MD5 一致 | `Suica/` |
| XorContainerReader | th105/th123（OGG） | **11/11**（61/61 + 49/49 条目字节一致；sfl 循环点与 tracklist.csv 全符） | `XorReader/` |
| TfpkReader | th135/145/155/175（OGG/TFWA） | **30/30**（四作条目数/覆盖语义/字节比对全过） | `TfpkReader/` |

每个目录含：自包含解析器（零 NuGet 依赖）、独立 net10.0 验证控制台工程（exit 0 = 全绿）、PORTING.md。

## 关键算法结论

### Suica（th075）
- u16 条目数 + n×108 字节条目表，整表滚动 XOR（k=t=0x64；逐字节 plain^=k; k+=t; t+=0x4D mod 256）。
- 记录 = name[100] CP932 + u32 size + u32 绝对 offset；数据区紧随表后原样连续存放。
- 循环点：WAV `cue ` 块首 cue 的 dwPosition（44.1kHz），终点=data 块尾。

### 双层 XOR（th105/th123）
- 条目表两层：**MT19937 流 XOR，seed = 6 + header_size（种子由表大小推导，非全局常量）** + 滚动密钥 XOR（key=0xc5 起，key+=0x83、step1+=0x53 环回）。
- 条目数据：单字节常量 XOR，key = `((offset>>1) | 0x23) & 0xff`（THCRYPT_PATCHCON_KEY），文件内不滚动。
- MT19937 已对照 thtk rng_mt.c + 官方标准向量（seed=5489 → 3499211612）双重验证。
- sfl：cue dwPosition=起点；LIST/adtl/ltxt 的 dwSampleLength=段长；终点=起点+段长；dwPosition 与 dwSampleOffset 同值只取其一。

### TFPK（th135/145/155/175）
- `TFPK`+版本字节后全部为 0x40 RSA 块（PKCS#1 自动探测，负载 4~16B）；目录表 → zlib 基名表 → 文件表（v0 明文读，v1/b3 逐 u32 XOR）→ **第二份文件表副本** → 数据。
- 目录种子：v0 = FNV-1(目录路径含尾分隔符)；v1 = 全路径 FNV-1a + neg32。cga/cgb 由 footer 驱动 + 位置相关流密码。
- **攻关发现（三参考实现均未记载）**：v0 与 v1 一样存在第二份未使用的文件表副本；数据区基址 = `tableEnd + 3 × fileCount × 64`，对两代通用。th135 v0 密钥本身与 135tk TFPK.cpp 一致（b3 前 16 字节原样 XOR），此前失败的根因是数据基址错位（由 chuboss1.sfl 的 RIFF/SFPL 双签名扫描唯一命中 delta=+1837632 = 3×9571×64 证实）。
- th155 121b：5 首替换曲（155tenshi1/155yukari1/bgst03/06/11）确认取 th155b 字节；多包覆盖语义 + 大小写/斜杠不敏感查找验证通过。

## 依赖结论（主工程接入时用）
- 现状：`net10.0-windows` + NAudio 3.0.1。
- 推荐只加 **NVorbis 0.10.5**（MIT、netstandard2.0 兼容），走「容器解出 OGG 字节 → NVorbis 内存解码 16bit PCM → 现有 LoopSampleProvider 循环」，不用 NAudio.Vorbis（其 1.5.0 依赖 NAudio.Core 2.0.0 与现有 NAudio 3.0.1 错位）。
- 本机 NuGet 走镜像 nuget.azure.cn；受限 shell 下 dotnet restore 需补标准环境变量（详见 Suica/PORTING.md）。

## 遗留风险
1. TFPK「第二份表副本 / dataBase 偏移」无权威文档佐证（135tk/arc_unpacker/touhouSE 均缺失），系实测三作必需；其他来源 pak 若布局不同需回退基址，暂未做自动回退（详见 TfpkReader/PORTING.md §7）。
2. XOR 条目名按原始字节返回（Latin1）：th105b/th123b 的 BGM 名全 ASCII 无影响；复用解析 th105a/c（SJIS/GBK 混合名）需调用方处理编码。
3. thtk version==105105 分支（跳过第二层滚动 XOR）未实现——本场景用不到。
4. Suica `ExtractAll` 全量载入峰值约 0.9GB（428MB 容器）；播放器侧建议用 ParseEntries + ExtractEntry 按需流式读取（API 已备好）。

## 状态
- 前期准备（曲表 7 份 + 解析器 3 套）全部就绪。
- **主播放器实现（IAudioSource / 索引 / 工厂接线）未开工，等用户下令。**
