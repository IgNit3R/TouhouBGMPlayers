using System.IO;
using System.Reflection;

namespace ThbgmPlayer.Core;

/// <summary>
/// 程序自己的路径。全部围绕 AppContext.BaseDirectory（exe 所在目录）。
///
/// 硬约束（DESIGN_v3.md §9）：
///   - 绝不写 AppData / IsolatedStorage / 注册表 / 临时目录。
///   - 不用 Properties.Settings.Default（它默认写 AppData）。
///   - 不用 Environment.SpecialFolder.* 任何一项。
///   - 不用单文件发布（会把自身解压到临时目录，破坏"一个文件夹"的干净性）。
/// 整个程序目录复制到哪都能跑，所有可写文件都留在自己文件夹里。
/// </summary>
public static class AppPaths
{
    /// <summary>exe 所在目录（末尾带分隔符）。运行时动态取，不写死。</summary>
    public static string BaseDirectory { get; } = AppContext.BaseDirectory;

    /// <summary>设置文件。</summary>
    public static string SettingsFile => Path.Combine(BaseDirectory, "settings.json");

    /// <summary>收藏。</summary>
    public static string FavoritesFile => Path.Combine(BaseDirectory, "favorites.json");

    /// <summary>自定义播放列表。</summary>
    public static string PlaylistsFile => Path.Combine(BaseDirectory, "playlists.json");

    /// <summary>外部覆盖索引（可选，用户自己放）。</summary>
    public static string OverrideFile => Path.Combine(BaseDirectory, "tracks.override.json");

    /// <summary>导出目录。</summary>
    public static string ExportDirectory => Path.Combine(BaseDirectory, "export");

    /// <summary>
    /// 显示用版本号（「关于」对话框）。读 InformationalVersion —— 可以带字母（如 1.0a）；
    /// 程序集的数字版本（AssemblyVersion）只是兜底，因为那里写不进字母。
    /// </summary>
    public static string AppVersion =>
        Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "0.0.0";

    /// <summary>
    /// 原子写文本文件：先写 .tmp，再整体替换目标文件。
    ///
    /// 直接 WriteAllText 的话，写到一半进程死掉（断电、强杀）会留下一个截断的文件，
    /// 下次启动解析失败就会静默退回默认设置 —— 用户看到的是「配置莫名其妙全没了」，
    /// 而现场已经没了，极难排查。先写临时文件再原子替换就没有这个窗口。
    /// </summary>
    public static bool WriteTextAtomic(string path, string contents)
    {
        try
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, contents);
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 程序目录是否可写。放在 Program Files 这类受保护位置时可能为 false，
    /// 此时应降级为"设置仅内存生效"并提示用户，而不是抛异常崩溃。
    /// </summary>
    public static bool IsWritable
    {
        get
        {
            try
            {
                var probe = Path.Combine(BaseDirectory, ".write_probe.tmp");
                File.WriteAllText(probe, "1");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>确保导出目录存在。失败不抛异常，交给调用方决定如何提示。</summary>
    public static bool EnsureExportDirectory()
    {
        try
        {
            Directory.CreateDirectory(ExportDirectory);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
