using System.Net.Http;
using System.Text.Json;

namespace RMQuickTune.Core;

/// <summary>云端单个固件条目的版本信息。</summary>
public sealed class CloudFirmware
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public string? Date { get; init; }
    public string? Description { get; init; }
}

/// <summary>云端版本数据集（含抓取时间）。</summary>
public sealed class CloudVersionData
{
    /// <summary>条目名 -> 该条目最新版本（取第一个）。</summary>
    public required IReadOnlyDictionary<string, CloudFirmware> Firmwares { get; init; }

    /// <summary>这份数据的抓取时间（本地时间）。</summary>
    public DateTime FetchedAt { get; init; }

    /// <summary>是否来自本地缓存（而非本次实时抓取）。</summary>
    public bool FromCache { get; init; }

    public CloudFirmware? Get(string name)
        => Firmwares.TryGetValue(name, out var f) ? f : null;
}

/// <summary>
/// 从 DJI 云端拉取固件/软件版本号，带本地缓存。
/// API: https://edu.dji.com/api/rm/firmwares_versions
/// </summary>
public sealed class CloudVersionChecker
{
    private const string ApiUrl = "https://edu.dji.com/api/rm/firmwares_versions";

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RMQuickTune");

    private static readonly string CachePath = Path.Combine(CacheDir, "cloud_versions.json");

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.Add("User-Agent", "RMQuickTune");
        return c;
    }

    /// <summary>内存中最近一次成功的数据。</summary>
    public CloudVersionData? Current { get; private set; }

    /// <summary>
    /// 异步刷新云端数据：请求 API，成功则更新内存与本地缓存。
    /// 失败时回退到内存或磁盘缓存（标记 FromCache）。返回最终可用数据，无任何数据时返回 null。
    /// </summary>
    public async Task<CloudVersionData?> RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            string body = await Http.GetStringAsync(ApiUrl, ct).ConfigureAwait(false);
            var firmwares = Parse(body);
            if (firmwares.Count > 0)
            {
                var data = new CloudVersionData
                {
                    Firmwares = firmwares,
                    FetchedAt = DateTime.Now,
                    FromCache = false,
                };
                Current = data;
                TrySaveCache(body, data.FetchedAt);
                return data;
            }
        }
        catch
        {
            // 网络/解析失败，走缓存回退
        }

        // 回退：内存
        if (Current is not null)
            return Current;

        // 回退：磁盘缓存
        var cached = TryLoadCache();
        if (cached is not null)
            Current = cached;
        return Current;
    }

    /// <summary>同步加载磁盘缓存到内存（程序启动早期可用，避免首屏空白）。</summary>
    public CloudVersionData? LoadCacheToMemory()
    {
        var cached = TryLoadCache();
        if (cached is not null)
            Current ??= cached;
        return Current;
    }

    /// <summary>解析 API JSON，返回 条目名 -> 最新版本。</summary>
    private static Dictionary<string, CloudFirmware> Parse(string json)
    {
        var result = new Dictionary<string, CloudFirmware>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("result", out var resultEl)) return result;
        if (!resultEl.TryGetProperty("root", out var rootEl)) return result;
        if (!rootEl.TryGetProperty("firmware_info", out var infoEl)) return result;
        if (infoEl.ValueKind != JsonValueKind.Array) return result;

        foreach (var item in infoEl.EnumerateArray())
        {
            string? name = GetString(item, "name");
            if (string.IsNullOrEmpty(name)) continue;
            if (!item.TryGetProperty("version", out var versionsEl) ||
                versionsEl.ValueKind != JsonValueKind.Array) continue;

            // 取第一个版本（API 中首个为最新）
            foreach (var v in versionsEl.EnumerateArray())
            {
                string? ver = GetString(v, "version");
                if (string.IsNullOrEmpty(ver)) continue;

                result[name] = new CloudFirmware
                {
                    Name = name,
                    Version = ver.Trim(),
                    Date = GetNested(v, "date", "date"),
                    Description = GetNested(v, "description", "description"),
                };
                break;
            }
        }
        return result;
    }

    // API 中很多字段是 {"name":{"name":"..."}} 这种嵌套包装
    private static string? GetString(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var p)) return null;
        if (p.ValueKind == JsonValueKind.String) return p.GetString();
        if (p.ValueKind == JsonValueKind.Object && p.TryGetProperty(prop, out var inner) &&
            inner.ValueKind == JsonValueKind.String)
            return inner.GetString();
        return null;
    }

    private static string? GetNested(JsonElement el, string outer, string inner)
    {
        if (el.TryGetProperty(outer, out var o))
        {
            if (o.ValueKind == JsonValueKind.String) return o.GetString();
            if (o.ValueKind == JsonValueKind.Object && o.TryGetProperty(inner, out var i) &&
                i.ValueKind == JsonValueKind.String)
                return i.GetString();
        }
        return null;
    }

    private static void TrySaveCache(string rawBody, DateTime fetchedAt)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            var wrapper = new CacheWrapper { FetchedAt = fetchedAt, Body = rawBody };
            File.WriteAllText(CachePath, JsonSerializer.Serialize(wrapper));
        }
        catch { /* 缓存写失败不影响功能 */ }
    }

    private static CloudVersionData? TryLoadCache()
    {
        try
        {
            if (!File.Exists(CachePath)) return null;
            var wrapper = JsonSerializer.Deserialize<CacheWrapper>(File.ReadAllText(CachePath));
            if (wrapper?.Body is null) return null;

            var firmwares = Parse(wrapper.Body);
            if (firmwares.Count == 0) return null;

            return new CloudVersionData
            {
                Firmwares = firmwares,
                FetchedAt = wrapper.FetchedAt,
                FromCache = true,
            };
        }
        catch
        {
            return null;
        }
    }

    private sealed class CacheWrapper
    {
        public DateTime FetchedAt { get; set; }
        public string? Body { get; set; }
    }
}
