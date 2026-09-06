# 构建日志 8b：Thickness 没有两参构造

日期：2026-09-04

## 报错

```
UI/PromptDialog.cs(26,27): error CS7036
UI/PromptDialog.cs(36,27): error CS7036
UI/PromptDialog.cs(43,27): error CS7036
    未提供与“Thickness.Thickness(double, double, double, double)”的所需参数“right”对应的参数
```

## 原因

WPF 的 `System.Windows.Thickness` **只有 1 参（四边等宽）和 4 参（左/上/右/下）构造**，
没有 2 参、3 参版本：

```csharp
public Thickness(double uniformLength)
public Thickness(double left, double top, double right, double bottom)
```

我给 `Padding` 传了 `new Thickness(5, 4)` —— 这是把 WinForms `Padding` 的习惯
（1/2/4 参都有）带过来了。`Margin` 我写的是 4 参所以没事，三处 `Padding` 全错。

顺便一提，**WPF 里 `Margin` 和 `Padding` 都是 `Thickness`**（不是 WinForms 那种
`Padding` 独立类型），所以两个属性共用同一套构造限制。

## 修法

```csharp
Padding = new Thickness(5, 4)        // ✗
Padding = new Thickness(5, 4, 5, 4)  // ✓
Padding = new Thickness(4)           // ✓ 四边等宽时用这个更省事
```

## 自检补了一条

静态检查加了一项：扫所有 `new Thickness(...)` 的实参个数，只允许 0 / 1 / 4：

```python
re.finditer(r'new Thickness\(([^()]*)\)', src)
# len(args) not in (0, 1, 4) → 报错
```

目前全项目 6 处调用全部合法。

## 顺带说明

这 3 个是**方法体绑定期**错误，说明本轮没有声明期错误——
不像上一轮那样会有「声明期错误压住后面的错误」的情况，
所以修完这三处即可，不会有隐藏的后续报错。
