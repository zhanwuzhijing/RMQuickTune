using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace RMQuickTune.Core;

/// <summary>Engine 检测的整体结果。</summary>
public sealed class EngineInfo
{
    /// <summary>RoboMasterEngine.exe 是否正在运行。</summary>
    public bool EngineRunning { get; init; }

    /// <summary>正在运行的 engine 安装目录（exe 所在目录）。未运行时为 null。</summary>
    public string? EngineDir { get; init; }

    /// <summary>解析到的 engine 版本号（如 12.0.0.107）。未运行或解析失败时为 null。</summary>
    public string? Version { get; init; }

    /// <summary>版本解析是否失败（engine 在运行但读不出版本）。</summary>
    public bool VersionReadFailed { get; init; }

    /// <summary>Server 归属校验结果。</summary>
    public ServerOwnership ServerOwnership { get; init; }

    /// <summary>当 Server 归属异常时，记录其实际 exe 路径。</summary>
    public string? ServerActualPath { get; init; }
}

/// <summary>RMServer 相对于当前 engine 的归属状态。</summary>
public enum ServerOwnership
{
    /// <summary>RMServer.exe 未运行。</summary>
    NotRunning,

    /// <summary>RMServer.exe 位于当前 engine 目录树下，归属正确。</summary>
    Valid,

    /// <summary>RMServer.exe 来自其他目录，归属异常。</summary>
    Mismatch,

    /// <summary>无法判定（engine 未运行或路径获取失败）。</summary>
    Unknown,
}

/// <summary>
/// 定位正在运行的 RoboMasterEngine，读取其版本号，并校验正在运行的 RMServer 是否归属于该 engine。
/// 全程异常兜底，任何失败都退化为明确状态，不向外抛出。
/// </summary>
public static class EngineLocator
{
    private const string EngineProcessName = "RoboMasterEngine";
    private const string ServerProcessName = "RMServer";

    // engine 目录下到 RMServer 根目录的相对路径
    private static readonly string ServerRelativeRoot =
        Path.Combine("RoboMasterEngine_Data", "StreamingAssets", "RMServer");

    // globalgamemanagers 相对路径
    private static readonly string GgmRelativePath =
        Path.Combine("RoboMasterEngine_Data", "globalgamemanagers");

    // 四段式版本号
    private static readonly Regex VersionRegex = new(@"^\d+\.\d+\.\d+\.\d+$", RegexOptions.Compiled);

    /// <summary>执行完整检测：定位 engine、读版本、校验 server 归属。</summary>
    public static EngineInfo Detect()
    {
        string? engineExe = TryGetRunningProcessPath(EngineProcessName);
        if (engineExe is null)
        {
            return new EngineInfo
            {
                EngineRunning = false,
                ServerOwnership = ServerOwnership.Unknown,
            };
        }

        string engineDir;
        try
        {
            engineDir = Path.GetDirectoryName(engineExe) ?? "";
        }
        catch
        {
            engineDir = "";
        }

        // 读取版本
        string? version = string.IsNullOrEmpty(engineDir) ? null : TryReadVersion(engineDir);

        // 校验 server 归属
        var (ownership, serverPath) = string.IsNullOrEmpty(engineDir)
            ? (ServerOwnership.Unknown, (string?)null)
            : ValidateServer(engineDir);

        return new EngineInfo
        {
            EngineRunning = true,
            EngineDir = engineDir,
            Version = version,
            VersionReadFailed = version is null,
            ServerOwnership = ownership,
            ServerActualPath = serverPath,
        };
    }

    /// <summary>
    /// 从 globalgamemanagers 读取 engine 版本号。
    /// 结构：产品名 RoboMasterEngine → public.app-category.games → 四段式版本号。
    /// 取产品名之后第一个匹配四段式格式的 ASCII 字符串。
    /// </summary>
    public static string? TryReadVersion(string engineDir)
    {
        try
        {
            string ggm = Path.Combine(engineDir, GgmRelativePath);
            if (!File.Exists(ggm))
                return null;

            byte[] bytes = File.ReadAllBytes(ggm);
            var strings = ExtractAsciiStrings(bytes, minLength: 3);

            int productIdx = strings.FindIndex(
                s => string.Equals(s, EngineProcessName, StringComparison.Ordinal));
            if (productIdx < 0)
                return null;

            for (int i = productIdx + 1; i < strings.Count; i++)
            {
                if (VersionRegex.IsMatch(strings[i]))
                    return strings[i];
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>校验正在运行的 RMServer.exe 是否位于 engine 目录树下。</summary>
    private static (ServerOwnership, string?) ValidateServer(string engineDir)
    {
        string? serverExe = TryGetRunningProcessPath(ServerProcessName);
        if (serverExe is null)
            return (ServerOwnership.NotRunning, null);

        try
        {
            string expectedRoot = Path.GetFullPath(Path.Combine(engineDir, ServerRelativeRoot));
            // 确保以分隔符结尾，避免 RMServer 误匹配 RMServerX 之类
            if (!expectedRoot.EndsWith(Path.DirectorySeparatorChar))
                expectedRoot += Path.DirectorySeparatorChar;

            string actualFull = Path.GetFullPath(serverExe);

            bool inside = actualFull.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase);
            return inside
                ? (ServerOwnership.Valid, actualFull)
                : (ServerOwnership.Mismatch, actualFull);
        }
        catch
        {
            return (ServerOwnership.Unknown, serverExe);
        }
    }

    /// <summary>
    /// 获取某进程名（不含扩展名）当前运行实例的完整 exe 路径。
    /// 用 Win32 QueryFullProcessImageName，避免 MainModule 在 32/64 位混用或权限不足时失败。
    /// 未运行返回 null。
    /// </summary>
    public static string? TryGetRunningProcessPath(string processName)
    {
        Process[] procs;
        try { procs = Process.GetProcessesByName(processName); }
        catch { return null; }

        try
        {
            foreach (var p in procs)
            {
                string? path = TryGetProcessImagePath(p.Id);
                if (!string.IsNullOrEmpty(path))
                    return path;
            }
            return null;
        }
        finally
        {
            foreach (var p in procs)
            {
                try { p.Dispose(); } catch { /* ignore */ }
            }
        }
    }

    private static string? TryGetProcessImagePath(int pid)
    {
        IntPtr handle = IntPtr.Zero;
        try
        {
            handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle == IntPtr.Zero)
                return null;

            int capacity = 1024;
            var sb = new StringBuilder(capacity);
            if (QueryFullProcessImageName(handle, 0, sb, ref capacity))
                return sb.ToString(0, capacity);

            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                try { CloseHandle(handle); } catch { /* ignore */ }
            }
        }
    }

    /// <summary>从字节数组中提取连续可打印 ASCII 字符串（长度 ≥ minLength）。</summary>
    private static List<string> ExtractAsciiStrings(byte[] bytes, int minLength)
    {
        var result = new List<string>();
        var cur = new StringBuilder();
        foreach (byte b in bytes)
        {
            if (b >= 32 && b < 127)
            {
                cur.Append((char)b);
            }
            else
            {
                if (cur.Length >= minLength)
                    result.Add(cur.ToString());
                cur.Clear();
            }
        }
        if (cur.Length >= minLength)
            result.Add(cur.ToString());
        return result;
    }

    // ---- Win32 ----
    private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        IntPtr handle, int flags, StringBuilder exeName, ref int size);
}
