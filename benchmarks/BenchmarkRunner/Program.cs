using System.Diagnostics;
using System.Text.Json;
using WebView2SystemTrayBenchmark.Shared;

var options = Args.Parse(args); var output = options.Get("output", Path.Combine(Environment.CurrentDirectory, "results", "raw")); var executable = options.Get("executable", "HybridWebView2App.exe"); var experiment = options.Get("experiment", "E1"); var lifecycle = options.Get("lifecycle", "reuse"); var warmup = options.GetInt("warmup", 0); var iterations = options.GetInt("iterations", 1); var payload = options.GetInt("payload", 1024); var records = new List<MeasurementRecord>();
for (var i = 0; i < warmup + iterations; i++)
{
    var runId = Guid.NewGuid().ToString("N"); var sessionId = Guid.NewGuid().ToString("N"); var started = Stopwatch.GetTimestamp(); Process? process = null;
    try
    {
        var psi = new ProcessStartInfo(executable, $"--benchmark --experiment={experiment} --run-id={runId} --session-id={sessionId} --lifecycle={lifecycle} --payload={payload}") { UseShellExecute = true };
        process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {executable}");
        var eventFile = Path.Combine(Path.GetDirectoryName(process.MainModule?.FileName) ?? Environment.CurrentDirectory, executable.Contains("Native", StringComparison.OrdinalIgnoreCase) ? "native-events.jsonl" : "hybrid-events.jsonl");
        var ready = await WaitForEventAsync(eventFile, "ui_ready", TimeSpan.FromSeconds(options.GetInt("timeout-seconds", 30)));
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        records.Add(new(runId, sessionId, DateTimeOffset.UtcNow, executable.Contains("Native", StringComparison.OrdinalIgnoreCase) ? "native" : "hybrid", lifecycle, payload, "process-to-ui", "tti", elapsed, "ms", i + 1, ready ? "ok" : "failed", ready ? null : "ui_ready timeout", "unknown", Environment.Version.ToString()));
    }
    catch (Exception ex) { records.Add(new(runId, sessionId, DateTimeOffset.UtcNow, "unknown", lifecycle, payload, "process-to-ui", "tti", 0, "ms", i + 1, "failed", ex.Message, "unknown", Environment.Version.ToString())); }
    finally { try { if (process is { HasExited: false }) process.Kill(true); } catch { } process?.Dispose(); }
}
Console.WriteLine(new ResultWriter().WriteMeasurements(records, output, experiment.ToLowerInvariant()));
static async Task<bool> WaitForEventAsync(string path, string name, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout; long offset = 0;
    while (DateTime.UtcNow < deadline) { if (File.Exists(path)) { await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); stream.Seek(offset, SeekOrigin.Begin); using var reader = new StreamReader(stream); while (await reader.ReadLineAsync() is { } line) { offset = stream.Position; if (line.Contains($"\"eventName\":\"{name}\"", StringComparison.Ordinal)) return true; } } await Task.Delay(50); }
    return false;
}
sealed class Args
{
    private readonly Dictionary<string, string> _values; private Args(Dictionary<string, string> values) => _values = values;
    public static Args Parse(string[] args) => new(args.Where(a => a.StartsWith("--")).Select(a => a[2..].Split('=', 2)).ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "true", StringComparer.OrdinalIgnoreCase));
    public string Get(string key, string fallback) => _values.TryGetValue(key, out var value) ? value : fallback; public int GetInt(string key, int fallback) => int.TryParse(Get(key, ""), out var value) ? value : fallback;
}
