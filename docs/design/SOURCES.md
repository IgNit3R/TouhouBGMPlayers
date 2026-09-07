# 来源核实与修订

> 日期：2026-09-04
> 用户限定社区参考来源：**thwiki / thparc / thcrap**
> 本篇**覆盖** `RESEARCH.md` 中被非白名单来源支撑的部分（受"不得改写已有文件"约束，以新建修订篇方式发布）。
> `RESEARCH.md` 中基于**本地实测**的结论继续有效。

---

## 一、来源可达性

| 来源 | 状态 | 说明 |
|---|---|---|
| thcrap（thpatch） | ✅ 可达 | 通过 GitHub API + raw 取到 `thcrap_bgmmod` 源码 |
| thwiki（en.touhouwiki.net） | ✅ 可达 | `Technical_Information/BGM` 拿到 FMT 权威字段定义 |
| thwiki.cc（中文） | ❌ 被 WAF 拦截 | 返回 chaitin WAF 拦截页，未能取到内容 |
| thparc | ❓ 无法确认 | GitHub 检索无此项目（仅两个无关同名近似仓库）。**待用户确认** |

---

## 二、修订一：`+0x14` 应视为未定义字段（重要）

**Touhou Wiki 原文**（Technical_Information/BGM → TH07 and above）：

```
Format: { <wav name>.16b <start offset>.4b <???>.4b <intro length>.4b
          <total length>.4b <RIFF WAVEfmt header (WAVEFORMATEX structure)>.18b 0000 }.52b*
```

字段映射：

| 偏移 | 大小 | Touhou Wiki | 我的实测 |
|---|---|---|---|
| +0x00 | 16 | wav name | ASCII，NUL 填充 |
| +0x10 | 4 | **start offset** | 绝对偏移，首轨 = 0x10 |
| +0x14 | 4 | **`???`（未定义）** | th13/th18 恒 = total；**th08 恒 > total（越界）** |
| +0x18 | 4 | **intro length** | 循环起点，可靠 |
| +0x1C | 4 | **total length** | 本轨总长，可靠 |
| +0x20 | 18 | WAVEFORMATEX | 44100/16/2ch；th13 灵界版 22050Hz |
| +0x32 | 2 | 0000 | 对齐 |

**结论修正**：`RESEARCH.md` 曾倾向把 `+0x14` 判为 loop_end。虽然该解释在数值上说得通（th13/th18 上它恰等于 total），但权威文档将其列为**未定义**，且实测 th08 上它会越界。

**播放器应改为以 `+0x18` / `+0x1C` 为准，`+0x14` 完全忽略**：

```
intro_len = +0x18
total_len = min(+0x1C, thbgm.dat 大小 - start)   # 末轨按文件剩余截断
loop_len  = total_len - intro_len

intro 段 = [start, start + intro_len)
loop  段 = [start + intro_len, start + total_len)  # 无限循环
```

这与 thcrap 的实现完全一致（见下），且无需为 `+0x14` 引入任何退化分支——**更简洁也更可靠**。
（若日后发现某作存在"轨尾不参与循环"的段落，再考虑把 `+0x14` 作为可选修正引入。）

---

## 三、thcrap_bgmmod：权威播放模型

`thcrap_bgmmod/src/bgmmod.hpp`（thpatch 官方）给出的抽象，与上述两段模型**完全吻合**：

```cpp
struct track_t {
    const pcm_format_t pcmf;
    // Sizes are in decoded PCM bytes according to [pcmf].
    const size_t intro_size;
    const size_t total_size;

    // Single decoding call that also handles looping...
    virtual size_t decode_single(void *buf, size_t size) = 0;
    // Seeks to the raw decoded audio byte, according to [pcmf].
    // Can get arbitrarily large, therefore the looping section
    // should be treated as infinitely long.
    virtual void seek_to_byte(size_t byte) = 0;
    // *Always* fills [buf] entirely...
    bool decode(void *buf, size_t size);
};

struct track_pcm_t : public track_t {
    std::unique_ptr<pcm_part_t> intro;
    std::unique_ptr<pcm_part_t> loop;
    // total_size = intro->part_bytes + loop->part_bytes
};
```

可直接借鉴的设计要点：

1. **intro / loop 是两个独立 part**，`total_size = intro + loop` —— 与 `titles_th` 的 `(start, intro_len, loop_len)` 同构
2. **循环段视为无限长**（`seek_to_byte` 注释原话），seek 可超出 total_size
3. `decode_single()` 允许返回**少于**请求的字节数（跨越 part 边界时），由 `decode()` 负责填满整个 buffer —— 这是流式播放的关键契约
4. 解码失败时**填零**而非中断
5. `LOOP_INFIX` 常量：循环文件用命名中缀区分（如 `*_loop.*`）
6. `stack_bgm_resolve(basename)`：按 basename 在补丁栈中解析曲目，支持替换
7. 支持的编解码器（`CODECS[3]`）：FLAC（dr_flac）、**Ogg Vorbis**（libogg/libvorbis/libvorbisfile）、MP3（libmpg123）

---

## 四、修订二：tf 侧与 th06 的准确信息

Touhou Wiki 提供了比 `RESEARCH.md` 更准确的细节，**覆盖**原先基于目录猜测的内容：

### th06 / 东方红魔乡

- wave 文件在 `bgm/*.wav`
- 循环点在 **`紅魔郷MD.dat` / `*.pos`**，格式：`<start sample>.4b <end sample>.4b`（**单位：样本，非字节**）

### th07.5 / th075 萃梦想

- wave 文件与循环数据都在 **`th075bgm.dat` / `*.wav`**（Brightmoon 归档）
- **循环点以特殊 RIFF chunk 形式存在每个 wave 文件末尾**：
  `<RIFF header>.40b <wave data size>.4b <wave data>.<size>b <junk>.16b <start sample>.4b <junk>.40b <loop section length>.4b`

### TH10.5 及以上（黄昏边境 tf 侧）

- **ogg 文件在 `th<xxx>b.dat` / `*.ogg`**（注意是 **b** 包，不是 a 包）
- 循环点在 **`*.sfl`** 文件，存放于 **Soundforge 元数据**中
  格式：`<junk>.28b <start sample>.4b <junk>.40b <length in samples>.4b`
- **部分曲目没有循环点**，播放器需能优雅处理无循环情况

### 换算规则（原文）

> All wave files are 44100 Hz 16 bit stereo, except the alternate *spirit world* themes in Ten Desires, which are 22050 Hz. When dealing with sample-based loop data (th06/Twilight Frontier games), **multiply the sample values by 4** and, in case of IaMP, **add the size of the wave header (0x2C = 44 bytes)** when calculating byte offsets.

即：样本 → 字节 = **× 4**（2 声道 × 16bit）；th075 还需 **+ 44**（wave 头）。

### MIDI 循环（th06–th08 及 th09 体验版）

- 循环由 MIDI 控制器事件定义：**#cc02（Breath Controller）= 循环起点，#cc04（Foot Controller）= 循环终点**

---

## 五、仍未解决

- **thparc 是什么**：无法检索到，需用户确认（是否指 thprac？或某个特定资源/站点）
- **thwiki.cc 中文站**：被 WAF 拦截，如需其中内容（如 `腳本對照表/其他#FMT文件`）需另想办法
- 播放器形态、一期范围仍待用户决定
