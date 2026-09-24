# build-53：乐评区正文显示（主窗口第 5 行右栏）

日期：2026-09-24
关联：`docs/2026-09-23-build-52-main-waveform.md`（同一行的左栏）、`docs/musiccmt/README.md`（数据侧）

| 项 | 结果 |
| --- | --- |
| 新增 | `src/Data/CommentIndex.cs`、`src/UI/CommentSelfTest.cs`、`src/Resources/musiccmt.json.gz`、`tools/make_musiccmt_resource.py`、本文 |
| 修改 | `MainWindow.xaml`、`MainWindow.xaml.cs`、`ThbgmPlayer.csproj`、`Viz/VizSelfTest.cs`、`README.md`、`docs/README.md`、`docs/2026-09-21-viz-overview.md` |
| `dotnet build` | 成功，0 警告 / 0 错误 |
| `--viz-selftest` | **37 / 37**（33 → 37） |

## 一、需求（用户原话拆解，全部已定）

- 乐评区从占位升级为**正文显示**；数据双语（ja/zh 严格成对），**先做单一语言、默认日语**；
- 文本框**只读、可选择、可复制、带滚动条、四角直角**；
- **多语言留口子**（用户定：将来**做进设置里**，不做外部切换）；
- 底色参考可视化区域（`BgDeep`）、文字白色（`Text`）、字体跟随设置（`Theme.ApplyContentFont`）。

## 二、数据事实（python 实测）

- `docs/musiccmt/musiccmt.json` **409,541 B**；29 作品 / 582 条；`comment_ja` = `comment_zh` = **569 条严格成对**；空对 **13**（原作没写）。
- `musicNo` 是**字符串键**（"1"…"58"）；json 29 键 ↔ 索引 29 个 `GameDef.Id` 一一对应。
- 正文纯文本安全（无 HTML/MD/制表符）；**`\n` 1,280 处（47.8% 含换行）** ⇒ `AcceptsReturn` 必须显式开。
- 副版（灵界版/原典）共享基础曲 `No`，且评论本就复制自基础曲 ⇒ **按 `gameId + No` 查即可，零特例**。

## 三、实现要点

### 数据层 `src/Data/CommentIndex.cs`
- 资源 `musiccmt.json.gz`，静态字段初始化**懒加载**（首次访问才读）；
  ⚠️ 不走静态构造函数（抛异常会固化成 `TypeInitializationException`）；`TryLoad` 全程 try/catch，
  缺资源/坏档**静默降级成空索引**（乐评区不显示而已，不拖死播放器）。
- API：`CommentLanguage { Ja, Zh }` + **`CurrentLanguage`（多语言口子，用户定：将来由设置驱动）** +
  `TryGetComment(gameId, musicNo, [lang], out comment)`。
  「无此作品」与「空正文」**合并返回 false**（UI 动作相同：整列收起；将来要区分只拆这一个方法）。
- 内部 `GameCount`（internal）供自检断言键数。

### 生成工具 `tools/make_musiccmt_resource.py`
- `docs/musiccmt/musiccmt.json` → `src/Resources/musiccmt.json.gz`（紧凑分隔符 + gzip 9）。
- **轻校验失败即退出非 0**：顶层键数 29 且都 `th` 开头；tracks 非空；**ja/zh 成对性**（每条都有或都空）；
  空对数、最长条打印（不硬断言快照数字）。
- ⚠️ **乐评数据更新后必须重跑本工具并提交**（gz 是提交的生成物，与 `tracks*.gz` 同惯例）。

### UI（`MainWindow.xaml`）
- **样式 `CommentBoxStyle` 放 Window.Resources**（不进主题：全项目仅此一处 TextBox；
  主题文件头明言只收通用控件）。
- ⚠️ **主题里没有 TextBox 样式**，默认模板在深色下是白底黑字 ⇒ 必须自写；
  **直角 = 不设 CornerRadius**（默认模板本就无圆角，无需重写模板）。
- 关键 Setter：`IsReadOnly` / **`AcceptsReturn`（多行刚需，代理方案漏了，我补上）** / `TextWrapping` /
  `VerticalScrollBarVisibility=Auto`（内嵌 ScrollBar 自动继承主题 12px 窄条 ✓）/
  `Background=BgDeep` / `Border=1` / `Foreground=Text` / `SelectionBrush=SelectionBg`（窗口本地画刷，
  与 ScrollThumb 同色 `#FF4A4A52`）/ `FontSize=12`。
- 右键菜单：显式深色菜单**只含「复制」**（默认菜单是系统浅色 ✗；观感吃应用级深色隐式 ContextMenu ✓）。
- 字体：`ApplyFont()` 里对 `CommentBox` 调 `Theme.ApplyContentFont` ✓（跟随设置的入口，用户定）。

### 空状态（用户拍板：整列收起）
- 查到正文 ⇒ TextBox 可见、列宽 `2*`；
- **无评论 / 查不到 / 未播放** ⇒ `CommentColumn.Width = 0` + TextBox `Collapsed` ⇒ **波形占满整行**
  （`3*` 列星宽自动占满 ✓；波形收到 SizeChanged 自动重分桶 ✓）。按「隐藏该区域而非留白块」的已定口径。

### 接线（三处挂点）
1. `ShowPlayingPanel`（与封面/波形同位）：`RefreshComment(game.Id, track.No)`
2. 主副版切换（与封面/波形成对）：查询键主副版相同 ⇒ 幂等零成本，为将来副版独立评论留挂点
3. 停止清空（`UpdateVizCover(null)` 旁）：清 Text + 整列收起（别留上一首正文）

## 四、自检（33 → 37）

- **加载**：作品数 ≥ 29；**跨容器抽查** th06 / th075 / th175 / **th06nc** / th13 各探一曲
  （只测一部会漏掉其它解码路径上键对不上的问题 ✗）。
- **查询**：th06#1 命中非空；th11#18（原作无评论）查不到；`th99` 查不到。
- **双语**：th075#3 Ja **188 字** / Zh **152 字**，内容不同；`CurrentLanguage` 默认 Ja。
- **空对**：th11#18 / th125#7 两种语言都查不到。
- ⚠️ ja/zh 成对性的**全量校验在生成工具**（它手里才有原始 json；运行时索引只有选中的字段）。
- ⚠️ **必须人工验**：直角观感、窄条滚动、深色右键复制菜单、13 曲整列收起后波形占满、
  长文（188 字 ≈ 十几行）的滚动与选择复制。

## 五、踩过的点 / 与方案代理的分歧

- **`AcceptsReturn` 漏了** ✗：代理方案没写，而正文近半含换行 —— 多行显示是刚需，必须显式 `True`。
- **csproj 资源行保留 `Condition="Exists"`**：代理建议不加 ✗ —— 与现有三个兄弟资源一致，
  且 gz 缺失时构建照常、运行时静默降级（`TryLoad` 返 null）。
- **成对性校验放生成工具**：运行时索引只嵌了 `comment_ja/zh` 两字段，全量成对性只能对原始 json 验 ⇒
  自检退化为「已知空对运行时查不到」的抽查。

## 六、还没做的

- 多语言：设置页 + 语言切换（多语言支线落地时做，口子已留：`CommentIndex.CurrentLanguage`）。
- 正文较长（最长 188 字）时是否需要字号/行距微调 —— 等实机反馈。


---

## 修订（2026-09-24 20:45）：实机三问题修复

用户实机反馈三条：① 部分乐评匹配错 ② 部分换行疑似丢失 ③ 文本框聚焦有高亮效果不要。

### ① 匹配错（根因 + 修复）

**全量对齐检查**（乐评 json 键 ↔ 播放器三索引的 No，569 条）：**标题不一致 85 条** ✗ ——
乐评支线的「曲目表行序」与播放器的「musicNo 序」在尾部曲目（ED/EX/Staffroll）与霊界版穿插处
**不是同一个序**（例：th08#15 乐评=ヴォヤージュ1970 vs 播放器=竹取飛翔；th13 因 13 条霊界版穿插整体偏移；
th09 从 #2 起错位）。另 13 条（th13 霊界版）在播放器里挂 Alt、不占独立 No。
少数用字/空格差异：th145 半角/全角空格、th155「・」有无、th135「ニッ vs 二ッ」。

**修法（生成工具 v2）**：按**规范化标题**（NFKC + 去全部空白 + 去「・」）把每条乐评重映射到
播放器的 `No`（以播放器三索引为权威 ✓）；已知用字差异走 ALIAS 表（th135 一条 ✓）；
对不上 ⇒ 列清单退出非 0（禁止静默错配）✓；歧义标题 ⇒ 同样报错 ✓；
霊界版等不占独立 No 的条目 ⇒ 计数跳过（评论随基础曲 ✓）。
产物 gz：键 = 播放器 No、**保留 title_ja**（= 播放器侧标题）。乐评支线的源 json **保持不动** ✓。

实测：**569 条全部挂上播放器 No（0 对不上）** ✓；跳过 13 条（全是 th13 霊界版 ✓）；gz 96,613 B。

### ② 换行 —— 数据层证明无损

内嵌 gz 与源 json **582/582 条一致** ✓；含换行 **297 条**全保留；CRLF 0、裸 CR 0 ✓。
⇒ 「换行丢了」最可能是 ① 的错评（拿错评论对照，格式当然不同）✗。修完 ① 请实机复查；
工具顺手加了换行归一化（CRLF/裸 CR → LF，防御 ✓）。自检新增「换行保留」（th06#11 = 2 个换行 ✓）。

### ③ 聚焦高亮 —— 自写极简 ControlTemplate（方案预警的返工点如期出现）

默认 TextBox 模板的 focus VisualState 有系统高亮边框，Setter 管不到 ✗。
改自写极简模板：`Border` + `ScrollViewer x:Name="PART_ContentHost"`（无 focus/hover 视觉状态 ⇒
聚焦时外观不变 ✓）。直角/颜色/滚动条/选择复制照旧 ✓。

### 自检 37 → 39

- 🔑 **对齐校验（新增，防复发闸门）**：遍历内嵌索引**每一条**，断言 title_ja（规范化后）与
  TrackIndex 同 No 的标题一致 —— 实测逐条比对 **569 条、不一致 0** ✓。
  乐评数据或曲目序任何一方漂移，这里当场红 ✗。
- **换行保留**：th06#11（实测 2 个换行）TryGet 返回不缩水 ✓。
- ⚠️ 工具 v2 首跑踩过一次**双重嵌套**（插入时已写 `out[gid]["tracks"]`，组装时又包一层 "tracks" ✗
  ⇒ C# 取不到 ✗）—— 自检当场红抓到 ✓，已修。


---

## 修订 3（2026-09-24 21:5x）：两栏改 1:1 + 数据侧断行/内容修复

### 1. 波形 / 乐评两栏：3:2 → **1:1**
乐评栏太窄会把作者断的 ~30 字行**二次折行**（3:2、窗口约 850px 时每行只放得下约 24 个日文字）。
改 `src/MainWindow.xaml` 两列均为 `1*`。
⚠️ **连带点（必须同步）**：`RefreshComment` 恢复列宽处原写的是 `2*`（3:2 时代的旧值）⇒
只改 XAML 不改代码的话，切到第二首有评论的曲子时列宽会被改成 `2*`、两栏当场变形 ✗。已一并改 `1*`。

### 2. 数据侧：断行还原 + 内容补齐（**逐字终检 0 不一致**）
| 块 | 结果 |
| --- | --- |
| 官方 STG 21 作（可对位 333 条） | 重跑生成后 **断行 333/333 与原作一致**、**逐字内容 333/333 一致** ✓ |
| **th165 #2 / #4** | 补回被吃掉的内容：`＊旧約酒場　～ Dateless Bar "Old Adam" より`（原作 41 字 → 曾只剩 15 字） |
| **th06nc 18 条** | 从**库内** `re_work/assets_nc_2026-09-19/text/localization_full.json` 的 `MD_XX_DESC`（ja + zh-CN 等 12 语言自带换行）还原 |
| 简中 26 → 24 条 | 抽取修好后有 2 条自然对齐 ✓；**其余 24 条仍保留旧样**（中文行数与原作口径不同，待单独处理） |

### 3. ⚠️ 一个方法论教训（重要）
「内容被吃掉」之所以能从我眼皮底下过去，是因为：
**流水线与校验脚本共用同一个抽取函数** ⇒ 两边"错得一致" ⇒ 校验给出一份漂亮的假绿 ✗。
根因是那个抽取用正则抠 JS 字符串，遇到**转义双引号**（`\"`）会静默丢内容 ✗（th165 就是它）。
修法：两处都改成**逐字符状态机**，并且**解析失败时保留原文而不是丢弃**（问题会显式暴露）。
🔑 **校验口径必须与被校验的实现独立** —— 共用一套解析的"自检"等于没有自检。
