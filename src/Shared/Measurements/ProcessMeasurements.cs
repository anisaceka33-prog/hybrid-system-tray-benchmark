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
    private static IEnumerable<int> DescendantsOf(int rootPid) => Array.Empty<int>();
}

public static class SystemMetadataProvider
{
    public static SystemMetadata Capture(string sdkVersion = "unknown", string runtimeVersion = "unknown", string build = "Release") => new(Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? Environment.OSVersion.VersionString, Environment.ProcessorCount, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, Environment.OSVersion.VersionString, Environment.Is64BitProcess ? "x64" : "x86", Environment.Version.ToString(), sdkVersion, runtimeVersion, build, TryGitSha());
    private static string TryGitSha() { try { using var p = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true }); return p?.StandardOutput.ReadToEnd().Trim() ?? "unknown"; } catch { return "unknown"; } }
}
