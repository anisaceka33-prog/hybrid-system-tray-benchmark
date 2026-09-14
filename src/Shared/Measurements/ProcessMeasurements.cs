using System.Diagnostics;
namespace WebView2SystemTrayBenchmark.Shared;

public sealed record ProcessTreeSnapshot(int RootProcessId, IReadOnlyList<ProcessSnapshotRecord> Processes)
{
    public long TotalWorkingSetBytes => Processes.Sum(p => p.WorkingSetBytes);
    public long TotalPrivateMemoryBytes => Processes.Sum(p => p.PrivateMemoryBytes);
    public TimeSpan TotalCpuTime => TimeSpan.FromTicks(Processes.Sum(p => p.CpuTime.Ticks));
}

public static class ProcessMeasurement
{
    public static ProcessTreeSnapshot Capture(Process root, string runId = "unknown", string sessionId = "unknown", IEnumerable<int>? relatedPids = null)
    {
        var ids = new HashSet<int> { root.Id }; if (relatedPids is not null) ids.UnionWith(relatedPids);
        foreach (var child in DescendantsOf(root.Id)) ids.Add(child);
        var rows = new List<ProcessSnapshotRecord>();
        foreach (var pid in ids)
        {
            try { using var p = Process.GetProcessById(pid); rows.Add(new(runId, sessionId, DateTimeOffset.UtcNow, p.Id, p.ProcessName, p.Id == root.Id ? "host" : "child", p.WorkingSet64, p.PrivateMemorySize64, p.TotalProcessorTime)); }
            catch { }
        }
        return new(root.Id, rows);
    }
    public static async Task<double> MeasureCpuPercentAsync(Process root, TimeSpan interval, CancellationToken cancellationToken = default)
    {
        var before = Capture(root); var start = Stopwatch.GetTimestamp(); await Task.Delay(interval, cancellationToken); var after = Capture(root); var wall = Stopwatch.GetElapsedTime(start);
        var cpuSeconds = (after.TotalCpuTime - before.TotalCpuTime).TotalSeconds; return wall.TotalSeconds <= 0 ? 0 : cpuSeconds / wall.TotalSeconds / Environment.ProcessorCount * 100d;
    }
    // Walk the system process tree to find descendant PIDs of the given root.
    // Implemented with Win32 Toolhelp snapshot to avoid extra package dependencies.
    private static IEnumerable<int> DescendantsOf(int rootPid)
    {
        try
        {
            var map = new Dictionary<int, List<int>>();
            var snapshot = CreateToolhelp32Snapshot(SnapshotFlags.Process, 0);
            if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1)) return Array.Empty<int>();
            try
            {
                var entry = new PROCESSENTRY32();
                entry.dwSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<PROCESSENTRY32>();
                if (Process32First(snapshot, ref entry))
                {
                    do
                    {
                        var pid = (int)entry.th32ProcessID;
                        var ppid = (int)entry.th32ParentProcessID;
                        if (!map.TryGetValue(ppid, out var list)) { list = new List<int>(); map[ppid] = list; }
                        list.Add(pid);
                    } while (Process32Next(snapshot, ref entry));
                }

                var results = new List<int>();
                var stack = new Stack<int>();
                if (map.TryGetValue(rootPid, out var children))
                {
                    foreach (var c in children) stack.Push(c);
                }

                while (stack.Count > 0)
                {
                    var cur = stack.Pop();
                    if (results.Contains(cur)) continue;
                    results.Add(cur);
                    if (map.TryGetValue(cur, out var ch))
                    {
                        foreach (var c in ch) stack.Push(c);
                    }
                }

                return results;
            }
            finally { CloseHandle(snapshot); }
        }
        catch
        {
            return Array.Empty<int>();
        }
    }

    // P/Invoke definitions
    [System.Flags]
    private enum SnapshotFlags : uint { Process = 0x00000002 }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(SnapshotFlags dwFlags, uint th32ProcessID);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}

public static class SystemMetadataProvider
{
    public static SystemMetadata Capture(string sdkVersion = "unknown", string runtimeVersion = "unknown", string build = "Release") => new(Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? Environment.OSVersion.VersionString, Environment.ProcessorCount, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, Environment.OSVersion.VersionString, Environment.Is64BitProcess ? "x64" : "x86", Environment.Version.ToString(), sdkVersion, runtimeVersion, build, TryGitSha());
    private static string TryGitSha() { try { using var p = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true }); return p?.StandardOutput.ReadToEnd().Trim() ?? "unknown"; } catch { return "unknown"; } }
}
