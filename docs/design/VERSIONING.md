# bgmplayer 版本号规则

> 状态：**已生效**（用户 2026-09-21 口述，同日确认起算点与多字母分支）。
> 适用范围：`src/ThbgmPlayer.csproj` 的 `<Version>` 与 `<InformationalVersion>`，以及 `v*` 发布标签。
> 最后更新：2026-09-24（§6 第 4 条：构建改为助手可代跑）

---

## 1. 总则

版本号**由用户手写**，不进任何自动流程。

- **触发时机**：只有**用户写下一个新版本号**时才变动。助手与 CI **都不主动 bump**。
- `[HEX]`（字母部分）**由用户自定**，没有固定含义表 —— `a` / `k` / `nc` 都是当时取的代号。
- 助手**不得代选** HEX，也**不得**自行推断三段版号的第三段（见 §3；多字母必须先问）。

---

## 2. 书写形式

```
x.xx[HEX]
```

| 部件 | 含义 | 例 |
| --- | --- | --- |
| `x.xx` | 两段数字（主 / 次） | `1.7` |
| `[HEX]` | 用户自定的字母串，**1 个或几个字母** | `a`、`k`、`nc` |

对应 csproj 两个字段，分工固定：

| 字段 | 取值 | 说明 |
| --- | --- | --- |
| `<InformationalVersion>` | 用户写的**原样**字符串 | 可带字母；「关于」对话框读的就是它 |
| `<Version>` | **三段纯数字** `a.b.c` | 程序集 / 文件版本；`System.Version` 解析，写不进字母 |

证据：

- `src/ThbgmPlayer.csproj:24-25` —— 两个字段（含分工注释）
- `src/Core/AppPaths.cs:40-45` —— `AppVersion` 读 `InformationalVersion`，数字版只作兜底
- `src/UI/AboutDialog.cs:241` —— 对话框显示 `版本 {AppPaths.AppVersion}`
- `src/ThbgmPlayer.csproj:28` —— `IncludeSourceRevisionInInformationalVersion=false`，不让 SDK 往显示串后面追加 git 哈希

`<Version>` 会同时决定 `AssemblyVersion` 与 `FileVersion`（生成物 `obj/<cfg>/net10.0-windows/ThbgmPlayer.AssemblyInfo.cs:15,19`），
所以 exe 属性页里的版本也走这条。

---

## 3. 第三段 `c` 的推导

| HEX 形态 | 规则 |
| --- | --- |
| **单个字母** | **字母表位置 − 1**（**a = 0 起算**，把 a 当 0 号） |
| **多个字母** | **不推导** —— 助手**当场询问用户**第三段填几，由用户报数字 |
| 无 HEX（如 `1.8`） | 取 `0` |

字母 → 数字速查：

```
a  0   b  1   c  2   d  3   e  4   f  5   g  6   h  7   i  8   j  9
k 10   l 11   m 12   n 13   o 14   p 15   q 16   r 17   s 18   t 19
u 20   v 21   w 22   x 23   y 24   z 25
```

前两段**永远照抄**用户写的数字，不参与推导。

### 示例

| 用户写 | `<InformationalVersion>` | `<Version>` |
| --- | --- | --- |
| `1.7a` | `1.7a` | `1.7.0` |
| `1.0c` | `1.0c` | `1.0.2` |
| `1.3k` | `1.3k` | `1.3.10` |
| `1.6nc` | `1.6nc` | 多字母 → 问用户（当时填的是 `1.6.0`） |
| `1.8` | `1.8` | `1.8.0` |

---

## 4. 发布标签

- 发布由 `.github/workflows/release.yml` 驱动，**只在推送 `v*` 前缀标签时触发**（`release.yml:5-6`）
  → 标签因此一律带 `v`，如 `v1.7a`。
- 推标签会构建 `win-x64` / `win-arm64` 两个 ZIP，生成 `SHA256SUMS.txt` 并创建 GitHub Release。
- ⚠️ **历史遗留不算错**：`1.0` / `1.1` 两个老标签没有 `v` 前缀、触发不了这个工作流，
  但**当时的产物是手工 build 并已发布 release 的**（用户 2026-09-21 明确）
  → **不要补前缀、不要重打**。

---

## 5. 不改的地方

- **历史测量记录里的旧版本串永不回改**。`docs/2026-09-10-dependency-check.md`、
  `docs/2026-09-20-build-39-viz-m1.md`、`src/bin/Debug/net10.0-windows/viz-selftest.txt` 里的 `1.6nc`
  是**那次运行的环境快照**，改它等于篡改测量。
- **git 历史**里的旧组合不回改（`1.0c` 当时配 `1.0.0`、`1.3k` 配 `1.3.0`）。
  也就是说：**本规则自 2026-09-21 起生效，此前第三段恒为 `0`**。
- ⚠️ **主窗口标题栏的「版本1.6nc」不是程序版本号**，那是 **th06nc 这个作品的名字**（`GameDef.name`）。别顺手改。

---

## 6. 操作清单（收到一个新版本号时）

1. 核对形式是 `x.xx[HEX]`；**HEX 为多字母则先问第三段**。
2. 改 `src/ThbgmPlayer.csproj` 的两行：`<Version>` 与 `<InformationalVersion>`。
3. 若 README 有「当前版本」行（`README.md:6`），同步。
4. **构建**：助手可**直接代跑**（2026-09-23 起的惯例，产物落 `bin/Debug` —— 用户就是从那儿跑程序的）。
   ⚠️ **提交与打 `v*` 标签仍由用户自行执行**（对外动作，助手不碰）。
5. 产物核对：`obj/<cfg>/net10.0-windows/ThbgmPlayer.AssemblyInfo.cs` 里两个特性应对上新值
   （Debug 与 Release 是两套 obj，改完只重建一个配置时另一个仍是旧值，属正常）。
