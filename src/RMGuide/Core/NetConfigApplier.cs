using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace RMGuide.Core;

/// <summary>一条待执行的外部命令。</summary>
public sealed class NetCmd
{
    public string File { get; set; } = "netsh";
    public string Args { get; set; } = string.Empty;

    public NetCmd() { }

    public NetCmd(string args) => Args = args;
}

/// <summary>单条命令的执行结果。</summary>
public sealed class NetCmdResult
{
    public string Args { get; set; } = string.Empty;
    public int Code { get; set; }
    public string Output { get; set; } = string.Empty;
    public bool Ok => Code == 0;
}

/// <summary>一批命令的执行结果。</summary>
public sealed class NetApplyResult
{
    public List<NetCmdResult> Results { get; set; } = new();
    public bool Canceled { get; set; }
    public string? Error { get; set; }

    public bool AllOk => !Canceled && Error is null && Results.All(r => r.Ok);
    public IEnumerable<NetCmdResult> Failed => Results.Where(r => !r.Ok);
}

/// <summary>
/// 网络配置的执行层：把「目标配置」翻译成 netsh 命令，并在需要时以管理员身份执行。
/// 只执行 netsh，不直接改注册表，便于回滚与复现。
/// </summary>
public static class NetConfigApplier
{
    /// <summary>当前进程是否具备管理员权限。</summary>
    public static bool IsAdmin { get; } = CheckAdmin();

    private static bool CheckAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    // ---- 命令构建 ----

    /// <summary>按方案生成「应用」命令。</summary>
    public static List<NetCmd> BuildApply(NetPlan plan)
    {
        var cmds = new List<NetCmd>();

        foreach (var t in plan.Targets)
        {
            cmds.AddRange(BuildAdapterCommands(t.AdapterName, t.Dhcp, t.Ip, t.Mask,
                t.Gateway, t.Dns, dnsDhcpWhenAuto: true));

            if (t.Metric is int m)
                cmds.Add(new NetCmd($"interface ipv4 set interface name=\"{t.AdapterName}\" metric={m}"));
        }

        return cmds;
    }

    /// <summary>恢复默认：全部改回自动获取（DHCP + 自动 DNS + 自动跃点）。</summary>
    public static List<NetCmd> BuildResetToDefault(IEnumerable<string> adapterNames)
    {
        var cmds = new List<NetCmd>();
        foreach (var name in adapterNames)
        {
            cmds.Add(new NetCmd($"interface ipv4 set address name=\"{name}\" source=dhcp"));
            cmds.Add(new NetCmd($"interface ipv4 set dnsservers name=\"{name}\" source=dhcp"));
            cmds.Add(new NetCmd($"interface ipv4 set interface name=\"{name}\" metric=automatic"));
        }
        return cmds;
    }

    /// <summary>按存档点恢复。</summary>
    public static List<NetCmd> BuildRestore(SnapshotFile snapshot)
    {
        var cmds = new List<NetCmd>();

        foreach (var a in snapshot.Adapters)
        {
            cmds.AddRange(BuildAdapterCommands(a.Name, a.DhcpEnabled, a.Ip, a.Mask,
                a.Gateway, a.Dns, dnsDhcpWhenAuto: true));

            cmds.Add(a.Metric is int m
                ? new NetCmd($"interface ipv4 set interface name=\"{a.Name}\" metric={m}")
                : new NetCmd($"interface ipv4 set interface name=\"{a.Name}\" metric=automatic"));
        }

        return cmds;
    }

    /// <summary>生成单块网卡 IP/DNS 的命令序列。</summary>
    private static IEnumerable<NetCmd> BuildAdapterCommands(string name, bool dhcp,
        string? ip, string? mask, string? gateway, IReadOnlyList<string>? dns, bool dnsDhcpWhenAuto)
    {
        if (dhcp)
        {
            yield return new NetCmd($"interface ipv4 set address name=\"{name}\" source=dhcp");
            if (dnsDhcpWhenAuto)
                yield return new NetCmd($"interface ipv4 set dnsservers name=\"{name}\" source=dhcp");
            yield break;
        }

        if (string.IsNullOrEmpty(ip) || string.IsNullOrEmpty(mask))
            yield break;

        string addr = $"interface ipv4 set address name=\"{name}\" static {ip} {mask}";
        if (!string.IsNullOrEmpty(gateway))
            addr += $" {gateway}";

        yield return new NetCmd(addr);

        // 静态网卡：有 DNS 就写入，显式空列表则清空，null 表示保持现有 DNS 不动
        if (dns is { Count: > 0 })
        {
            yield return new NetCmd(
                $"interface ipv4 set dnsservers name=\"{name}\" source=static address={dns[0]} register=primary");
            for (int i = 1; i < dns.Count; i++)
                yield return new NetCmd(
                    $"interface ipv4 add dnsservers name=\"{name}\" address={dns[i]} index={i + 1}");
        }
        else if (dns is { Count: 0 })
        {
            yield return new NetCmd($"interface ipv4 set dnsservers name=\"{name}\" source=static address=none");
        }
    }

    /// <summary>关闭防火墙（两份手册都要求）。</summary>
    public static NetCmd BuildFirewallOff()
        => new("advfirewall set allprofiles state off");

    // ---- 执行 ----

    /// <summary>执行命令序列；非管理员时以 UAC 提权重启自身执行。</summary>
    public static NetApplyResult Run(IReadOnlyList<NetCmd> cmds)
    {
        if (cmds.Count == 0) return new NetApplyResult();

        return IsAdmin ? RunInline(cmds) : RunElevated(cmds);
    }

    private static NetApplyResult RunInline(IReadOnlyList<NetCmd> cmds)
    {
        var result = new NetApplyResult();

        foreach (var cmd in cmds)
        {
            var psi = new ProcessStartInfo
            {
                FileName = cmd.File,
                Arguments = cmd.Args,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            try
            {
                using var p = Process.Start(psi);
                if (p is null)
                {
                    result.Results.Add(new NetCmdResult { Args = cmd.Args, Code = -1, Output = "无法启动进程" });
                    continue;
                }

                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(30000);

                result.Results.Add(new NetCmdResult
                {
                    Args = cmd.Args,
                    Code = p.ExitCode,
                    Output = string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : stderr.Trim(),
                });
            }
            catch (Exception ex)
            {
                result.Results.Add(new NetCmdResult { Args = cmd.Args, Code = -1, Output = ex.Message });
            }
        }

        return result;
    }

    /// <summary>以管理员身份重启自身执行（弹出 UAC）。</summary>
    private static NetApplyResult RunElevated(IReadOnlyList<NetCmd> cmds)
    {
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
            return new NetApplyResult { Error = "无法定位程序自身路径，请以管理员身份运行本程序" };

        string jobPath = Path.Combine(Path.GetTempPath(), $"rmqt-net-{Guid.NewGuid():N}.json");
        string resultPath = Path.Combine(Path.GetTempPath(), $"rmqt-net-{Guid.NewGuid():N}.result.json");

        try
        {
            var job = new HelperJob { Commands = cmds.ToList(), ResultPath = resultPath };
            File.WriteAllText(jobPath, JsonSerializer.Serialize(job), Encoding.UTF8);

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"--netsh-helper \"{jobPath}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            using var p = Process.Start(psi);
            if (p is null)
                return new NetApplyResult { Error = "无法启动提权进程" };

            p.WaitForExit(120000);

            if (!File.Exists(resultPath))
                return new NetApplyResult { Canceled = true, Error = "提权执行被取消或未返回结果" };

            var parsed = JsonSerializer.Deserialize<NetApplyResult>(File.ReadAllText(resultPath));
            return parsed ?? new NetApplyResult { Error = "提权执行结果无法解析" };
        }
        catch (Exception ex)
        {
            // 用户在 UAC 弹窗点了「否」
            return ex is System.ComponentModel.Win32Exception
                ? new NetApplyResult { Canceled = true, Error = "已取消管理员授权" }
                : new NetApplyResult { Error = ex.Message };
        }
        finally
        {
            TryDelete(jobPath);
            TryDelete(resultPath);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }

    /// <summary>提权 helper 的作业文件。</summary>
    public sealed class HelperJob
    {
        public List<NetCmd> Commands { get; set; } = new();
        public string ResultPath { get; set; } = string.Empty;
    }

    /// <summary>
    /// 提权 helper 入口：由带 --netsh-helper 参数的自身进程调用。
    /// 逐个执行命令，把结果写入结果文件。
    /// </summary>
    public static int RunHelper(string jobPath)
    {
        try
        {
            var job = JsonSerializer.Deserialize<HelperJob>(File.ReadAllText(jobPath));
            if (job is null) return 2;

            var result = RunInline(job.Commands);
            File.WriteAllText(job.ResultPath, JsonSerializer.Serialize(result), Encoding.UTF8);
            return 0;
        }
        catch
        {
            return 3;
        }
    }
}
