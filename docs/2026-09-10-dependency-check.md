# 依赖更新核查（Dependabot）+「关于」对话框依赖表

日期：2026-09-10 · 仓库 `IgNit3R/TouhouBGMPlayers`（public，默认分支 master）

---

## 「关于」对话框的依赖表已过时（待改）

`src/UI/AboutDialog.cs:20-31` 的 `Dependencies()` 是硬编码的 5 行，
比实际依赖图少了后来接入的监听器/解码器。

实际依赖（`src/obj/project.assets.json` 与 `bin/Debug/net10.0-windows` 实际输出）：

| 包 | 版本 | 许可 | 来源 |
|---|---|---|---|
| NAudio | 3.0.1 | MIT（nuspec expression） | 直接引用 |
| VorbisPizza | 1.4.2 | **MIT**（LICENSE 文件，Andrew Ward / TechPizza） | 直接引用 |
| Concentus | 2.2.2 | **BSD-3-Clause**（LICENSE 文件，含 "Neither the name" 条款） | 直接引用 |
| NAudio.Core | 3.0.1 | MIT | NAudio 元包 |
| NAudio.Asio / Dmo / Midi / Wasapi / WinForms / WinMM | 3.0.1 | MIT | NAudio 元包 |
| System.Numerics.Tensors | 9.0.0 | MIT | **NAudio.Core** 的传递依赖 |
| addons | 1.0.0 | — | ProjectReference，非 NuGet |

程序集简单名（`AssemblyVersionOf` 用 `Assembly.Load(name)`，必须是这个名字）：
`VorbisPizza.dll` / `Concentus.dll` / `NAudio.Wasapi.dll` —— 均与包内 `lib/` 下的文件名一致。

`WasapiPlayer` / `WasapiPlayerBuilder` 的实际所在程序集：**NAudio.Wasapi.dll**
（对 8 个 NAudio*.dll 逐个做二进制元数据检索，只有 NAudio.Wasapi.dll 命中 2 次）。

问题点：
1. 缺 **VorbisPizza**（黄昏作 OGG）与 **Concentus**（新典 Opus）—— 实际在用的两个解码器。
2. 缺 **NAudio.Wasapi** —— 输出链路真正调用的就是它，而表里只有 NAudio.Core（WaveFormat / SampleProviders）。
3. 版本是动态读的，NAudio 那几行会跟着包版本自己走，合了 PR #1 就自动变 3.1.0，不用手改。
4. `System.Numerics.Tensors` 属实现细节，建议不列。

### 最终结构（第三次定稿，已实施）

中途试过 T1「全展开树（.NET Desktop Runtime → .NET Runtime / WPF / Windows Forms）」，
用户推翻，改为**运行时出树、只占信息区一行**，树只用在外部依赖上：

```
版本 1.6nc
构建 2026-09-10 18:33:25
内嵌曲目 581 首 / 29 部作品（另含 30 首灵界版）
运行时 .NET Desktop Runtime 10.0.12（含 WPF / Windows Forms※）   ← 运行时只占这一行

依赖
NAudio              3.0.1   MIT
  ├ NAudio.Core     3.0.1   MIT
  └ NAudio.Wasapi   3.0.1   MIT
VorbisPizza         1.4.2   MIT
Concentus           2.2.2   BSD-3-Clause

※ 仅使用了 WinForms 下的 FolderBrowserDialog 一个类型
```

决策链（都来自用户）：
1. 运行时不该写成 `.NET`，要写明是**运行时产品名**；因为用 WPF+WinForms，正确的是
   **.NET Desktop Runtime**（基础版 .NET Runtime 里没有 WPF/WinForms，装了也起不来）。
2. 一度上 T1 全展开树，随后推翻 —— 运行时只显示版本，不进树。
3. 传递依赖 `System.Numerics.Tensors` 不进树。
4. 脚注的 `※` 要挂在被注释处：收进括号、紧跟 `Windows Forms` → `（含 WPF / Windows Forms※）`。

**版本列到底是谁的版本（用户提问过）**：
- `.NET  10.0.12` 来自 `Environment.Version` = CLR / `Microsoft.NETCore.App` 版本 → **运行时**版本，
  **不是**构建用的 SDK 版本（本机 SDK 是 `10.0.401`，程序里没有任何地方记录它）。
- 本机共享框架目录：`shared/Microsoft.NETCore.App/10.0.12`、`shared/Microsoft.WindowsDesktop.App/10.0.12`
  —— 桌面框架随基础运行时成套发，同号，所以 WPF / WinForms 两行直接复用同一个 `runtime` 变量。
- 程序是框架依赖发布（`SelfContained=false`），所以显示的是**跑这个 exe 的机器上装的运行时**，
  换机器会变，这是预期行为。
- 原先那两行写死的 `随 .NET` 是个"假版本"，挤在版本列里突兀，已按用户决定换成真实运行时版本。

### 已实施（2026-09-10）

`src/UI/AboutDialog.cs`：
- 窗口 `Height = 440` → **去掉固定高度**，改 `SizeToContent = SizeToContent.Height`
  （高度跟内容走，不会被裁），配 `MinHeight = 360` / `MaxHeight = 900` 作安全阀；
  `ResizeMode = NoResize`、`Width = 460` 不变
- `Dependencies()` 改为返回 `(Depth, IsLast, Name, Version, License)` 的树形数据：
  `NAudio` 带 `NAudio.Core`（非末项）/ `NAudio.Wasapi`（末项）两个子项，`VorbisPizza`、`Concentus` 为顶层叶子
- 新增 `RuntimeVersion()`（`Environment.Version.ToString()`），运行时那行复用它
- 信息区新增一行 `运行时 .NET Desktop Runtime {版本}（含 WPF / Windows Forms※）`
- 依赖行渲染加一列 18px 的**树形导线**：不用 `├ └` 制表符，改用 1px 矩形画 ——
  Consolas 对 U+2500 区段的覆盖没保证，WPF 走字体回退后字宽不一致会让名字起点漂移。
  做法：两行等分网格，竖线 `RowSpan=2`（末项不跨行，只到中线），横线居中且 `RowSpan=2`，天然对齐。
  颜色 = `TextDim` 画刷 + `Opacity 0.45`
- **列宽按行不同**：顶层行不预留导线列（`col0 = 0` / `col1 = 210`），名字贴齐框内左边缘；
  子项行 `col0 = 18` / `col1 = 192`。`col0 + col1` 恒为 210，所以版本/许可两列在所有行上起点一致。
  （用户反馈"左边名字没有左对齐"—— 起初每行都预留 18px 导线列，整列名字被推右 18px，
  离框内左边缘留出一道空档；且子项只缩进 4px，读起来像"差一点点没对齐"而不是"有缩进"。）
- `depScroll.MaxHeight` 保持 **180**（5 行约 110px，富余）；窗口 `SizeToContent.Height` 不变
- `AssemblyVersionOf()` 重写：先扫 `AppDomain.CurrentDomain.GetAssemblies()`，
  没加载过就读 `AppContext.BaseDirectory/<name>.dll` 的元数据
  （`AssemblyName.GetAssemblyName` 只解析文件头，**不会**把程序集载进进程）——
  原先的 `Assembly.Load(name)` 对 VorbisPizza / Concentus 这种"冷启动还没用到"的程序集不可靠

> 尺寸估算：定稿后客户区约 460×365、含标题栏约 460×397
> （比原来的固定 `Height = 440` 略矮 —— 原 440 在「关闭」按钮下方留了一截空白）。

### 同批修掉：「另含 30 首灵界版」是错的

用户发现内嵌曲目那行写着「另含 30 首灵界版」，但 TH13 不可能有 30 首。
原因：那行直接 `TrackIndex.Games.Sum(g => g.AltCount)`，把 **TH06NC 的 17 首「原典」副版**
也算进「灵界版」里了。

实测（解析三个内嵌索引）：

| 作品 | 曲数 | 副版 | AltLabel | 实际叫什么 |
|---|---|---|---|---|
| TH13 | 18 | **13** | 无 | 霊界版 |
| TH06NC | 18 | **17** | 原典 | 原典版 |
| 黄昏作 ×7 | 230 | 0 | — | — |
| 合计 | 581 | **30** | | |

修法（按用户口径「灵界版跟原典版有本质区别，不要并进来」）：
只统计**没有** `MainLabel`/`AltLabel` 的作品（目前只有 TH13），有标签的（新典 ↔ 原典）不计数：

```csharp
int legacyAlt = TrackIndex.Games.Where(g => !g.HasVariantLabels).Sum(g => g.AltCount);
// 文案：内嵌曲目 581 首 / 29 部作品（另含 13 首灵界版）
// legacyAlt == 0 时整个「（另含 …）」括号不出现
```

> 中途曾试过按 `AltLabel` 分组列出「13 首霊界版、17 首原典版」，用户否决：
> 原典版跟灵界版不是一类东西，不该并列展示。

**用字（注意范围）**：「关于」对话框里写**简体「灵界版」**；
主界面按钮等其它文案保持原来的日文旧字体**「霊界版」**，不动。

> 曾误按"全仓统一"把 `MainWindow.xaml`（2 处）、`MainWindow.xaml.cs`（6 处）、
> `TrackIndex.cs`（1 处）的 `霊界版` 一起改成了简体，用户指出**只针对「关于」这个标签**，
> 已逐处回滚（含 `MainWindow.xaml.cs` 里被顺手删掉的重复 `<summary>` 行，一并复原）。
> 回滚后逐一核对确认 `霊界版` 的位置与改动前完全一致，四个文件的 BOM 也与 HEAD 一致
> （`TrackIndex.cs` 原本无 BOM，脚本替换时被加过，已去掉）。
>
> 教训：批量文本替换前先确认**改动范围**，别把"某个界面用简体"当成"全项目统一用字"。

### 同批同步 README（用户点名）

`README.md` 两处过期：

1. 运行环境原来写「**.NET 10 运行时**」→ 改为「**.NET 10 Desktop Runtime**」，
   并补注"要装 Desktop 版 —— 基础版 .NET Runtime 不含 WPF / Windows Forms，装了起不来"。
   理由同「关于」对话框：程序是 WPF + WinForms，依赖的是 `Microsoft.WindowsDesktop.App`。
2. 构建前置原来写「联网还原 NuGet 依赖：NAudio 3、VorbisPizza」→ 补上 **Concentus**
   （新典 Opus 解码，后加的，漏在 README 里了）。

`README.md` 无 BOM（首字符是 `# `），改动后复核与 HEAD 一致。

### 再补：README 里新典内容的缺口（用户点名两处 + 顺手一处）

1. **特性 · 直读原始数据**：只写了整数作 / 黄昏作 → 补上新典
   `data\bgm\th06_NN.opus`（自定义 Opus 容器，非 Ogg / RIFF）；
   顺手补了 `（TH06 为`bgm\th06_NN.wav`）` 里漏掉的一个空格。
2. **使用 · 路径**：只有「整数正作 / 黄昏作」两条 → 补第三条
   「官方重制版（新典 TH06NC）：指到游戏根目录（含 `data\` 的那一级）；音频在 `data\bgm`（新典编曲）
   与 `data\bgm2`（原典编曲），用播放列表上的副版按钮切换。」
3. **tools 表格**：漏了 `csv_to_ncjson.py`（生成 `tracks.nc.json.gz`），已补一行。

> 另发现一处**未改**的维护者不一致：`tools/csv_to_ncjson.py:25` 的输入是**仓库外绝对路径**
> `E:\GitWorkspace\thworks\re_work\reports\projectKouma\nc_tracks_full.csv`，
> 而 `csv_to_tracksjson.py` / `csv_to_tfjson.py` 走的是相对路径。
> 因为那份 CSV 不在仓库里（在 re_work/ 下），没法改成相对路径；仅记录，未动。

### README 运行环境补下载链接

`- **[.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)**（…）`。
选**版本页**而不是首页 `dotnet.microsoft.com/download`，因为那页能直接看到
Desktop Runtime / ASP.NET Core Runtime / .NET Runtime 三者的并列选项，
跟 README 里「要装 Desktop 版，基础版跑不起来」这句正好对上。

该页原文也印证了「关于」对话框里的树形结构：
> "The .NET Desktop Runtime enables you to run existing Windows desktop applications.
> **This release includes the .NET Runtime; you don't need to install it separately.**"

---

## 附录：本机（助手 shell）跑不了 dotnet build 的根因

不是项目问题，是 shell 环境缺变量。异常栈（`dotnet restore -v d`）：

```
System.ArgumentNullException: Value cannot be null. (Parameter 'path1')
  at System.IO.Path.Combine(String path1, String path2)
  at NuGet.Common.NuGetEnvironment.CalculateFolderPath(NuGetFolderPath folder)
  at NuGet.Configuration.XPlatMachineWideSetting..ctor()
  at NuGet.Build.Tasks.RestoreSettingsUtils.ReadSettings(...)
  at NuGet.Build.Tasks.GetRestoreSettingsTask.Execute()
```

`NuGet.targets(782,5): error : Value cannot be null. (Parameter 'path1')` 就是这个。
助手 shell 里 `ProgramData` / `ALLUSERSPROFILE` / `APPDATA` 三个变量**缺失**
（`LOCALAPPDATA`、`USERPROFILE` 有），NuGet 解析机器级配置目录时拿到 null。
手动补 `ProgramData=` 前缀仍失败（worker node 侧未生效），沙箱开关也无关。
用户自己的终端正常，构建以用户侧为准。

---

## 附录二：`System.Numerics.Tensors.dll` 是谁

- Microsoft 官方包（dotnet/runtime 仓库），**9.0.0**，MIT。
- **传递依赖**，不是直接引用：依赖图 `NAudio 3.0.1 → NAudio.Core 3.0.1 → System.Numerics.Tensors 9.0.0`。
  佐证：`NAudio.Core.dll` 里能检索到 `TensorPrimitives` 与 `System.Numerics.Tensors` 字样。
- 用途：`Tensor<T>` / `TensorPrimitives` 这类 SIMD 加速的数值原语；NAudio 3.x 用它做
  逐样本运算的向量化（采样格式换算、音量斜坡这类路径）。
- 按"只列直接引用"的口径不进「关于」表；若走 T1 树，可挂在 `NAudio.Core` 下当孙子节点。

**运行时它的作用**：`TensorPrimitives` 是一组按 `Span` 批量运算的静态方法（.NET 9 起从 40 个
重载扩到近 200 个，很多有 SIMD 优化）。NAudio.Core 拿它做逐样本的批量算术——采样格式换算、
音量/混音这类；本项目的热路径就是 `SampleToWaveProvider16(_volume)` 及其上游的音量与交叉淡化运算。
本仓自己的代码从未引用它（全仓 grep 无 `System.Numerics.Tensors`），引用方是 NAudio.Core
（dll 元数据里有 `TensorPrimitives` 类型名 + assets 图里的 `NAudio.Core → System.Numerics.Tensors`）。
具体哪个方法调了哪个原语需要反编译 IL 才能确定，未做。

**它属于 .NET 9 吗？不属于。**
它是**独立 NuGet 包**，**不在任何一代 .NET 的共享框架里** —— 核对了
`Microsoft.NETCore.App/8.0.31`、`Microsoft.NETCore.App/10.0.12`、`Microsoft.WindowsDesktop.App/10.0.12`
三处，都只有 `System.Numerics.Vectors` / `System.Numerics` / `System.Runtime.Numerics`，没有 Tensors。
包内还提供 net462 / netstandard2.0 资产，这也是"外挂包"而非共享框架成员的典型特征。

但它**跟着 .NET 版本发版**（dotnet/runtime 仓库产出），NuGet 上有完整 servicing 线：
`8.0.0` → `9.0.0 … 9.0.20` → `10.0.0 … 10.0.12` → `11.0.0-preview.*`。
本仓拿到的 **9.0.0 不是因为跑在 .NET 9**，而是 **NAudio.Core 3.0.1 把依赖钉在 `[9.0.0, )`**
（NAudio 3.x 最低 TFM 就是 net9.0）；net9.0 编译的库在 net10 运行时上跑是官方支持的。

> 若想对齐到 .NET 10 世代，可显式加一条
> `<PackageReference Include="System.Numerics.Tensors" Version="10.0.12" />` 覆盖传递版本
> （10.0.x 线当前最新恰好是 10.0.12，与运行时同号）。不会减少文件数，只是版本对齐。
> 注意 Dependabot 只管直接依赖，不升级传递依赖，所以不显式钉它就会一直停在 9.0.0。

**Release 必须带**：正因为不在共享框架里（上面已核对），它不像 WPF/WinForms 能"随 .NET"白拿，
必须以文件形式跟 exe 一起发布。包内提供 net462 / net8.0 / **net9.0** / netstandard2.0 四套，
net10.0-windows 取的是 net9.0 那套（无害）。

删不掉：给 NAudio.Core 加 `ExcludeAssets="runtime"` → 走到那条路径会抛 `FileNotFoundException`；
NAudio 3.0.1 起就带着它，退回 2.x 不现实；trimming 与 WPF 不兼容。MIT 许可、纯托管、无原生依赖，
老实带上即可。

体积（Debug 输出，供参考）：

| dll | 大小 |
|---|---|
| ThbgmPlayer.dll | 1.76 MB |
| NAudio.Wasapi.dll | 467 KB |
| **System.Numerics.Tensors.dll** | **401 KB** |
| Concentus.dll | 398 KB |
| NAudio.Core.dll | 341 KB |
| VorbisPizza.dll | 100 KB |
| addons.dll | 37 KB |

## 附录三：解包 / 容器层全在自产程序集里，无第三方压缩库

`addons.dll`（黄昏作容器解析，5 个源文件零改动移植）：
`pakReader/pakReader.cs`（TFPK + 内嵌名称表）、`XorReader/XorContainerReader.cs`、
`XorReader/SflReader.cs`、`XorReader/Mt19937.cs`、`Suica/SuicaReader.cs`。

`ThbgmPlayer.dll`：`Audio/Tf/TfContainerSet.cs`、`Audio/Tf/OggMemorySource.cs`、
`Audio/Tf/WavEntrySource.cs`、`Audio/Nc/OpusMemorySource.cs`（新典自定义 Opus 容器）、
`Audio/AudioSourceFactory.cs`（按魔数分派 OggS / RIFF / TFWA / 新典）、
`Data/TrackIndex.cs`（索引解压）。

压缩全部走 BCL：`System.IO.Compression.GZipStream`（索引）、`ZLibStream`（TFPK 名称表）。
全仓 grep 不到 zstd / zlib 的第三方实现，`bin` 输出里也没有相关 dll。

> PKGL 封装（含 zstd / flag 位）属于**准备阶段**——从 `th06MD.dat` 里取文件清单用，
> **不在播放路径上**。运行时新典直接读 `data\bgm\th06_NN.opus`（Alt 走 `data\bgm2\`），
> 见 `AudioSourceFactory.cs:73-84`。

---

## 第一部分：远端 Dependabot 现状

## 远端现状
- master HEAD：`a9c432a`（"add checking system"，2026-09-07 09:27 UTC，SSH 签名有效）
- 本地 `bgmplayer/` HEAD 与 origin/master **齐平**（0 / 0），但工作区是脏的
  （10 改 + 4 未跟踪，NC 全套，其中含 `src/ThbgmPlayer.csproj`）
- Dependabot 配置：`.github/dependabot.yml` → nuget、`weekly`，目录 `/src` 与 `/addons`
- CI：只有 `.github/workflows/release.yml`，触发条件是 **push tag `v*`**
  → PR / 分支**完全没有 CI**，合并前不会得到任何自动化验证

## 待处理：PR #1
| 项 | 值 |
|---|---|
| 标题 | Bump NAudio from 3.0.1 to 3.1.0 |
| 作者 | dependabot[bot] |
| 状态 | open（未合并、非 draft） |
| 改动 | 1 个文件 `src/ThbgmPlayer.csproj`，+1 / -1 |
| head | `dependabot/nuget/src/NAudio-3.1.0` |
| mergeable | `true`，`mergeable_state: clean` |
| check runs | **0 条**（无 CI） |
| 链接 | https://github.com/IgNit3R/TouhouBGMPlayers/pull/1 |

## 另外两个包：无更新
| 包 | 本仓 | NuGet 最新稳定 | 结论 |
|---|---|---|---|
| VorbisPizza | 1.4.2 | 1.4.2 | 已最新 |
| Concentus | 2.2.2 | 2.2.2 | 已最新 |
| NAudio | 3.0.1 | 3.1.0（3.1.1-preview.1 是预览） | 有 PR #1 |

`/addons` 无任何 `PackageReference`，Dependabot 那边永远不会有产出。

安全告警：GitHub Advisory 公开库里 NAudio / Concentus 均**无** advisory，无已知漏洞需要修。

## 判断：可以合，且建议合
逐条核对 NAudio 3.1.0 的两个 breaking change，本仓**都没用到**：

1. `WaveFormat` 及子类不再带 `[StructLayout]`（`Marshal.SizeOf` / `PtrToStructure` 会抛）
   → 全仓 grep 无 `Marshal.SizeOf` / `Marshal.PtrToStructure` / `MarshalToPtr` / `MarshalFromPtr`。
2. `AudioClient.IsFormatSupported` / `WasapiPlayer.IsFormatSupported` 改签名
   （`out WaveFormatExtensible` → `out WaveFormat`）
   → 全仓无 `IsFormatSupported` 调用。

本仓 WASAPI 走的是高层 API，不碰格式协商（`src/Audio/PlayerEngine.cs:66-94`）：
`WasapiPlayerBuilder().WithSharedMode().WithEventSync().WithMmcssThreadPriority("Pro Audio")`
`.WithLatency(...)` → 指定设备或 `WithDefaultDeviceStreamRouting()` → `BuildAsync()` →
`player.Init(new SampleToWaveProvider16(_volume))`。

反过来 3.1.0 修了一个**正好压在播放链上**的 3.0.0 回归：
`WdlResamplingSampleProvider` 在「被要求输出多于音源能提供的样本」时会丢样本、最终永久返回 0，
以及 `WdlResampler.ResampleOut` 在 feed 模式下少喂样本时的漂移。
本仓唯一的重采样点就是 `src/Audio/PlayerEngine.cs:302`
`new WdlResamplingSampleProvider(loop, OutputRate)`（灵界版 22050 → 44100 走这条路）。

其余修复（`Mp3FileReader` Xing 头 seek、WAV 解析器两个死循环、`WaveFormatExtraData` 缓冲）
与本仓无关，也无害。

## 合并时的时序陷阱
本地 `src/ThbgmPlayer.csproj` **已改未提交**（NC 那批里加了 Concentus 等）。
若直接在 GitHub 上 Merge PR #1：

- master 的那一行变成 `NAudio 3.1.0`；
- 本地提交后再 push 会因非快进而被拒 → pull 后需要解一次**单行冲突**
  （本地 `3.0.1` vs 远端 `3.1.0`）。

两种更干净的走法：
- **A（推荐）本地改、关掉 PR**：在本地把 `NAudio` 写成 `3.1.0`，跟 NC 那批一起提交推送，
  然后在 PR 上 `@dependabot close`。改动已被本仓取代，PR 会自动关闭。
- **B 先合再开工**：先 commit / stash 本地改动 → `git pull` → 再继续，保留 Dependabot 的提交记录。

无论哪种，合完都要**自己本地跑一次 `dotnet build -c Debug`**——PR 上没有任何 CI。
