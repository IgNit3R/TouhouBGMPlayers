# build 31 —— 设置闪退修复 + 流路由 BuildAsync 修复（探针诊断）

2026-09-06

## 症状

1. 首次启动很慢；开第二个实例很快，第一个随后也出现
2. 设置→路径 一点就闪退（CrashDumps 里有 4 份 dump）

## 诊断方法（值得记住）

- `~/AppData/Local/CrashDumps/` 找到 4 份 ThbgmPlayer dump（WER 本地转储）
- dotnet-dump 装不上（沙盒 NuGet 环境缺变量）→ 改为写**探针工程**（`_repro/`，
  引用主项目，分段执行设置窗口路径上的每一步），直接拿到托管异常全文
- **沙盒里跑 dotnet 需要补环境变量**：`APPDATA`、`ProgramData`、`ProgramFiles`、
  `ProgramFiles(x86)`、`CommonProgramFiles`、`CommonProgramFiles(x86)` —— 缺了会报
  NuGet `Value cannot be null (Parameter 'path1')`（XPlatMachineWideSetting 解析失败）。
  同一原因导致 `dotnet tool install -g` 在沙盒里不可用。

## 根因与修复（两个独立 bug）

### ① 设置闪退：相对 pack URI 按 XAML 文件位置解析

`Icon="Resources/bgmplayer.ico"` 是**相对于 XAML 文件自身位置**解析的：
MainWindow.xaml 在根 → 解析正确；UI/SettingsWindow.xaml → 解析成
`ui/resources/bgmplayer.ico` → IOException → XamlParseException → 闪退。
**修：两个窗口都改绝对 pack URI** `/ThbgmPlayer;component/Resources/bgmplayer.ico`。
教训：XAML 里引用程序集资源，非根目录的 XAML 文件必须用绝对 pack URI。

### ② 播放从 build 26 起就是坏的：流路由必须 BuildAsync

`WithDefaultDeviceStreamRouting()` 时 NAudio 规定**只能用 `BuildAsync()`**，
同步 `Build()` 抛「call BuildAsync() instead」。异常被引擎构造 catch 吞成
InitError —— 选「系统默认」时引擎根本没有输出设备。用户 build 26 后一直在做
UI/git 没试播放，未暴露；探针复现出来。
**修：`BuildOutput()` 改用 `BuildAsync().GetAwaiter().GetResult()`**（ctor 同步等一次即可）。

### ③ 首次启动慢：非代码问题

引擎初始化失败是毫秒级，撑不起「等很久」。「新 exe 首次慢、第二个快、第一个随后出现」
是 **Windows Defender 实时扫描新编译的未签名 exe** 的典型表现。每次重编译后首次启动
都会这样，正常，不治。

## 验证

探针四步全绿（RenderEndpoints 3 端点 / PlayerEngine 无 InitError / App 主题 /
SettingsWindow 构造成功）；check_src.py 7 项全过；探针工程已删除。

**待用户实机**：设置→路径 不再闪退；实际播放确认输出设备工作（build 26 的设备
选择功能至此才真正生效）。
