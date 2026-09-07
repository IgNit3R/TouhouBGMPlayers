# tf 系作品元数据（P1 生成脚本输入）

> 2026-09-06 用户逐条确认。作品顺序与全称均已拍板，P1 按「编号序」+ 本 NAME 表构建 games 列表。

## 一、作品顺序（编号序，用户确认）

7 部黄昏作按编号插入主系列列表（th06–th20，编号序；编号序 ≠ 严格发售日序，已知晓并采用编号序）：

```
th06 → th07 → th075 → th08 → th09 → th095 → th10 → th105 → th11
→ th12 → th123 → th125 → th128 → th13 → th135 → th14 → th143 → th145
→ th15 → th155 → th16 → th165 → th17 → th175 → th18 → th185 → th19 → th20
```

实现要求：生成脚本里**写死显式顺序表**，不做运行时字符串排序。

## 二、全称 NAME 表（7 部）

分隔符规范（与曲名规范一致）：**`～` 前全角空格（U+3000），后半个角空格**。

| id | code | name（全称） | 来源 | 编码 |
|---|---|---|---|---|
| th075 | TH07.5 | 東方萃夢想　～ Immaterial and Missing Power. | 上海アリス通信（用户指定源；exe 原文空格为半角，按规范取全角前导） | — |
| th105 | TH10.5 | 東方緋想天　～ Scarlet Weather Rhapsody. | `tf/th105/Omake（日文版）.txt` 首行 | UTF-8 |
| th123 | TH12.3 | 東方非想天則　～ 超弩級ギニョルの謎を追え | 用户指定（手册丢失过；本作官方副标题即日文，无英文部分）。**编号更正（2026-09-07 用户指正）：非想天則官方编号是 TH12.3，此前误写 TH12.5** | — |
| th135 | TH13.5 | 東方心綺楼　～ Hopeless Masquerade. | `tf/th135/おまけ.txt` 首行 | **GBK** |
| th145 | TH14.5 | 東方深秘録　～ Urban Legend in Limbo. | `tf/th145/omake(utf8-jp).txt` 首行 | UTF-8 |
| th155 | TH15.5 | 東方憑依華　～ Antinomy of Common Flowers. | `tf/th155/omake.txt` 首行（CP932）；omake 原文 ～ 后为全角空格，**用户拍板归一为后半角** | CP932 |
| th175 | TH17.5 | 東方剛欲異聞　～ 水没した沈愁地獄 | `tf/th175/omake.txt` 首行（CP932）；**用户拍板用日文副标题** | CP932 |

### th175 副标题说明（用户口述，留档）
官方副标题 = OP 曲名 = **「水没した沈愁地獄」**，两者本就是同一个名字（ZUN 亲笔 2021/10 あとがき可证）。
游戏发售初期 OP 曲名隐藏防剧透，社区早期看不到副标题，「Sunken Fossil World」是**当时社区自行补的英文名，非官方**——故不采用。
主标题切分（ShortName）不受影响：切出的主标题是「東方剛欲異聞」。

## 三、字段对接

- `GameDef.Name` ← 上表 name；`GameDef.Code` ← 上表 code（TH07.5 等）。
- `ShortName` 现有逻辑（认 U+301C/U+FF5E）已兼容，无需改。
- 主系列 346 条内嵌索引不变；tf 索引为独立 `tracks.tf.json.gz`（EmbeddedResource，LogicalName 固定）。
