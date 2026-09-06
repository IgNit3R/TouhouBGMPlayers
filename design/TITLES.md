# 曲名方案与数据源定稿

> 日期：2026-09-04
> 需求：**曲名取日语原名**
> 来源限定：thwiki / thprac / thcrap（社区）；本地以 `tools/` 与 `tsa/`、`tf/` 原始文件为准

---

## 一、曲名方案：双源互补，均以日语原名呈现

实测发现 `musiccmt.txt` **并非每作都有**，因此分两路获取曲名，两者在重叠区间可交叉校验。

| 覆盖范围 | 曲名来源 | 编码 | 位置 |
|---|---|---|---|
| **th11 / th12 / th125 / th128 / th13 / th14 / th143 / th15 / th16 / th165 / th17 / th18 / th185 / th19 / th20**（15 作） | `musiccmt.txt` | Shift-JIS | 在原始 `thXX.dat` 内 |
| **th06 / th07 / th075 / th08 / th09 / th095 / th10**（7 作） | `bgminfo/thXX.bgm` 的 `name_jp` | UTF-8 BOM | `tools/musicroom/.../bgminfo/` |

> 早期 7 作的 dat 内**没有**曲名文件（已确认 th08 的 dat 只有 `thbgm.fmt`），必须依赖 bgminfo。

### 源 A：`musiccmt.txt`（优先）

Shift-JIS，结构清晰：

```
@bgm/th18_01
No. 1  虹の架かる幻想郷
♪虹の架かる幻想郷
　
　タイトル画面のテーマです。
```

解析规则：
- `@bgm/<name>` → 曲目 key，**与 `thbgm.fmt` 的 name 字段直接对应**（`th18_01.wav` ↔ `th18_01`）
- `No. <N>  <标题>`（两空格分隔）→ **音乐室编号 + 日语原名**，标题取自此处
- `♪<标题>` → 曲名的二次出现，可作兜底
- `　` 开头的缩进行 → ZUN 的音乐室评论（可做成"曲目详情"面板）

**关键价值**：`No. N` 就是音乐室编号，一举解决"物理序 ≠ 音乐室序"的问题，无需再用偏移去猜。

### 源 B：`bgminfo/thXX.bgm`（早期作品 + 交叉校验）

INI 风格，UTF-8 with BOM：

```ini
[game]
name = "東方永夜抄　～ Imperishable Night"
packmethod = 2
bgmfile = "thbgm.dat"
zwavid_08 = 0x00
zwavid_09 = 0x08

[01]
name_jp = "永夜抄　～ Eastern Night"
position = "0x00000010, 0x000F1AC0, 0x00C20500"
name_en = "Eternal Night Vignette ~ Eastern Night"
comment2_jp = "(Music Room)\nタイトル画面テーマです。..."
[update]
wikipage = "Imperishable_Night/Music"
wikirev = 360480
```

- `name_jp` = **日语原名**（用户需求）
- `comment2_jp` = ZUN 音乐室评论原文（日语）
- `wikipage` / `wikirev` 字段标明数据来自 Touhou Wiki 及具体修订号 —— 与 thwiki 来源要求相衔接
- 覆盖 **th01~th20 全部正作**（另含 PC98 五作、商业 CD、黄昏边境各作）

### 交叉校验结果（已实测）

th18 前 5 曲，两个独立来源**逐字一致**：

| # | musiccmt.txt | bgminfo th18.bgm |
|---|---|---|
| 1 | 虹の架かる幻想郷 | 虹の架かる幻想郷 |
| 2 | 妖異達の通り雨 | 妖異達の通り雨 |
| 3 | 大吉キトゥン | 大吉キトゥン |
| 4 | 深緑に隠された断崖 | 深緑に隠された断崖 |
| 5 | バンデットリィテクノロジー | バンデットリィテクノロジー |

建议在播放器启动时对重叠作品做一致性检查，不一致则告警并优先采信 `musiccmt.txt`（游戏本体的权威数据）。

---

## 二、`position` 三元组 ≡ `thbgm.fmt`（重要等价关系）

bgminfo 的 `position` 是三个十六进制值 **(start, intro_len, loop_len)**，与 `titles_th` 格式同构，也与 fmt 字段等价：

```
start     = fmt +0x10（绝对偏移）
intro_len = fmt +0x18
loop_len  = fmt +0x1C - +0x18
```

**实测验证（th08）**：`0x10 + 0xF1AC0 + 0xC20500 = 0xC20510`，与下一轨 `[02]` 的起始偏移**完全吻合**。

这提供了三重独立校验手段：fmt、bgminfo position、titles_th 三者可互验，任何一处的解析错误都会立刻暴露。

### 附带收获：`zwavid` 可用于文件校验

`thbgm.dat` 头 16 字节中第 9、10 字节是作品标识（实测 th18 为 `00 18`），而 bgminfo 的 `zwavid_08` / `zwavid_09` 正是它的期望值（th08 → `0x00, 0x08`）。
播放器打开 `thbgm.dat` 时可据此**校验版本是否匹配**，防止用户选错目录或版本不符。

---

## 三、thprac 的参考价值（诚实评估）

`github.com/touhouworldcup/thprac`（东方练习工具）**不含曲名数据**：其核心本地化文件 `loc_json.cpp` 中 `bgm` 仅作为**整数 ID** 出现（`int bgm_id`），用于把游戏段落（关卡/符卡）关联到 BGM 编号，没有任何曲名字符串。

它真正能提供的：

- **「段落 ↔ BGM 编号」映射** —— `section_t { int bgm_id; ... }`，即哪首曲子出现在哪一关/哪张符卡
- 三语（zh/en/ja）的关卡名与符卡名结构，以及术语表 `glossary`
- 各作的游戏版本定义

可作为**增值功能**：在播放器里显示"该曲出现于 Stage 3 道中 / ◯◯ 的符卡"，不做曲名来源。

---

## 四、最终数据模型（定稿）

```
曲目 = {
    game,                 # th18
    key,                  # th18_01        (fmt name / musiccmt @bgm/)
    title_ja,             # 虹の架かる幻想郷  ← 日语原名，优先 musiccmt，回退 bgminfo
    music_room_no,        # 1              (musiccmt "No. N" / bgminfo [NN])
    comment_ja,           # ZUN 音乐室评论   (可选展示)
    start, intro_len, loop_len,   # 播放三元组
    pcmf { rate, bits, ch },      # 44100/16/2，th13 灵界版 22050
    section_hint,         # 出现场景        (thprac bgm_id，可选)
}
```

播放：（`thcrap_bgmmod` 的 `track_t` 抽象）
```
intro 段 = [start, start + intro_len)                     播一遍
loop  段 = [start + intro_len, start + intro_len + loop_len)  视为无限长
```

---

## 五、仍待确认

- 播放器形态（单文件 HTML / Python 桌面）
- 一期范围（建议 tsa 21 作；th06 需单独分支——其循环点在 `*.pos`，单位为样本）
- `musiccmt.txt` 在 th13 灵界版（`*_b`）上的编号对应关系，需单独核对
