# 构建日志 18：设置持久化修复确认

日期：2026-09-06

## 用户确认

> 这次配置保存了

## 磁盘验证（02:02）

```json
"paths": { "th06": "...\\kouma", "th11": "...\\th11" },
"playback": { "loopMode": 0, "loopCount": 1, "extraSeconds": 10,
              "fadeSeconds": 5, "volume": 1, "fadeOnSeekSeconds": 0.03 },
"export":   { "loopCount": null, "extraSeconds": null, "fadeSeconds": null, "directory": "...\\export" },
"lastPlayed": { "game": "th06", "trackNo": 9 }
```

- `settings.load-error.txt` **不存在** → 反序列化正常（private 构造那个坑已填）
- 无 `.tmp` 残留 → 原子写正常

## 两个修复都被验证了

这轮的证据很干净，因为**两个 bug 各有各的指纹**：

| 字段 | 值 | 证明了什么 |
|---|---|---|
| `volume: 1` | XAML 默认是 **0.8** | `_ready` 开关生效 —— 否则会被冲回 0.8 |
| `loopCount: 1`、`extra: 10`、`fade: 5` | 都不是默认值 | 播放参数不再被覆盖 |
| `paths` 有两条 | 上一轮是 `{}` | 反序列化成功（公开构造函数那个坑已填） |
| `lastPlayed: th06 #9` | 上一轮是 `""` | **整条链路通了** —— 写入 → 保存 → 重开 → 读回 |

`lastPlayed` 这条最有说服力：它只在播放时写、启动时读，
之前永远是空的，现在有值了，说明读和写都真的在工作。

## 复盘

这个 bug 藏了两轮，原因是我第一次只解释了「部分」现象就收手了：
「XAML 默认值覆盖」能解释循环模式和音量，却解释不了路径。
当时应该追问一句「路径为什么也没了」，而不是接受一个不完整的解释。

**判断依据一定要能覆盖全部现象，不能只覆盖大部分。**

另外「静默 catch」是这次最大的帮凶 —— 异常被吞掉，
现场一闪即逝，只能靠对比文件大小反推。现在兜底会写
`settings.load-error.txt`，同类问题不会再这么难查。

## 项目状态

按 DESIGN_v3.md 的全部功能均已落地且实机验证通过：
播放内核 / 列表体系 / 导出 / 设置持久化。

唯一未实现：`TrackIndex.ApplyOverride`（外部覆盖索引，设计里的 L1 层），
用户同意先不做。
