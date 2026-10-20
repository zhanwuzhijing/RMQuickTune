using System.Net.NetworkInformation;

namespace RMGuide.Core;

/// <summary>网络配置的两个大模块，对应两份官方手册。</summary>
public enum NetModule
{
    /// <summary>裁判端：《RoboMaster 赛事引擎联网操作手册》。</summary>
    Referee,

    /// <summary>选手端：《自定义客户端配置说明手册》。</summary>
    Player,
}

/// <summary>一项目标配置。</summary>
public sealed class NetTarget
{
    /// <summary>网卡连接名（netsh 使用的 name）。</summary>
    public required string AdapterName { get; init; }

    /// <summary>角色显示名，如「局域网网卡」。</summary>
    public required string RoleLabel { get; init; }

    /// <summary>true = 自动获取（DHCP）；false = 静态 IP。</summary>
    public required bool Dhcp { get; init; }

    public string? Ip { get; init; }
    public string? Mask { get; init; }

    /// <summary>静态配置时的网关；null 表示不设网关。</summary>
    public string? Gateway { get; init; }

    /// <summary>建议跃点数；null 表示不调整。</summary>
    public int? Metric { get; init; }

    /// <summary>静态配置时是否要求网关留空。</summary>
    public bool NoGateway { get; init; }

    /// <summary>静态配置时要写入的 DNS；null = 不修改现有 DNS。</summary>
    public IReadOnlyList<string>? Dns { get; init; }

    /// <summary>该角色是否必须用有线（手册明确不建议无线）。</summary>
    public bool RequireWired { get; init; }

    /// <summary>目标配置的一句话描述。</summary>
    public string TargetText
    {
        get
        {
            if (Dhcp)
                return "自动获取 IP（DHCP）" + (Metric is int m ? $"，跃点 {m}" : "");

            string gw = Gateway is not null
                ? $"，网关 {Gateway}"
                : NoGateway ? "，网关留空" : "";
            string dns = Dns is { Count: > 0 } ? $"，DNS {string.Join("/", Dns)}" : "";
            string metric = Metric is int m2 ? $"，跃点 {m2}" : "";
            return $"静态 {Ip} / {Mask}{gw}{dns}{metric}";
        }
    }
}

/// <summary>一次方案计算的结果。</summary>
public sealed class NetPlan
{
    public required NetModule Module { get; init; }

    /// <summary>裁判端 = 局域网网卡；选手端 = 赛事引擎网卡。</summary>
    public string? FirstName { get; init; }

    /// <summary>裁判端 = 外网网卡；选手端 = 自定义客户端网卡。</summary>
    public string? SecondName { get; init; }

    public IReadOnlyList<NetTarget> Targets { get; init; } = Array.Empty<NetTarget>();
    public IReadOnlyList<NetworkCheck> Checks { get; init; } = Array.Empty<NetworkCheck>();

    public string FirstLabel => Module == NetModule.Referee ? "局域网网卡" : "赛事引擎网卡";
    public string SecondLabel => Module == NetModule.Referee ? "外网网卡" : "自定义客户端网卡";

    /// <summary>需要修改的目标数量。</summary>
    public int PendingCount => Checks.Count(c => c.Result is NetResult.Warn or NetResult.Error);
}

/// <summary>
/// 把两份官方手册的规则固化成「该改哪块网卡、改成什么」，
/// 并给出当前状态与目标的差异判定。
/// </summary>
public static class NetAdvisor
{
    // ---- 手册中的固定参数 ----
    public const string LanIp = "192.168.1.2";        // 裁判端局域网网卡
    public const string LanMask = "255.255.255.0";
    public const string LanGateway = "192.168.1.1";   // 赛事引擎路由器 LAN 口

    public const string ClientIp = "192.168.12.1";    // 选手端接自定义客户端
    public const string ClientMask = "255.255.255.0";
    public const string ClientPeerIp = "192.168.12.2"; // 自定义客户端设备的地址

    /// <summary>外网网卡必须避开的网段（手册明确要求）。</summary>
    public static readonly string[] ForbiddenSubnets = { "192.168.1.0/24", "192.168.12.0/24" };

    public const int LanMetric = 20;   // 局域网网卡跃点（外网不可用时）
    public const int WanMetric = 10;   // 外网网卡跃点

    /// <summary>
    /// 识别「接赛事引擎局域网的那块网卡」。
    /// 裁判端的局域网网卡与选手端的赛事引擎网卡默认必须指同一张，所以两边共用这一套判定。
    /// </summary>
    public static string? DetectEventLanAdapter(IReadOnlyList<AdapterInfo> adapters)
    {
        var physical = adapters.Where(a => a.IsUp && !a.IsVirtual).ToList();
        var wired = physical.Where(a => !a.IsWireless).ToList();

        static bool InLan(AdapterInfo a)
            => a.Addresses.Any(x => !x.IsApipa && NetUtil.InSubnet(x.Ip, "192.168.1.0/24"))
               || a.Gateways.Any(g => NetUtil.InSubnet(g, "192.168.1.0/24"));

        // 自定义客户端网段（192.168.12.0/24）不是赛局域网，排除
        static bool RealIp(AdapterInfo a)
            => a.Addresses.Any(x => !x.IsApipa && !NetUtil.InSubnet(x.Ip, "192.168.12.0/24"));

        return (wired.FirstOrDefault(InLan)
                ?? physical.FirstOrDefault(InLan)
                ?? wired.FirstOrDefault(RealIp)
                ?? physical.FirstOrDefault(RealIp)
                ?? wired.FirstOrDefault()
                ?? physical.FirstOrDefault())?.Name;
    }

    /// <summary>推荐网卡角色（仅在用户未手动指定时使用）。</summary>
    public static (string? First, string? Second) Recommend(NetModule module, IReadOnlyList<AdapterInfo> adapters)
    {
        // 无线也能用（学生常这么接），只是优先级低于有线
        var physical = adapters.Where(a => a.IsUp && !a.IsVirtual).ToList();

        if (module == NetModule.Referee)
        {
            // 局域网网卡：与选手端「赛事引擎网卡」同一套判定，默认指向同一张
            string? lan = DetectEventLanAdapter(adapters);

            var others = physical.Where(a => a.Name != lan).ToList();

            // 外网网卡：优先「有网关的有线」→「有网关的无线」→「其他有线」→「其他」
            string? wan = others.FirstOrDefault(a => a.Gateways.Count > 0 && !a.IsWireless)?.Name
                          ?? others.FirstOrDefault(a => a.Gateways.Count > 0)?.Name
                          ?? others.FirstOrDefault(a => !a.IsWireless)?.Name
                          ?? others.FirstOrDefault()?.Name;

            return (lan, wan);
        }
        else
        {
            // 赛事引擎网卡：与裁判端「局域网网卡」是同一张
            string? engine = DetectEventLanAdapter(adapters);

            // 自定义客户端网卡：只有存在第二块物理网卡时才推荐；
            // 只有一块网卡时保持 null＝「同局域网直连」模式。
            string? client = physical.FirstOrDefault(a =>
                a.Name != engine &&
                (a.Addresses.Any(x => !x.IsApipa && NetUtil.InSubnet(x.Ip, "192.168.12.0/24"))
                 || a.Addresses.Count == 0 || a.Addresses.All(x => x.IsApipa)))?.Name;

            return (engine, client);
        }
    }

    /// <summary>根据当前网卡与角色选择，生成目标配置与差异判定（不含网络探测）。</summary>
    public static NetPlan Build(NetModule module, IReadOnlyList<AdapterInfo> adapters,
        string? firstName, string? secondName)
    {
        var targets = new List<NetTarget>(2);
        var checks = new List<NetworkCheck>();

        var first = Find(adapters, firstName);
        var second = Find(adapters, secondName);

        if (module == NetModule.Referee)
        {
            // ---- 局域网网卡：静态 192.168.1.2 ----
            if (first is null)
            {
                checks.Add(new NetworkCheck
                {
                    Label = "局域网网卡",
                    Detail = "未找到可用网卡，请插入网线或手动指定网卡",
                    Result = NetResult.Error,
                });
            }
            else
            {
                // 方案 1（单网卡直连路由器）：局域网网卡必须带网关，否则外网测试过不了；
                // 方案 2（双网卡）：局域网网卡网关留空，由外网网卡提供默认路由。
                bool dual = second is not null && second.Name != first.Name;

                var lanTarget = new NetTarget
                {
                    AdapterName = first.Name,
                    RoleLabel = "局域网网卡",
                    Dhcp = false,
                    Ip = LanIp,
                    Mask = LanMask,
                    Gateway = dual ? null : LanGateway,
                    NoGateway = dual,
                    // 方案 1 单网卡：外网要走这块网卡的网关，DNS 指向路由器，避免域名解析失败
                    Dns = dual ? null : new[] { LanGateway },
                    Metric = dual ? LanMetric : null,
                    RequireWired = true,
                };
                targets.Add(lanTarget);

                bool ipOk = first.Addresses.Any(a => a.Ip == LanIp && a.Mask == LanMask);
                bool gwOk = lanTarget.Gateway is not null
                    ? first.Gateways.Contains(lanTarget.Gateway)
                    : first.Gateways.Count == 0;
                string linkWarn = LinkWarning(first);

                checks.Add(new NetworkCheck
                {
                    Label = $"局域网网卡（{first.Name}）",
                    Detail = linkWarn.Length > 0
                        ? $"{linkWarn}；目标 {lanTarget.TargetText}"
                        : ipOk && gwOk
                            ? $"已是 {lanTarget.TargetText}"
                            : $"当前 {Describe(first)}，目标 {lanTarget.TargetText}",
                    Result = linkWarn.Length > 0 ? NetResult.Warn
                        : ipOk && gwOk ? NetResult.Ok
                        : NetResult.Error,
                });
            }

            // ---- 外网网卡：自动获取，避开两个网段 ----
            if (second is null)
            {
                checks.Add(new NetworkCheck
                {
                    Label = "外网接入",
                    Detail = $"未指定第二块网卡：单网卡直连路由器时，局域网网卡需带网关 {LanGateway}"
                        + "（路由器 WAN 口 IP 须避开 192.168.1.0/24 与 192.168.12.0/24）",
                    Result = NetResult.Ok,
                });
            }
            else
            {
                targets.Add(new NetTarget
                {
                    AdapterName = second.Name,
                    RoleLabel = "外网网卡",
                    Dhcp = true,
                    Metric = WanMetric,
                });

                bool conflict = second.Addresses.Any(a => !a.IsApipa &&
                    ForbiddenSubnets.Any(s => NetUtil.InSubnet(a.Ip, s)));
                bool dhcpOk = second.DhcpEnabled != false;

                checks.Add(new NetworkCheck
                {
                    Label = $"外网网卡（{second.Name}）",
                    Detail = conflict
                        ? "地址落在 192.168.1.0/24 或 192.168.12.0/24，外网网卡必须避开这两个网段"
                        : dhcpOk
                            ? $"当前 {Describe(second)}"
                            : $"当前为静态配置，目标改为自动获取（{Describe(second)}）",
                    Result = conflict ? NetResult.Error : dhcpOk ? NetResult.Ok : NetResult.Warn,
                });
            }

            // ---- 防火墙 ----
            checks.Add(FirewallCheck());
        }
        else
        {
            // ---- 赛事引擎网卡：自动获取 ----
            if (first is null)
            {
                checks.Add(new NetworkCheck
                {
                    Label = "赛事引擎网卡",
                    Detail = "未找到可用网卡（有线/无线均可），请检查连接或手动指定网卡",
                    Result = NetResult.Error,
                });
            }
            else
            {
                targets.Add(new NetTarget
                {
                    AdapterName = first.Name,
                    RoleLabel = "赛事引擎网卡",
                    Dhcp = true,
                    RequireWired = true,
                });

                bool dhcpOk = first.DhcpEnabled != false && first.Addresses.Any(a => !a.IsApipa);
                string linkWarn = LinkWarning(first);

                // 选手端网卡上留着裁判端的配置（192.168.1.2）：
                // 常见于「本机同时开裁判端和选手端」，这种用法虽然不符合选手端手册，但能正常工作，
                // 所以只给黄色警告、不判错，也不在文案里强推修改。
                bool refereeConfig = first.DhcpEnabled == false
                    && first.Addresses.Any(a => a.Ip == LanIp && a.Mask == LanMask);

                checks.Add(new NetworkCheck
                {
                    Label = $"赛事引擎网卡（{first.Name}）",
                    Detail = linkWarn.Length > 0
                        ? linkWarn
                        : dhcpOk
                            ? $"当前 {Describe(first)}"
                            : refereeConfig
                                ? $"当前是裁判端配置（{LanIp}）：如果这台机器裁判端和选手端一起开，保持原样即可正常使用；"
                                  + "只跑选手端时建议改为自动获取 IP"
                                : $"当前 {Describe(first)}，目标改为自动获取 IP",
                    Result = linkWarn.Length > 0 ? NetResult.Warn
                        : dhcpOk ? NetResult.Ok
                        : refereeConfig ? NetResult.Warn
                        : NetResult.Error,
                });
            }

            // ---- 自定义客户端直连 IP：跟随「自定义客户端网卡」的选择 ----
            // 选同局域网 → 显示本机在赛事引擎局域网的地址；
            // 选独立网卡 → 显示那张网卡当前的地址（应用后变为 192.168.12.1）。
            string? directIp;
            string directNote;
            if (second is null)
            {
                directIp = first?.Addresses.FirstOrDefault(a => !a.IsApipa)?.Ip;
                directNote = "本机在赛事引擎局域网中的地址，自定义客户端设备与它同网段即可通信";
            }
            else
            {
                directIp = second.Addresses.FirstOrDefault(a => !a.IsApipa)?.Ip;
                directNote = second.Addresses.Any(a => a.Ip == ClientIp)
                    ? $"自定义客户端网卡地址，对端设备应设为 {ClientPeerIp}"
                    : $"自定义客户端网卡当前地址，应用配置后变为 {ClientIp}，对端设备应设为 {ClientPeerIp}";
            }

            checks.Add(directIp is null
                ? new NetworkCheck
                {
                    Label = "自定义客户端直连 IP",
                    Detail = second is null
                        ? "尚未获取到局域网 IP，接入赛事引擎路由器后才会显示"
                        : $"该网卡还没有 IP，应用配置后会设为 {ClientIp}",
                    Result = NetResult.Warn,
                }
                : new NetworkCheck
                {
                    Label = "自定义客户端直连 IP",
                    Detail = $"{directIp}　{directNote}",
                    Result = NetResult.Ok,
                });

            // ---- 自定义客户端接入方式：同局域网直连 / 独立网卡 ----
            if (second is null)
            {
                checks.Add(new NetworkCheck
                {
                    Label = "自定义客户端接入方式",
                    Detail = directIp is null
                        ? "同局域网直连：把自定义客户端设备接入赛事引擎路由器（无需第二块网卡）"
                        : $"同局域网直连：把自定义客户端设备接入赛事引擎路由器，与 {directIp} 同网段即可（无需第二块网卡）",
                    Result = directIp is null ? NetResult.Warn : NetResult.Ok,
                });
            }
            else
            {
                targets.Add(new NetTarget
                {
                    AdapterName = second.Name,
                    RoleLabel = "自定义客户端网卡",
                    Dhcp = false,
                    Ip = ClientIp,
                    Mask = ClientMask,
                    NoGateway = true,
                    RequireWired = true,
                });

                checks.Add(new NetworkCheck
                {
                    Label = "自定义客户端接入方式",
                    Detail = $"独立网卡直连：自定义客户端设备用网线接在这块网卡上，设备地址设为 {ClientPeerIp}",
                    Result = NetResult.Ok,
                });

                bool ipOk = second.Addresses.Any(a => a.Ip == ClientIp && a.Mask == ClientMask);
                bool gwOk = second.Gateways.Count == 0;
                string linkWarn = LinkWarning(second);

                checks.Add(new NetworkCheck
                {
                    Label = $"自定义客户端网卡（{second.Name}）",
                    Detail = linkWarn.Length > 0
                        ? $"{linkWarn}；目标 静态 {ClientIp} / {ClientMask}"
                        : ipOk && gwOk
                            ? $"已是 {ClientIp} / {ClientMask}，网关留空"
                            : $"当前 {Describe(second)}，目标 {ClientIp} / {ClientMask}（网关留空）",
                    Result = linkWarn.Length > 0 ? NetResult.Warn : ipOk && gwOk ? NetResult.Ok : NetResult.Error,
                });
            }

            checks.Add(FirewallCheck());
        }

        // 角色重复
        if (firstName is not null && firstName == secondName)
        {
            checks.Add(new NetworkCheck
            {
                Label = "网卡角色",
                Detail = "两个角色选了同一块网卡，请分别指定不同的网卡",
                Result = NetResult.Error,
            });
        }

        return new NetPlan
        {
            Module = module,
            FirstName = firstName,
            SecondName = secondName,
            Targets = targets,
            Checks = checks,
        };
    }

    /// <summary>连通性探测（异步，仅在刷新时执行）。</summary>
    public static async Task<IReadOnlyList<NetworkCheck>> ProbeAsync(
        NetModule module, CancellationToken ct)
    {
        var checks = new List<NetworkCheck>();

        bool lanOk = await NetUtil.PingAsync(LanGateway, 1200, ct).ConfigureAwait(false);
        checks.Add(new NetworkCheck
        {
            Label = "赛事引擎路由器可达",
            Detail = lanOk
                ? $"{LanGateway} 可连通（局域网正常）"
                : $"{LanGateway} 无响应：网线未接路由器 LAN 口、路由器未上电或网段不对",
            Result = module == NetModule.Referee
                ? (lanOk ? NetResult.Ok : NetResult.Error)
                : (lanOk ? NetResult.Ok : NetResult.Warn),
        });

        if (module == NetModule.Referee)
        {
            bool dns = await NetUtil.ResolveAsync("edu.dji.com", ct).ConfigureAwait(false);
            checks.Add(new NetworkCheck
            {
                Label = "外网可访问",
                Detail = dns
                    ? "可解析 edu.dji.com，外网连接正常"
                    : "无法解析 edu.dji.com：外网网卡未联网或 DNS 不可用",
                Result = dns ? NetResult.Ok : NetResult.Error,
            });
        }
        else
        {
            bool peer = await NetUtil.PingAsync(ClientPeerIp, 1200, ct).ConfigureAwait(false);
            checks.Add(new NetworkCheck
            {
                Label = "自定义客户端设备可达",
                Detail = peer
                    ? $"{ClientPeerIp} 可连通（自定义客户端已接入）"
                    : $"{ClientPeerIp} 无响应：设备未开机、IP 未设为 {ClientPeerIp} 或未插好网线",
                Result = peer ? NetResult.Ok : NetResult.Warn,
            });
        }

        return checks;
    }

    /// <summary>防火墙是否开启（三个配置文件中任一开启即视为开启）。</summary>
    public static bool IsFirewallOn()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy");
            if (key is null) return false;

            foreach (var profile in new[] { "StandardProfile", "PublicProfile", "DomainProfile" })
            {
                using var p = key.OpenSubKey(profile);
                if (p?.GetValue("EnableFirewall") is int v && v == 1) return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static NetworkCheck FirewallCheck()
        => IsFirewallOn()
            ? new NetworkCheck
            {
                Label = "Windows 防火墙",
                Detail = "防火墙处于开启状态，赛事环境需要关闭（或放行全部）",
                Result = NetResult.Warn,
            }
            : new NetworkCheck
            {
                Label = "Windows 防火墙",
                Detail = "防火墙已关闭",
                Result = NetResult.Ok,
            };

    private static AdapterInfo? Find(IReadOnlyList<AdapterInfo> adapters, string? name)
        => name is null ? null : adapters.FirstOrDefault(a => a.Name == name);

    /// <summary>网卡适用性提示：虚拟网卡 / 无线。返回空串表示没有需要注意的。</summary>
    private static string LinkWarning(AdapterInfo a)
    {
        if (a.IsVirtual)
            return $"这是虚拟网卡（{a.Name}），VMware / VPN / 蓝牙这类接口不适合作为赛事网络接口，建议改选物理网卡";
        if (a.IsWireless)
            return $"检测到通过无线连接（{a.Name}）：可以继续使用，建议改用网线，无线可能影响稳定性";
        return string.Empty;
    }

    private static string Describe(AdapterInfo a)
    {
        if (a.Addresses.Count == 0) return "未获取到 IP";
        var parts = a.Addresses.Select(x => $"{x.Ip}/{x.PrefixLength}").ToList();
        string ip = string.Join("、", parts);
        string gw = a.Gateways.Count > 0 ? $"（网关 {string.Join("、", a.Gateways)}）" : "（无网关）";
        string mode = a.DhcpEnabled is bool d ? (d ? "DHCP " : "静态 ") : "";
        return mode + ip + gw;
    }
}

/// <summary>网段与探测工具。</summary>
public static class NetUtil
{
    /// <summary>判断 IP 是否落在 CIDR 网段内，如 192.168.1.0/24。</summary>
    public static bool InSubnet(string ip, string cidr)
    {
        try
        {
            var parts = cidr.Split('/');
            if (parts.Length != 2) return false;
            int prefix = int.Parse(parts[1]);

            uint netIp = IpToUInt(ip);
            uint netBase = IpToUInt(parts[0]);
            uint mask = prefix <= 0 ? 0u : prefix >= 32 ? 0xFFFFFFFFu : (0xFFFFFFFFu << (32 - prefix));
            return (netIp & mask) == (netBase & mask);
        }
        catch
        {
            return false;
        }
    }

    public static uint IpToUInt(string ip)
    {
        var p = ip.Split('.');
        return (uint.Parse(p[0]) << 24) | (uint.Parse(p[1]) << 16) | (uint.Parse(p[2]) << 8) | uint.Parse(p[3]);
    }

    /// <summary>掩码转前缀长度，如 255.255.255.0 → 24。</summary>
    public static int MaskToPrefix(string mask)
    {
        try
        {
            uint m = IpToUInt(mask);
            int n = 0;
            while ((m & 0x80000000u) != 0) { n++; m <<= 1; }
            return n;
        }
        catch
        {
            return 24;
        }
    }

    public static async Task<bool> PingAsync(string host, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, timeoutMs)
                .WaitAsync(TimeSpan.FromMilliseconds(timeoutMs + 800), ct)
                .ConfigureAwait(false);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> ResolveAsync(string host, CancellationToken ct)
    {
        try
        {
            var addrs = await System.Net.Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
            return addrs.Length > 0;
        }
        catch
        {
            return false;
        }
    }
}
