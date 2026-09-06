# 决定：gen_tracklist_v2.py 的作品名错误不订正

日期：2026-09-06

`dependence/03_tools/gen_tracklist_v2.py` 里的 GAMES 表有两处作品名错误：

| 代号 | 表里的 | 正确（おまけ.txt） |
|---|---|---|
| TH07 | 東方妖妖夢 | 東方妖々夢 |
| TH128 | 東方三月精 | 妖精大戦争 |

**用户决定：不订正，保持原样。**

理由推测：该表只用于生成 `TRACKLIST.md`（人工核对用的文档），
不参与播放器运行时的数据链路 —— 播放器的作品名走的是
`csv_to_tracksjson.py` 的 NAME 表（已用おまけ.txt 校准过）。

## 对后续工作的影响

- 不要再提议改这个文件
- 重新生成 TRACKLIST.md 时，文档里的作品名仍是旧的错误写法，
  这是**已知且接受的**，不算 bug
- 若将来要用到作品名，一律以 `csv_to_tracksjson.py` 的 NAME 表为准

## 相关文件

- 权威来源提取：`dependence/03_tools/extract_game_names.py`
- 播放器用的表：`dependence/03_tools/csv_to_tracksjson.py` 的 `NAME`
- 记录见：`_log/2026-09-06-build-19-game-names.md`
