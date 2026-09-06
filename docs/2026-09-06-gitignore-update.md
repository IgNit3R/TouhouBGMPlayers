# 2026-09-06 补记 —— .gitignore 按构建输入清单校准

- 用户已自行删掉 `_runtime_backup` 条目与目录，本轮在其基础上补：
  - `.vs/`、`*.suo`（VS 本地状态）
  - bin 路径结构注释（输出根\配置\TFM）
  - 末尾"必须被跟踪"清单（src、assets\icon\bgmplayer.ico、data/design/docs/tools），防止以后误加忽略规则
- 解答：bin\Debug\net10.0-windows\ = 输出根 + Configuration（dotnet build 默认 Debug）+
  TFM（来自 csproj 的 TargetFramework，非随机默认名）。-windows 后缀因 WPF 是 Windows 专属。
  可 AppendTargetFrameworkToOutputPath=false 拍平，不建议（无收益、丢自解释性）。
  发布用 `dotnet build -c Release`。
