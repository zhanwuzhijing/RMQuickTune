namespace RMQuickTune.Core;

/// <summary>单项版本对比结果。</summary>
public sealed class VersionCompareItem
{
    /// <summary>显示名，如 "Engine / 裁判&服务端"。</summary>
    public required string Label { get; init; }

    /// <summary>本地版本号（可能为 null = 读取失败）。</summary>
    public string? LocalVersion { get; init; }

    /// <summary>云端版本号（可能为 null = 云端无此项或未获取）。</summary>
    public string? CloudVersion { get; init; }

    /// <summary>对比结论。</summary>
    public CompareResult Result { get; init; }
}

public enum CompareResult
{
    /// <summary>本地与云端一致。</summary>
    Match,
    /// <summary>云端版本高于本地（本地过旧，需要更新）。</summary>
    CloudNewer,
    /// <summary>云端版本低于本地（可能云端尚未更新）。</summary>
    CloudOlder,
    /// <summary>缺少本地或云端数据，无法对比。</summary>
    Unknown,
}

/// <summary>整体严重级别。</summary>
public enum AuditSeverity
{
    /// <summary>全部一致或无可比项。</summary>
    Ok,
    /// <summary>存在云端低于本地（黄色警告）。</summary>
    Warning,
    /// <summary>存在云端高于本地（红色报错）。</summary>
    Error,
}

/// <summary>整体云端校验结果。</summary>
public sealed class CloudAuditResult
{
    public required IReadOnlyList<VersionCompareItem> Items { get; init; }

    /// <summary>推导出的赛事类型显示名（如 RMUC）。无法推导时为 null。</summary>
    public string? EventType { get; init; }

    /// <summary>云端数据抓取时间。</summary>
    public DateTime? CloudFetchedAt { get; init; }

    /// <summary>云端数据是否来自缓存。</summary>
    public bool CloudFromCache { get; init; }

    /// <summary>是否拿到了云端数据。</summary>
    public bool HasCloudData { get; init; }

    /// <summary>整体严重级别：取所有项中最严重的。</summary>
    public AuditSeverity Severity
    {
        get
        {
            bool anyError = Items.Any(i => i.Result == CompareResult.CloudNewer);
            if (anyError) return AuditSeverity.Error;
            bool anyWarn = Items.Any(i => i.Result == CompareResult.CloudOlder);
            if (anyWarn) return AuditSeverity.Warning;
            return AuditSeverity.Ok;
        }
    }

    public int MatchCount => Items.Count(i => i.Result == CompareResult.Match);
    public int CloudNewerCount => Items.Count(i => i.Result == CompareResult.CloudNewer);
    public int CloudOlderCount => Items.Count(i => i.Result == CompareResult.CloudOlder);
    public int ComparableCount => Items.Count(i => i.Result != CompareResult.Unknown);
}

/// <summary>
/// 将本地多项版本与云端对比。
/// 赛事类型从 GameSystemConfig.xml 的 scene 字段自动推导，映射到云端固件条目名。
/// </summary>
public static class CloudVersionAudit
{
    /// <summary>
    /// 对比项定义：显示名 + 云端条目名 + 本地版本来源类型。
    /// </summary>
    private enum LocalSource { Engine, Client, ServerConfig }

    /// <summary>
    /// (赛事类型, 版本类型) -> 各对比项。
    /// 云端条目名来自 edu.dji.com API 的 firmware_info[].name。
    /// 学生版与比赛版是云端不同的条目，不可混比。
    /// </summary>
    private static readonly Dictionary<(string Event, ProductEdition Edition), (string Label, string CloudName, LocalSource Source)[]> EventMap = new()
    {
        // ---- RMUC 学生版：referee&server(引擎一体) + client ----
        [("RMUC", ProductEdition.Student)] = new[]
        {
            ("Engine / 裁判&服务端", "RMUC referee&server", LocalSource.Engine),
            ("选手端（Client）", "RMUC client", LocalSource.Client),
        },
        // ---- RMUC 比赛版：server / Official Referee / Official Client 分开 ----
        [("RMUC", ProductEdition.Official)] = new[]
        {
            ("裁判端（Referee）", "RMUC Official Referee", LocalSource.Engine),
            ("服务器（Server）", "RMUC server", LocalSource.ServerConfig),
            ("选手端（Client）", "RMUC Official Client", LocalSource.Client),
        },

        // ---- RMUL 各项（均为学生版形态：referee&server + client）----
        [("RMUL_3V3", ProductEdition.Student)] = new[]
        {
            ("Engine / 裁判&服务端", "RMUL 3V3 referee&server", LocalSource.Engine),
            ("选手端（Client）", "RMUL 3V3 client", LocalSource.Client),
        },
        [("RMUL_1V1", ProductEdition.Student)] = new[]
        {
            ("Engine / 裁判&服务端", "RMUL 1V1 referee&server", LocalSource.Engine),
            ("选手端（Client）", "RMUL 1V1 client", LocalSource.Client),
        },
        [("RMUL_ENGINEER", ProductEdition.Student)] = new[]
        {
            ("Engine / 裁判&服务端", "RMUL Engineer referee&server", LocalSource.Engine),
            ("选手端（Client）", "RMUL Engineer client", LocalSource.Client),
        },
    };

    /// <summary>解析对应的对比项映射。RMUL 暂只有学生版形态，未知版本按学生版兜底。</summary>
    private static (string Label, string CloudName, LocalSource Source)[]? ResolveMapping(string eventKey, ProductEdition edition)
    {
        if (EventMap.TryGetValue((eventKey, edition), out var m))
            return m;
        // 兜底：未能判定版本类型时，按学生版处理（赛事现场学生版最常见）
        if (EventMap.TryGetValue((eventKey, ProductEdition.Student), out var s))
            return s;
        return null;
    }

    /// <summary>从 scene 字符串推导赛事类型键。</summary>
    public static string? DeriveEventKey(string? scene)
    {
        if (string.IsNullOrEmpty(scene)) return null;
        string s = scene.ToUpperInvariant();

        // 顺序敏感：先判更具体的 RMUL 细分
        if (s.Contains("RMUL"))
        {
            if (s.Contains("3V3")) return "RMUL_3V3";
            if (s.Contains("1V1")) return "RMUL_1V1";
            if (s.Contains("ENGINEER") || s.Contains("ENG")) return "RMUL_ENGINEER";
            return "RMUL_3V3"; // 默认 3V3
        }
        if (s.Contains("RMUC") || s.Contains("RMU")) return "RMUC";
        return null;
    }

    /// <summary>赛事类型键的友好显示名。</summary>
    public static string EventDisplayName(string eventKey) => eventKey switch
    {
        "RMUC" => "RMUC（超级对抗赛）",
        "RMUL_3V3" => "RMUL 3V3（联盟赛 3V3）",
        "RMUL_1V1" => "RMUL 1V1（联盟赛 1V1）",
        "RMUL_ENGINEER" => "RMUL 工程（联盟赛工程）",
        _ => eventKey,
    };

    /// <summary>
    /// 执行对比。需要：运行中的 engine 信息（本地版本来源）、云端数据。
    /// </summary>
    public static CloudAuditResult Build(EngineInfo engine, CloudVersionData? cloud)
    {
        var items = new List<VersionCompareItem>();

        // 推导赛事类型
        string? scene = engine.EngineDir is null ? null : EngineLocator.TryReadScene(engine.EngineDir);
        string? eventKey = DeriveEventKey(scene);

        // 本地版本来源
        string? localEngine = engine.Version; // globalgamemanagers
        string? localServer = engine.EngineDir is null ? null : EngineLocator.TryReadServerVersion(engine.EngineDir);
        string? localClient = EngineLocator.TryDetectClientVersion(); // 运行中的选手端

        var mapping = eventKey is null ? null : ResolveMapping(eventKey, engine.Edition);
        if (mapping is not null)
        {
            foreach (var (label, cloudName, source) in mapping)
            {
                string? local = source switch
                {
                    LocalSource.Engine => localEngine,
                    LocalSource.Client => localClient,
                    LocalSource.ServerConfig => localServer,
                    _ => null,
                };

                string? cloudVer = cloud?.Get(cloudName)?.Version;

                items.Add(new VersionCompareItem
                {
                    Label = label,
                    LocalVersion = local,
                    CloudVersion = cloudVer,
                    Result = Compare(local, cloudVer),
                });
            }
        }

        return new CloudAuditResult
        {
            Items = items,
            EventType = eventKey is null ? null : EventDisplayName(eventKey) + EditionSuffix(engine.Edition),
            CloudFetchedAt = cloud?.FetchedAt,
            CloudFromCache = cloud?.FromCache ?? false,
            HasCloudData = cloud is not null,
        };
    }

    private static string EditionSuffix(ProductEdition edition) => edition switch
    {
        ProductEdition.Student => " · 学生版",
        ProductEdition.Official => " · 比赛版",
        _ => "",
    };

    private static CompareResult Compare(string? local, string? cloud)
    {
        if (string.IsNullOrEmpty(local) || string.IsNullOrEmpty(cloud))
            return CompareResult.Unknown;

        string l = local.Trim();
        string c = cloud.Trim();

        if (string.Equals(l, c, StringComparison.OrdinalIgnoreCase))
            return CompareResult.Match;

        int cmp = CompareVersionStrings(l, c);
        // cmp < 0：本地 < 云端 → 云端更新（报错）；cmp > 0：本地 > 云端 → 云端更旧（警告）
        return cmp < 0 ? CompareResult.CloudNewer : CompareResult.CloudOlder;
    }

    /// <summary>
    /// 按四段式数字逐段比较版本号。返回 &lt;0 表示 a&lt;b，&gt;0 表示 a&gt;b，0 表示相等。
    /// 非数字段做兜底字符串比较。
    /// </summary>
    private static int CompareVersionStrings(string a, string b)
    {
        var pa = a.Split('.');
        var pb = b.Split('.');
        int len = Math.Max(pa.Length, pb.Length);
        for (int i = 0; i < len; i++)
        {
            string sa = i < pa.Length ? pa[i] : "0";
            string sb = i < pb.Length ? pb[i] : "0";
            if (int.TryParse(sa, out int na) && int.TryParse(sb, out int nb))
            {
                if (na != nb) return na.CompareTo(nb);
            }
            else
            {
                int c = string.CompareOrdinal(sa, sb);
                if (c != 0) return c;
            }
        }
        return 0;
    }
}
