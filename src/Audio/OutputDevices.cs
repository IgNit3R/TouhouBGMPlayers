using NAudio.CoreAudioApi;

namespace ThbgmPlayer.Audio;

/// <summary>一个输出端点：ID 用于持久化与 WithDevice，Name 用于显示。</summary>
public sealed record OutputDeviceInfo(string Id, string Name);

/// <summary>输出设备枚举。给设置界面用；引擎自己按 ID 找设备。</summary>
public static class OutputDevices
{
    /// <summary>当前启用的渲染端点。枚举失败（音频服务异常之类）返回空列表，不让设置界面开不起来。</summary>
    public static IReadOnlyList<OutputDeviceInfo> RenderEndpoints()
    {
        var list = new List<OutputDeviceInfo>();
        try
        {
            using var en = new MMDeviceEnumerator();
            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (d) list.Add(new OutputDeviceInfo(d.ID, d.FriendlyName));
            }
        }
        catch
        {
            // 返回已经收集到的（或空）
        }
        return list;
    }

    /// <summary>按 ID 找设备；找不到（被拔了）返回 null，调用方退回系统默认。</summary>
    public static MMDevice? FindById(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        try
        {
            using var en = new MMDeviceEnumerator();
            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                if (d.ID == id) return d;   // 找到了就交出去，不 Dispose
                d.Dispose();
            }
        }
        catch
        {
            // 找不到就按 null 处理
        }
        return null;
    }
}
