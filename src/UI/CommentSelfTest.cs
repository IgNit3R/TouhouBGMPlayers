using ThbgmPlayer.Data;
using ThbgmPlayer.Viz;

namespace ThbgmPlayer.UI;

/// <summary>
/// 音乐室评论索引的离屏自检。
///
/// ⚠️ 覆盖边界（与 WaveformSelfTest 同一条纪律）：这里验的全是「**决定**」——
/// 资源加载 / 查询命中与否 / 双语内容 / 成对性。TextBox 的观感（直角、窄条滚动、
/// 右键复制菜单、整列收起后的布局）要**真窗口**，自检验不了 ⇒ 人工验。
///
/// ja/zh 成对性的**全量校验在生成工具**（<c>tools/make_musiccmt_resource.py</c>，它手里才有原始 json）；
/// 这里只抽「已知空对」在运行时确实查不到。
///
/// 裏评（裏音楽コメント，当前仅 th06/07/08）两项：计数**快照断言**（内嵌 gz 若是没透传 omake
/// 的旧版当场红 —— 抓「改了乐评数据忘重跑生成工具」）+ 内容抽查（裏≠表、双语非复制、HasOmake 三态）。
/// </summary>
internal static class CommentSelfTest
{
    public static List<VizSelfTest.Result> Run() => new()
    {
        CheckLoaded(),
        CheckLookup(),
        CheckBilingual(),
        CheckEmptyPairs(),
        CheckAlignment(),
        CheckNewlines(),
        CheckOmakeCounts(),
        CheckOmakeContent(),
    };

    /// <summary>
    /// 资源加载：已加载作品数 ≥ 29，且**跨容器**抽查都能命中 ——
    /// 整数作（th06）、黄昏作（th075 Suica / th175 pak）、新典自定义 Opus（th06nc）各探一个，
    /// 只测一部的话，另外几条解码路径上索引键对不上就漏了。
    /// </summary>
    private static VizSelfTest.Result CheckLoaded()
    {
        const string Title = "乐评索引加载（作品数 / 跨容器抽查）";

        try
        {
            int games = CommentIndex.GameCount;

            bool th06 = CommentIndex.TryGetComment("th06", 1, out var c06);
            bool th075 = CommentIndex.TryGetComment("th075", 1, out var c075);
            bool th175 = CommentIndex.TryGetComment("th175", 1, out var c175);
            bool nc = CommentIndex.TryGetComment("th06nc", 1, out var cnc);
            bool th13 = CommentIndex.TryGetComment("th13", 12, out var c13);

            bool ok = games >= 29 && th06 && th075 && th175 && nc && th13;

            return new VizSelfTest.Result(Title, ok,
                $"作品数 {games}（期望 ≥ 29）；命中 th06#{1}={th06}、th075#1={th075}、th175#1={th175}、" +
                $"th06nc#1={nc}、th13#12={th13}（跨 4 类容器各探一曲）");
        }
        catch (Exception ex)
        {
            return new VizSelfTest.Result(Title, false, ex.GetType().Name + "：" + ex.Message);
        }
    }

    /// <summary>三态查询：普通曲命中；已知无评论的曲（原作没写）查不到；不存在的作品查不到。</summary>
    private static VizSelfTest.Result CheckLookup()
    {
        const string Title = "乐评查询（普通命中 / 空正文 / 无此作品）";

        try
        {
            bool hit = CommentIndex.TryGetComment("th06", 1, out var hit1) && hit1.Length > 0;
            bool emptyMiss = !CommentIndex.TryGetComment("th11", 18, out _);   // Player's Score：原作无评论
            bool unknownMiss = !CommentIndex.TryGetComment("th99", 1, out _);  // 没有这个作品

            bool ok = hit && emptyMiss && unknownMiss;

            return new VizSelfTest.Result(Title, ok,
                $"th06#1 命中且非空 = {hit}；th11#18（无评论）查不到 = {emptyMiss}；th99 查不到 = {unknownMiss}");
        }
        catch (Exception ex)
        {
            return new VizSelfTest.Result(Title, false, ex.GetType().Name + "：" + ex.Message);
        }
    }

    /// <summary>
    /// 双语口子：同一曲 Ja / Zh 都能取到、且内容**不同**（th075 的评论是真正的中日双语文本，
    /// 不是复制品 —— 复制品（灵界版等）这里验不了，那属于生成期校验）。
    /// </summary>
    private static VizSelfTest.Result CheckBilingual()
    {
        const string Title = "乐评双语（Ja/Zh 都非空且不同 / 默认语言为 Ja）";

        try
        {
            bool ja = CommentIndex.TryGetComment("th075", 3, CommentLanguage.Ja, out var jaText);
            bool zh = CommentIndex.TryGetComment("th075", 3, CommentLanguage.Zh, out var zhText);

            bool both = ja && zh;
            bool different = both && jaText != zhText;
            bool defaultJa = CommentIndex.CurrentLanguage == CommentLanguage.Ja;

            bool ok = both && different && defaultJa;

            return new VizSelfTest.Result(Title, ok,
                $"th075#3：Ja {jaText.Length} 字 / Zh {zhText.Length} 字，两者不同 = {different}；" +
                $"CurrentLanguage 默认 Ja = {defaultJa}");
        }
        catch (Exception ex)
        {
            return new VizSelfTest.Result(Title, false, ex.GetType().Name + "：" + ex.Message);
        }
    }

    /// <summary>空对在**两种语言下**都查不到（成对性的运行时抽查；全量校验在生成工具里）。</summary>
    private static VizSelfTest.Result CheckEmptyPairs()
    {
        const string Title = "乐评空对（无评论曲两种语言都查不到）";

        try
        {
            bool th1118 = !CommentIndex.TryGetComment("th11", 18, CommentLanguage.Ja, out _)
                          && !CommentIndex.TryGetComment("th11", 18, CommentLanguage.Zh, out _);
            bool th1257 = !CommentIndex.TryGetComment("th125", 7, CommentLanguage.Ja, out _)
                          && !CommentIndex.TryGetComment("th125", 7, CommentLanguage.Zh, out _);

            bool ok = th1118 && th1257;

            return new VizSelfTest.Result(Title, ok,
                $"th11#18 Ja/Zh 都查不到 = {th1118}；th125#7 Ja/Zh 都查不到 = {th1257}");
        }
        catch (Exception ex)
        {
            return new VizSelfTest.Result(Title, false, ex.GetType().Name + "：" + ex.Message);
        }
    }

    /// <summary>
    /// 🔑 永久对齐校验：内嵌索引里**每一条**的 title_ja（规范化后）都必须与
    /// <see cref="TrackIndex"/> 同 No 的曲目标题一致。
    ///
    /// 背景：乐评 json 的键序（曲目表行序）与播放器的 musicNo 序在尾部曲目/霊界版穿插处**不是同一个序**，
    /// 曾导致 85 条评论挂错曲（生成工具 v2 起按标题重映射修复）。这条断言就是防复发的闸门 ——
    /// 乐评数据或曲目序任何一方漂移，这里当场红。
    /// </summary>
    private static VizSelfTest.Result CheckAlignment()
    {
        const string Title = "乐评对齐校验（每条 title_ja 与播放器同 No 曲目一致）";

        try
        {
            int checkedEntries = 0;
            var bad = new List<string>();

            foreach (var game in TrackIndex.Games)
            {
                foreach (var track in game.Tracks)
                {
                    if (!CommentIndex.TryGetEntry(game.Id, track.No, out var entry) || entry is null) continue;

                    checkedEntries++;
                    if (Norm(entry.TitleJa) != Norm(track.Title))
                        bad.Add($"{game.Id}#{track.No}");
                }
            }

            bool ok = checkedEntries > 0 && bad.Count == 0;

            return new VizSelfTest.Result(Title, ok,
                $"逐条比对 {checkedEntries} 条（无评论的曲不含在内）；标题不一致 {bad.Count} 条" +
                (bad.Count > 0 ? "：" + string.Join("、", bad.Take(6)) : ""));
        }
        catch (Exception ex)
        {
            return new VizSelfTest.Result(Title, false, ex.GetType().Name + "：" + ex.Message);
        }
    }

    /// <summary>
    /// 换行保留：th06#11 的评论实测含两个换行，TryGet 返回的文本必须原样保留 ——
    /// 「换行丢了」已在数据层排除（gz 与源 json 582/582 一致），这条守住数据侧不再退化。
    /// </summary>
    private static VizSelfTest.Result CheckNewlines()
    {
        const string Title = "乐评换行保留（多段文本的换行数不缩水）";

        try
        {
            bool got = CommentIndex.TryGetComment("th06", 11, out var text);
            int breaks = text.Count(ch => ch == '\n');

            bool ok = got && breaks >= 2;

            return new VizSelfTest.Result(Title, ok,
                $"th06#11 命中 = {got}；换行数 {breaks}（期望 ≥ 2；按原作作者断行还原后为 5）");
        }
        catch (Exception ex)
        {
            return new VizSelfTest.Result(Title, false, ex.GetType().Name + "：" + ex.Message);
        }
    }

    /// <summary>
    /// 裏评计数闸门：当前数据快照下，有裏评的条目**恰好**是 th06=17 / th07=20 / th08=21、
    /// 其余作品全 0（含 th06nc —— 新典只有一套评论）。
    /// 内嵌 gz 若是「没透传 omake 的旧版」，这里当场红（抓忘重跑生成工具）。
    /// ⚠️ 这是数据快照断言：将来给其他作品补了裏评数据，期望值随数据一起更新。
    /// 顺带全量断言裏评文本不含 '\r'（换行归一的回归）。裏评当前 58/58 单段无 '\n'，
    /// 但**不**断言「无 '\n'」—— 那会挡住将来合法的数据修正。
    /// </summary>
    private static VizSelfTest.Result CheckOmakeCounts()
    {
        const string Title = "裏乐评计数（th06=17 / th07=20 / th08=21，其余为 0）";

        try
        {
            var expect = new Dictionary<string, int>
            {
                ["th06"] = 17,
                ["th07"] = 20,
                ["th08"] = 21,
            };

            var counts = new Dictionary<string, int>();
            var crBad = new List<string>();

            foreach (var game in TrackIndex.Games)
            {
                foreach (var track in game.Tracks)
                {
                    if (!CommentIndex.TryGetEntry(game.Id, track.No, out var entry) || entry is null) continue;

                    // 口径与 HasOmake 一致：任一语言非空即算有裏评
                    if (string.IsNullOrEmpty(entry.OmakeJa) && string.IsNullOrEmpty(entry.OmakeZh)) continue;

                    counts[game.Id] = counts.TryGetValue(game.Id, out var c) ? c + 1 : 1;

                    if ((entry.OmakeJa ?? "").Contains('\r') || (entry.OmakeZh ?? "").Contains('\r'))
                        crBad.Add($"{game.Id}#{track.No}");
                }
            }

            var wrong = new List<string>();
            foreach (var (gid, want) in expect)
            {
                int got = counts.TryGetValue(gid, out var c) ? c : 0;
                if (got != want) wrong.Add($"{gid}={got}（期望 {want}）");
            }
            // 快照必须是完整的：期望之外冒出来的作品同样算错
            foreach (var (gid, c) in counts)
                if (!expect.ContainsKey(gid) && c > 0)
                    wrong.Add($"{gid}={c}（期望 0）");

            bool ok = wrong.Count == 0 && crBad.Count == 0;

            return new VizSelfTest.Result(Title, ok,
                $"计数 {string.Join("、", counts.Select(kv => $"{kv.Key}={kv.Value}"))}；" +
                $"异常 {wrong.Count} 处" + (wrong.Count > 0 ? "：" + string.Join("、", wrong) : "") +
                $"；含 '\\r' {crBad.Count} 条");
        }
        catch (Exception ex)
        {
            return new VizSelfTest.Result(Title, false, ex.GetType().Name + "：" + ex.Message);
        }
    }

    /// <summary>
    /// 裏评内容抽查：th06/th07/th08 各抽 #1 —— 裏评 Ja/Zh 都能取到、彼此不同、
    /// 且与同曲**表评**不同（防「裏评字段误塞了表评内容」这类静默错配）。
    /// HasOmake 三态：有裏评的曲 = true；无裏评作品（th09#1）= false；连表评都没有的曲（th11#18）= false。
    /// </summary>
    private static VizSelfTest.Result CheckOmakeContent()
    {
        const string Title = "裏乐评内容（三作抽查裏不同于表 / HasOmake 三态）";

        try
        {
            var bad = new List<string>();

            foreach (string gid in new[] { "th06", "th07", "th08" })
            {
                bool ja = CommentIndex.TryGetOmakeComment(gid, 1, CommentLanguage.Ja, out var jaText);
                bool zh = CommentIndex.TryGetOmakeComment(gid, 1, CommentLanguage.Zh, out var zhText);
                CommentIndex.TryGetComment(gid, 1, CommentLanguage.Ja, out var omote);

                if (!ja || !zh) { bad.Add($"{gid}#1 裏评缺语言"); continue; }
                if (jaText == zhText) bad.Add($"{gid}#1 裏评 Ja=Zh（疑似复制）");
                if (jaText == omote) bad.Add($"{gid}#1 裏评与表评相同（疑似塞错字段）");
            }

            bool has = CommentIndex.HasOmake("th06", 1);
            bool noGame = !CommentIndex.HasOmake("th09", 1);
            bool noTrack = !CommentIndex.HasOmake("th11", 18);

            bool ok = bad.Count == 0 && has && noGame && noTrack;

            return new VizSelfTest.Result(Title, ok,
                $"三作抽查异常 {bad.Count} 处" + (bad.Count > 0 ? "：" + string.Join("、", bad) : "") +
                $"；HasOmake：th06#1={has}、th09#1(无裏评作品)={noGame}、th11#18(无表评曲)={noTrack}");
        }
        catch (Exception ex)
        {
            return new VizSelfTest.Result(Title, false, ex.GetType().Name + "：" + ex.Message);
        }
    }

    /// <summary>与生成工具同规则的标题规范化：NFKC + 去全部空白 + 去「・」。</summary>
    private static string Norm(string? s)
    {
        var t = (s ?? string.Empty).Normalize(System.Text.NormalizationForm.FormKC);
        return string.Concat(t.Where(ch => !char.IsWhiteSpace(ch) && ch != '・'));
    }
}
