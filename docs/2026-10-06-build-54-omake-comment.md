# build-54：乐评区「表/裏」切换（裏音楽コメント）

日期：2026-10-06
状态：已实现、已构建、自检 50 项全绿；**人工验清单未跑**（见文末）

> **修订 r3（2026-10-06 深夜，用户实机看过 r2 后拍板）**：① 按钮从右上角挪**左上角**
> （右上角盖滚动条）；② 做成**自动收放**——默认向左收成一小条（露右端 5px），鼠标悬停滑出
> （120ms 平移动画，TranslateTransform + CommentPane ClipToBounds 裁剪）；
> ③ **复制场景抑制弹出**：正文有选中文本（`SelectionLength > 0`，覆盖拖选→右键/Ctrl+C 全程）
> 或鼠标左键按着时不弹出，`CommentBox.SelectionChanged` 兜底立刻收回。
> 换曲/重查/清场时按钮回到收起态。数据层与自检不变。

> **修订 r2（2026-10-06 晚，用户实机看过首版后拍板）**：交互从「顶部切换条 + 表/裏双 tab」
> 改为「**单个切换按钮**叠在乐评区右上角」——文字「裏音楽コメント」，高亮 = 启用裏评，
> 点按切换。按钮不占独立行 ⇒ **正文高度一 px 不损失**；代价是盖住正文第一行右端约 95px
> （用户看了实机截图后接受）。实现：`UraToggleStyle`（BasedOn 主题 ToggleSwitch，只覆盖
> Padding 8,1 / FontSize 11 / Height 18）；CommentPane 内 CommentBox 与 UraToggle 同格叠加
> （后声明者在上层）；Checked/Unchecked 共用一个处理器 `UraToggle_Changed`。
> 数据层、生成工具、自检均不变。

## 做了什么

乐评区（主窗口第 5 行右栏）新增显示**裏音楽コメント**（里乐评）的能力：

- **交互**：乐评栏顶部窄切换条，两个互斥 tab（RadioButton + GroupName）——「表」=音乐室通常评论（默认），
  「裏」=おまけ.txt 的隐藏评论。点 tab 即切换正文。
- **每曲重置为表**（用户拍板）：换曲自动回表评。实现靠 RefreshComment 内部「键比较」
  （`gameId#trackNo`），三个调用点（ShowPlayingPanel / Alt_Click / PlayTrack）**零改动**；
  键没变（光标移回正在播放行 / 副版切换）则保持当前表/裏态。停止清场键置 null ⇒ 同曲重播也回表。
- **切换条可见性 = 纯数据驱动**（用户强调）：按 `CommentIndex.HasOmake(gameId, musicNo)` 判定，
  **不按作品白名单硬编码**。当前数据下只有 th06（17/17）/ th07（20/20）/ th08（21/21）有裏评
  ——th06nc 与其余 25 作无此字段，切换条不出现，乐评区与现状逐像素一致。
  将来任何作品补裏评数据 → 生成工具自动透传 → 切换条自动出现，零代码改动。
- 契约不变：不显示曲名（`omake_title_*` 继续丢弃，th07#17 / th08#8 表记差异天然免疫）、
  只读一个字段、无正文整列收起、语言跟随 `CommentIndex.CurrentLanguage`（裏评日/中双全）。

## 数据事实（本次探明）

- 裏评来源：游戏目录おまけ.txt（tsa/kouma=Shift-JIS、youmu=GBK、eiya=UTF-8）+ THBWiki 社区简中译文；
  `docs/musiccmt/musiccmt.json` 每曲早有 `comment_omake_ja/zh` 字段（2026-09-19 整理入库），只是生成链路丢弃。
- 裏评 58 对**全部单段无换行**、无 CRLF；最长 th07#9 = 174 字（≈5-6 行，需滚动）。
- th09 的 Omake 无音乐栏目、th10 起无 omake 文件 ⇒ 裏评是 th06/07/08 三作独有（已查证）。

## 改动清单

| 文件 | 位置 | 改动 |
|---|---|---|
| `tools/make_musiccmt_resource.py` | docstring、~113-140、打印 | v3：透传 omake（换行归一 + 成对 fail-fast + **条件写键**，无 omake 不写键）；打印「裏评论 N 对」 |
| `src/Resources/musiccmt.json.gz` | — | 重跑生成：96,613 → 107,585 B |
| `src/Data/CommentIndex.cs` | CommentEntry、查询区 | +`OmakeJa`/`OmakeZh`（三字段→五字段）；`TryGetOmakeComment`×2 重载、`HasOmake`（切换条可见性口，任一语言非空，不依赖语言设置）；表评取值抽私有 `TryGetText` 共用 |
| `src/MainWindow.xaml` | Window.Resources、第 5 行 | 新键样式 `CommentTabStyle`（RadioButton，视觉抄 ToggleSwitch，Height 18/FontSize 11）；CommentBox 外包 `CommentPane`（Auto+* 两行），Row0 = `OmakeToggleBar`（表/裏 tab）；CommentBox 的 Margin/Visibility 职责上移；列宽注释落点改指 UpdateCommentText |
| `src/MainWindow.xaml.cs` | 448-450、499-617 区域 | 字段 `_commentKey/_commentGameId/_commentTrackNo/_showUraComment`；RefreshComment 重构（键比较重置 + HasOmake 可见性 + tab 程序化同步）；新 `UpdateCommentText`（唯一动正文/列宽处）；`OmoteTab_Checked`/`UraTab_Checked`（null 守卫挡 InitializeComponent 首触）；448-450 清空块抽 `ClearComment()`；+using System.Globalization |
| `src/UI/CommentSelfTest.cs` | Run 列表 +2 方法 | `CheckOmakeCounts`（快照断言 th06=17/th07=20/th08=21、其余 0、无 `\r`——**抓忘重跑生成工具的闸门**）；`CheckOmakeContent`（三作抽查裏≠表、Ja≠Zh、HasOmake 三态） |

## 自检（--viz-selftest，50 项全绿；乐评 6 → 8）

```
[ ok ] 裏乐评计数（th06=17 / th07=20 / th08=21，其余为 0）
         计数 th06=17、th07=20、th08=21；异常 0 处；含 '\r' 0 条
[ ok ] 裏乐评内容（三作抽查裏不同于表 / HasOmake 三态）
         三作抽查异常 0 处；HasOmake：th06#1=True、th09#1(无裏评作品)=True、th11#18(无表评曲)=True
```

构建：`dotnet build src/ThbgmPlayer.csproj` → 0 警告 0 错误（12.4s，bin/Debug/net10.0-windows）。
XAML 契约检查 `.workbuddy/tools/check_xaml.py` 6 文件全过。

## 设计决策记录（为什么这么做）

1. **键比较重置 vs 调用点传参**：RefreshComment 签名不变、三个调用点零改动——同曲重渲染
   （光标移回 / 副版切换 / 同曲重播）与真换曲靠键自查区分，语义集中在一处。
2. **RadioButton vs Button 对 / ToggleSwitch 对**：同 GroupName 免费拿互斥 + 选中态声明式
   （Button 对要代码换样式、ToggleSwitch 对要手写互斥防重入）。主题隐式 RadioButton 是圆点样式
   ⇒ 必须整份换模板的键样式；放 Window.Resources 不进主题（仅此一处，CommentBoxStyle 同款惯例）。
3. **条件写键**：产物只给有 omake 的条目写两个键，58 对 ≈ +23KB 原文（gz +11KB），其余 511 条不背空串。
4. **计数断言放运行时而非工具侧**：gz 是提交的生成物，「改了乐评数据忘重跑工具」只有运行时
   CheckOmakeCounts 抓得住（工具打印只报告不断言）。⚠️ 17/20/21 是数据快照，将来补数据随更新。
5. **「表空裏有」的曲**：当前数据不存在。若将来出现，需把收起判定改「表或裏任一有正文」
   （现为「当前侧有正文」，裏评将不可达）。

## 坑与对策（实现时踩到/规避的）

- **启动期 Checked**：OmoteTab `IsChecked="True"` 在 InitializeComponent 期就触发事件 →
  两个处理器以 `_commentGameId is null` 早退（此时字段全空）。
- **程序化同步 tab 与事件双触发**：设 IsChecked 值变了会走 Checked → UpdateCommentText，
  RefreshComment 再直刷一次——同路径幂等，无害；值没变不触发，直刷兜底「同键重查」的场景。
- **切换条挤正文**：第 5 行内容高 95px，切换条占 ~22px（按钮 18 + 间距 4）→ 正文视口 ~73px（≈4-5 行）。
  已用 Height 18 / FontSize 11 / Padding 10,1 压到最小；174 字长文靠 TextBox 既有 Auto 滚动兜底。
- **tab 文字用单字「表」「裏」**：窄条空间留给正文；「裏音楽コメント」是 ZUN 官方概念，
  东方玩家对单字「裏」无歧义；ToolTip 补全语义（裏 = おまけ.txt 的隐藏评论）。

## 人工验清单（未跑，实机过一遍）

- th06/07/08：切换条出现、默认「表」高亮、「裏」往返切换正文变、换曲自动回「表」；
- th13 副版（霊界版）来回切：乐评不变、表/裏态保持（键相同）；
- th06nc / th09 / th10 等：无切换条，乐评区与现状逐像素一致（1:1 列宽、无评论曲整列收起、右键复制）；
- th07#9（裏评最长 174 字）滚动正常；
- 停止播放后再播同一首：回表评（键置 null 后算新播放）。
