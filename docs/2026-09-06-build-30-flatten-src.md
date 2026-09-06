# build 30 —— 去掉 src\ThbgmPlayer 中间层

2026-09-06

## 变更

项目内容从 `src\ThbgmPlayer\` 整体上移一层到 `src\`（用户定的结构：  
留 src 层、去项目名层，bin 直接挂在 src 下）。bin 连同运行时数据  
（settings/favorites/playlists）一起走，未丢失。obj 删除（含旧绝对路径，再生品）。

```
src\
├── ThbgmPlayer.csproj  App.xaml  MainWindow.xaml(.cs)  AssemblyInfo.cs
├── Audio\  Core\  Data\  UI\  Themes\  Resources\
└── bin\               ← 构建产物 + 运行时数据
```

## 引用修复

- `src\ThbgmPlayer.csproj`：两处 `..\..\assets\icon\bgmplayer.ico` → `..\assets\...`
- `tools\check_src.py`：ROOT → `parents[1] / "src"`
- `tools\csv_to_tracksjson.py`：OUT → `src/Resources/tracks.json.gz`（含 docstring）
- `README.md`：目录结构、构建命令（`cd src`）、数据流图

## 验证

- check_src.py 7 项全过；csv_to_tracksjson.py 重新生成索引一致（确定性）
- bin 内 settings/favorites/playlists 完好；export 目录与 \_runtime_backup  
  在上次重组后已被用户清理（导出物可再生）

## 新构建命令

```
cd E:\GitWorkspace\thworks\bgmplayer\src
dotnet build
```

