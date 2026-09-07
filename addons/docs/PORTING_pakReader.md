# TFPK / th175 cga 容器解析移植说明（TfpkReader）

交付物：`TfpkReader.cs`（自包含、零 NuGet 依赖，BCL only）+ `TfpkReader.Tests/`（.NET 10 控制台验证套件）。
移植来源：

- TFPK v0/v1：`tools/arc_unpacker/src/dec/twilight_frontier/tfpk_archive_decoder.cc`、`tools/135tk/th135arc-alt/TFPK.cpp` + `Rsa.cpp` + `FilesList.cpp`、`tools/touhouSE/touhouSE_src/th135.h`
- th175 cga/cgb：brliron 135tk `th175arc.h / read.c / common.c`

## 1. TFPK 头部与条目表结构

magic `"TFPK"` + 1 字节版本号（0 = th135，1 = th145/th155）。**其后所有元数据都以 0x40 字节 RSA 块组织**：
每块 `m = RSA_public_decrypt(chunk)`（512-bit 每游戏模组，e = 65537），PKCS#1 v1.5 type-1 去皮后得到消息。
注意两点：

- **消息长度可变（4~16 字节有效负载）**：th145 实际把负载放在块尾（padding 宽度随负载变化），
  arc_unpacker/135tk 都只按所需字节数从 `padding 结束位置` 起读，因此读取逻辑必须
  "去皮后从 payload 起点取 N 字节"，不能假设消息恒为 32 字节再右对齐/左对齐。
- th145 英文补丁版本块未加密（原始 0x40 字节的前 0x20 直接用，且字节 4..16 为 0）。
  已知模组：th135 日 / th135 英 / th145 / th155（+ MarisaLand 等 3 个，见 135tk Rsa.cpp），按 PKCS#1 校验自动探测。

块序：

```
块 0                    : u32 dir_count
dir_count 块            : u32 dir_hash_seed, u32 file_count（每目录两字段同占一块）
1 块                    : u32 table_zsize, u32 table_size, u32 block_count
block_count 块          : 拼接后前 table_zsize 字节为 zlib 流，解压 = cp932 NUL 结尾的文件基名序列
                          （按目录表顺序依次消费）
1 块                    : u32 file_count
每文件 3 块 b1/b2/b3    : v0: size=b1[0..4], offset=b1[4..8], name_hash=b2[0..4], key=b3[0..16] 原样
                          v1: b3 按 u32 顺序消耗 K0..K3：
                              size=b1[0]^K0, offset=b1[4]^K1, name_hash=b2[0]^K2, unk=b2[4]^K3
                              key[j*4..] = neg32(b3 的第 j 个 u32)（j=0..3，共 16 字节）
```

**关键发现：v0 与 v1 都在文件表之后、数据区之前存有第二份未使用的文件表副本**
（再占 `3 × file_count × 0x40` 字节）。真实数据基址：

```
dataBase = tableEnd + 3 × file_count × 0x40
绝对地址 = dataBase + 表内 offset
```

- v1 证据：th155b 四个已知明文位置恒定漂移 +249408 = 3×1299×64；th155.pak 同法验证。
- v0 证据：th135.pak 用 chuboss1.sfl 的表密钥做 `RIFF`/`SFPL`(+8) 签名扫描，唯一命中点
  相对表尾漂移 +1837632 = 3×9571×64。（arc_unpacker / touhouSE / 135tk 均未处理该副本，
  本工作区内的 th135.pak 实测必须加上；v1 曾据此修复，v0 由 crib-drag 定位后统一。）

## 2. 内容解密

- **v0（th135）**：`plain[i] = data[i] ^ key[i % 16]`（key = b3 前 16 字节原样，逐条目重置）。
- **v1（th145/th155）**：密文反馈链。`aux = key[0..4]`；逐字节
  `t = data[i]; plain[i] = t ^ key[i%16] ^ aux[i&3]; aux[i&3] = t`。
  特性：**首 4 字节不受密钥影响**（透传），可用于 magic 探测/调试。

### v0 密钥推导过程（crib-drag 定位记录）

th135 起初内容字节不符但名称/大小均正确。排查过程：

1. **crib**：以明文树 OGG 文件头 4 字节 `"OggS"` 为已知明文，
   `keystream = 错误解密输出 ⊕ 真实明文`。多条目 keystream 互不相同且**非 16 字节周期**
   → 排除"密钥错误"（若仅密钥错，keystream 必呈周期 16），指向**偏移错位**。
2. **签名扫描**：选 chuboss1.sfl（明文 `RIFF`@0 + `SFPL`@8），用其表密钥
   `1602B801 366FCCDE 93FD4182 26826566` 算出存盘签名
   `"RIFF"^key[0..4]`@p 与 `"SFPL"^key[8..12]`@p+8，在原始 pak 中流式扫描，
   全文件唯一命中 p=133646026，`delta = p - (tableEnd+offset) = +1837632 = 3×9571×64`。
3. **结论**：v0 与 v1 一样存在第二份文件表副本，dataBase 统一为
   `tableEnd + 3×fileCount×64`，密钥本身（b3 前 16 字节原样 XOR）与 135tk/arc_unpacker 一致。
   修正后 th135 全部条目（bgm 全量 + 随机样本 + th135b 补丁树）字节级比对通过。

## 3. 名称解析与哈希

归档只存 32 位名字哈希。归一化：ASCII 小写 + `/`→`\`，cp932 字节序列。

- **v0**：目录种子 = `FNV-1(目录路径含尾部分隔符, seed=0x811C9DC5)`
  （如 `data\bgm\`；注意 touhouSE 的 DIR_NAME_LIST 全部带尾 `/`，`th135.h:406` 亦确认）。
  文件哈希 = `FNV-1(basename, seed=目录种子)`，FNV-1 为 `h *= 0x1000193; h ^= c`。
  因此 v0 需要外部提供**目录路径列表**（嵌入表只有基名）；未命中目录记 `unk-<seed:x8>`。
- **v1**：完整路径哈希 = `neg32(FNV-1a(全路径))`（`h ^= c; h *= 0x1000193`，末尾取负），
  需要游戏 fileslist.txt（本工作区已有 th145/th155 合用文件）。
  th145 英文补丁的嵌入目录种子是垃圾值，故 v1 目录名不可从嵌入表恢复，一律依赖文件表。
- **th175 cga/cgb**：描述表 key = `FNV-1a`（无大小写折叠、正斜杠路径），
  名称来自 fileslist.js（JSON 数组）。

未命中条目命名 `unk-%08x`（v0 第 2 个起 `unk-%05d-%08x`，与 arc_unpacker 一致）。

## 4. TFWA vs OGG 检测

`DetectKind(ReadOnlySpan<byte>)`：`OggS` → Ogg；`TFWA` → TFWA（tasofro WAV 容器，内部实为 Ogg）。
TFPK v0 内容非透传，探测前须先用该条目 key 解出前 4 字节；v1 首 4 字节透传可直接读；
cga 用 `size^offset` 派生密钥解首字。注意 **th175 的 .wav 是裸 RIFF**（无 TFWA 容器），
th135/th145 的 .wav 才是 TFWA。

## 5. 多包顺序覆盖语义

`TfpkReader.Open(archivePaths, fileNameList)` 按传入顺序打开多个包，归一化名
（小写 + `/`→`\`）为键建字典，**后打开的包覆盖同名条目**，与引擎加载顺序一致：

- th135 + th135b（1.1 补丁，1996 条）
- th155.pak + th155b.pak（121b 补丁；容器表 1299 条，MANIFEST 1245 = 实际文件数。
  5 首替换曲：155tenshi1 / 155yukari1 / bgst03 / bgst06 / bgst11）
- th175 data.cga + data.cgb（共同条目 1012，其中 1001 内容不同，全部取 cgb）

查找大小写/斜杠不敏感；全部内容仅在内存解密，不落盘。

## 6. 验证结果（dotnet run，30/30 全绿）

| 检查 | 结果 |
|---|---|
| th175 data.cga 条目数 == 4976（MANIFEST） | PASS |
| th175 data.cgb 条目数 == 2193（本地 1.15 安装；MANIFEST 2115 = 1.14 基线） | PASS |
| th175 合并包 == union(cga,cgb)；unk == 0（fileslist.js 完整） | PASS |
| th175 bgm 全量字节比对（cga+cgb 树，53+53 项） | PASS |
| th175 非音频随机样本 20 项字节比对 | PASS |
| th175 覆盖语义：共同名 1012 个全取 cgb 字节，mismatches=0 | PASS |
| th155 th155.pak == 17507、th155b == 1299（容器表；MANIFEST 1245 = 文件数） | PASS |
| th155 bgm ogg 全量字节比对（99 项，merged 按补丁语义取 b 树） | PASS |
| th155 bgm.csv(TFCS) 尺寸 3167（主）/3185（b） | PASS |
| th155 5×121b 替换曲目：主/b/merged 三方字节比对 + sfl 尺寸 108/108/104/104/104 | PASS |
| th135 th135.pak == 9571、th135b == 1996（MANIFEST） | PASS |
| th135 bgm 全量字节比对（主树 41 项 + 补丁树 17 项） | PASS |
| th135 非音频随机样本 10 项 | PASS |
| th145 th145.pak == 14064（MANIFEST） | PASS |
| th145 bgm 全量字节比对（138 项） | PASS |
| th145 897 个 .wav 全部 TFWA；TFWA 随机样本 8 项 | PASS |
| 多包覆盖：merged 含两包全部名称；大小写/斜杠不敏感查找 | PASS |

（树中 tracklist.csv 为拆包时附加的文档文件，不在容器内，已从比对中排除。）

## 7. 遗留风险

- **第二份文件表副本**在权威参考（arc_unpacker/135tk/touhouSE）中均无记载，本实现在
  th135/th145/th155 三包上实测必需；若遇到其他来源的 pak（如官方原版 vs 汉化重打包）
  布局不同，可通过"首 4 字节 magic 校验失败则回退 tableEnd 基址"兜底（当前未实现自动回退）。
- v0 目录名依赖外部目录列表；目录列表不全时相应条目记为 `unk-<seed>`（不影响已知名访问）。
- th175 data.cgb 为 1.15 补丁安装，条目数 2193 与 MANIFEST(1.14) 2115 不同属预期。
- cp932 非.creator 范围内的罕见双字节序列经 CodePagesEncodingProvider 往返，理论上有极小概率
  与游戏原生处理存在差异（实测 fileslist 全量哈希命中正常）。
