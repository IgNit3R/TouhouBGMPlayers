using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ThbgmPlayer.Core;

namespace ThbgmPlayer.Data;

/// <summary>
/// 一个自定义播放列表。允许跨作品混排（DESIGN_v3.md §5.3）。
/// 条目统一存成 "th13:16" 这样的短串，便于用户用记事本直接编辑。
/// </summary>
public sealed class CustomPlaylist
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";

    [JsonPropertyName("items")] public List<string> Items { get; set; } = new();

    [JsonIgnore] public int Count => Items.Count;
}

/// <summary>
/// 收藏与自定义列表的持久化。
///
/// 存在程序目录下的 <c>favorites.json</c> / <c>playlists.json</c>，不写 AppData（DESIGN_v3.md §2）。
/// 条目一律是 <see cref="TrackRef"/>（作品代号 + 曲序），**不存文件路径** ——
/// 用户改了路径设置之后，列表依然有效。
/// </summary>
public static class PlaylistStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static List<CustomPlaylist> _lists = new();
    private static List<TrackRef> _favorites = new();

    public static IReadOnlyList<CustomPlaylist> Lists => _lists;
    public static IReadOnlyList<TrackRef> Favorites => _favorites;

    /// <summary>启动时调用一次。文件不存在或内容损坏都当作空，不抛异常。</summary>
    public static void Load()
    {
        var rawFav = JsonLoad<List<string>>(AppPaths.FavoritesFile);
        _favorites = new List<TrackRef>();
        if (rawFav is not null)
        {
            foreach (var s in rawFav)
            {
                var r = TrackRef.Parse(s);
                if (!r.IsEmpty) _favorites.Add(r);
            }
        }

        _lists = JsonLoad<List<CustomPlaylist>>(AppPaths.PlaylistsFile) ?? new List<CustomPlaylist>();

        // 名字可能被手工改坏，兜底补一个，否则界面上会出现空白项
        for (int i = 0; i < _lists.Count; i++)
        {
            _lists[i].Items ??= new List<string>();
            if (string.IsNullOrWhiteSpace(_lists[i].Name)) _lists[i].Name = $"列表 {i + 1}";
        }
    }

    private static T? JsonLoad<T>(string file) where T : class
    {
        try
        {
            if (File.Exists(file))
                return JsonSerializer.Deserialize<T>(File.ReadAllText(file), JsonOpts);
        }
        catch
        {
            // 同上：损坏就当没有，下次保存会覆盖
        }
        return null;
    }

    /// <summary>两个文件一起写。目录不可写时返回 false，由调用方提示。</summary>
    public static bool Save()
    {
        try
        {
            AppPaths.WriteTextAtomic(AppPaths.FavoritesFile,
                JsonSerializer.Serialize(_favorites.Select(f => f.ToString()).ToList(), JsonOpts));
            AppPaths.WriteTextAtomic(AppPaths.PlaylistsFile, JsonSerializer.Serialize(_lists, JsonOpts));
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ---------- 自定义列表 ----------

    public static CustomPlaylist CreateList(string name)
    {
        var list = new CustomPlaylist { Name = name };
        _lists.Add(list);
        Save();
        return list;
    }

    public static void RenameList(CustomPlaylist list, string name)
    {
        list.Name = name;
        Save();
    }

    public static void DeleteList(CustomPlaylist list)
    {
        _lists.Remove(list);
        Save();
    }

    /// <summary>追加到末尾，已存在的跳过。返回实际新增条数。</summary>
    public static int AddToList(CustomPlaylist list, IEnumerable<TrackRef> refs)
    {
        var have = new HashSet<string>(list.Items, StringComparer.OrdinalIgnoreCase);
        int added = 0;

        foreach (var r in refs)
        {
            if (r.IsEmpty) continue;
            if (!have.Add(r.ToString())) continue;
            list.Items.Add(r.ToString());
            added++;
        }

        if (added > 0) Save();
        return added;
    }

    public static int RemoveFromList(CustomPlaylist list, IEnumerable<TrackRef> refs)
    {
        var kill = new HashSet<string>(refs.Select(r => r.ToString()), StringComparer.OrdinalIgnoreCase);
        int n = list.Items.RemoveAll(s => kill.Contains(s));
        if (n > 0) Save();
        return n;
    }

    /// <summary>
    /// 把列表里 from 位置的条目挪到 to 处（to 是**原列表**里的插入位，0..Count）。
    /// 拖动排序用。to == from 或 to == from+1 等于没动。
    /// </summary>
    public static void MoveInList(CustomPlaylist list, int from, int to)
    {
        if (from < 0 || from >= list.Items.Count) return;
        to = Math.Clamp(to, 0, list.Items.Count);
        if (to == from || to == from + 1) return;

        var s = list.Items[from];
        list.Items.RemoveAt(from);
        if (to > from) to--;
        list.Items.Insert(to, s);
        Save();
    }

    // ---------- 收藏 ----------

    public static bool IsFavorite(TrackRef r) => !r.IsEmpty && _favorites.Contains(r);

    /// <summary>收藏列表内的拖动排序，语义同 <see cref="MoveInList"/>。</summary>
    public static void MoveFavorite(int from, int to)
    {
        if (from < 0 || from >= _favorites.Count) return;
        to = Math.Clamp(to, 0, _favorites.Count);
        if (to == from || to == from + 1) return;

        var r = _favorites[from];
        _favorites.RemoveAt(from);
        if (to > from) to--;
        _favorites.Insert(to, r);
        Save();
    }

    /// <summary>整批设置收藏状态。已经是的跳过，避免重复添加。</summary>
    public static void SetFavorite(IEnumerable<TrackRef> refs, bool favorite)
    {
        bool changed = false;

        foreach (var r in refs)
        {
            if (r.IsEmpty) continue;
            bool has = _favorites.Contains(r);

            if (favorite && !has) { _favorites.Add(r); changed = true; }
            else if (!favorite && has) { _favorites.Remove(r); changed = true; }
        }

        if (changed) Save();
    }
}
