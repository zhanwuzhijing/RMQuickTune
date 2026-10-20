using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Microsoft.Win32;

namespace RMGuide.Core;

/// <summary>单项网络检查的结论。</summary>
public enum NetResult
{
    /// <summary>正常。</summary>
    Ok,
    /// <summary>需要注意（不致命）。</summary>
    Warn,
    /// <summary>异常，需要处理。</summary>
    Error,
    /// <summary>无法判定。</summary>
    Unknown,
}

/// <summary>网卡上的一个 IPv4 地址。</summary>
public sealed class NetworkAddress
{
    public required string Ip { get; init; }
    public required string Mask { get; init; }
    public required int PrefixLength { get; init; }

    /// <summary>169.254.x.x 自动专用地址（说明 DHCP 没拿到地址）。</summary>
    public bool IsApipa => Ip.StartsWith("169.254.", StringComparison.Ordinal);
}

/// <summary>单块网卡的信息快照。</summary>
public sealed class AdapterInfo
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public bool IsWireless { get; init; }
    public bool IsVirtual { get; init; }
    public bool IsUp { get; init; }

    /// <summary>物理地址（AA-BB-CC-DD-EE-FF），帮助区分同型号网卡。</summary>
    public string MacAddress { get; init; } = string.Empty;

    public long SpeedMbps { get; init; }

    public IReadOnlyList<NetworkAddress> Addresses { get; init; } = Array.Empty<NetworkAddress>();
    public IReadOnlyList<string> Gateways { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DnsServers { get; init; } = Array.Empty<string>();

    /// <summary>是否启用 DHCP；无法读取时为 null。</summary>
    public bool? DhcpEnabled { get; init; }

    /// <summary>是否为「有线且已连接」的可用网卡。</summary>
    public bool IsUsableWired
        => IsUp && !IsWireless && !IsVirtual && Addresses.Any(a => !a.IsApipa);

    /// <summary>卡片上显示的一句话状态。</summary>
    public string StatusText
    {
        get
        {
            if (!IsUp) return "未连接";
            if (Addresses.Count == 0) return "无 IPv4 地址";
            if (Addresses.All(a => a.IsApipa)) return "仅自动地址";
            return "已连接";
        }
    }
}

/// <summary>一条检查项结果。</summary>
public sealed class NetworkCheck
{
    public required string Label { get; init; }
    public required string Detail { get; init; }
    public required NetResult Result { get; init; }
}

/// <summary>一次网络体检的完整结果。</summary>
public sealed class NetworkReport
{
    public required IReadOnlyList<AdapterInfo> Adapters { get; init; }
    public required IReadOnlyList<NetworkCheck> Checks { get; init; }
    public required DateTime At { get; init; }

    public int OkCount => Checks.Count(c => c.Result == NetResult.Ok);
    public int WarnCount => Checks.Count(c => c.Result == NetResult.Warn);
    public int ErrorCount => Checks.Count(c => c.Result == NetResult.Error);

    /// <summary>整体结论：取最严重项。</summary>
    public NetResult Overall
        => ErrorCount > 0 ? NetResult.Error
         : WarnCount > 0 ? NetResult.Warn
         : Checks.Count == 0 ? NetResult.Unknown
         : NetResult.Ok;
}

/// <summary>
/// 网络体检：枚举网卡、读取地址/网关/DNS/DHCP，并执行连通性探测。
/// 只做客观检测，不写入任何配置（修复功能后续再做）。
/// </summary>
public static class NetworkInspector
{
    /// <summary>云端校验用到的域名，用于 DNS 与 HTTPS 连通性探测。</summary>
    private const string ProbeHost = "edu.dji.com";

    /// <summary>虚拟网卡关键字（避免把 VMware / Hyper-V 当成现场网卡）。</summary>
    private static readonly string[] VirtualKeywords =
    {
        "vmware", "virtualbox", "hyper-v", "vethernet", "tap-", "tap ", "loopback",
        "bluetooth", "wan miniport", "vpn", "zerotier", "tailscale", "clash", "wintun",
        "wi-fi direct", "wifi direct", "virtual adapter",
    };

    /// <summary>枚举所有物理网卡（已过滤回环、隧道、虚拟网卡）。</summary>
    public static IReadOnlyList<AdapterInfo> GetAdapters()
    {
        var result = new List<AdapterInfo>();

        NetworkInterface[] nics;
        try { nics = NetworkInterface.GetAllNetworkInterfaces(); }
        catch { return result; }

        foreach (var nic in nics)
        {
            try
            {
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;

                string desc = nic.Description ?? "";
                string name = nic.Name ?? "";

                // 跳过明显无关的网卡类型
                bool relevantType = nic.NetworkInterfaceType
                    is NetworkInterfaceType.Ethernet
                    or NetworkInterfaceType.GigabitEthernet
                    or NetworkInterfaceType.FastEthernetT
                    or NetworkInterfaceType.FastEthernetFx
                    or NetworkInterfaceType.Ethernet3Megabit
                    or NetworkInterfaceType.Wireless80211
                    or NetworkInterfaceType.Unknown;
                if (!relevantType) continue;

                IPInterfaceProperties props;
                try { props = nic.GetIPProperties(); }
                catch { continue; }

                var addresses = new List<NetworkAddress>();
                foreach (var ua in props.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    int prefix = ua.PrefixLength;
                    addresses.Add(new NetworkAddress
                    {
                        Ip = ua.Address.ToString(),
                        Mask = PrefixToMask(prefix),
                        PrefixLength = prefix,
                    });
                }

                var gateways = props.GatewayAddresses
                    .Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(g => g.Address.ToString())
                    .Distinct()
                    .ToList();

                var dns = props.DnsAddresses
                    .Where(d => d.AddressFamily == AddressFamily.InterNetwork)
                    .Select(d => d.ToString())
                    .Distinct()
                    .ToList();

                bool virtualNic = VirtualKeywords.Any(k =>
                    desc.Contains(k, StringComparison.OrdinalIgnoreCase) ||
                    name.Contains(k, StringComparison.OrdinalIgnoreCase));

                result.Add(new AdapterInfo
                {
                    Name = name,
                    Description = desc,
                    IsWireless = IsWirelessAdapter(nic, desc, name),
                    IsVirtual = virtualNic,
                    IsUp = nic.OperationalStatus == OperationalStatus.Up,
                    MacAddress = FormatMac(nic),
                    SpeedMbps = SafeSpeed(nic),
                    Addresses = addresses,
                    Gateways = gateways,
                    DnsServers = dns,
                    DhcpEnabled = TryReadDhcp(nic.Id),
                });
            }
            catch
            {
                // 单块网卡读取失败不影响整体
            }
        }

        // 有线优先、已连接优先
        return result
            .OrderBy(a => a.IsVirtual ? 1 : 0)
            .ThenBy(a => a.IsWireless ? 1 : 0)
            .ThenBy(a => a.IsUp ? 0 : 1)
            .ToList();
    }

    /// <summary>执行完整体检：先读网卡，再做连通性探测。</summary>
    public static async Task<NetworkReport> InspectAsync(CancellationToken ct = default)
    {
        var adapters = GetAdapters();
        var checks = new List<NetworkCheck>();

        bool anyWiredUp = adapters.Any(a => a.IsUp && !a.IsWireless && !a.IsVirtual && a.Addresses.Count > 0);
        var usable = adapters.Where(a => a.IsUp).ToList();

        checks.Add(anyWiredUp
            ? new NetworkCheck
            {
                Label = "有线网卡连接",
                Detail = "检测到已连接的有线网卡",
                Result = NetResult.Ok,
            }
            : new NetworkCheck
            {
                Label = "有线网卡连接",
                Detail = "没有已连接的有线网卡（网线未插或驱动异常）",
                Result = NetResult.Error,
            });

        var allAddrs = usable.SelectMany(a => a.Addresses).ToList();
        bool anyRealIp = allAddrs.Any(a => !a.IsApipa);
        bool anyApipa = allAddrs.Any(a => a.IsApipa);

        if (!anyRealIp)
        {
            checks.Add(new NetworkCheck
            {
                Label = "IPv4 地址",
                Detail = anyApipa
                    ? "只拿到 169.254.x.x 自动地址，说明未获得 DHCP 分配的地址"
                    : "没有可用的 IPv4 地址",
                Result = NetResult.Error,
            });
        }
        else
        {
            checks.Add(new NetworkCheck
            {
                Label = "IPv4 地址",
                Detail = "已获取有效 IPv4 地址" + (anyApipa ? "（另有自动地址，可忽略）" : ""),
                Result = NetResult.Ok,
            });
        }

        var gateways = usable.SelectMany(a => a.Gateways).Distinct().ToList();
        checks.Add(gateways.Count > 0
            ? new NetworkCheck
            {
                Label = "默认网关",
                Detail = "已配置：" + string.Join("、", gateways),
                Result = NetResult.Ok,
            }
            : new NetworkCheck
            {
                Label = "默认网关",
                Detail = "未配置默认网关，只能与同网段设备通信",
                Result = NetResult.Warn,
            });

        var dnsServers = usable.SelectMany(a => a.DnsServers).Distinct().ToList();
        checks.Add(dnsServers.Count > 0
            ? new NetworkCheck
            {
                Label = "DNS 服务器",
                Detail = "已配置：" + string.Join("、", dnsServers),
                Result = NetResult.Ok,
            }
            : new NetworkCheck
            {
                Label = "DNS 服务器",
                Detail = "未配置 DNS，域名类访问会失败（直连 IP 不受影响）",
                Result = NetResult.Warn,
            });

        // ---- 网段重复（多网卡同网段会抢路由）----
        var subnets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in usable)
        {
            foreach (var addr in a.Addresses.Where(x => !x.IsApipa))
            {
                string key = SubnetKey(addr);
                if (!subnets.TryGetValue(key, out var list))
                    subnets[key] = list = new List<string>();
                list.Add(addr.Ip);
            }
        }

        var conflict = subnets.Where(kv => kv.Value.Count > 1).Select(kv => kv.Key).ToList();
        checks.Add(conflict.Count > 0
            ? new NetworkCheck
            {
                Label = "网段冲突",
                Detail = "多块网卡处于同一网段：" + string.Join("、", conflict) + "，可能导致路由混乱",
                Result = NetResult.Warn,
            }
            : new NetworkCheck
            {
                Label = "网段冲突",
                Detail = "各网卡网段互不冲突",
                Result = NetResult.Ok,
            });

        // ---- 网关连通性 ----
        string? probeGateway = gateways.FirstOrDefault();
        if (probeGateway is null)
        {
            checks.Add(new NetworkCheck
            {
                Label = "网关连通性",
                Detail = "无网关可测",
                Result = NetResult.Unknown,
            });
        }
        else
        {
            bool ok = await TryPingAsync(probeGateway, 1200, ct).ConfigureAwait(false);
            checks.Add(new NetworkCheck
            {
                Label = "网关连通性",
                Detail = ok ? $"{probeGateway} 可连通" : $"{probeGateway} 无响应（网线/交换机/网段问题）",
                Result = ok ? NetResult.Ok : NetResult.Error,
            });
        }

        // ---- DNS 解析 ----
        bool dnsOk = await TryResolveAsync(ProbeHost, ct).ConfigureAwait(false);
        checks.Add(new NetworkCheck
        {
            Label = "DNS 解析",
            Detail = dnsOk ? $"可解析 {ProbeHost}" : $"无法解析 {ProbeHost}（DNS 不可用或未联网）",
            Result = dnsOk ? NetResult.Ok : (dnsServers.Count > 0 ? NetResult.Error : NetResult.Warn),
        });

        // ---- 云端接口可达（HTTPS 443）----
        bool tcpOk = await TryTcpAsync(ProbeHost, 443, 3000, ct).ConfigureAwait(false);
        checks.Add(new NetworkCheck
        {
            Label = "云端服务可达",
            Detail = tcpOk
                ? $"{ProbeHost}:443 可连接（云端版本校验可用）"
                : $"{ProbeHost}:443 连接失败（云端版本校验会退回本地缓存）",
            Result = tcpOk ? NetResult.Ok : NetResult.Warn,
        });

        return new NetworkReport
        {
            Adapters = adapters,
            Checks = checks,
            At = DateTime.Now,
        };
    }

    // ---- 探测辅助 ----

    private static async Task<bool> TryPingAsync(string host, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, timeoutMs).WaitAsync(TimeSpan.FromMilliseconds(timeoutMs + 800), ct)
                .ConfigureAwait(false);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> TryResolveAsync(string host, CancellationToken ct)
    {
        try
        {
            var addrs = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
            return addrs.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> TryTcpAsync(string host, int port, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            var connect = client.ConnectAsync(host, port, ct).AsTask();
            var completed = await Task.WhenAny(connect, Task.Delay(timeoutMs, ct)).ConfigureAwait(false);
            if (completed != connect) return false;
            await connect.ConfigureAwait(false);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    // ---- 工具 ----

    /// <summary>
    /// 判断是否无线网卡：类型是 Wireless80211，或名称/型号里带无线关键字。
    /// 有些无线网卡驱动会把自己报成 Ethernet，所以不能只看类型。
    /// </summary>
    private static bool IsWirelessAdapter(NetworkInterface nic, string desc, string name)
    {
        if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) return true;

        string[] keys = { "wi-fi", "wifi", "wlan", "wireless", "802.11", "无线", "wlan0" };
        return keys.Any(k =>
            desc.Contains(k, StringComparison.OrdinalIgnoreCase) ||
            name.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatMac(NetworkInterface nic)
    {
        try
        {
            var bytes = nic.GetPhysicalAddress().GetAddressBytes();
            if (bytes.Length < 6) return string.Empty;
            return string.Join("-", bytes.Take(6).Select(b => b.ToString("X2")));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string PrefixToMask(int prefix)
    {
        if (prefix <= 0) return "0.0.0.0";
        if (prefix >= 32) return "255.255.255.255";
        uint mask = 0xFFFFFFFFu << (32 - prefix);
        return $"{(mask >> 24) & 0xFF}.{(mask >> 16) & 0xFF}.{(mask >> 8) & 0xFF}.{mask & 0xFF}";
    }

    /// <summary>网段标识：IP 与掩码相与后取到该网段的网络地址。</summary>
    private static string SubnetKey(NetworkAddress addr)
    {
        try
        {
            uint ip = IpToUInt(addr.Ip);
            uint mask = IpToUInt(addr.Mask);
            uint net = ip & mask;
            return $"{net >> 24}.{(net >> 16) & 0xFF}.{(net >> 8) & 0xFF}.{net & 0xFF}/{addr.PrefixLength}";
        }
        catch
        {
            return addr.Ip;
        }
    }

    private static uint IpToUInt(string ip)
    {
        var parts = ip.Split('.');
        return (uint.Parse(parts[0]) << 24) | (uint.Parse(parts[1]) << 16)
             | (uint.Parse(parts[2]) << 8) | uint.Parse(parts[3]);
    }

    private static long SafeSpeed(NetworkInterface nic)
    {
        try
        {
            long bps = nic.Speed;
            return bps <= 0 ? 0 : bps / 1_000_000;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>从注册表读取该网卡是否启用 DHCP。</summary>
    private static bool? TryReadDhcp(string nicId)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{nicId}");
            if (key?.GetValue("EnableDHCP") is int v) return v != 0;
        }
        catch { /* 读不到就显示未知 */ }
        return null;
    }

    /// <summary>把一次体检结果导出为可粘贴的纯文本。</summary>
    public static string ToPlainText(NetworkReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"RMGuide 网络诊断  {report.At:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"整体结论：{ResultText(report.Overall)}");

        sb.AppendLine();
        sb.AppendLine("== 网卡 ==");
        foreach (var a in report.Adapters)
        {
            string kind = a.IsVirtual ? "虚拟" : a.IsWireless ? "无线" : "有线";
            sb.AppendLine($"[{kind}] {a.Name} — {a.StatusText}"
                + (a.SpeedMbps > 0 ? $" / {a.SpeedMbps} Mbps" : "")
                + (a.DhcpEnabled is bool d ? (d ? " / DHCP" : " / 静态") : ""));
            if (a.Description.Length > 0) sb.AppendLine($"  型号：{a.Description}");
            foreach (var addr in a.Addresses)
                sb.AppendLine($"  地址：{addr.Ip}/{addr.PrefixLength}  掩码 {addr.Mask}");
            if (a.Gateways.Count > 0) sb.AppendLine($"  网关：{string.Join("、", a.Gateways)}");
            if (a.DnsServers.Count > 0) sb.AppendLine($"  DNS ：{string.Join("、", a.DnsServers)}");
        }

        sb.AppendLine();
        sb.AppendLine("== 检查项 ==");
        foreach (var c in report.Checks)
            sb.AppendLine($"[{ResultText(c.Result)}] {c.Label}：{c.Detail}");

        return sb.ToString();
    }

    public static string ResultText(NetResult r) => r switch
    {
        NetResult.Ok => "正常",
        NetResult.Warn => "注意",
        NetResult.Error => "异常",
        _ => "未知",
    };
}
