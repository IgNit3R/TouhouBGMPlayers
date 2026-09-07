# dependence —— 构建期依赖集中目录

> 用途：播放器项目所需的**数据、脚本、生成器输入**全在这里，与工作区其他部分解耦。
> 建立日期：2026-09-04

## 目录结构

```
dependence/
  01_design/          设计文档
    DESIGN_v3.md            唯一有效设计文档（取代 DESIGN.md / v2 与 _notes 全部笔记）
    LOG.md                  决策日志，含 brightmoon 授权结论与环境发现
    TITLE_EVIDENCE.md       曲名差异溯源（裁定规则 R1–R4）
    TITLES.md / SOURCES.md  曲名来源说明

  02_data/            定稿数据（**内嵌索引的数据源**）
    tracklist.csv           346 首，含 start/intro/total/rate/ch/bits 等 19 字段
    TRACKLIST.md            人类可读全表
    REVIEW.md               21 作分节核对表
    REVIEW_TITLES.csv       曲名核对表
    COMPARE.md              与 release 参考表比对结果

  03_tools/           脚本（路径全部改为相对定位，见下）
    gen_tracklist_v2.py     主生成器：csv + TRACKLIST.md
    gen_review.py           生成 REVIEW.md + REVIEW_TITLES.csv
    compare_tracklist.py    与 release/tsa/<作>/bgm 参考表比对
    verify_report.py        按 _bgm_report.md 校验曲目表
    read_review_edit.py     读取改过的 REVIEW_TITLES（自动识别 CSV/XLSX/GBK/SJIS）
    probe_th06*.py          th06 循环点探测（4 个）

  04_source/          生成器输入
    extract/<作>/thbgm.fmt        20 作（329~1629 B）
    extract/<作>/musiccmt.txt     21 作
    extract/th06/                 md.dat、*.pos、*.mid、musiccmt.txt（1.5 MB，整目录保留）
    BgmForAll.ini                 th06–th10 的曲名来源（34 KB，从 tools/ 复制）

  _gen/               脚本输出目录（重生成不覆盖 02_data 定稿）
```

## 路径机制

所有脚本已改为 **`__file__` 相对定位**，不再写死绝对路径：

```python
DEP  = pathlib.Path(__file__).resolve().parents[1]   # .../bgmplayer/dependence
ROOT = DEP.parents[1]                                 # .../thworks
```

因此整个 `dependence/` 可随项目搬移，只要与 `thworks/` 的相对位置不变即可正常工作。

## 未复制的外部依赖（红线原始数据，按路径访问）

| 依赖 | 体积 | 用途 |
|---|---|---|
| `thworks/tsa/kouma/bgm/*.wav` | **310 MB** | th06 生成器需读真实 wav 头长（118~166 B）。<br>运行时音频源也在这里，**不复制** |
| `thworks/release/tsa/` | 大 | `compare_tracklist.py` 比对用的参考表，仅重新核对时才需要 |

其余输入（fmt、musiccmt、th06 的 pos、BgmForAll.ini）已全部复制进 `04_source/`，
**从零重生成曲目表无需再解包**。

## 用法

```bash
cd dependence
python 03_tools/gen_tracklist_v2.py    # → _gen/tracklist.csv、_gen/TRACKLIST.md（346 首）
python 03_tools/verify_report.py       # 校验，应输出「全部通过」
python 03_tools/gen_review.py          # → _gen/REVIEW.md、_gen/REVIEW_TITLES.csv
python 03_tools/compare_tracklist.py   # 与 release 参考表比对
```

**注意**：脚本输出一律进 `_gen/`，不覆盖 `02_data/` 里的定稿数据。

## 验证记录（2026-09-04）

改路径后重跑 `gen_tracklist_v2.py`，产出的 `tracklist.csv`（347 行）与 `TRACKLIST.md`
与 `02_data/` 定稿**逐字节一致**；`verify_report.py` 全部通过。
