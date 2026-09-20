# 音乐室评论整理资料（官方 STG 21 作）

- 来源：`E:\GitWorkspace\thcrap\repos\thpatch` 的 `lang_ja` / `lang_zh-hans`
  - 简中曲名：`themes.js`；各作评论：`<thXX>/musiccmt.js`
  - 日文曲名与音乐室编号：项目 `docs/tracklist.csv`（musiccmt.txt 原文）
- 对齐键：游戏内音乐室号 `music_no`（th07 等 fmt 物理顺序与音乐室顺序不同，已按音乐室号对齐）
- 每作一个 md，按播放器曲目顺序列出；`musiccmt.json` 为按音乐室号的机读汇总
- 已知结构性事实：
  - th11–th18 音乐室 #18 `プレイヤーズスコア` 游戏内无评论（thpatch 亦无）
  - th125 #7 `はたてアンロック` 无评论
  - th13 霊界トランス版 13 首不在音乐室，评论随基础曲
  - th19 戦闘前会話1/2、th143/th20 プレイヤーズスコア 不在音乐室
  - th06 简中评论：thpatch 主仓缺失，**已从 thaddons 拷贝补齐 17/17**（`F:\SteamLibrary\steamapps\common\thaddons\repos\thpatch\lang_zh-hans\th06\musiccmt.js`）
  - th07 #14 `ボーダーオブライフ` 与 #17 `妖々跋扈　～ Who done it!` 评论首句为 `？？？のテーマです。`——原作音乐室写死的防剧透写法（藏幽幽子/Phantasm 面，已对照原作与 wiki 查证），日/简中均如此，按原文保留，非数据缺失

## 核验总表

| 作品 | 日文标题 | 曲目行数 | 音乐室条目 | 日文评论 | 简中评论 | 简中曲名 | 无评论条目 | 核验 |
|---|---|---:|---:|---:|---:|---:|---|---|
| th06 | 東方紅魔郷　～ the Embodiment of Scarlet Devil | 17 | 17 | 17 | 17（thaddons） | 17/17 | - | OK（简中评论取自 thaddons 拷贝） |
| th07 | 東方妖々夢　～ Perfect Cherry Blossom | 20 | 20 | 20 | 20 | 20/20 | - | OK |
| th08 | 東方永夜抄　～ Imperishable Night | 21 | 21 | 21 | 21 | 21/21 | - | OK |
| th09 | 東方花映塚　～ Phantasmagoria of Flower View | 19 | 19 | 19 | 19 | 19/19 | - | OK |
| th095 | 東方文花帖　～ Shoot the Bullet | 6 | 6 | 6 | 6 | 6/6 | - | OK |
| th10 | 東方風神録　～ Mountain of Faith | 18 | 18 | 18 | 18 | 18/18 | - | OK |
| th11 | 東方地霊殿　～ Subterranean Animism | 18 | 18 | 17 | 17 | 18/18 | 18 | OK |
| th12 | 東方星蓮船　～ Undefined Fantastic Object | 18 | 18 | 17 | 17 | 18/18 | 18 | OK |
| th125 | ダブルスポイラー　～ 東方文花帖 | 7 | 7 | 6 | 6 | 7/7 | 7 | OK |
| th128 | 妖精大戦争　～ 東方三月精 | 10 | 10 | 10 | 10 | 10/10 | - | OK |
| th13 | 東方神霊廟　～ Ten Desires | 31 | 18 | 17 | 17 | 18/18 | 18 | OK |
| th14 | 東方輝針城　～ Double Dealing Character | 18 | 18 | 17 | 17 | 18/18 | 18 | OK |
| th143 | 弾幕アマノジャク　～ Impossible Spell Card | 10 | 9 | 9 | 9 | 9/9 | - | 音乐室外: プレイヤーズスコア |
| th15 | 東方紺珠伝　～ Legacy of Lunatic Kingdom | 18 | 18 | 17 | 17 | 18/18 | 18 | OK |
| th16 | 東方天空璋　～ Hidden Star in Four Seasons | 18 | 18 | 17 | 17 | 18/18 | 18 | OK |
| th165 | 秘封ナイトメアダイアリー　～ Violet Detector | 8 | 8 | 8 | 8 | 8/8 | - | OK |
| th17 | 東方鬼形獣　～ Wily Beast and Weakest Creature | 18 | 18 | 17 | 17 | 18/18 | 18 | OK |
| th18 | 東方虹龍洞　～ Unconnected Marketeers | 18 | 18 | 17 | 17 | 18/18 | 18 | OK |
| th185 | バレットフィリア達の闇市場　～ 100th Black Market | 10 | 10 | 10 | 10 | 10/10 | - | OK |
| th19 | 東方獣王園　～ Unfinished Dream of All Living Ghost | 24 | 22 | 22 | 22 | 22/22 | - | 音乐室外: 戦闘前会話1；音乐室外: 戦闘前会話2 |
| th20 | 東方錦上京　～ Fossilized Wonders | 19 | 18 | 18 | 18 | 18/18 | - | 音乐室外: プレイヤーズスコア |

## 追加：東方紅魔郷：New Classic（th06nc，2026-09-19 整理）

- 来源：`re_work/assets_nc_2026-09-19/text/localization_full.json`（ja / zh-CN），顺序按 `musiccmt_utf8.txt`
- 音乐室 18 曲 = 紅魔郷原 17 曲全收录 + 新增 No.16 `スカーレットデビルは遅れて顕れる`（Extra Phantom 主题曲）
- 日/简中评论 18/18 全齐；bgm 文件为 `data/bgm/th06_01..18.opus`（游戏内部沿用 th06_ 命名）
- 详见 `th06nc.md`；机读数据已并入 `musiccmt.json` 的 `th06nc` 键

## 追加：裏音楽コメント（th06/th07/th08，2026-09-19 整理）

- 来源（日文）：游戏目录 `おまけ.txt`（`E:\GitWorkspace\thworks\tsa\kouma|youmu|eiya`）
- 来源（简中）：THBWiki 社区译文（用户复制提供），文中来源页的脚注编号（9–14 共 6 处）已清理
- 仅这三作有裏音楽コメント（曲名由来自评）：已查证 th09 的 Omake 无音乐栏目，th10 起 tsa 各作目录无 omake 文件
- 条目数与音乐室一一对应：th06 17/17、th07 20/20、th08 21/21，日/简中全齐
- 已并入 `musiccmt.json`：每曲 `comment_omake_ja` / `omake_title_ja` / `comment_omake_zh` / `omake_title_zh`，作品级 `comment_omake_source`；md 每曲追加日/简中两个裏音楽コメント小节
- omake 曲名与游戏音乐室表记的两处差异（md 内已标注）：
  - th07 #17：omake 作 `妖々跋扈　～ Who done it?`，游戏音乐室作 `Who done it!`（THBWiki 译文随游戏表记用 `!`）
  - th08 #8：omake 作 `永夜の報い　～ Imperishable Night.`（句尾有点），游戏无点
- 注意源文件编码混杂（本机历史原因）：kouma=Shift-JIS、youmu=GBK、eiya=UTF-8，已分别正确解码

## 追加：黄昏（tasofro）格斗作（th075–th175，2026-09-19 整理）

- 来源（th135/th145/th155）：`E:\GitWorkspace\thworks\release\tf\` 解包产物中的游戏数据 `data/bgm/bgm.csv`（TFCS 容器：`TFCS\0` + u32 压缩长 + u32 原长 + zlib@13；内层为长度前缀二进制行格式，字符串 cp932，th135c 为 GBK）
- **游戏内有真正乐评的只有 th135（心綺楼）**：21 首音乐室曲全部带编曲者自评（あきやまうに 19、ＺＵＮ 2），仅日文。⚠️ 原 th135c（官方简中本地化版）的评论简中译文**已删除**（用户判定需更换），待补充新资料；简中曲名仍保留官方简中版表记
- th145（深秘録）：comment 列全空——本作音乐室只有曲名+编曲署名，无评论
- th155（憑依華）：comment 列只记演奏者名单（Guitar/Bass/Piano 等 credits），无散文评论
- th175（剛欲異聞）：**游戏内无乐评**（music.json/musicroom.nut 仅曲名+空 hint）——`musiccmt.json` 中为 stub（`comment_source:"none"` + `note`）

## 追加：th075 / th105 / th123 结论修正（2026-09-19，用户提供来源）

- **th075（萃夢想）有完整乐评**：来源 `pushfiles/th075/th075.dat/musicroom.dat/tracklist.csv`（musicroom.dat 解包产物，UTF-8）——34 曲全带编曲者评论（NKZ 15、Ｕ２ 16、ＺＵＮ 3），仅日文；csv 中评论以 `\n` 转义存储，末行为署名。已按项目曲目表（文件名 join，34/34 全命中）生成 `th075.md` 并入 json 键 `th075`
  - ⚠️ 修正此前结论：release 格式报告曾判 musicroom.dat「高熵未解密」，实际 pushfiles 已有其解包产物
- **th105（緋想天）音乐室本身无评论，但 omake 有 3 首**：游戏目录 `tf/th105/Omake（日文版）.txt`（UTF-16LE）「■２．曲のコメント」——原文注记「今回は音楽室でコメントが無いのでここで」；仅 3 首 ZUN 新曲（#13 黒い海に紅く / #15 有頂天変 / #16 幼心地の有頂天），仅日文。⚠️ 原 `Omake（汉化版）.txt`（GBK）的简中译文**已删除**（用户判定需更换），待补充新资料。已生成 `th105.md`（3 首日文评论 + 31 曲一览表）并入 json 键 `th105`（其余 28 首仅元数据无评论字段）
- **th123（非想天則）经用户查证确无乐评**：json 保持 stub（`comment_source:"none"`），note 已更新为用户查证结论
- 曲目对齐：以项目 `docs/th1?5_tracklist_final.csv` 的 `order` 列（播放器曲序）为准，按音频文件名连接，全部命中无缺失
- 产物汇总：`th075.md`（34 曲日文评论）、`th105.md`（3 曲日文 + 一览，简中待补）、`th135.md`（21 曲日文 + 12 首番外表，简中待补）、`th145.md`（30 曲署名表）、`th155.md`（58 曲署名+credits）；json 键 `th075/th105/th135/th145/th155` + th123/th175 两个 stub
- 注意：bgm.csv 的 `track_no` 字段**不是**音乐室显示顺序（th135 本体与补丁间编号有漂移），排序一律用项目曲目表 order

