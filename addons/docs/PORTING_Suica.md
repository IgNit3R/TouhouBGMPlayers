# PORTING.md — th075 Suica 容器解析器（Python → C#）移植说明

- 源目录：`bgmplayer/TfParsers/Suica/`（自包含，零 NuGet 依赖，纯 BCL）
  - `SuicaReader.cs` — 解析器类（条目表解析、条目提取、WAV cue 循环点解析）
  - `Program.cs` — 独立控制台验证工程（net10.0，内存中比对，不落盘解包产物）
- 参考实现：`release/tf/th075/dat/th075_suica_parser.py`（上游对齐 go-brightmoon `pkg/pbgarc/suica.go`）
- 本移植不做任何播放器集成，不修改 bgmplayer 现有代码。

## 1. 算法要点

### 1.1 Suica 外层容器（little-endian）

```
[u16 entry_count]
[entry_count × 0x6C (108) 字节条目表]   ← 整表经滚动 XOR 流混淆
```

滚动 XOR 密钥流：`k = t = 0x64`；对每个字节：`plain ^= k; k += t; t += 0x4D`（均 mod 256）。
参考实现中 k/t 按 32 位累加，但只有 k 的低 8 位参与 XOR，低 8 位算术与高位无关，
故 C# 直接用 `byte` 溢出回绕即可，行为等价。

每条 108 字节记录布局：

| 偏移 | 类型 | 含义 |
|---|---|---|
| +0x00 | byte[100] | 条目名，NUL 结尾/填充，CP932 编码（如 `wave\bgm\00a.wav`） |
| +0x64 | u32 | size（原始大小；Suica 不压缩，size == 存储大小） |
| +0x68 | u32 | offset（容器内绝对偏移） |

数据区紧跟条目表之后（`offset == 2 + 108*n`），各条目连续无缝、原样存储。
校验特征：文件首字节 LE 解读即条目数（th075bgm 首两字节 `22 00` = 34）。

### 1.2 WAV cue 块（循环点）

RIFF 块遍历（4 字节 chunkId + u32 chunkSize，字对齐）：

- `fmt `：取 blockAlign（+12 处 u16），用于把字节长度换算为样本数；
- `cue `：`u32 numCuePoints` + 每点 24 字节 CUEPOINT
  (`dwIdentifier, dwPosition, fccChunk, dwChunkStart, dwBlockStart, dwSampleOffset`)。
  取**第一个 cue 点的 dwPosition** 作为循环起点样本数（44100 Hz）；
- `data`：记录 `dataEnd = body + chunkSize` 作为音频数据结尾（即"文件尾"）。

返回：`LoopStartSeconds = dwPosition / 44100.0`；
`LoopEndSeconds = (dataEnd / blockAlign) / 44100.0`（无 cue 块返回 null）。
实测这些 WAV 的 cue 块位于 data 块之后，且 `dwPosition == dwSampleOffset`、指向 data 内样本偏移。

## 2. C# 移植注意点

1. **CP932 编码**：.NET 默认不含代码页 932，首次 `GetEncoding(932)` 抛
   `NotSupportedException`（新运行时；旧文档写 `ArgumentException`，两者都要兜）。
   需 `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)`。该类型随
   共享框架（System.Text.Encoding.CodePages.dll）分发，无需额外 NuGet 包。
   本项目条目名实际全为 ASCII，但按参考实现保留 CP932 语义。
2. **XOR 密钥流**：直接用 `byte` 累加（默认 checked 上下文对 byte 复合赋值
   `k += t` 不抛异常，溢出静默回绕），与 Python 的 32 位 mod 运算低 8 位等价。
3. **BitConverter.ToUInt32**：等价 `struct.unpack_from('<I', ...)`；注意流式 API
   与全量 byte[] API 均提供（`ParseEntries(Stream/byte[])`、`ExtractAll`、`ExtractEntry`）。
4. **内存策略**：验证工程先流式计算 34 个已知 WAV 的 MD5（1 MB 缓冲），再读入容器
   （428 MB）一次性 `ExtractAll`，峰值内存约 0.9 GB；解出的条目仅在内存中比对，全程不写盘。
5. **对齐细节**：RIFF 块尺寸为奇数时跳过 1 字节填充；解析 cue 只取第一点，
   numCuePoints ≥ 1 时 `dwPosition` 位于 body+8（跳过 count 字段后的第 2 个 u32）。

## 3. 验证结果（dotnet run，Release，34/34 PASS）

基准：`release/tf/th075/dat/th075bgm/wave/bgm/*.wav` 已知明文逐条 MD5 比对；
容器：`E:/GitWorkspace/thworks/tf/th075/th075bgm.dat`（428,003,126 字节，只读）。

| # | 条目 | size (B) | 比对 | cue 循环起点 (s) | 循环终点 (s) |
|---|---|---|---|---|---|
| 0 | wave\bgm\00a.wav | 14,953,564 | PASS | 2.085 | 84.770 |
| 1 | wave\bgm\00b.wav | 12,841,956 | PASS | 6.345 | 72.800 |
| 2 | wave\bgm\00c.wav | 24,676,720 | PASS | 10.002 | 139.890 |
| 3 | wave\bgm\01a.wav | 11,179,924 | PASS | 3.142 | 63.377 |
| 4 | wave\bgm\01b.wav | 11,726,988 | PASS | 0.617 | 66.479 |
| 5 | wave\bgm\02a.wav | 18,118,036 | PASS | 1.222 | 102.709 |
| 6 | wave\bgm\02b.wav | 15,250,600 | PASS | 1.737 | 86.454 |
| 7 | wave\bgm\03a.wav | 21,075,460 | PASS | 3.124 | 119.474 |
| 8 | wave\bgm\03b.wav | 11,446,440 | PASS | 2.431 | 64.888 |
| 9 | wave\bgm\04a.wav | 18,260,120 | PASS | 2.752 | 103.514 |
| 10 | wave\bgm\04b.wav | 15,586,472 | PASS | 13.059 | 88.358 |
| 11 | wave\bgm\05a.wav | 12,489,284 | PASS | 2.809 | 70.800 |
| 12 | wave\bgm\05b.wav | 15,328,424 | PASS | 14.480 | 86.895 |
| 13 | wave\bgm\06a.wav | 12,787,948 | PASS | 23.498 | 72.493 |
| 14 | wave\bgm\07a.wav | 9,824,700 | PASS | 12.020 | 55.695 |
| 15 | wave\bgm\08a.wav | 26,089,892 | PASS | 5.998 | 147.901 |
| 16 | wave\bgm\09a.wav | 34,637,624 | PASS | 3.506 | 196.358 |
| 17 | wave\bgm\sys00_op.wav | 16,924,840 | PASS | 6.856 | 95.945 |
| 18 | wave\bgm\sys99_ed.wav | 14,743,664 | PASS | 0.000 | 83.580 |
| 19 | wave\bgm\51.wav | 6,002,956 | PASS | 1.029 | 34.029 |
| 20 | wave\bgm\52.wav | 3,287,752 | PASS | 6.961 | 18.636 |
| 21 | wave\bgm\53.wav | 10,276,852 | PASS | 3.912 | 58.257 |
| 22 | wave\bgm\54.wav | 13,418,328 | PASS | 3.074 | 76.066 |
| 23 | wave\bgm\56.wav | 3,972,532 | PASS | 0.640 | 22.518 |
| 24 | wave\bgm\57.wav | 5,491,336 | PASS | 3.430 | 31.128 |
| 25 | wave\bgm\58.wav | 7,053,696 | PASS | 1.573 | 39.985 |
| 26 | wave\bgm\59.wav | 6,604,804 | PASS | 1.061 | 37.088 |
| 27 | wave\bgm\60.wav | 6,508,712 | PASS | 1.576 | 36.897 |
| 28 | wave\bgm\61.wav | 7,247,016 | PASS | 3.565 | 41.082 |
| 29 | wave\bgm\62.wav | 8,291,468 | PASS | 3.613 | 47.003 |
| 30 | wave\bgm\63.wav | 8,628,392 | PASS | 2.907 | 48.913 |
| 31 | wave\bgm\65.wav | 6,537,384 | PASS | 4.012 | 37.059 |
| 32 | wave\bgm\67.wav | 7,554,216 | PASS | 1.751 | 42.824 |
| 33 | wave\bgm\68.wav | 9,181,352 | PASS | 2.998 | 52.048 |

条目表 34 条、表尾 @3,674，与 `entries.csv`/`MANIFEST.md` 完全一致；全部条目
MD5 与已知明文逐字节一致（0 FAIL）。所有 34 个 WAV 均含 1 个 cue 点；
`sys99_ed.wav` 起点为 0（合理：ED 曲从头循环）。

## 4. 复现方式

```
cd bgmplayer/TfParsers/Suica
dotnet run -c Release
# 可选参数: dotnet run -c Release -- <容器路径> <已知WAV目录>
# 退出码: 0=全绿, 1=有 FAIL, 2=输入错误
```

## 5. 遗留风险

- 本会话 shell 缺少 `ProgramData`/`ProgramFiles(x86)` 等标准环境变量，`dotnet
  restore` 会报 `Value cannot be null. (Parameter 'path1')`；在正常终端/CI 中不受影响。
- `ExtractAll` 一次性载入全部条目（th075bgm 约 428 MB）；将来若在播放器内解析
  th075.dat（715 MB+）级别容器，建议改用 `ParseEntries + ExtractEntry` 按需流式读取。
- CP932 路径尚未遇到非 ASCII 条目（th075bgm 全为 ASCII 名），若其它 Suica 档案
  （th075/th075b/th075c.dat）含日文名，解码路径已按 CP932 处理但未实测。
