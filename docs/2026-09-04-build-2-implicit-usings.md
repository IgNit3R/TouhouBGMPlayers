# 构建日志 · 第 2 轮：隐式 using 在 WPF 项目里的陷阱

（按红线不改写已有文件，本轮另开新文件记录。）

## 现象

修掉 CS0104 之后重新 `dotnet build`，冒出 **18 个 CS0103**：`File`、`Directory`、`Path` 在当前上下文中不存在，
全部集中在 `Core/AppSettings.cs` 与 `Core/PathValidator.cs`。

## 根因

WPF 项目里 **`System.IO` 不在隐式 using 中**。SDK 主动摘除：

```
sdk/10.0.400/Sdks/Microsoft.NET.Sdk.WindowsDesktop/targets/
    Microsoft.NET.Sdk.WindowsDesktop.WPF.props:6-7
        <Using Remove="System.IO" />
        <Using Remove="System.Net.Http" />
```

摘除理由：WPF 生成代码（`obj/.../*.g.cs`）里有 `using System.Windows.Shapes;`，
若全局引入 `System.IO`，`Path` 会在 `System.IO.Path` 与 `System.Windows.Shapes.Path` 之间撞名。

生成的全局 using 实际只有（见 `obj/Debug/net10.0-windows/ThbgmPlayer.GlobalUsings.g.cs`）：

```
System / System.Collections.Generic / System.Linq / System.Threading / System.Threading.Tasks
```

## 为什么上一轮没报出来

上一轮 `App.xaml.cs` 的 `public partial class App : Application` 中 `Application` 歧义属于
**声明期错误**。Roslyn 一旦遇到声明期错误，就**跳过整个编译单元的方法体绑定**，
于是这些方法体里的 CS0103 全被压住不报。声明期错误修好后，绑定阶段才真正跑起来，
18 个错误一次性暴露。

**推论**：本轮 18 个错误就是全部的方法体错误——其余文件（MainWindow.xaml.cs、
Data/TrackIndex.cs、UI/SettingsWindow.xaml.cs、Core/AppPaths.cs、Core/ViewModelBase.cs）
在同一轮里方法体已被绑定且未报错，说明它们是干净的。

## 修复

只给用到的文件加文件级 `using System.IO;`：

- `Core/AppSettings.cs` —— `File.Exists/ReadAllText/WriteAllText`
- `Core/PathValidator.cs` —— `Directory.Exists/GetParent`、`File.Exists/OpenRead`、`Path.Combine`

`Core/AppPaths.cs` 本来就写了 `using System.IO;`，所以它没出现在错误列表里——正好印证了上面的判断。

## 固化规则

1. **不要**用 `<Using Include="System.IO" />` 全局加回。那会让生成代码里的
   `System.Windows.Shapes.Path` 与 `System.IO.Path` 撞名（CS0104）。
2. WPF 项目里凡是写 `File` / `Directory` / `Path` / `Stream` / `GZipStream` 的文件，
   **逐个显式加 `using System.IO;`（或 `System.IO.Compression`）**，不要依赖隐式 using。
3. 排查误报：`typeof(X).Assembly` 是属性不是类型名，不需要 `using System.Reflection;`；
   `System.Text.StringBuilder` 若写了全称也不需要 `using System.Text;`。
