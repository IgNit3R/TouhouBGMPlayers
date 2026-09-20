# -*- coding: utf-8 -*-
"""
读取用户用 Excel 编辑后的 REVIEW_TITLES.csv（实为 xlsx 包），
与 tracklist.csv 的原始生成值对比，列出所有改动。
"""
import csv
import pathlib
import re
import zipfile
import xml.etree.ElementTree as ET

NS = "{http://schemas.openxmlformats.org/spreadsheetml/2006/main}"
DEP = pathlib.Path(__file__).resolve().parents[1]   # .../bgmplayer（tools/ 的上一级）
XLSX = DEP / "docs" / "REVIEW_TITLES.csv"
BASE = DEP / "docs" / "tracklist.csv"


def col_idx(ref):
    """A1 -> 0, B2 -> 1"""
    m = re.match(r"([A-Z]+)", ref)
    n = 0
    for ch in m.group(1):
        n = n * 26 + (ord(ch) - 64)
    return n - 1


def read_xlsx(path):
    z = zipfile.ZipFile(path)
    # 共享字符串
    shared = []
    root = ET.fromstring(z.read("xl/sharedStrings.xml"))
    for si in root.findall(f"{NS}si"):
        shared.append("".join(t.text or "" for t in si.iter(f"{NS}t")))
    # 工作表
    ws = ET.fromstring(z.read("xl/worksheets/sheet1.xml"))
    rows = []
    for row in ws.iter(f"{NS}row"):
        cells = {}
        for c in row.findall(f"{NS}c"):
            ref = c.get("r")
            t = c.get("t")
            v = c.find(f"{NS}v")
            if v is None:
                txt = ""
            elif t == "s":
                txt = shared[int(v.text)]
            else:
                txt = v.text or ""
            cells[col_idx(ref)] = txt
        if cells:
            rows.append(cells)
    return rows


def load_review(path):
    """自动识别：Excel 另存会把 .csv 变成 xlsx 包（PK 头），这里两种都支持。
    同时兼容 UTF-8 / UTF-8-BOM / GBK / Shift-JIS 文本。
    返回 (header, data)"""
    raw = pathlib.Path(path).read_bytes()
    if raw[:2] == b"PK":                      # xlsx 包
        rows = read_xlsx(path)
        header = [rows[0].get(i, "") for i in range(max(rows[0]) + 1)]
        data = [{header[i]: r.get(i, "") for i in range(len(header))}
                for r in rows[1:]]
        return header, data
    for enc in ("utf-8-sig", "gbk", "cp932"):  # 纯文本
        try:
            txt = raw.decode(enc)
            break
        except UnicodeDecodeError:
            continue
    else:
        raise UnicodeDecodeError("无法识别编码", raw[:32], 0, 1, "")
    rd = csv.DictReader(txt.splitlines())
    return list(rd.fieldnames or []), list(rd)


def main():
    header, data = load_review(XLSX)
    print("表头:", header)

    base = {}
    for r in csv.DictReader(open(BASE, encoding="utf-8-sig")):
        base[(r["game"], int(r["index"]))] = r["title"]

    print(f"数据行数: {len(data)}")
    print()
    print("=== 与原始生成值不同的行 ===")
    n = 0
    for r in data:
        g, idx = r["game"], int(r["index"])
        orig = base.get((g, idx), "<缺失>")
        if r["my_title"] != orig:
            n += 1
            print(f"{g:<7}#{idx:>3}  {r['file']}")
            print(f"    原   : {orig}")
            print(f"    改为 : {r['my_title']}")
            print(f"    参考表: {r['ref_title']}")
            print()
    print(f"共修改 {n} 行")

    # 检查是否有空曲名
    empty = [r for r in data if not r["my_title"].strip()]
    if empty:
        print(f"\n!! 空曲名 {len(empty)} 行:")
        for r in empty[:10]:
            print(f"   {r['game']}#{r['index']} {r['file']}")

    # 检查行数是否一致
    if len(data) != len(base):
        print(f"\n!! 行数不符：xlsx {len(data)} / 原始 {len(base)}")


if __name__ == "__main__":
    main()
