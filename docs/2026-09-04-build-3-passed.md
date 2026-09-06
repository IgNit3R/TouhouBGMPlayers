# 构建日志 · 第 3 轮：第一步骨架编译通过

（按红线不改写已有文件，另开新文件记录。）

## 结果

```
ThbgmPlayer net10.0-windows 已成功 (0.5 秒)
  → bin\Debug\net10.0-windows\ThbgmPlayer.dll
```

## 产物

`bin\Debug\net10.0-windows\`：

- `ThbgmPlayer.exe` 159K、`ThbgmPlayer.dll` 67K、`*.deps.json`、`*.runtimeconfig.json`
- NAudio 3.0.1 子包共 8 个 dll：`NAudio` / `NAudio.Core` / `NAudio.Wasapi` / `NAudio.WinMM` /
  `NAudio.Asio` / `NAudio.Dmo` / `NAudio.Midi` / `NAudio.WinForms`，外加 `System.Numerics.Tensors`
- 框架依赖（不自带运行时），产物就是一个文件夹，符合便携要求

## 内嵌索引验证（重要）

无法运行 dotnet，改用 Python 直接从 dll 里抠出资源验证：

```python
p   = d.find(b'\x1f\x8b\x08')
raw = zlib.decompressobj(16 + zlib.MAX_WBITS).decompress(d[p:])
```

**坑**：不能用 `gzip.decompress(d[p:])`。它读完第一个 member 后会继续把尾部字节当下一个
member 解析，报 `BadGzipFile: Not a gzipped file`。嵌入式资源后面还有别的字节，必须单次解压。

验证结果：

| 项 | 值 |
|---|---|
| JSON 解压后 | 37,594 字节 |
| version | 1 |
| 作品数 | 21 |
| 可见曲目 | 333 |
| 灵界版（TH13 alt） | 13 |
| 合计 | 346 ✓ |

样例：

- `TH06 source=wav 17 首`，首轨 `s=122`（wav 头长，不固定，已按实际头长记录）
- `TH13 source=zwav 18 首`，首轨 `s=16`（ZWAV 头），`i=3853696 l=10812928`
- `TH20 source=zwav 19 首`，首轨 `s=16`

另注：dll 里搜到的 `TH13` 字符串来自 MainWindow 的 XAML ToolTip
（"切换到灵界版（仅 TH13 部分曲目可…"），**不是** JSON —— JSON 是压缩的，明文搜不到。

## 下一步

第一步（骨架 + 内嵌索引 + 路径设置与校验）完成，等用户运行验收：

1. 21 作默认全部灰显
2. TH13 指到 `tsa\th13` → 变青，显示「18 首」，且能列出曲目
3. TH06 指到 `tsa\kouma`（**不是** `kouma\bgm`）→ 走 wav 分支，识别出 17 首
4. 故意把某作指到别的作品目录 → BCD 作品号校验应报「版本不符」

验收通过后进入第二步：播放内核（IAudioSource + 多源调度 + 三模式循环）。
