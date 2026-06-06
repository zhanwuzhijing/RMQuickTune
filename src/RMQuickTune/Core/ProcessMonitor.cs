using System.Diagnostics;

namespace RMQuickTune.Core;

/// <summary>程序所属的分类（用于界面分栏）。</summary>
public enum ProcessCategory
{
    /// <summary>裁判端：服务 / 引擎侧程序。</summary>
    Referee,

    /// <summary>选手端：客户端侧程序。</summary>
    Player,
}

/// <summary>分类相关辅助。</summary>
public static class ProcessCategoryInfo
{
    public static string DisplayName(this ProcessCategory category) => category switch
    {
        ProcessCategory.Referee => "裁判端",
        ProcessCategory.Player => "选手端",
        _ => category.ToString(),
    };

    public static string Description(this ProcessCategory category) => category switch
    {
        ProcessCategory.Referee => "服务 / 引擎侧程序",
        ProcessCategory.Player => "客户端侧程序",
        _ => string.Empty,
    };
}

/// <summary>单个被监控程序的定义。</summary>
public sealed class MonitoredProcess
{
    /// <summary>进程的可执行文件名，例如 <c>RMServer.exe</c>。</summary>
    public string ExeName { get; }

    /// <summary>不含扩展名的进程名，用于与系统进程列表匹配。</summary>
    public string ProcessName { get; }

    /// <summary>所属分类。</summary>
    public ProcessCategory Category { get; }

    public MonitoredProcess(string exeName, ProcessCategory category)
    {
        ExeName = exeName;
        ProcessName = Path.GetFileNameWithoutExtension(exeName);
        Category = category;
    }
}

/// <summary>单个程序某一次检测的运行状态。</summary>
public sealed class ProcessStatus
{
    public required string ExeName { get; init; }
    public ProcessCategory Category { get; init; }
    public bool IsRunning { get; init; }

    /// <summary>匹配到的进程实例数量。</summary>
    public int InstanceCount { get; init; }

    /// <summary>若在运行，主进程的 PID（取第一个实例）。</summary>
    public int? Pid { get; init; }
}

/// <summary>
/// 进程运行状态检测服务。
/// 负责查询一组目标程序当前是否正在运行。
/// </summary>
public sealed class ProcessMonitor
{
    /// <summary>
    /// RoboMaster 赛事引擎相关的目标程序清单（顺序即显示顺序）。
    /// 已剔除工具类程序（如 RMUploadTool.exe）。
    /// </summary>
    public static readonly IReadOnlyList<MonitoredProcess> RoboMasterTargets = new[]
    {
        // ---- 裁判端：服务 / 引擎侧 ----
        new MonitoredProcess("AdapterSvrS0.exe",      ProcessCategory.Referee),
        new MonitoredProcess("RMServer.exe",          ProcessCategory.Referee),
        new MonitoredProcess("RMServerLogClient.exe", ProcessCategory.Referee),
        new MonitoredProcess("RoboMasterEngine.exe",  ProcessCategory.Referee),

        // ---- 选手端：客户端侧 ----
        new MonitoredProcess("RMClientDaemon.exe",                  ProcessCategory.Player),
        new MonitoredProcess("RMClientAssist_VideoRecord.exe",      ProcessCategory.Player),
        new MonitoredProcess("RMClientAssist_MarkRecognition.exe",  ProcessCategory.Player),
        new MonitoredProcess("RMClientAssist_SerialPort.exe",       ProcessCategory.Player),
        new MonitoredProcess("RMClientAssist_RemoteSerialPort.exe", ProcessCategory.Player),
        new MonitoredProcess("RoboMasterClient.exe",                ProcessCategory.Player),
    };

    private readonly IReadOnlyList<MonitoredProcess> _targets;

    public ProcessMonitor(IReadOnlyList<MonitoredProcess>? targets = null)
    {
        _targets = targets ?? RoboMasterTargets;
    }

    public IReadOnlyList<MonitoredProcess> Targets => _targets;

    /// <summary>
    /// 检测所有目标程序的运行状态。
    /// 一次性抓取系统进程快照，避免逐个查询的开销。
    /// </summary>
    public IReadOnlyList<ProcessStatus> CheckAll()
    {
        // 按进程名分组建立索引（不区分大小写）
        var running = new Dictionary<string, List<Process>>(StringComparer.OrdinalIgnoreCase);
        Process[] all;
        try
        {
            all = Process.GetProcesses();
        }
        catch
        {
            all = Array.Empty<Process>();
        }

        foreach (var p in all)
        {
            string name;
            try
            {
                name = p.ProcessName;
            }
            catch
            {
                continue;
            }

            if (!running.TryGetValue(name, out var list))
            {
                list = new List<Process>();
                running[name] = list;
            }
            list.Add(p);
        }

        var result = new List<ProcessStatus>(_targets.Count);
        foreach (var target in _targets)
        {
            running.TryGetValue(target.ProcessName, out var instances);
            int count = instances?.Count ?? 0;
            int? pid = null;
            if (count > 0)
            {
                try { pid = instances![0].Id; } catch { pid = null; }
            }

            result.Add(new ProcessStatus
            {
                ExeName = target.ExeName,
                Category = target.Category,
                IsRunning = count > 0,
                InstanceCount = count,
                Pid = pid,
            });
        }

        // 释放抓取到的进程句柄
        foreach (var p in all)
        {
            try { p.Dispose(); } catch { /* ignore */ }
        }

        return result;
    }

    /// <summary>
    /// 结束指定分类下所有正在运行的目标程序。
    /// 先尝试优雅关闭（CloseMainWindow），稍候未退出则强制结束（Kill）。
    /// </summary>
    /// <returns>实际结束的进程数量，以及失败的程序名列表。</returns>
    public KillResult KillCategory(ProcessCategory category)
    {
        var targetNames = _targets
            .Where(t => t.Category == category)
            .Select(t => t.ProcessName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int killed = 0;
        var failed = new List<string>();

        Process[] all;
        try { all = Process.GetProcesses(); }
        catch { return new KillResult { Killed = 0, Failed = { } }; }

        var toKill = new List<Process>();
        foreach (var p in all)
        {
            string name;
            try { name = p.ProcessName; }
            catch { continue; }

            if (targetNames.Contains(name))
                toKill.Add(p);
            else
            {
                try { p.Dispose(); } catch { /* ignore */ }
            }
        }

        // 第一轮：尝试优雅关闭有主窗口的进程
        foreach (var p in toKill)
        {
            try
            {
                if (p.MainWindowHandle != IntPtr.Zero)
                    p.CloseMainWindow();
            }
            catch { /* ignore */ }
        }

        // 给优雅关闭一点时间
        System.Threading.Thread.Sleep(400);

        // 第二轮：仍未退出的强制结束
        foreach (var p in toKill)
        {
            try
            {
                p.Refresh();
                if (!p.HasExited)
                {
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(2000);
                }
                killed++;
            }
            catch (Exception)
            {
                string exe;
                try { exe = p.ProcessName + ".exe"; } catch { exe = "(未知)"; }
                if (!failed.Contains(exe))
                    failed.Add(exe);
            }
            finally
            {
                try { p.Dispose(); } catch { /* ignore */ }
            }
        }

        return new KillResult { Killed = killed, Failed = failed };
    }
}

/// <summary>批量结束进程的结果。</summary>
public sealed class KillResult
{
    public int Killed { get; init; }
    public List<string> Failed { get; init; } = new();
    public bool HasFailures => Failed.Count > 0;
}
