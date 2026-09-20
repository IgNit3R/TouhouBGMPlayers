# 可视化模块实施方案（隔离先行，暂不接入主播放器）

日期：2026-09-20　状态：**待拍板**　关联：`docs/2026-09-19-research-visualization.md`（第七节定稿参数、第九节宿主形态）

用户定的推进方式：**先把「可视化窗口 + 功能 + 入口」做好，暂不做接入；做完后再整体接入。**
本文就是这条路的施工图。

---

## 一、范围边界（先划死）

### 本次做

| # | 内容 |
|---|---|
| 1 | **可视化内核**：取数分接 + 环形缓冲 + 帧数据装配（宿主无关） |
| 2 | **四个定稿渲染器**：A 频谱条 / B 示波器（上下分屏）/ C 竖条电平 + J 相位相关 / D 利萨如 |
| 3 | **可视化窗口**：第二窗口，几何持久化 + 附着跟随能力（跟随目标可后指定） |
| 4 | **一个入口**：命令行开关打开该窗口（见第七节） |
| 5 | **调试声源**：让窗口在没有播放引擎的情况下也能自证（见第三节末） |

### 本次不做（留给「整体接入」）

| 不做的事 | 原因 |
|---|---|
| 不改 `PlayerEngine` 播放链 | 这就是「接入」本身 |
| 不改 `MainWindow.xaml` / `MainWindow.xaml.cs` | 布局与入口都属接入 |
| 不加主窗口菜单项、不改设置窗口 UI | 设置项先写进 `AppSettings`，UI 后补 |
| 不做静态波形总览（路线 B） | 已暂缓 |

---

## 二、为什么必须这样切：内核只认一个接口

整个方案的关键只有一条：**可视化窗口不依赖 `PlayerEngine`，只依赖 `IVizFeed`。**

```
现在（隔离期）    调试声源 ──→ VizTap ──→ 窗口
接入时            引擎混音器 ──→ VizTap ──→ 窗口
                                  ↑ 同一个 VizTap 类型，窗口侧一行不改
```

由此得到的**接入总量预估**（届时只是一次小改）：

| 接入动作 | 位置 | 量 |
|---|---|---|
| 在混音器与音量之间插入分接节点 | `Audio/PlayerEngine.cs:47` 附近（`_mixer` → `_volume` 之间） | ~3 行 |
| 暴露分接给外部 | `PlayerEngine` 加一个只读属性 | 1 行 |
| 主窗口菜单加「视图 → 可视化窗口」 | `MainWindow.xaml:39-45`（「设置」菜单同级） | ~4 行 |
| 把 feed 递给窗口 | `MainWindow.xaml.cs` 新建窗口处 | 1 行 |
| 设置窗口加面板开关 | `UI/SettingsWindow.xaml` | 一处 UI |

**分接位置定在「音量之前」**（`_mixer` 与 `_volume` 之间）——即已定的「取数点音量前」建议：画面不随音量条变小。
分接节点是纯 pass-through，不改动任何样本，对播放行为零影响。

---

## 三、接口契约

```csharp
// ── 读侧：窗口只认这个 ──────────────────────────────
public interface IVizFeed
{
    int SampleRate { get; }                  // 44100（引擎恒定输出）
    int Channels { get; }                    // 2
    /// 把「最近 frames 帧」拷进调用方缓冲（分离声道）。返回实际帧数。
    int ReadLatest(Span<float> dstL, Span<float> dstR, int frames);
}

// ── 写侧：插在音频链里 ──────────────────────────────
// VizTap : ISampleProvider, IVizFeed
//   Read(): 原样透传 + 顺手写环形缓冲（零分配、无锁）
//   音频线程只写，UI 线程只读；写指针 Volatile，环长取 2 的幂便于掩码取模

// ── 渲染器：纯绘制逻辑，不持有窗口 ──────────────────
public interface IVizRenderer
{
    string Name { get; }
    void Draw(DrawingContext dc, Rect area, VizFrame frame);
    void Reset();                            // 停播/切曲时清状态
}

// ── 帧数据：每帧算一次，四个渲染器共享 ──────────────
public sealed class VizFrame
{
    public float[] TimeL;      // ~12ms 时域窗口（B 用）
    public float[] TimeR;
    public float[] Mono;       // 4096 点，FFT 输入
    public float[] SpectrumDb; // 2048 bin 幅度(dB)，每帧算一次
    public float RmsL, RmsR, PeakL, PeakR, Correlation;
    public double Dt;          // 距上帧秒数
    public bool Active;        // 停播时为 false（渲染器衰减归零）
}
```

**每帧只算一次**的重活：`SpectrumDb`（FFT）、`Rms`/`Peak`、`Correlation`。四个渲染器各自只做平滑与几何，
不重复计算。FFT 用 NAudio 内置的 `NAudio.Dsp.FastFourierTransform`（**零新依赖**），real/imag 数组复用。

### 调试声源（隔离期的数据来源）

`VizDebugFeed` —— 读单个音频文件并实时播放，同时供数。**复用项目已有解码栈**，不写新解码器：

| 扩展名 | 走哪条 | 现成设施 |
|---|---|---|
| `.wav` | NAudio `WaveFileReader` → `ToSampleProvider()` | NAudio 原生 |
| `.ogg` | `VorbisPizza` 的 `VorbisReader(fs, true)`，直接给 float | `Audio/Tf/OggMemorySource.cs` 已验证 VorbisPizza 在本项目可用 |

三条验收音频（`.workbuddy/viz-demo/` 已有副本，源文件未动）：

- `th17_13.wav`（tsa / 44100）
- `th175_op.ogg`（黄昏 / 44100）
- `th06nc_16.ogg`（新典 Opus，上一轮已由 `nc_to_ogg.py` 重打包为标准 Ogg —— **调试源不需要碰 NC 自定义容器**）

播完自动回到头循环，便于长时间观察。

---

## 四、新增文件清单

全部新增，**不改动任何既有源码**（除第七节的启动分流一处）。

| 文件 | 职责 | 估算 |
|---|---|---|
| `src/Viz/IVizFeed.cs` | 读侧接口 | ~20 |
| `src/Viz/VizTap.cs` | 环形缓冲 + 分接节点（`ISampleProvider` + `IVizFeed`） | ~90 |
| `src/Viz/VizFrame.cs` | 帧数据结构 | ~40 |
| `src/Viz/VizMath.cs` | FFT 包装、对数频带边界表、dB 换算、平滑助手 | ~110 |
| `src/Viz/VizPump.cs` | 帧节拍：订阅 `CompositionTarget.Rendering` → 装配 `VizFrame` → 广播 | ~90 |
| `src/Viz/VizTheme.cs` | 定稿配色常量 + 从主题取画刷 | ~60 |
| `src/Viz/IVizRenderer.cs` | 渲染器接口 | ~20 |
| `src/Viz/Renderers/SpectrumRenderer.cs` | A：52 对数频柱 20Hz–20kHz，无峰值帽 | ~110 |
| `src/Viz/Renderers/OscilloscopeRenderer.cs` | B：上下分屏 L/R，L `#3A96DD` / R `#9CDCFE`，1.6× 线宽 | ~90 |
| `src/Viz/Renderers/LevelRenderer.cs` | C 竖条电平 -36…0dB + J 相位相关（同面板下方） | ~130 |
| `src/Viz/Renderers/LissajousRenderer.cs` | D：(L,R) 散点 + 余辉，0.6× 细线 | ~70 |
| `src/Viz/VizPanel.cs` | `FrameworkElement`：托管一个渲染器 + 一个 `DrawingVisual` | ~70 |
| `src/Viz/VizWindow.xaml` / `.cs` | 可视化窗口：四面板布局、帧节拍、几何持久化、附着跟随 | ~90 / ~200 |
| `src/Viz/VizDebugFeed.cs` | 调试声源（**隔离期专用**，接入后保留为诊断入口） | ~120 |
| `Core/AppSettings.cs` | **追加** `VizSettings` 类（不动现有字段） | +40 |

合计约 **1300 行**，其中真正需要反复调的是四个渲染器（约 400 行）。

### 窗口内布局（对应验证页的观感）

```
┌──────────────────────────────────────┐
│ A · 频谱条（全宽）                    │
├────────────────────┬─────────────────┤
│ B · 示波器（上下分屏）│ D · 利萨如      │
├────────────────────┴─────────────────┤
│ C 竖条电平 + J 相位相关（全宽，J 在下）│
└──────────────────────────────────────┘
```
布局只是 `Grid` 行列定义，改起来零成本；这里先按验证页的比例定。

---

## 五、定稿配色落位（复用主题，只新增两个画刷）

`Themes/DarkTheme.xaml` 已够用大半，**只需新增两个**（项目规矩：配色只住在主题文件里）。

| 用途 | 颜色 | 现有 key / 新增 |
|---|---|---|
| 画布底 | `#171717` | ✅ `BgDeep` |
| 面板底 | `#222224` | ✅ `BgPanel` |
| 刻度/中线/网格 | `#3F3F45` | ✅ `Border` |
| J 表底色 | `#2E2E31` | ✅ `BgElevated` |
| 示波器 R 声道 | `#9CDCFE` | ✅ `Emphasis` |
| 电平余量区 / 反相 | `#CEA86A` | ✅ `Warn` |
| 读数文字 | `#ADADB4` / `#71717A` | ✅ `TextDim` / `TextFaint` |
| **频谱柱 / 电平柱** | `#2E86C4` | ❗ 新增 `VizBar` |
| **示波器 L 声道** | `#3A96DD` | ❗ 新增 `VizLineL` |

⚠️ 项目已有教训（`MainWindow.xaml:19-21`、`DarkTheme.xaml` 头部注释）：**窗口级隐式样式会盖掉应用级**。
所以 `VizWindow.xaml` 里**不要**再定义 Button/ComboBox 等隐式样式，画刷一律从应用级取。

---

## 六、渲染与线程纪律（低开销路线）

| 项 | 做法 |
|---|---|
| 帧节拍 | `CompositionTarget.Rendering`（vsync 驱动，**不开定时器**）；**只在播放时订阅，暂停即退订** —— 否则空闲时也在狂刷 UI 线程 |
| 绘制 | 每个面板一个 `DrawingVisual`，`RenderOpen()` → 画 → `Close()`；免分配、不建元素树 |
| 音频线程 | `VizTap.Read()` 零分配、无锁（只写环形缓冲 + `Volatile` 写指针） |
| 读写竞争 | 环长取 2 的幂（4096 帧 ≈ 93ms，覆盖需求的 50ms）；撕裂最多影响单帧，可视化上不可见，**接受** |
| 暂停语义 | 帧数据 `Active=false` → 各渲染器按定稿行为衰减归零（验证页演示过的语义） |

按此路线，每帧成本：FFT < 0.1ms + 52 个矩形 + 约 1200 点折线，UI 线程无感。

---

## 七、入口设计（唯一碰共享代码的地方）

隔离期不给主窗口加菜单（那是接入）。入口走**命令行开关**：

```
ThbgmPlayer.exe --viz                    打开可视化窗口（空，等文件）
ThbgmPlayer.exe --viz "D:\path\x.wav"    打开并用调试源播这个文件
```

落法：`App.xaml` 去掉 `StartupUri`，`App.xaml.cs` 加 `OnStartup` 分流。**这是本次唯一改动既有文件的地方**，约 15 行。

⚠️ 两个连带细节：
1. **`ShutdownMode` 现在是默认的 `OnLastWindowClose`**（`App.xaml` 未设）。顺手显式设为 `OnMainWindowClose` —— 这正是宿主形态评估里方案一的头号坑（文档 §9.2）。
2. `--viz` 路径下没有主窗口，若用 `OnMainWindowClose` 会导致**永不退出**。对策：该路径里把可视化窗口赋给 `Application.MainWindow`，或改用显式 `ShutdownMode` 分支。**这点必须实测**。

接入期把这个开关同时保留（诊断用），并额外加主窗口菜单项。

---

## 八、验收方式

1. `--viz` 打开窗口，左上出现四块面板，**未播放时安静空白**。
2. 拖入三条验收音频任意一条：四块面板同时活动、有声音、暂停时画面衰减归零。
3. 与 `.workbuddy/viz-demo/index.html`（浏览器版）**并排对照同一首曲子**，四块面板形态、配色、量程一致。
4. 窗口拖动/缩放/关闭重开：几何被记住（写进 exe 旁的 `settings.json`，**不写 APPDATA**）。
5. 性能：播放时任务管理器 CPU 占用与不开窗口相比无可感上升。

---

## 九、待拍板（需你确认后才动工）

1. **「不接入」的边界**（第三节）—— 本方案定为：**内核 + 窗口 + 调试声源**，引擎与主窗口一行不碰。
   若你希望更省事，也可改为「引擎先装分接节点、窗口从引擎取数」（那样隔离期就能听真曲子，但严格说已算轻接入）。
2. **入口形态**：命令行开关（本方案）／主窗口菜单项先加但数据未接／两者都要。
3. **调试声源要不要做**：不做的话窗口在隔离期没有数据来源，只能靠合成信号调渲染参数（HTML 验证页已做过合成，但真实音频的验收会缺一块）。
4. **工程组织**：`src/Viz/` 放进主工程（本方案，推荐）／还是单独一个可运行工程。
5. 跨方案遗留两项（沿用，不阻塞本次开工）：**暂停语义**（验证页演示为衰减归零）、**取数点音量前/后**（建议音量前）。
