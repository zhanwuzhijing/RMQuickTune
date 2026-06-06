using System.Diagnostics;

namespace RMQuickTune.Core;

/// <summary>单个被监控程序的定义。</summary>
public sealed class MonitoredProcess
{
    /// <summary>进程的可执行文件名，例如 <c>RMServer.exe</c>。</summary>
    public string ExeName { get; }

    /// <summary>不含扩展名的进程名，用于与系统进程列表匹配。</summary>
    public string ProcessName { get; }

    public MonitoredProcess(string exeName)
    {
        ExeName = exeName;
        ProcessName = Path.GetFileNameWithoutExtension(exeName);
    }
}

/// <summary>单个程序某一次检测的运行状态。</summary>
public sealed class ProcessStatus
{
    public required string ExeName { get; init; }
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
    /// <summary>RoboMaster 赛事引擎相关的目标程序清单（顺序即显示顺序）。</summary>
    public static readonly IReadOnlyList<MonitoredProcess> RoboMasterTargets = new[]
    {
        "AdapterSvrS0.exe",
        "RMServer.exe",
        "RMServerLogClient.exe",
        "RMUploadTool.exe",
        "RoboMasterEngine.exe",
        "RMClientDaemon.exe",
        "RMClientAssist_VideoRecord.exe",
        "RMClientAssist_MarkRecognition.exe",
        "RMClientAssist_SerialPort.exe",
        "RMClientAssist_RemoteSerialPort.exe",
        "RoboMasterClient.exe",
    }.Select(n => new MonitoredProcess(n)).ToArray();

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
}
