# PORTING.md — th105/th123 双层 XOR 音乐容器解析（C# 移植）

移植来源：thtk v12（`E:/GitWorkspace/thworks/tools/thtk/thtk/`）
- `rng_mt.c` → `Mt19937.cs`（MT19937）
- `thdat105.c` + `thcrypt105.c` → `XorContainerReader.cs`（容器/条目解密）
- `.sfl` 解析 → `SflReader.cs`（依据 th105b 实测布局 + tracklist.csv 交叉验证）

## 容器布局（th105b.dat / th123b.dat，thtk 版本号 105 / 123）

```
[0..2)   uint16 LE  entry_count
[2..6)   uint32 LE  header_size
[6..6+header_size)  加密条目表
[6+header_size..)   条目数据（明文 OGG/SFL 经 XOR 加密后连续存放）
```

条目表每项（解密后）：`u32 offset`（文件绝对偏移）+ `u32 size` + `u8 name_len` + `name[name_len]`
（原始字节，CP932/GBK 混合；th105b/th123b 的 BGM 条目名均为 ASCII，`data/bgm/st00.ogg` 形式）。

## 两层 XOR（条目表）

解密顺序（XOR 自逆，加密即逆过程）：

1. **MT19937 流式 XOR**（`th_crypt105_list`）：
   `seed = 6 + header_size`（6 = 表头两字段字节数），初始化 MT 后逐字节
   `data[i] ^= rng.NextUint32() & 0xff`。
2. **滚动密钥 XOR**（`th_crypt75_list`）：`key=0xc5, step1=0x83, step2=0x53`，
   每字节：`data[i] ^= key; key += step1; step1 += step2;`（全部按 byte 环回）。

注：thtk 对 `version==105105`（Megamari？）跳过第 2 层；th105/th123 均两层都做。

## 种子来源与条目数据解密

- 种子不是全局常量，而是**由条目表自身大小推导**：`6 + header_size`。header_size 在文件头
  明文存放，无需搜索。
- 条目数据（`th_crypt105_file`）：单字节**常量** XOR，
  `key = ((offset >> 1) | 0x23) & 0xff`，同一文件内不滚动。
  `0x23 = THCRYPT_PATCHCON_KEY`（th105/th123 专用常量；0x08 为 Megamari）。

## MT19937 移植核对点（最高风险）

- `mt[0] = seed`，`mt[i] = 0x6c078965 * (mt[i-1] ^ (mt[i-1]>>30)) + i`（标准 init_genrand，
  **不是** 1812433253 变体）。
- `mti` 初始 = 624，**惰性 twist**：首次取数才生成一批。
- twist：标准 N=624/M=397，mag01 = {0, 0x9908b0df}。
- tempering：`y^=y>>11; y^=(y<<7)&0x9d2c5680; y^=(y<<15)&0xefc60000; y^=y>>18`。
- 自检：`seed=5489` 首个输出 = `3499211612`（官方标准向量）。

## SflReader（RIFF "SFPL"）

- `"cue "` 块：第 1 个 cue 的 `dwPosition`（cue body[8:12]，即跳过 numCues+dwIdentifier）
  = 循环起点（采样数）。cue 内 `dwSampleOffset` 与 `dwPosition` 同值，只取其一，不叠加。
- `LIST("adtl")` → `"ltxt"` 块：`body[4:8]` = `dwSampleLength` = 循环段长（采样数）。
- 循环终点样本 = 起点 + 段长；RATE = 44100；秒 = 采样数/44100。
- 实测文件中 cue 与 LIST 之间有一个 size=0 的空 `"data"` 块，解析器按通用 RIFF 块遍历跳过
  （含 word 对齐填充）。

## 验证结果（dotnet run，2026-09-06，11 PASS / 0 FAIL）

| 检查项 | 结果 |
|---|---|
| th105b.dat：61/61 条目与 `release/tf/th105/dat/th105b/` 明文逐字节一致 | PASS |
| th105：31 个 OGG 头部魔数 `OggS` | PASS |
| th123b.dat：49/49 条目与 `release/tf/th123/dat/th123b/` 明文逐字节一致 | PASS |
| th123：25 个 OGG 头部魔数 `OggS` | PASS |
| th105：30 首 sfl 循环点（start/body/end）与 tracklist.csv 秒值一致（容差 0.0005s=csv 舍入半位） | PASS |
| th123：24 首 sfl 与 tracklist.csv 一致 | PASS |
| MT19937 seed=5489 标准向量 3499211612 | PASS |
| 滚动 XOR 密钥序列 c5,48,1e,47,c3（手算逐字节累进验证） | PASS |
| 文件 XOR key=((offset>>1)|0x23)&0xff | PASS |

交叉印证：`release/tf/th123/dat/th123b/MANIFEST.md`（t28 自研 Python 解析器）独立记录的
算法与本次移植完全一致（seed=6+1275、滚动 0xc5/0x83/0x53、文件 key=((offset>>1)|0x23)&0xff）。

## 环境备注

本机 Git Bash 会话缺失 `ProgramFiles(x86)`/`APPDATA`/`ProgramData` 等 Windows 标准环境变量，
导致 NuGet restore 报 `Value cannot be null. (Parameter 'path1')`。运行方式：

```
env APPDATA='C:\Users\Yuuka\AppData\Roaming' \
    ProgramFiles='C:\Program Files' \
    'ProgramFiles(x86)=C:\Program Files (x86)' \
    ProgramW6432='C:\Program Files' \
    ProgramData='C:\ProgramData' \
    dotnet run --project TfXorReaderTest
```

## 遗留风险

- 条目名按原始字节读出（Latin1 解码后由调用方按 CP932/GBK 解释）；th105b/th123b 的 BGM 条目
  名全为 ASCII，不受影响，但如复用该 Reader 解析 th105a/c（含 SJIS/GBK 名）需注意编码策略。
- thtk 中存在 `version==105105` 跳过第 2 层滚动 XOR 的分支；本实现固定两层都做（th105/th123
  正确），不支持 105105。
