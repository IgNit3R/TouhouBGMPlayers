using System.Buffers.Binary;

namespace ThbgmPlayer.Audio;

/// <summary>
/// Vorbis（Ogg 容器）的**页级瞬时码率统计**：逐 Ogg 页算"页字节 × 8 ÷ 页样本/采样率"，纯算术。
///
/// **无 IO、无解码** —— 吃的就是 <c>TfContainerSet.GetEntry</c> 解出的整条 OGG 字节
/// （与播放同一条取字节路径），所以可以被自检离屏驱动（<c>UI/BitrateSelfTest</c> 用合成 Ogg 断言）。
/// Python 参考实现：`.workbuddy/tools/vorbis_page_bps_probe.py`（196 首实测已落
/// `.workbuddy/vorbis_page_bps_196.csv`，移植时算法逐行对齐，含线性插值分位）。
///
/// 页结构（只用到这三个量）：
/// <list type="bullet">
/// <item>granule：页头偏移 +6 的 i64 LE，本页末样本的绝对位置；页样本 = 本页与前页 granule 之差。</item>
/// <item>页段数：偏移 +26；段表紧随其后，每段一字节（0~255），页内包长 = 段值之和（255 表示跨页包）。</item>
/// <item>页字节数 = 27 + 段数 + 段值和。</item>
/// </list>
/// 头两页（identification / comment+setup）granule 为 0 ⇒ 样本增量为 0，天然被跳过。
/// </summary>
public static class VorbisPages
{
    /// <summary>
    /// 逐页统计瞬时码率。非 Ogg / 头里读不出采样率 / 一个有效页都没有 → <c>null</c>
    /// （调用方按"探不出"处理，面板占位保留）。
    /// </summary>
    public static BitrateStats? Scan(byte[] ogg)
    {
        ArgumentNullException.ThrowIfNull(ogg);
        if (ogg.Length < 58 || !ogg.AsSpan(0, 4).SequenceEqual("OggS"u8))
            return null;

        int sr = ReadSampleRate(ogg);
        if (sr <= 0) return null;

        var vals = new List<double>();
        int i = 0;
        long prev = -1;
        while (i + 27 <= ogg.Length && ogg.AsSpan(i, 4).SequenceEqual("OggS"u8))
        {
            long gran = BinaryPrimitives.ReadInt64LittleEndian(ogg.AsSpan(i + 6));
            int n = ogg[i + 26];

            long size = 27 + n;
            for (int s = 0; s < n; s++) size += ogg[i + 27 + s];
            if (size <= 0 || i + size > ogg.Length) break;   // 截断 / 脏数据：到此为止

            if (prev >= 0 && gran > prev)
                vals.Add(size * 8.0 / ((gran - prev) / (double)sr));

            prev = gran;
            i += (int)size;
        }

        if (vals.Count == 0) return null;
        vals.Sort();

        return new BitrateStats(
            vals[0], Pct(vals, 0.05), Pct(vals, 0.25), Pct(vals, 0.50),
            Pct(vals, 0.75), Pct(vals, 0.95), vals[^1],
            vals.Average(), vals.Count, isVbr: true);
    }

    /// <summary>
    /// 从首页第一个包里读 Vorbis identification header 的采样率。
    /// 布局（docs/2026-09-30-codec-container-naming.md §7.3）：包内 "\u0001vorbis" 七字节之后
    /// 再跳 5 字节（version u32 + channels u8）就是 audio_sample_rate u32 LE。
    /// identification header 固定 30 字节，一定完整落在第一页里。
    /// </summary>
    private static int ReadSampleRate(byte[] ogg)
    {
        int n = ogg[26];
        int body = 27 + n;
        int sum = 0;
        for (int s = 0; s < n; s++) sum += ogg[27 + s];

        // 只在首页范围内找魔数，不搜整个文件（防止误把载荷当头）
        int pageEnd = Math.Min(ogg.Length, body + sum);
        ReadOnlySpan<byte> head = ogg.AsSpan(0, pageEnd);
        int j = head.IndexOf("\u0001vorbis"u8);
        if (j < 0) return -1;

        int k = j + 7;
        if (k + 9 > head.Length) return -1;
        return BinaryPrimitives.ReadInt32LittleEndian(head.Slice(k + 5));
    }

    /// <summary>线性插值分位（与 Python 探针的 pct 一致）。vals 必须已升序排序、非空。</summary>
    private static double Pct(List<double> vals, double q)
    {
        if (vals.Count == 1) return vals[0];

        double pos = q * (vals.Count - 1);
        int lo = (int)pos;
        int hi = Math.Min(lo + 1, vals.Count - 1);
        return vals[lo] + (vals[hi] - vals[lo]) * (pos - lo);
    }
}
