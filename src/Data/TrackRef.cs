namespace ThbgmPlayer.Data;

/// <summary>
/// 一首曲子的引用。只存 (作品代号, 曲序)，**不存文件路径** ——
/// 这样用户改了路径设置之后，自定义列表和收藏依然有效（DESIGN_v3.md §5.3）。
/// </summary>
public readonly record struct TrackRef(string GameId, int TrackNo)
{
    /// <summary>未指向任何曲目。</summary>
    public static TrackRef Empty => new("", 0);

    public bool IsEmpty => string.IsNullOrEmpty(GameId);

    public override string ToString() => IsEmpty ? "—" : $"{GameId}:{TrackNo}";

    /// <summary>
    /// 解析 <see cref="ToString"/> 的输出（形如 "th13:16"）。
    /// 格式不对时返回 <see cref="Empty"/> —— 手工编辑过的 json 不该让程序起不来。
    /// </summary>
    public static TrackRef Parse(string? s)
    {
        if (string.IsNullOrEmpty(s)) return Empty;
        int i = s.IndexOf(':');
        if (i <= 0) return Empty;
        return new TrackRef(s[..i], int.TryParse(s[(i + 1)..], out var n) ? n : 0);
    }
}
