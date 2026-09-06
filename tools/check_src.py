# -*- coding: utf-8 -*-
"""
C# / XAML 源码静态自检。每次改完代码、交给用户编译之前跑一遍。

用法：
    python dependence/03_tools/check_src.py

为什么要这个：编译在用户那边的机器上跑，报错一来一回很慢。
下面这些检查能提前抓住绝大多数低级错误（XML 结构坏、括号不配平、
事件处理器写错名字、x:Name 和代码对不上、WPF API 用错签名）。
"""
import pathlib
import re
import sys
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[1] / "src"   # tools/ 上一级即仓库根

# XAML 里会出现的事件属性名
EVENT_ATTRS = (
    "Click", "SelectionChanged", "ValueChanged", "DragStarted", "DragCompleted",
    "Loaded", "Closing", "ContextMenuOpening", "PreviewKeyDown", "Checked", "Unchecked",
)

# ── 已知坑：构造函数的实参个数限制 ──────────────────────────────
# WPF 的 Thickness 只有 1 参（四边等宽）或 4 参（左/上/右/下），
# 没有 WinForms Padding 那种 2 参版本。
CTOR_ARITY = {
    "Thickness": (0, 1, 4),
}

fail = 0


def report(msg):
    global fail
    fail += 1
    print("  !! " + msg)


# 一趟交替匹配：字符串 / 字符字面量 / 注释谁先出现谁先消费。
# 不能分四趟替换 —— 先剥 // 注释会把字符串里的 "pack://..." 当成注释起点（Theme.cs 误报过一次）。
_NOISE = re.compile(
    r'"(?:\\.|[^"\\])*"'          # 字符串
    r"|'(?:\\.|[^'\\])'"          # 字符字面量
    r"|//[^\n]*"                  # 行注释
    r"|/\*.*?\*/",                # 块注释
    re.S)


def strip_noise(src):
    """去掉注释、字符串、字符字面量，免得干扰括号计数。"""
    def repl(m):
        t = m.group(0)
        if t.startswith('"'):
            return '""'
        if t.startswith("'"):
            return "''"
        return ""   # 注释整段抹掉
    return _NOISE.sub(repl, src)


def main():
    if not ROOT.is_dir():
        print("找不到源码目录：%s" % ROOT)
        return 1

    srcs = [(f, f.read_text(encoding="utf-8-sig"))
            for f in sorted(ROOT.rglob("*.cs"))
            if "obj" not in f.parts and "bin" not in f.parts]
    xamls = [f for f in sorted(ROOT.rglob("*.xaml"))
             if "obj" not in f.parts and "bin" not in f.parts]
    code = "".join(s for _, s in srcs)

    print("=== 1. XAML 能否通过 XML 解析 ===")
    for f in xamls:
        try:
            ET.parse(str(f))
            print("  OK  %s" % f.relative_to(ROOT))
        except Exception as e:
            report("%s -> %s" % (f.relative_to(ROOT), e))

    print()
    print("=== 2. C# 括号配平 ===")
    for f, s in srcs:
        t = strip_noise(s)
        cnt = {c: t.count(c) for c in "(){}[]"}
        if not (cnt["("] == cnt[")"] and cnt["{"] == cnt["}"] and cnt["["] == cnt["]"]):
            report("%s  ( ) %d/%d   { } %d/%d   [ ] %d/%d" % (
                f.relative_to(ROOT), cnt["("], cnt[")"],
                cnt["{"], cnt["}"], cnt["["], cnt["]"]))
    if fail == 0:
        print("  全部配平")

    print()
    print("=== 3. 构造函数实参个数（WPF 特有的签名坑）===")
    hits = 0
    for f, s in srcs:
        for name, allowed in CTOR_ARITY.items():
            for m in re.finditer(r"new\s+%s\s*\(([^()]*)\)" % name, s):
                n = len([a for a in m.group(1).split(",") if a.strip()])
                if n not in allowed:
                    hits += 1
                    line = s[:m.start()].count("\n") + 1
                    report("%s:%d  new %s 传了 %d 个参数（允许 %s）"
                           % (f.relative_to(ROOT), line, name, n, allowed))
    if hits == 0:
        print("  全部合法")

    print()
    print("=== 4. XAML 事件处理器是否都有对应方法 ===")
    missing = []
    for x in xamls:
        for m in re.finditer(r'(?:%s)\s*=\s*"([A-Za-z_][A-Za-z0-9_]*)"' % "|".join(EVENT_ATTRS),
                             x.read_text(encoding="utf-8-sig")):
            n = m.group(1)
            if not re.search(r"void\s+" + re.escape(n) + r"\s*\(", code):
                missing.append((str(x.relative_to(ROOT)), n))
    if missing:
        for f, n in missing:
            report("%s 里的 %s 在代码中找不到" % (f, n))
    else:
        print("  全部有对应方法")

    print()
    print("=== 5. 逐文件核对 x:Name 与代码引用 ===")
    for x in xamls:
        cs = x.with_suffix(".xaml.cs")
        if not cs.exists():
            continue
        names = re.findall(r'x:Name="([A-Za-z_][A-Za-z0-9_]*)"', x.read_text(encoding="utf-8-sig"))
        miss = [n for n in names if not re.search(r"\b" + re.escape(n) + r"\b",
                                                  cs.read_text(encoding="utf-8-sig"))]
        tag = "%s -> %s" % (x.relative_to(ROOT), cs.name)
        if miss:
            report("%s  未引用: %s" % (tag, ", ".join(miss)))
        else:
            print("  OK  %s  （%d 个命名控件）" % (tag, len(names)))

    print()
    print("=== 6. 供 STJ 反序列化的类型是否有公开无参构造 ===")
    # System.Text.Json 要求目标类型有**公开**的无参构造函数。
    # 写成 private 的话 Deserialize 抛 NotSupportedException，
    # 调用方若再有兜底 catch，就变成「文件存得好好的却永远读不回来」的静默失败。
    hits = 0
    for f, s in srcs:
        if "[JsonPropertyName" not in s:
            continue
        for m in re.finditer(r"\bclass\s+(\w+)", s):
            name = m.group(1)
            if re.search(r"private\s+" + re.escape(name) + r"\s*\(\s*\)", s):
                hits += 1
                report("%s  类型 %s 的无参构造是 private 的，JsonSerializer.Deserialize 会抛异常"
                       % (f.relative_to(ROOT), name))
    if hits == 0:
        print("  全部合法")

    print()
    print("=== 7. 已删除/改名的符号是否有残留引用 ===")
    for name in sys.argv[1:]:
        n = len(re.findall(r"\b" + re.escape(name) + r"\b", code))
        if n:
            report("%s 仍有 %d 处引用" % (name, n))
        else:
            print("  %-24s 已清理干净" % name)

    print()
    print("=" * 56)
    print("失败项 %d 处" % fail if fail else "全部通过")
    return 1 if fail else 0


if __name__ == "__main__":
    sys.exit(main())
