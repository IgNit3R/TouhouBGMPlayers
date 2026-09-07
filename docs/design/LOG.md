# bgmplayer 工作日志

## 红线（用户明确约定）

- **不改写、不覆盖任何已有文件**，产物一律新建到 `bgmplayer/` 内
- 播放器运行时只读 `tsa/`、`tf/` 原始文件；拆包产物（csv/raw wav）仅可**参考查阅**
- 社区参考来源限定：**thwiki、thcrap、thprac**（thwiki.cc 被 WAF 拦，改用 en.touhouwiki.net）
- 曲名一律取**日语原名**

---

## 2026-09-04　tsa 侧全曲目表生成（346 首）

产出：`TRACKLIST.md`（人读）、`tracklist.csv`（机读）、`gen_tracklist_v2.py`（生成脚本）

### 数据链路（全部从原始文件现场解出）

```
tsa/<dir>/thXX.dat  --brightmoon -x-->  _extract/<dir>/{thbgm.fmt, musiccmt.txt}
tsa/<dir>/thbgm.dat ------------------>  仅取文件大小，校验末轨不越界
tools/BGMforALL/BgmForAll.ini -------->  补曲名（灵界版 / 复用曲 / th06）
```

- `brightmoon.exe -x -o <dir> <dat>`：th07~th20 全支持，约 5~15 秒/作
- **th06 不支持**（thdat v6 老格式），改用 `tools/thtk/thtk-bin-12/thdat.exe -x 6`；
  日文路径在 bash 下会挂，需复制到产物目录或用 Python 处理

### 关键结论 1：`titles_th` 有 bug，不要用

`tools/go-brightmoon/titles_th` 的产物对 th13 会**漏 8 条 b 版、重复 8 条主版**，
总数凑巧仍是 31。必须直接解析 `thbgm.fmt`（52 字节/条）：

```
name[16]  start[4]  unk[4]  intro[4]  total[4]  WAVEFORMATEX[18]  pad[2]
```

字段语义：`start` 绝对偏移（首轨恒为 0x10 = ZWAV 头长）、`intro` 前奏字节、
`total` 整轨字节；`loop_len = total - intro`。`+0x14`（unk）是未定义字段，**不要用**。

### 关键结论 2：fmt 是几何真值，ini 是听感微调版

同一判据 `start[k+1] == start[k] + total[k]`：

| | fmt | ini |
|---|---|---|
| th09 | 0 断点 | **9 断点** |
| th10 | 0 断点 | **17 断点** |
| th11 | 0 断点 | **10 断点** |
| th14 | 0 断点 | **2 断点** |
| 其余 17 作 | 0 | 0 |

ini 在这些作上 intro/loop **分割点**被人为挪过（总长基本不变），
如 th10 #1：ini intro 0x442B40 / fmt 0x3C3000，两者 `intro+loop` 同为 0xE2FA10。
**几何数据一律以 fmt 为准。**

### 关键结论 3：灵界版（霊界トランス）

th13 的 fmt 有 31 条 = 18 主版 + **13 灵界版**（`th13_XXb.wav`，22050Hz，主版 44100）。
`musiccmt.txt` 只命名主版（17 条），灵界版曲名靠 ini 补 —— ini 的格式是
`主版曲名（霊界トランス）`。fmt 物理顺序里主版与灵界版**交替排列**。

### 关键结论 4（已修正）：th06 的 `.pos` 就是纯样本偏移，不要减 44

`.pos` = `(loop_start_sample, loop_end_sample)` 两个 uint32，**44100Hz 样本偏移，
直接当帧号用**。可播放区 = `[0, pos[1])`，循环段 = `[pos[0], pos[1])`，
`pos[1]` 之后是尾奏，**游戏里永不播放**。

```python
intro_bytes = pos[0] * 4      # 2ch × 16bit
total_bytes = pos[1] * 4
loop_bytes  = total_bytes - intro_bytes
循环起点(绝对) = wav数据起始偏移 + intro_bytes
```

**曾犯的错**：我早先按「ZUN 用标准 44 字节头算样本」推出 `intro = pos[0]*4 - 44`，
并用 `BgmForAll.ini` 验证「17/17 一致」—— 但 ini 有同样的 −44 偏倚，
**是两个同源错误互相印证**。

判决性检验（`probe_th06_origin2.py`，用 t101 渲染件当真值）：

- `rendered` 长度 **恰好 == pos[1] + 15×44100**，17/17 逐帧吻合
  → 硬证可播放区是 `[0, pos[1])`，尾奏被裁掉
- `rendered[pos[1] : ]` 与 `raw[pos[0] : ]` 比对 0.5 秒：
  A=`pos0` **MAE 0.0（逐样本相等，17/17）**；B=`pos0−11` 与 C=`pos0+11` 均在 2500~7700

修正幅度只有 44 字节 / 11 帧 / 0.25 毫秒，听感为零，但数值要改。

来源：`release/tsa/kouma/bgm/README.md`（t101 逆向结论，含三条判据：
① 17/17 满足 `0 < pos0 < pos1 ≤ wav帧数`；② th06_02 开头 0.5s 静音而 pos0 处 RMS=8434；
③ 重渲染续播尾首样本与 `wav[pos0]` 逐字节相等）。

### 关键结论 5：曲名匹配要剥离扩展名

`musiccmt.txt` 的 key 后缀不统一：th09 是 `.mid`（fmt 里是 `.wav`），th143 无后缀。
统一 `stem = name.rsplit('.',1)[0]` 后匹配，键集合完全吻合。

跨作品复用曲（如 `th128_08.wav` = プレイヤーズスコア）需**两轮**：
先收集所有已命中的 `stem -> 曲名`，再回填未命中的。

曲名来源分布：musiccmt 320 / ini 25 / reuse 1，346 首**零缺名**。

### 坑记录

- `du -sh` 在这个量级（几十万文件）会超时被杀
- bash 下日文文件名（紅魔郷MD.DAT）会挂，用 Python 或先复制副本
- `parse_ini` 在两个脚本里返回结构不同（dict vs 列表），别混用
- ini 专辑名前缀冲突：`東方緋想天則` 会被 `東方緋想天` 抢走 → 按 key 长度倒序匹配
- 16bit PCM 要用 `<h` 解，不能用 `<i`（曾导致 MAE 4 亿量级的假结果）

---

## 2026-09-04　形态更正 + 构建环境探测

### 形态：用户否掉浏览器方案，要求 **Win 原生应用**

（此前我擅自定义为「Python 后端 + 浏览器前端」，作废。`server.py` 已落盘但未运行，
留作逻辑参考，`player.html` 未开始。）

### 本机构建环境实测

| 项 | 结果 |
|---|---|
| OS | Windows 11，build 26200 |
| Visual Studio | **未安装** |
| .NET Framework 运行时 | v4.0.30319 存在（4.x 可跑） |
| dotnet SDK | **仅 5.0.403**（太旧，建不了 net8/10 的 WinForms，需用户自行装新 SDK） |
| WindowsDesktop 运行时 | 5.0.12 / 8.0.30 / 10.0.11 **均已装** |
| MinGW-w64 交叉编译器 | **无**（cygwin 里只有 mingw64 的 openssl/zlib 运行时库，没有 gcc） |
| Cygwin gcc | 14.4.0 有，但直接编出的 exe 依赖 `cygwin1.dll` |
| Go | 有 |

→ C++ 路线需从 cygwin setup 装 `mingw64-x86_64-gcc-g++`；C# 路线需装新版 .NET SDK。

### 现成的 .NET 参考件（tools/ThbgmExtractor-1.6.7）

Smdn 一套 netfx4.0 组件，其中：

- `Smdn.Formats.Thbgm.dll` —— **thbgm.fmt 解析器**（Smdn.Formats.Thbgm / Smdn.Media / Smdn.IO）
- `Smdn.Windows.Multimedia.dll` —— `Smdn.Windows.Multimedia.WaveformAudio`，waveOut 封装
- `Smdn.Windows.Forms.ThbgmPlayer.dll` —— WinForms 播放器 UI（EffectEditor / WaveFormView / ProductSelector）

**决定不用**：ThbgmExtractor 主体是 GPLv2，这些 DLL 放在 exe 同级目录、
授权归属不明（README 只说 `core/` 下的库是 MIT）。我们自己的 fmt 解析器已验证过
（346 首 / 20 作零断点），移植到 C# 约 80 行，没必要沾许可风险。

### ✅ th06 存疑已解决（2026-09-04 补充）

权威依据：`release/tsa/kouma/bgm/README.md`（任务 t101，规范依据 `_bgm_report.md` §4）

- th06 **无 thbgm.dat、无 thbgm.fmt**，就是 `tsa/kouma/bgm/` 下 17 个散装 wav
- `.pos` = 8 字节 = 2×u32 LE，**单位是 44100Hz 样本偏移**
  - `pos[0]` = 循环起点（intro 结束）、`pos[1]` = 循环终点，循环段 `[pos[0], pos[1])`
- t101 已排除的假设：MIDI tick / MIDI 文件字节偏移 / 小节对齐(88200/176400) / 4B 帧对齐
- 判定依据：① 17/17 满足 `0 < pos[0] < pos[1] ≤ wav帧数`；
  ② th06_02 开头 0.5s 纯静音(RMS=0) 而 pos[0] 处 RMS=8434；
  ③ **重渲染后续播尾首样本逐字节等于 `wav[pos[0]]` 处内容**

→ 我原先"wav 是完整录音、pos 是 MIDI 循环结构"的存疑**不成立**。
wav 比 pos[1] 长的部分就是**尾奏**，循环不会走到那里。

**用 README 给的例子复核我们的 pos 值，完全吻合**：
th06_01 intro 6.00s / 循环 53.21s（实测 6.0003 / 53.213）；
th06_02 intro 20.77s / 循环 66.20s（实测 20.769 / 66.202）。

### ⚠️ th06 待修：我数据里有 11 样本偏差

- README 解读：PCM 内字节偏移 = `pos * 4`（**不减 44**）
- 我在 `gen_tracklist_v2.py` 用了 `pos*4 - 44`，依据是"与 BgmForAll.ini 逐首吻合 17/17"
- **那是循环论证**——ini 自己也按 44 字节头算的
- 差距仅 **11 样本 = 0.25 毫秒**，且 `loop_len = (pos[1]-pos[0])*4` 做减法时会被抵消，
  所以 CSV 里 **loop 长度本来就是对的**，只有 intro/total 的绝对字节偏移偏 11 样本
- 想用接缝 MAE 判死，但**测不出来**：三假设 MAE 差 <0.5%，随机对照有时更低
  （`#17` 对照 424 vs 6420）—— th06 是 MIDI 合成循环乐，周期性太强，MAE 不适用
- **结论：按 README 改成 `pos*4`**（一行改动，待用户指示后执行）

### 构建环境决定（2026-09-04）

- **装 .NET 10 SDK**（最新 LTS，2025-11-11 发布，支持到 **2028-11-14**）
  - 别选 .NET 8：虽是 LTS 但已进入 Maintenance，**2026-11-10 EOL**（只剩 2 个月）
  - 别选 .NET 9（STS）/ .NET 11（仍是 preview）
- 本机**已装** `Microsoft.WindowsDesktop.App 10.0.11` 运行时（WPF 直接能跑）
  → 装完 SDK 10 即可，无需再补任何运行时
- 目标框架 `net10.0-windows`；NAudio 由 NuGet 联网拉一次

### 产品形态（用户已定，2026-09-04）

- **C# + WPF** + **NAudio / WASAPI** + **框架依赖（多文件）发布**
- 不用 `tools/ThbgmExtractor-1.6.7` 里的 Smdn 组件（GPL 授权含糊），自行实现 fmt 解析

### 曲目表裁定规则（用户确认，2026-09-04）

| 编号 | 规则 | 依据 |
|---|---|---|
| **R1** | 末轨整轨长度以 `start + total == thbgm.dat 文件大小`（Δ=0）为准。参考表末轨普遍少 16 字节（4 帧 / 0.09 ms） | `release/tsa/_bgm_report.md` §3.5：「th07–th20 末轨 start+len == 文件大小 精确成立，Δ=0（全部）」，并点名「早期 +16B 误判」已由 t65 勘误 |
| **R2** | `musiccmt.txt` 有条目时，以游戏本体原文为准（Shift-JIS 原样） | 用户要求「曲名取日语原名」 |
| **R3** | 人工覆写：以**官方最终确定的曲名**为准 —— 游戏内拼写/标点与**商业 CD 收录版**不一致时，**以 CD 为准**；`musiccmt.txt` 无条目的按参考表或用户给定 |

R2 生效后，参考表在 7 首上是「修正版」而非原样，一律采信我方：

- th08 #1 —— 句点：原文 `Eastern Night.`，参考表漏了句点
- th185 #5 —— `バレットフィリア達よ`（参考写成 `パレッドフィリア`）
- th185 #10 —— `ルナティックドリーマー`（参考 `ルナティックドリマー`）
- th19 #12 —— `Kingdom of Nothingness.`（参考 `Kingdam`，参考侧 typo）
- th20 #11/#13 —— 波浪号码位：原文是 **U+FF5E FULLWIDTH TILDE**，参考表用 U+301C WAVE DASH
- th20 #14 —— `ファンタスティックドリフト`（参考 `ドルフト`）

### R3 人工覆写（用户 2026-09-04 审定，已固化进 `gen_tracklist_v2.TITLE_OVERRIDE`）

| 作品# | 采纳曲名 | 原值 | 理由 |
|---|---|---|---|
| th08 #8 | `永夜の報い　～ Imperishable Night` | `…Imperishable Night.` | **该曲收录进商业 CD 时无句点**；同作 #1 `Eastern Night.` 游戏内带句点，保留 |
| th125 #7 | `はたてアンロック` | `th125_07`（缺名） | musiccmt 无条目，取参考表 |
| th128 #6 | `妖精大戦争　～ Fairy Wars` | `…Faily Wars` | `Faily` 系 ZUN 笔误，CD 作 Fairy |
| th19 #23 | `戦闘前会話1` | `Undefined_Object1` | fmt 原名是 ZUN 占位命名，用户给定 |
| th19 #24 | `戦闘前会話2` | `Undefined_Object2` | 同上 |

| **R4** | 用户在 `REVIEW_TITLES.csv` 中**未改动即视为认可** | 用户 2026-09-04：「我没动的那列基本上就是我已经认定好的」 |

### ✅ 曲目表已定稿（2026-09-04）

21 作 / **346 首**，逐作与 `release/tsa/<作>/bgm/` 参考表比对完毕，
**待判 0 条**。共 **36 行**存在差异，全部归入 R1–R4（合计 37 次命中，
一行可能同时命中多条，如 th19 #24 兼有 R3+R1）：

| 规则 | 命中 | 说明 |
|---|---:|---|
| R1 末轨长度 | 15 | 15 作末轨整轨长度比参考多 16B（4 帧 / 0.09 ms），采信我方 |
| R2 musiccmt 原文 | 7 | th08 #1、th185 #5/#10、th19 #12、th20 #11/#13/#14 |
| R3 人工覆写 | 2 | 5 条覆写中 3 条（th08 #8 / th125 #7 / th128 #6）生效后已与参考一致，仅 th19 #23/#24 仍不同 |
| R4 用户认可 | 13 | th13 灵界版 |

### th13 灵界版命名（R4 已认可，2026-09-04）

- 最终采用 **`（霊界トランス）`**（全角括号、无空格，取自 `BgmForAll.ini`）
- 参考表用 ` (霊界バージョン)`（半角括号带空格）—— 不采纳
- 涉及 #3 #5 #7 #9 #11 #13 #15 #17 #19 #21 #23 #28 #30 共 13 首
- 依据：th13 音乐室不单独列出灵界版（musiccmt 仅 17 条主版），
  两种写法均为后人补名，无官方原文；用户未改动即认可

### 索引来源调研（2026-09-04，待用户拍板）

**只有「几何索引」需要配置，音频本体永远运行时读。**
`thbgm.dat` = 16B ZWAV 头 + 裸 PCM，seek 即播，不进任何配置。
但下面这些**无法从裸 PCM 推导**，必须外部给：
`start` / `intro` / `total`（在 `thbgm.fmt`）、
`rate`/`ch`/`bits`（在 fmt 条目的 WAVEFORMATEX 18B）、曲名（在 `musiccmt.txt`）。
→ 而 fmt 与 musiccmt 都藏在**加密的 `thXX.dat`** 里。

**索引体积（346 首，13 字段）**：JSON 89,331 B；gzip 后 14,485 B。小到可忽略。

**三种来源方案**：

| 方案 | 代价 | 风险 |
|---|---|---|
| 外置配置文件 | ≈0 | 文件丢失/被改/与 exe 版本不同步 |
| 内嵌资源（编译进 exe） | ≈0 | 更新索引要重编 |
| 运行时自解 dat | 移植 7 套档案解密 | **授权不成立，见下** |

**⚠️ 授权硬约束（决定性的）**

- `tools/go-brightmoon`：**整个仓库没有任何 LICENSE 文件**
  → 默认「保留所有权利」，**其代码不可移植进播放器**
- 它实现了 7 套档案格式（Hinanawi / Yukari / Yumemi / Kaguya / Marisa /
  Kanako / Suica），非测试代码 **3,191 行 Go**，覆盖 th06–th20
- `tools/thtk` 是 BSD 风格宽松授权（COPYING：「Redistribution and use in
  source and binary forms, with or without modification, are permitted…」），
  可放心参考 —— 但它只覆盖到 th17，**th18+ 的 Kanako/THA1 只有 brightmoon 有**
- → 「运行时自解」在 th18/th185/th19/th20 上无合法蓝本，**否决**

**免解密校验锚点（关键补偿手段）**

即使走配置方案，也能 O(1) 验出「选错游戏 / 游戏被改版」：

1. `thbgm.dat` 头 16B：`5a 57 41 56`（"ZWAV"）+ `01 00 00 00`(version=1)
   + **byte[8..9] = 作品号 BCD** + 6 字节 0
   - byte[9] 主版本：`07 08 09 10 11 12 13 14 15 16 17 18 19 20`
   - byte[8] 小数位：`00`=整数作，`50`=.5(th095/th165/th185)，
     `30`=.3(th143)，`80`=.8(th128)
2. 文件大小 == `16 + Σ 所有轨 total`（20 作实测 **Δ=0**）

两条都不需要任何解密，可在启动/切换作品时瞬间完成。

**✅ 已定（用户 2026-09-04）**：**内嵌 + 外部可覆盖** + **dat 路径可设置**。
详见 `DESIGN.md`（三层配置、路径映射、免解密校验、索引字段）。

**建议（待定）**：内嵌 gzip 索引 + 上述免解密校验；
另提供「从原始 dat 重新扫描」按钮，**以外部进程调用** `tools/` 下已有的
brightmoon.exe / thdat.exe —— 移植代码有授权风险，调用现成二进制则没有。

### 沙箱限制（探环境时踩到）

- PowerShell 工具被安全策略拦（LOLBin）
- Bash 里 `cmd`、`csc` 之类关键词会触发命令拦截
- → 探环境优先用 `ls` + `command -v` + 读目录，别碰 `cmd` / `wmic` / `csc`
