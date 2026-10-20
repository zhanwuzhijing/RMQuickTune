using System.Text.Json;

namespace RMGuide.Core;

/// <summary>单块网卡在某时刻的配置。</summary>
public sealed class AdapterSnapshot
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool DhcpEnabled { get; set; }

    /// <summary>静态配置时的 IP；DHCP 时为 null。</summary>
    public string? Ip { get; set; }
    public string? Mask { get; set; }
    public string? Gateway { get; set; }

    public List<string> Dns { get; set; } = new();

    /// <summary>跃点数；读不到时为 null（恢复时按自动跃点处理）。</summary>
    public int? Metric { get; set; }
}

/// <summary>一个存档点：整机网卡配置的完整快照。</summary>
public sealed class SnapshotFile
{
    public string Label { get; set; } = string.Empty;
    public DateTime SavedAt { get; set; }
    public bool IsAuto { get; set; }
    public List<AdapterSnapshot> Adapters { get; set; } = new();

    /// <summary>存档文件完整路径（不写入文件内容）。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Path { get; set; } = string.Empty;

    public string DisplayName
        => $"{SavedAt:MM-dd HH:mm}  {Label}" + (IsAuto ? "" : "（手动）");
}

/// <summary>
/// 存档点管理：把改配置之前的网卡状态存成 JSON，随时可以整机恢复。
/// 存放位置：%LOCALAPPDATA%\RMGuide\snapshots。
/// </summary>
public static class SnapshotStore
{
    private static readonly string Dir = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RMGuide", "snapshots");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>采集当前网卡配置并保存为存档点。</summary>
    public static SnapshotFile Save(IReadOnlyList<AdapterInfo> adapters, string label, bool isAuto)
    {
        var snapshot = new SnapshotFile
        {
            Label = label,
            SavedAt = DateTime.Now,
            IsAuto = isAuto,
        };

        foreach (var a in adapters.Where(x => !x.IsVirtual))
        {
            var first = a.Addresses.FirstOrDefault();
            snapshot.Adapters.Add(new AdapterSnapshot
            {
                Name = a.Name,
                Description = a.Description,
                DhcpEnabled = a.DhcpEnabled ?? true,
                Ip = a.DhcpEnabled == true ? null : first?.Ip,
                Mask = a.DhcpEnabled == true ? null : first?.Mask,
                Gateway = a.Gateways.FirstOrDefault(),
                Dns = a.DnsServers.ToList(),
            });
        }

        Directory.CreateDirectory(Dir);
        string path = System.IO.Path.Combine(Dir, $"snapshot-{snapshot.SavedAt:yyyyMMdd-HHmmss}.json");

        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(snapshot, JsonOptions));
            snapshot.Path = path;
        }
        catch
        {
            // 存档失败不阻断主流程（调用方通过 Path 是否为空判断）
        }

        // 只保留最近 20 个，避免无限增长
        TrimOld();

        return snapshot;
    }

    /// <summary>列出所有存档点（新的在前）。</summary>
    public static IReadOnlyList<SnapshotFile> List()
    {
        var list = new List<SnapshotFile>();
        try
        {
            if (!Directory.Exists(Dir)) return list;

            foreach (var file in Directory.GetFiles(Dir, "snapshot-*.json"))
            {
                try
                {
                    var snap = JsonSerializer.Deserialize<SnapshotFile>(File.ReadAllText(file));
                    if (snap is null) continue;
                    snap.Path = file;
                    list.Add(snap);
                }
                catch { /* 坏文件跳过 */ }
            }
        }
        catch { /* ignore */ }

        return list.OrderByDescending(s => s.SavedAt).ToList();
    }

    /// <summary>删除一个存档点。</summary>
    public static void Delete(SnapshotFile snapshot)
    {
        try
        {
            if (File.Exists(snapshot.Path)) File.Delete(snapshot.Path);
        }
        catch { /* ignore */ }
    }

    private static void TrimOld()
    {
        try
        {
            var files = Directory.GetFiles(Dir, "snapshot-*.json")
                .OrderByDescending(File.GetLastWriteTime)
                .Skip(20)
                .ToList();

            foreach (var f in files) File.Delete(f);
        }
        catch { /* ignore */ }
    }
}
