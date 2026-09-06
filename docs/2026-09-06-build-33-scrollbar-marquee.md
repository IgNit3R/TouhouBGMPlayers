# build 33 —— 滚动条拖动被框选手势抢走

2026-09-06

## 症状

曲目表右侧滚动条几乎没法拖：要么直接进框选状态，要么只动一点点。

## 根因

滚动条在 DataGrid 的可视树**内部**。框选手势挂在 `PreviewMouseLeftButtonDown`
（隧道事件），先于滚动条收到点击——按下瞬间就武装了框选，一拖动就
`CaptureMouse()` 把鼠标从滚动条手里抢走。拇指条于是要么触发框选、
要么在失去捕获前挪动一丁点。

## 修法

`TrackGrid_PreviewMouseLeftButtonDown` 开头加来源判定 `IsOnScrollbarOrHeader`：
从 `e.OriginalSource` 沿可视树向上走，遇到 `ScrollBar` 或 `DataGridColumnHeader`
就直接返回，不武装任何手势（列标题顺带也排除了——在它上面框选没有意义）。

版本保持 1.0c（本轮修复尚未提交）。check_src.py 7 项全过。

## 待实机

拖动滚动条滑块应能正常翻页；滚动条轨道点击翻页也不应再触发框选。
