using System.Windows;
using System.Windows.Media.Imaging;

namespace ThbgmPlayer.Viz;

/// <summary>
/// 封面：把「作品 id + 是否副版」解析成**程序集里**的那张图。
///
/// 为什么是"程序集里"（固化，用户 2026-09-21 定）：
/// 封面是固定的、每作一张，没有"用户换图"的需求；固化之后**没有路径问题、没有文件句柄、
/// 换机器/换目录都不丢**。素材在 `assets/cover/embed/`，经 csproj 的
/// <c>&lt;Resource Include="..\assets\cover\embed\*.jpg"&gt;</c> 打进程序集，
/// 运行时按 pack URI 读 —— 读的是内存流，**不会占住文件**。
///
/// ⚠️ 素材是**归一化过**的（长边 512 的 JPEG，见 `.workbuddy/tools/normalize_covers.py`）。
/// 原图（38.9MB）留在 `assets/cover/` 不动，只把 2.64MB 的归一化版本编进去。
/// 自检里有一条盯着「每张都 ≤512」—— 所以运行时**不需要** DecodePixelWidth 再解一遍
/// （设了反而会把 400px 的少数几张**放大**解码，白费内存）。
///
/// <b>副版规则（用户 2026-09-21 明确）</b>：<c>&lt;id&gt;_alt.jpg</c> **存在才用**，否则回退主版。
/// 于是 th13 那种"只有一张封面、不跟霊界版走"的作品**不需要任何特例代码** —— 不给它 `_alt` 就行；
/// 而 th06nc 给了两张，就自然跟着「新典 / 原典」切换走。
/// </summary>
public static class VizCover
{
    /// <summary>副版文件名的后缀。</summary>
    public const string AltSuffix = "_alt";

    /// <summary>归一化后的长边上限。**只用于自检断言**，运行时不参与解码。</summary>
    public const int MaxEdge = 512;

    /// <summary>
    /// 同一个 <paramref name="gameId"/> 解析出来的图**两份宿主共用同一张**（冻结后可共享）：
    /// 附件窗口与内嵌那套各解一遍纯属白费，何况切来切去会反复解。
    /// </summary>
    private static readonly Dictionary<string, BitmapImage?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>pack URI。资源名进程序集后统一挂在 <c>Resources/cover/</c> 下（由 csproj 的 Link 决定）。</summary>
    public static string ResourcePath(string name) =>
        $"pack://application:,,,/Resources/cover/{name}.jpg";

    /// <summary>
    /// 取这张封面。<paramref name="alt"/> 为真**且副版文件存在**时才用副版，否则回退主版。
    /// 失败（没有图 / 资源读不出来）返回 <c>null</c> —— 调用方据此退回虚线占位，**绝不抛**。
    /// </summary>
    public static BitmapImage? Load(string? gameId, bool alt)
    {
        if (string.IsNullOrWhiteSpace(gameId)) return null;

        string name = alt && Exists(gameId + AltSuffix) ? gameId + AltSuffix : gameId;

        if (Cache.TryGetValue(name, out var cached)) return cached;

        var image = TryLoad(name);
        Cache[name] = image;
        return image;
    }

    /// <summary>这个 id 有没有素材。用 <see cref="Application.GetResourceStream"/> 探，失败即没有。</summary>
    public static bool Exists(string name)
    {
        if (Cache.ContainsKey(name)) return Cache[name] is not null;

        try
        {
            // ⚠️ StreamResourceInfo 本身不是 IDisposable —— 要放掉的是它包的那个流。
            var info = Application.GetResourceStream(new Uri(ResourcePath(name)));
            info?.Stream?.Dispose();
            return info is not null;
        }
        catch (Exception)
        {
            // 资源缺失是**正常情况**（不是每作都有副版图），不是错误
            return false;
        }
    }

    private static BitmapImage? TryLoad(string name)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(ResourcePath(name), UriKind.Absolute);

            // 读完就把流放掉：pack 资源本身不占文件句柄，但 OnLoad 让它**立刻解码完**，
            // 之后冻结共享、渲染端可缓存，也不会拖住任何延迟初始化。
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();

            // ⚠️ 冻结是硬要求：不冻结的 ImageSource 有线程亲和性，
            // 而这个实例要给「附件窗口 + 主窗口内嵌」两套宿主共用。
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            // 解码失败也不该把播放器带下去 —— 退回占位即可
            return null;
        }
    }
}
