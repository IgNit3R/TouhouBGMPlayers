# docs/ —— 文档索引

> bgmplayer 项目的全部文档，**先看这里再翻目录**。
> 建立 2026-09-04（原 `dependence/README.md`）｜**重写为真实索引：2026-09-21**
>
> ⚠️ 本文旧版描述的是**已消失的 `dependence/` 布局**（`01_design/`、`02_data/`、`03_tools/`、`04_source/`）。
> 那套结构在 `2026-09-06-build-28-repo-restructure.md` 里被拆掉，现在是 `design/` + `gen/` + 根目录平铺。

---

## 三条入口

| 想知道什么 | 去看 |
| --- | --- |
| **现在的规矩是什么** | `design/DESIGN_v3.md`（设计定稿）· `design/VERSIONING.md`（版本号规则）· `2026-09-21-project-notes.md`（项目要点明细）· `2026-09-21-viz-overview.md`（可视化总览） |
| **某个改动为什么这么做** | `2026-*-build-N-*.md` 构建日志，按日期找，全索引见 §3 |
| **要数据 / 曲目表 / 循环点** | `tracklist.csv`、`TRACKLIST.md`、`REVIEW.md`、`REVIEW_TITLES.csv`、`COMPARE.md`、`tf_games_meta.md`、`th0*_tracklist_final.csv`、`loop_*.csv`、`循环点审计.xlsx`、`曲目序与循环总表.md` |

## 目录结构

```
docs/
  design/             规格档（6）—— 设计与定稿类，不是日志
  gen/                生成器输出（4）—— 与根下同名文件逐字节相同（已验 md5）
  musiccmt/           音乐室评论（29）—— 27 作 md + README + musiccmt.json
  2026-*-build-*.md   构建日志（52），§3 有全索引
  其余                 现状档 / 待办 / 研究档 / 数据表，见 §2、§4
```

---

## 1. `design/` —— 规格档（6）

| 文件 | 内容 |
| --- | --- |
| `DESIGN_v3.md` | **唯一有效**的设计文档（取代 `DESIGN.md` / `DESIGN_v2.md` / 全部 `_notes`）：硬约束、目录布局、路径设置、播放模型、导出 |
| `VERSIONING.md` | 版本号规则：`x.xx[HEX]` 到 `<Version>` / `<InformationalVersion>` 的映射、`v*` 发布标签约定 |
| `LOG.md` | 工作日志 / 决策日志（含 brightmoon 授权结论与环境发现） |
| `TITLES.md` | 曲名方案与数据源定稿 |
| `SOURCES.md` | 来源核实与修订 |
| `TITLE_EVIDENCE.md` | 曲名差异溯源（裁定规则 R1–R4） |

## 2. 现状 / 总览 / 待办 / 研究（根目录，非 build 前缀）

| 文件 | 日期 | 大小 | 内容 |
| --- | --- | --- | --- |
| `2026-09-21-viz-overview.md` | 09-21 | 7.3 KB | **可视化总览**（M0～M6 完成）+ 文档地图 / 代码地图 / 自检覆盖边界 |
| `2026-09-21-project-notes.md` | 09-21 | 7.0 KB | 项目要点明细（曲序名源 / 黄昏作 / 专辑 / 解码矩阵 / 多语言 / 接入期触点） |
| `2026-09-21-viz-frame-pacing-pending.md` | 09-21 | 16 KB | 待查：可视化的帧率观感（掉帧感） |
| `2026-09-20-plan-visualization-module.md` | 09-20 | 12 KB | 可视化模块**实施方案**（隔离先行，暂不接入主播放器） |
| `2026-09-19-research-localization.md` | 09-19 | 52 KB | 多语言（日本語 / 简体中文）改造调研 |
| `2026-09-19-research-visualization.md` | 09-19 | 20 KB | 音频可视化 / 波形显示前期调研 |
| `2026-09-10-dependency-check.md` | 09-10 | 19 KB | 依赖更新核查（Dependabot）+「关于」对话框依赖表 |
| `2026-09-10-pending-alt-button-label.md` | 09-10 | 5.6 KB | 待办：切版本按钮的文案要按作品区分 |
| `IMPLEMENT_2026-09-06.md` | 09-06 | 9.6 KB | 黄昏作接入实现记录（开工日） |
| `2026-09-06-checkpoint-preload-plan.md` | 09-06 | 1.2 KB | 决策检查点：切曲滞后的预读方案（已定，未实施） |
| `2026-09-06-decision-generator-names.md` | 09-06 | 1.1 KB | 决定：生成器的作品名错误不订正 |
| `2026-09-06-pending-font.md` | 09-06 | 1.5 KB | 待办：字体支持（后由 build 34–37 完成） |
| `2026-09-06-gitignore-update.md` | 09-06 | 0.7 KB | `.gitignore` 按构建输入清单校准 |

## 3. 构建日志（52 份）

### 3.1 2026-09-04 · 奠基期（build 1–12）

| 日志 | 主题 |
| --- | --- |
| `build.md` | 第 1 轮（无编号） |
| `build-2` | 隐式 using 在 WPF 项目里的陷阱 |
| `build-3` | 第一步骨架编译通过 |
| `build-4` | 播放内核（第二步） |
| `build-5` | 修 CS1061 + 迁移过时 API |
| `build-6` | 播放内核编译通过 |
| `build-7` | 修「逻辑位置与文件读指针脱节」 |
| `build-8` | 列表体系 + 设置页（第三步 A） |
| `build-8b` | `Thickness` 没有两参构造 |
| `build-9` | 进度条时间轴按模式区分 |
| `build-10` | 换链交叉淡化失效（切模式时卡滞 / 混乱） |
| `build-11` | 切模式的跳转与残留卡滞 |
| `build-12` | 整轨驻留内存，根治 loop 折返卡滞 |

### 3.2 2026-09-05 ~ 09-06 · 功能补全与 UI 打磨（build 13–37）

| 日志 | 主题 |
| --- | --- |
| `build-13` | 导出音频（第三步最后一块） |
| `build-14` | 导出编译错误（两个签名问题） |
| `build-15` | 按设计文档的全部功能落地 |
| `build-16` | 设置「重启后回到默认值」 |
| `build-17` | 设置读不回来（真正的根因） |
| `build-18` | 设置持久化修复确认 |
| `build-19` | 作品名 + 两处 UI 调整 |
| `build-20` | 深色主题配色 + 滚动条 + 按钮状态 + 下拉框 |
| `build-21` | 菜单白边 / 标签页区分度 / 正在播放高亮 / 关于对话框 |
| `build-22` | 播放高亮被压掉 / 菜单残留 / 标题空格 / 下拉去计数 |
| `build-23` | 分隔线白线（靠截图定位） |
| `build-24` | Slider 样式 + 正在播放区调整 |
| `build-25` | 霊界版按钮挪位 + 激活态紫色 |
| `build-26` | 回车播放 + 多媒体键 / 框选 + 拖动排序 / 输出设备选择 |
| `build-27` | CheckBox / RadioButton 深色样式 |
| `build-28` | **仓库结构重组**（git 风格）—— `dependence/` 布局在此被拆掉 |
| `build-29` | 程序图标 + 预读缓存（三预测源） |
| `build-30` | 去掉 `src\ThbgmPlayer` 中间层 |
| `build-31` | 设置闪退修复 + 流路由 `BuildAsync` 修复（探针诊断） |
| `build-32` | **1.0c**：「应用」即时生效 + 排序延迟 |
| `build-33` | 滚动条拖动被框选手势抢走 |
| `build-34` | 字体支持：系统已安装字体任选 + 文件字体预留入口 |
| `build-35` | 双字体：界面字体（中文 UI）与曲名字体（日文内容）拆分 |
| `build-36` | 双字体编译错误修复（4 处） |
| `build-37` | `ApplyContentFont` 的最终形态（更正 build 36 的说法） |

### 3.3 2026-09-20 ~ 09-21 · 可视化 M0–M7（build 38–51）

| 日志 | 主题 |
| --- | --- |
| `build-38` | M0「骨架 + 入口」 |
| `build-39` | M1「画布 + 取数链路」 |
| `build-40` | M2「五块区域渲染器落地」 |
| `build-41` | M3「窗口几何」 |
| `build-42` | M4「节拍与语义收口」 |
| `build-43` | 渲染路线核查 + 渲染开销审计（M2 遗留的一次回头查） |
| `build-44` | M5「切换判定」 |
| `build-45` | M6a「接入 · 贴附路径打通」 |
| `build-46` | M6a 实机复验后的四处修复 |
| `build-47` | 淡影期「往小跳变」与相位表初值 |
| `build-48` | 暂停语义的终点改成 0（方案 §2 修订） |
| `build-49` | M6b 第一步 —— 抽出 `VizSurfaceHost` |
| `build-50` | M6b 内嵌（方案 §十 落地） |
| `build-51` | **封面（M7）** —— 每作一张、固化进程序集 |

> 2026-09-07 ~ 09-19 没有构建日志，对应黄昏作接入收尾（见 §2 `IMPLEMENT_2026-09-06.md`）与两篇调研（§2）。

## 4. 数据表

| 文件 | 大小 | 内容 |
| --- | --- | --- |
| `tracklist.csv` | 50.8 KB | tsa 侧 346 首，19 字段（start / intro / total / rate / ch / bits…）——**内嵌索引的数据源** |
| `TRACKLIST.md` | 51.6 KB | tsa 侧 21 作曲目总表（人类可读） |
| `REVIEW.md` | 39.8 KB | tsa 侧 21 作分节核对表 |
| `REVIEW_TITLES.csv` | 29.2 KB | 曲名核对表 |
| `COMPARE.md` | 7.0 KB | 与 `release/tsa/<作>/bgm` 参考表逐作比对 |
| `tf_games_meta.md` | 2.9 KB | tf 系作品元数据（P1 生成脚本输入） |
| `th075/105/123/135/145/155/175_tracklist_final.csv` | 2.1–8.2 KB | 黄昏作 7 部定稿曲目表（230 条）→ 生成 `tracks.tf.json.gz` |
| `曲目序与循环总表.md` | 19.9 KB | 黄昏作曲目序与循环总表（源 = 内嵌索引 `tracks.tf.json.gz`） |
| `loop_audit.csv` / `loop_delta.csv` | 28.2 / 17.5 KB | 循环点审计（2026-09-07） |
| `循环点审计.xlsx` | 47.3 KB | 同上，表格版 |

## 5. `musiccmt/` —— 音乐室评论（29）

| 文件 | 说明 |
| --- | --- |
| `README.md` | 整理说明（官方 STG 21 作） |
| `musiccmt.json` | 312 KB，汇总数据 |
| `th06` `th06nc` `th07` `th075` `th08` `th09` `th095` `th10` `th105` `th11` `th12` `th125` `th128` `th13` `th135` `th14` `th143` `th145` `th15` `th155` `th16` `th165` `th17` `th18` `th185` `th19` `th20` | 27 份作品评论（`.md`）。⚠️ `th145` / `th155` 两份是「音乐室**信息**」而非逐曲评论 |

## 6. 注意事项

- ⚠️ **build-28 之前的日志里的路径已失效**。那时写的是 `dependence/01_design/`、`02_data/`、`03_tools/`、`04_source/`、`_gen/`，
  现在对应 `design/`、根目录平铺、`../tools/`、根目录平铺、`gen/`。那些日志是**当时的快照，不回改**。
- ⚠️ 同理，旧日志与 `design/` 里的版本串（`1.6nc` 等）是**历史测量值**，见 `design/VERSIONING.md` §5。
- `gen/` 是**生成器输出位**：4 份文件与根下同名文件**逐字节相同**（md5 实测一致），是 `03_tools` 时代的脚本输出。
  要改数据请改根下那份定稿或重跑生成器，**别在 `gen/` 里手改**。
- 构建日志编号连续递增（`build` → `build-51`，其中 `build-8b` 是插入的补丁轮）。
  **只增不改写**——`2026-09-04-build.md:3` 记着这条红线：「已有文件一律不改写，记录只写进新文件」⇒ 新日志请接在末尾。
- 生成器 / 校验脚本不在本目录，在 `../tools/`。其中**读 `docs/source/` 输入缓存的那组**
  （`gen_tracklist_v2`、`gen_review`、`verify_report`、`probe_th06*`）**大概率已弃用**：
  输入缓存（`thbgm.fmt` / `musiccmt.txt` / `BgmForAll.ini`）自 2026-09-21 起不再随仓库提供，
  各脚本文件头带 ⚠️ 标记。脚本输出位是 `gen/`。
