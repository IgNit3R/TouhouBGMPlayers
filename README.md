# 东方ProjectBGM播放器（thbgmplayer）

Windows 平台的东方 Project BGM 播放器。覆盖 **TH06–TH20 共 21 部整数正作**，
**运行时直接读取游戏原始数据文件**（`thbgm.dat`，TH06 为 `bgm\th06_NN.wav`），
不预提取音频资产。曲名使用各作 `musiccmt.txt` 里的原始日文名，循环点全部内嵌。

当前版本：**1.0a**

## 特性

- **直读原始数据**：运行时按内嵌字节索引从 dat 里取 PCM，循环折返零磁盘 IO（整轨驻留内存）
- **三种循环模式**：无限循环 / 普通（intro → loop×N → 额外 X 秒 → 淡出 F 秒）/ 随机，参数可调
- **TH13 灵界版**：主版 / 灵界版一键切换，交叉淡化无爆音，来回 A/B 零等待
- **预读缓存**：列表顺序、灵界版配对、选中防抖三个预测源，切曲基本零等待
- **播放列表**：21 部作品列表 + 收藏 + 自定义跨作品列表；框选多选、拖动排序、回车即播
- **导出 WAV**：按播放时间线渲染（N / X / F 生效），支持批量与灵界版可选
- **全局多媒体键**、**输出设备选择**（跟随系统自动流路由 / 指定声卡，切换即时生效）
- **深色主题**、便携运行（设置/收藏/列表都写在 exe 旁，不写 AppData）

## 运行环境

- Windows 10 1607 或更高（输出设备跟随系统用到了自动流路由）
- **.NET 10 运行时**（框架依赖部署，程序本体只有几 MB）
- 至少一部游戏的原始数据（程序不含任何游戏资产，需自行准备，见下文「版权」）

## 构建

前置：**[.NET 10 SDK](https://dotnet.microsoft.com/download)**（首次构建需联网还原 NuGet 依赖 NAudio 3）

```
git clone <仓库地址>
cd bgmplayer/src
dotnet build -c Release
```

产物在 `src/bin/Release/net10.0-windows/`。把这一层整个拷走即可运行（便携式）。
Debug 配置把 `-c Release` 去掉即可，产物落在 `src/bin/Debug/...`。

> 构建输入只有 `src/` + `assets/icon/bgmplayer.ico`（csproj 以相对路径引用），
> 仓库其余目录与编译无关。

## 使用

首次运行后到 **设置 → 路径** 为每部作品指定目录：

- **常规作品**：指到 `thbgm.dat` 所在的目录
- **TH06**：指到同时包含 `bgm\` 与 `紅魔郷MD.DAT` 的那一级

没配置的作品在列表里灰显，配了就能播。其余（循环参数、导出、输出设备、外观）见各设置页。

## 目录结构

```
├── src/        WPF 程序本体（csproj 在这层，bin/obj 挂在 src 下）
├── design/     设计文档（DESIGN_v3.md 及佐证材料）
├── data/       曲目数据：tracklist.csv（索引的唯一事实来源）、审校记录、_gen/ 中间产物
│               （source/ 是游戏提取素材，git 忽略，见「版权」）
├── tools/      维护脚本（见下）
├── assets/     图标素材（bgmplayer.ico 是编译输入）
└── docs/       构建日志、决策记录、检查点
```

## 维护脚本（tools/，需 Python 3）

| 脚本 | 用途 |
|---|---|
| `check_src.py` | 源码静态自检（XAML / 括号 / 构造签名 / 事件绑定 / 命名控件 / STJ / 残留符号） |
| `csv_to_tracksjson.py` | `data/tracklist.csv` → 内嵌索引 `src/Resources/tracks.json.gz` |
| `extract_game_names.py` | 从各游戏自带 おまけ.txt 首行提取正式作品名（对照用） |
| `make_icon.py` | PNG → 多尺寸 `bgmplayer.ico`（16~256 七档，需 Pillow） |

数据流：

```
data/tracklist.csv ──csv_to_tracksjson.py──> src/Resources/tracks.json.gz（EmbeddedResource）
```

## 版权

- 本仓库**不含任何游戏资产**。`data/source/`（musiccmt 提取件、fmt/dat/mid 等）
  已加入 .gitignore 不入库——它们是 ZUN / 上海爱丽丝幻乐团的版权内容，
  且随时可从本地游戏重新生成。
- 运行播放器需要你**自行拥有正版游戏**，程序只读取你本机的原始数据文件。
- 东方 Project 是上海爱丽丝幻乐团（ZUN）的作品。本项目为粉丝工具，与官方无关。
