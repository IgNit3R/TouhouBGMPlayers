# build 28 —— 仓库结构重组（git 风格）

2026-09-06

## 变更（用户手动搬运，助手修引用）

```
_log\                  → docs\
dependence\01_design\  → design\
dependence\02_data\    → data\
dependence\04_source\  → data\source\
dependence\_gen\       → data\_gen\
dependence\README.md   → data\README.md
dependence\03_tools\   → tools\
src\, assets\          不动
bin\, obj\             删除（可再生）
```

运行时数据（settings/favorites/playlists/export）先备份到 `_runtime_backup\`，
重新编译后拷回 exe 旁。

## 引用修复（本轮改动）

- `tools/check_src.py`：`parents[2]` → `parents[1]`
- `tools/csv_to_tracksjson.py`：DEP → ROOT（仓库根），CSV → `data/tracklist.csv`，OUT 不变
- `tools/extract_game_names.py`：`parents[3]` → `parents[2]`（thworks 根，tsa/ 所在）
- 其余 9 个历史一次性脚本（probe_*/gen_* 等）路径常量已失效，**不修**，要用再说

## 新建

- `README.md`：项目说明、目录结构、构建、维护脚本用法、数据流
- `.gitignore`：bin/obj、.workbuddy、_runtime_backup

## 新约定（替代旧约定）

- **构建日志/决策/检查点 → `docs\`**（原 `_log\` 约定作废）
- 脚本统一放 `tools\`，数据统一放 `data\`，设计文档在 `design\`
- `bgmplayer\.workbuddy\` 是图标生成会话的残留，真正的项目记忆在 `thworks\.workbuddy\`

## 验证

- check_src.py 7 项全过
- csv_to_tracksjson.py 重新生成索引：333 + 13 首，与搬迁前一致（确定性输出）
- extract_game_names.py 正常从 Steam/tsa 读取
