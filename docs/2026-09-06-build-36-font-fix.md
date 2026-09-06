# build 36 —— 双字体编译错误修复（4 处）

2026-09-06

## 错误与修法

1. `Theme.ApplyContentFont(FrameworkElement)` —— **FontFamily 定义在 Control 上，
   FrameworkElement 没有**。参数类型改为 `System.Windows.Controls.Control`
   （TrackGrid / TextBlock 都是 Control，调用点不变）。
2. 三个对话框「FontFamily 初始化重复」—— build 34 批量插入时没注意到它们**本来就有**
   `FontFamily = owner.FontFamily`（继承主窗口字体，本来就是对的，主窗口已套用户字体）。
   移除我误加的行，保留原有的 owner 继承；ExportDialog 清单的曲名字体行保留。

教训：批量插入初始化器成员前，先 grep 目标成员是否已存在。

check_src.py 7 项全过。
