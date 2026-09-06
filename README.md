# 东方ProjectBGM播放器（thbgmplayer）

运行时直接读取原始 tsa 游戏数据（thbgm.dat / TH06 的 bgm 目录）的 BGM 播放器，
不走预提取资产路线。曲名使用 musiccmt.txt 的原始日文名。

## 目录结构

```
├── src/                WPF 程序本体（.NET 10，csproj 直接在这层，bin/obj 挂在 src 下）
│                       Resources/tracks.json.gz 是内嵌曲目索引
├── design/             设计文档（DESIGN_v3.md 及其佐证材料）
├── data/               曲目数据：tracklist.csv（索引的唯一事实来源）、审校记录、
│                       _gen/ 中间产物；source/ 是游戏提取的原始素材（**git 忽略**，
│                       游戏资产有版权，且随时可重新生成）
├── tools/              维护脚本（见下）
├── assets/             图标等素材
└── docs/               构建日志、决策记录、检查点
```

## 构建

```
cd src
dotnet build
```

产物在 `src/bin/`。程序是便携式的：设置、收藏、播放列表、导出的 wav
都写在 exe 旁边，不写 AppData。删 bin 前注意备份这四项。

## 维护脚本（tools/）

| 脚本 | 用途 |
|---|---|
| `check_src.py` | 源码静态自检（7 项：XAML 解析、括号、构造实参数、事件绑定、x:Name 核对、STJ 构造、残留符号）。每次改完 UI/代码后跑 |
| `csv_to_tracksjson.py` | 从 `data/tracklist.csv` 重新生成内嵌索引 `src/Resources/tracks.json.gz` |
| `extract_game_names.py` | 从各游戏自带 おまけ.txt 首行提取正式作品名（对照用） |
| `make_icon.py` | 从 `assets/icon/bgmplayer-icon-2048.png` 生成多尺寸打包的 `bgmplayer.ico`（16~256 七档） |

其余 `probe_*` / `gen_*` / `compare_*` / `verify_*` 是历史一次性脚本，路径常量已失效，要用先改路径。

## 数据流

```
data/tracklist.csv  ──csv_to_tracksjson.py──>  src/Resources/tracks.json.gz
                                                      │  EmbeddedResource (LogicalName=tracks.json.gz)
                                                      ▼
                                              运行时内嵌读取（TrackIndex）
```
