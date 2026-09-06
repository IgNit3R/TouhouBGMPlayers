# build 29 —— 程序图标 + 预读缓存（三预测源）

2026-09-06

## 1. 图标接入

- `tools/make_icon.py`（新建，可复跑）：2048 PNG → 多尺寸 `assets/icon/bgmplayer.ico`
  （16/24/32/48/64/128/256 七档，256 档 PNG 压缩，59KB）。
- csproj：`<ApplicationIcon>` 指 assets 的 ico（exe 图标）；ico 以 `<Resource>` + Link
  打进程序集（包内路径 `Resources/bgmplayer.ico`）。顺带修正了 csproj 注释里的旧路径
  （dependence/… → data/、tools/）。
- 窗口图标：MainWindow / SettingsWindow 走 XAML `Icon="Resources/bgmplayer.ico"`；
  三个代码对话框（Prompt/Export/About）走 `Theme.AppIcon`（BitmapImage + Freeze，
  失败回退 null 用默认图标）。

### 自检脚本修复（重要）

`check_src.py` 的括号配平误报：旧实现分四趟替换（先剥 `//` 注释再剥字符串），
`Theme.cs` 里 `pack://application:...` 的 `//` 被当成注释起点，字符串被截断。
改成**一趟交替匹配**（字符串/字符/行注释/块注释谁先出现谁先消费）。
教训：处理"注释 vs 字符串"必须单趟扫描，分趟替换顺序永远是坑。

## 2. 预读缓存（按 docs/2026-09-06-checkpoint-preload-plan.md 实施）

### 核心：`Audio/PreloadCache.cs`（新建）

- 固定 **2 槽 LRU**，键 = 作品 + 曲序 + 版本（main/alt）。
- 后台 `Task.Run` 读（复用 `AudioSourceFactory.Create`，线程安全）；
  同键在途只有一份；**不取消在途**（一次 20–50ms，取消逻辑不值）。
- **代数次元**：`Clear()` 时 `_generation++`，在途完成发现过期直接 Dispose
  —— 防止改路径后旧文件音源污染缓存（写代码时抓到自己的第一版漏洞）。
- 被淘汰/清空/取出的条目都正确 Dispose（缓冲回 PcmFileSource 复用池）。
- 内存：播放 1 + 预读 ≤2 + 池 1，常态 ~60MB，全遇最大轨 ~190MB 封顶。
- 预读失败（未配路径/读不到）安静吞掉——播放路径有自己的报错渠道。

### 消费点（PlayerEngine，2 处）

`Play` / `SwitchAlt` 建源前先 `PreloadCache.TryTake(...)`，未命中才同步读。

### 预测源（MainWindow，3 处）

| 源 | 触发点 | 行为 |
|---|---|---|
| ① 列表顺序 | PlayTrack 后 | 非随机模式预读 `NextRef()`（含回卷到第一首的情形） |
| ② 灵界版配对 | PlayTrack 后 + Alt_Click 后 | 预读没在播的那个版本，A/B 来回切零等待 |
| ③ 选中即预读 | OnTrackSelectionChanged → 250ms 防抖 | 预读选中行（跳过不可用与正在播的） |

设置窗口关闭后 `PreloadCache.Clear()`（路径可能变了）。

### 预期效果

- 顺序连播、手动点歌（浏览→双击/回车）、TH13 首切灵界版 → 全部零等待
- 唯一仍付读盘代价的：完全无征兆跳到从未预读过的曲子（随机模式点下一首）

## 验证

check_src.py 7 项全过。待实机：切曲应几乎瞬时；TH13 首切灵界版无滞后；
内存占用（任务管理器）常态应 <150MB。
