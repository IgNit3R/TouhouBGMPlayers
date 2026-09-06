# build 37 —— ApplyContentFont 的最终形态（更正 build 36 的说法）

2026-09-06

build 36 把参数改成 Control 是**错的**：TextBlock 不继承 Control
（它直接继承 FrameworkElement），MainWindow 两处调用编译失败。

WPF 字体属性的正确知识：
- `Control.FontFamilyProperty` 与 `TextBlock.FontFamilyProperty` 是**同一个依赖属性** ——
  TextElement 定义，Control 经 AddOwner 注册
- 所以 `TextElement.SetFontFamily(el, f)` 对 Control 和 TextBlock 都生效，
  参数用 FrameworkElement 即可，不用按类型分派

最终形态：`ApplyContentFont(FrameworkElement)` + `TextElement.SetFontFamily`。
check_src.py 7 项全过。
