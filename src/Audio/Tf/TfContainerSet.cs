// 黄昏作（tf 系）容器组：按 GameDef.Containers 的覆盖优先序打开一组容器，
// 提供「一条音轨文件名 → 原始音频字节」的宽松查找。
//
// 红线：容器绝不整文件读入内存（th075bgm.dat 有 428MB）——条目表构造时解析，
// 条目数据按 offset+size 现场读（SuicaReader.ExtractEntry / 手工读块+解密 / pakReader 内部流式）。

using System.IO;
using System.Text;
using System.Text.Json;
using ThbgmPlayer.Data;
using addons;
using addons.Suica;
using addons.XorReader;

namespace ThbgmPlayer.Audio;

/// <summary>
/// 一部黄昏作的容器集合。查找语义：后面的包覆盖前面包的同名条目（补丁包优先）。
/// 条目名在索引里是短名（"op.ogg"/"00a.wav"），容器内可能是全路径
/// （"data\bgm\op.ogg"/"wave\bgm\00a.wav"），故查找按四档宽松匹配。
/// </summary>
public sealed class TfContainerSet : IDisposable
{
    /// <summary>单个容器的统一视图（可能是 TFPK 多包合并后的整体）。</summary>
    private interface IEntrySource : IDisposable
    {
        IEnumerable<string> EntryNames { get; }
        bool TryGetEntry(string name, out byte[] data);
    }

    private readonly List<IEntrySource> _sources = new();
    private readonly string _code;

    public TfContainerSet(GameDef game, string dir)
    {
        _code = game.Code;
        if (game.Containers is not { Count: > 0 })
            throw new InvalidOperationException($"{game.Code} 索引缺少容器信息（cont）。");

        if (game.Source == "tfsuica")
        {
            // th075：th075bgm.dat（Suica 外层容器）
            foreach (var rel in game.Containers)
                _sources.Add(new SuicaContainer(Path.Combine(dir, rel)));
        }
        else // tfogg
        {
            var dat = new List<string>();
            var pk = new List<string>();
            foreach (var rel in game.Containers)
            {
                string p = Path.Combine(dir, rel);
                if (rel.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)) dat.Add(p);
                else pk.Add(p);   // .pak / .cga / .cgb → TFPK / th175 cga
            }
            foreach (var p in dat) _sources.Add(new XorContainer(p));
            if (pk.Count > 0)
            {
                // th175 的 cga/cgb 用 fileslist.js（全路径 JSON 数组）；
                // TFPK（.pak）用 fileslist.txt（全路径行集，v0 th135 亦可作目录名反查）
                bool isCga = pk.Any(p => Path.GetExtension(p).Equals(".cga", StringComparison.OrdinalIgnoreCase)
                                      || Path.GetExtension(p).Equals(".cgb", StringComparison.OrdinalIgnoreCase));
                _sources.Add(new TfpkContainer(pk, LoadFileList(dir, isCga)));
            }
        }
    }

    /// <summary>
    /// 全部条目名（跨容器合并、去重，归一化为小写 + 反斜杠）。
    /// 主要供诊断与自测枚举；正常播放走 <see cref="GetEntry"/>。
    /// </summary>
    public IEnumerable<string> EntryNames => _sources.SelectMany(s => s.EntryNames).Distinct();

    /// <summary>按覆盖优先序查条目，返回原始音频字节（OGG 或 WAV）。找不到抛 FileNotFoundException。</summary>
    public byte[] GetEntry(string name)
    {
        for (int i = _sources.Count - 1; i >= 0; i--)
            if (TryFind(_sources[i], name, out var data))
                return data;

        throw new FileNotFoundException(
            $"{_code}：容器里找不到条目 {name}（共 {_sources.Sum(s => s.EntryNames.Count())} 个候选）。");
    }

    // ---- 宽松匹配：精确 → 斜杠/大小写归一 → 无扩展名 → 基名唯一命中 ----

    private static string Norm(string name) => name.Replace('\\', '/').ToLowerInvariant();

    /// <summary>旁挂数据文件扩展名（如 .sfl 循环点，RIFF 头）——宽松查找中永不顶替音频条目。</summary>
    private static readonly HashSet<string> SidecarExts = new(StringComparer.Ordinal) { ".sfl" };

    private static bool TryFind(IEntrySource src, string name, out byte[] data)
    {
        if (src.TryGetEntry(name, out data)) return true;

        string target = Norm(name);
        string targetNoExt = Path.GetFileNameWithoutExtension(target);
        string targetBase = Path.GetFileName(target);

        // 归一后的全路径匹配（容器内可能是 "data\bgm\op.ogg" 而请求是 "data/bgm/op.ogg" 等）
        foreach (var entry in src.EntryNames)
            if (Norm(entry) == target) return src.TryGetEntry(entry, out data);

        // 无扩展名匹配（索引名与容器名的扩展名习惯不一致时兜底）。
        // ⚠️ XOR/TFPK 容器里音频条目旁挂着同名 .sfl 循环点文件（RIFF 头），
        //    绝不能让它顶替音频条目：先找同扩展名的；找不到再在非旁挂扩展名里找，
        //    且必须唯一才命中（歧义直接放弃，宁可不匹配也不猜错）。
        string targetExt = Path.GetExtension(target);
        var noExtSame = new List<string>();
        var noExtOther = new List<string>();
        foreach (var entry in src.EntryNames)
        {
            var n = Norm(entry);
            if (n == target) return src.TryGetEntry(entry, out data);
            if (Path.GetFileNameWithoutExtension(n) != targetNoExt) continue;
            if (SidecarExts.Contains(Path.GetExtension(n))) continue;
            if (string.Equals(Path.GetExtension(n), targetExt, StringComparison.Ordinal))
                noExtSame.Add(entry);
            else
                noExtOther.Add(entry);
        }
        var noExtHits = noExtSame.Count > 0 ? noExtSame : noExtOther;
        if (noExtHits.Count == 1) return src.TryGetEntry(noExtHits[0], out data);

        // 按文件名（basename）唯一命中：容器里是全路径、索引里只有短名
        var hits = src.EntryNames.Where(e => Path.GetFileName(Norm(e)) == targetBase).ToList();
        if (hits.Count == 1) return src.TryGetEntry(hits[0], out data);

        data = Array.Empty<byte>();
        return false;
    }

    /// <summary>
    /// TFPK v1（th145/th155）与 th175 cga 的条目真名不在容器里（内嵌表只有零星基名），
    /// 要靠 fileslist 名单做哈希反查。三级回退：
    /// ① 游戏目录（官方安装的 th135+ 自带 fileslist.txt，th175 自带 fileslist.js）；
    /// ② 程序目录（被清理过的游戏目录可以从旁边补一份）；
    /// ③ 内嵌资源（主工程 EmbeddedResource：TfFileslist.txt / TfFileslist.js）。
    /// <paramref name="isCga"/>：th175 cga/cgb 取 .js 名单，TFPK 取 .txt 名单。
    /// </summary>
    private static IEnumerable<string>? LoadFileList(string dir, bool isCga)
    {
        string jsName = "fileslist.js";
        string txtName = "fileslist.txt";
        string[] candidates = isCga ? [jsName, txtName] : [txtName, jsName];

        foreach (var baseDir in new[] { dir, AppContext.BaseDirectory })
        {
            foreach (var name in candidates)
            {
                try
                {
                    string p = Path.Combine(baseDir, name);
                    if (name == jsName && File.Exists(p))
                        return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(p));
                    if (name == txtName && File.Exists(p))
                        return File.ReadAllLines(p).Where(l => !string.IsNullOrWhiteSpace(l));
                }
                catch
                {
                    // 名单坏了继续试下一级，不拖死播放
                }
            }
        }

        // ③ 内嵌资源：TfFileslist.js（th175）优先，TfFileslist.txt（TFPK）兜底
        string[] resources = isCga ? ["TfFileslist.js", "TfFileslist.txt"] : ["TfFileslist.txt", "TfFileslist.js"];
        foreach (var res in resources)
        {
            try
            {
                using var s = typeof(TfContainerSet).Assembly.GetManifestResourceStream(res);
                if (s is null) continue;

                if (res.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
                {
                    using var sr = new StreamReader(s);
                    return JsonSerializer.Deserialize<List<string>>(sr.ReadToEnd());
                }
                var lines = new List<string>(4096);
                using (var sr = new StreamReader(s))
                    while (sr.ReadLine() is { } line)
                        if (line.Length > 0) lines.Add(line);
                return lines;
            }
            catch
            {
                // 内嵌名单有问题也不抛 —— 退化为 unk-{hash} 名，按基名仍可能命中 th135
            }
        }
        return null;
    }

    public void Dispose()
    {
        foreach (var s in _sources) s.Dispose();
        _sources.Clear();
    }

    // ---------------------------------------------------------------- 实现

    /// <summary>th075 Suica 容器（th075bgm.dat）：条目数据原样存放，按 offset 现场读。</summary>
    private sealed class SuicaContainer : IEntrySource
    {
        private readonly FileStream _fs;
        private readonly List<SuicaReader.SuicaEntry> _entries;

        public SuicaContainer(string path)
        {
            _fs = File.OpenRead(path);
            _entries = SuicaReader.ParseEntries(_fs);
        }

        public IEnumerable<string> EntryNames => _entries.Select(e => e.Name);

        public bool TryGetEntry(string name, out byte[] data)
        {
            var e = _entries.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (e is null) { data = Array.Empty<byte>(); return false; }
            data = SuicaReader.ExtractEntry(_fs, e);
            return true;
        }

        public void Dispose() => _fs.Dispose();
    }

    /// <summary>th105/th123 双层 XOR 容器（th105b.dat 等）：条目数据按 offset 读出后单字节 XOR 解密。</summary>
    private sealed class XorContainer : IEntrySource
    {
        private readonly FileStream _fs;
        private readonly IReadOnlyList<XorContainerEntry> _entries;

        public XorContainer(string path)
        {
            _fs = File.OpenRead(path);
            _entries = XorContainerReader.ReadEntries(_fs);
        }

        public IEnumerable<string> EntryNames => _entries.Select(e => e.Name);

        public bool TryGetEntry(string name, out byte[] data)
        {
            // ReadEntries 已把名字统一为 '/' 分隔，做一次大小写不敏感比对
            var e = _entries.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
                 ?? _entries.FirstOrDefault(x => string.Equals(x.Name.Replace('\\', '/'), name.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
            if (e is null) { data = Array.Empty<byte>(); return false; }

            var buf = new byte[e.Size];
            _fs.Seek(e.Offset, SeekOrigin.Begin);
            int read = 0;
            while (read < buf.Length)
            {
                int n = _fs.Read(buf, read, buf.Length - read);
                if (n <= 0) throw new EndOfStreamException($"条目 {e.Name} 数据被截断（offset={e.Offset} size={e.Size}）");
                read += n;
            }
            XorContainerReader.DecryptFileData(buf, e.Offset);
            data = buf;
            return true;
        }

        public void Dispose() => _fs.Dispose();
    }

    /// <summary>TFPK（.pak）与 th175 cga/cgb：多包覆盖语义由 pakReader 自带。</summary>
    private sealed class TfpkContainer : IEntrySource
    {
        private readonly pakReader _reader;

        public TfpkContainer(IReadOnlyList<string> paths, IEnumerable<string>? fileList)
        {
            _reader = pakReader.Open(paths, fileList);
        }

        public IEnumerable<string> EntryNames => _reader.Entries.Select(e => e.Name);

        public bool TryGetEntry(string name, out byte[] data) => _reader.TryGetEntry(name, out data);

        public void Dispose() => _reader.Dispose();
    }
}
