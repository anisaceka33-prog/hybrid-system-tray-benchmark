using System.Diagnostics;
using System.Text.Json;
using System.Globalization;
using WebView2SystemTrayBenchmark.Shared;

var cfg = RunnerConfig.Parse(args);
if (cfg.ShowHelp)
{
    Console.WriteLine(RunnerConfig.HelpText);
    return 0;
}

cfg.Validate();

var gitCommit = GitHelper.GetCommitSha() ?? "unknown";

var writer = new ResultWriter();
Directory.CreateDirectory(cfg.Output);

switch (cfg.Experiment)
{
    case "E1":
        await RunE1Async(cfg, writer, gitCommit);
        break;
    case "E2":
        await RunE2Async(cfg, writer, gitCommit);
        break;
    case "E3":
        await Experiments.RunE3Async(cfg, writer, gitCommit);
        break;
    case "E4":
        await Experiments.RunE4Async(cfg, writer, gitCommit);
        break;
    case "E5":
        await Experiments.RunE5Async(cfg, writer, gitCommit);
        break;
    case "E6":
        await Experiments.RunE6Async(cfg, writer, gitCommit);
        break;
    default:
        Console.Error.WriteLine($"Unknown experiment '{cfg.Experiment}'. Valid values: E1, E2, E3, E4, E5, E6.");
        return 2;
}

return 0;

static async Task RunE1Async(RunnerConfig cfg, ResultWriter writer, string gitCommit)
{
    var records = new List<MeasurementRecord>();
    for (var i = 0; i < cfg.Warmup + cfg.Iterations; i++)
    {
        var runId = Guid.NewGuid().ToString("N");
        var sessionId = Guid.NewGuid().ToString("N");
        var started = Stopwatch.GetTimestamp();
        Process? process = null;
        try
        {
            var psi = new ProcessStartInfo(cfg.Executable, $"--benchmark --experiment=E1 --run-id={runId} --session-id={sessionId} --lifecycle={cfg.Lifecycle} --payload={cfg.Payload}") { UseShellExecute = true };
            process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {cfg.Executable}");
            var eventFile = Path.Combine(Path.GetDirectoryName(process.MainModule?.FileName) ?? Environment.CurrentDirectory, cfg.Executable.Contains("Native", StringComparison.OrdinalIgnoreCase) ? "native-events.jsonl" : "hybrid-events.jsonl");
            var ready = await WaitForFreshEventAsync(eventFile, "ui_ready", TimeSpan.FromSeconds(cfg.TimeoutSeconds));
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            records.Add(new MeasurementRecord(runId, sessionId, DateTimeOffset.UtcNow, cfg.Executable.Contains("Native", StringComparison.OrdinalIgnoreCase) ? "native" : "hybrid", cfg.Lifecycle, cfg.Payload, "process-to-ui", "fresh_process_tti_ms", elapsed, "ms", i + 1, ready ? "ok" : "failed", ready ? null : "ui_ready timeout", gitCommit, Environment.Version.ToString()));
        }
        catch (Exception ex)
        {
            records.Add(new MeasurementRecord(runId, sessionId, DateTimeOffset.UtcNow, "unknown", cfg.Lifecycle, cfg.Payload, "process-to-ui", "fresh_process_tti_ms", 0, "ms", i + 1, "failed", ex.Message, gitCommit, Environment.Version.ToString()));
        }
        finally
        {
            try { if (process is { HasExited: false }) process.Kill(true); } catch { }
            process?.Dispose();
        }
    }

    var path = writer.WriteMeasurements(records, cfg.Output, "e1");
    Console.WriteLine(path);
}

static async Task RunE2Async(RunnerConfig cfg, ResultWriter writer, string gitCommit)
{
    // Basic resource snapshot per fresh process start
    var records = new List<MeasurementRecord>();
    for (var i = 0; i < cfg.Warmup + cfg.Iterations; i++)
    {
        var runId = Guid.NewGuid().ToString("N"); var sessionId = Guid.NewGuid().ToString("N"); Process? process = null;
        try
        {
            var psi = new ProcessStartInfo(cfg.Executable, $"--benchmark --experiment=E2 --run-id={runId} --session-id={sessionId} --lifecycle={cfg.Lifecycle} --payload={cfg.Payload}") { UseShellExecute = true };
            process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {cfg.Executable}");
            var eventFile = Path.Combine(Path.GetDirectoryName(process.MainModule?.FileName) ?? Environment.CurrentDirectory, cfg.Executable.Contains("Native", StringComparison.OrdinalIgnoreCase) ? "native-events.jsonl" : "hybrid-events.jsonl");
            var ready = await WaitForFreshEventAsync(eventFile, "ui_ready", TimeSpan.FromSeconds(cfg.TimeoutSeconds));
            if (!ready) { records.Add(new MeasurementRecord(runId, sessionId, DateTimeOffset.UtcNow, cfg.Executable.Contains("Native", StringComparison.OrdinalIgnoreCase) ? "native" : "hybrid", cfg.Lifecycle, cfg.Payload, "resources", "startup_failed", 0, "", i + 1, "failed", "ui_ready timeout", gitCommit, Environment.Version.ToString())); continue; }

            // snapshot process info
            var proc = Process.GetProcessById(process.Id);
            var workingSet = proc.WorkingSet64; var privateBytes = proc.PrivateMemorySize64; var cpu = 0.0; // placeholder
            records.Add(new MeasurementRecord(runId, sessionId, DateTimeOffset.UtcNow, cfg.Executable.Contains("Native", StringComparison.OrdinalIgnoreCase) ? "native" : "hybrid", cfg.Lifecycle, cfg.Payload, "resources", "working_set_bytes", workingSet, "bytes", i + 1, "ok", null, gitCommit, Environment.Version.ToString()));
            records.Add(new MeasurementRecord(runId, sessionId, DateTimeOffset.UtcNow, cfg.Executable.Contains("Native", StringComparison.OrdinalIgnoreCase) ? "native" : "hybrid", cfg.Lifecycle, cfg.Payload, "resources", "private_bytes", privateBytes, "bytes", i + 1, "ok", null, gitCommit, Environment.Version.ToString()));
        }
        catch (Exception ex)
        {
            records.Add(new MeasurementRecord(runId, sessionId, DateTimeOffset.UtcNow, "unknown", cfg.Lifecycle, cfg.Payload, "resources", "error", 0, "", i + 1, "failed", ex.Message, gitCommit, Environment.Version.ToString()));
        }
        finally { try { if (process is { HasExited: false }) process.Kill(true); } catch { } process?.Dispose(); }
    }

    var path = writer.WriteMeasurements(records, cfg.Output, "e2"); Console.WriteLine(path);
}

static async Task<bool> WaitForFreshEventAsync(string path, string name, TimeSpan timeout)
{
    // Wait for file to exist and then only consider lines appended after we started waiting
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        if (File.Exists(path)) break;
        await Task.Delay(50);
    }

    if (!File.Exists(path)) return false;

    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    // start reading from end to avoid stale events
    stream.Seek(0, SeekOrigin.End);
    using var reader = new StreamReader(stream);
    var sw = Stopwatch.StartNew();
    while (sw.Elapsed < timeout)
    {
        var line = await reader.ReadLineAsync();
        if (line is null) { await Task.Delay(50); continue; }
        if (line.Contains($"\"eventName\":\"{name}\"", StringComparison.Ordinal)) return true;
    }

    return false;
}

sealed class RunnerConfig
{
        public static string HelpText => @"Usage: --experiment=E1|E2|E3|E4|E5|E6 --executable=path --iterations=1 --warmup=0 --payload=1024 --lifecycle=reuse --output=results/raw --timeout-seconds=30
Options:
    --experiment    E1|E2|E3|E4|E5|E6 (required)
  --executable    path to executable (default: HybridWebView2App.exe)
  --iterations    measurements per session (default: 1)
  --warmup        warmup iterations (default: 0)
  --payload       payload bytes (default: 1024)
  --lifecycle     reuse|destroy (default: reuse)
  --output        output directory (default: results/raw)
  --timeout-seconds timeout for awaiting events (default: 30)
  --help, -h      show this help";

    public string Experiment { get; init; } = "E1";
    public string Executable { get; init; } = "HybridWebView2App.exe";
    public int Iterations { get; init; } = 1;
    public int Warmup { get; init; } = 0;
    public int Payload { get; init; } = 1024;
    public string Lifecycle { get; init; } = "reuse";
    public string Output { get; init; } = Path.Combine(Environment.CurrentDirectory, "results", "raw");
    public int TimeoutSeconds { get; init; } = 30;
    public bool ShowHelp { get; init; }

    public static RunnerConfig Parse(string[] args)
    {
        var dict = args.Where(a => a.StartsWith("--") || a == "-h" || a == "--help").ToDictionary(a => a.StartsWith("--") ? a[2..].Split('=', 2)[0] : a, a => a.StartsWith("--") ? (a.Contains('=') ? a.Split('=', 2)[1] : "true") : "true", StringComparer.OrdinalIgnoreCase);
        if (dict.ContainsKey("-h") || dict.ContainsKey("--help")) return new RunnerConfig { ShowHelp = true };
        return new RunnerConfig
        {
            Experiment = dict.TryGetValue("experiment", out var e) ? e : "E1",
            Executable = dict.TryGetValue("executable", out var ex) ? ex : "HybridWebView2App.exe",
            Iterations = dict.TryGetValue("iterations", out var it) && int.TryParse(it, out var iv) ? iv : 1,
            Warmup = dict.TryGetValue("warmup", out var w) && int.TryParse(w, out var wv) ? wv : 0,
            Payload = dict.TryGetValue("payload", out var p) && int.TryParse(p, out var pv) ? pv : 1024,
            Lifecycle = dict.TryGetValue("lifecycle", out var l) ? l : "reuse",
            Output = dict.TryGetValue("output", out var o) ? o : Path.Combine(Environment.CurrentDirectory, "results", "raw"),
            TimeoutSeconds = dict.TryGetValue("timeout-seconds", out var t) && int.TryParse(t, out var tv) ? tv : 30
        };
    }

    public void Validate()
    {
        if (Iterations <= 0) throw new ArgumentOutOfRangeException(nameof(Iterations));
        if (Warmup < 0) throw new ArgumentOutOfRangeException(nameof(Warmup));
        if (Payload < 0) throw new ArgumentOutOfRangeException(nameof(Payload));
        if (Lifecycle != "reuse" && Lifecycle != "destroy") throw new ArgumentException("lifecycle must be 'reuse' or 'destroy'");
    }
}

static class GitHelper
{
    public static string? GetCommitSha()
    {
        try
        {
            var psi = new ProcessStartInfo("git", "rev-parse HEAD") { RedirectStandardOutput = true, UseShellExecute = false };
            using var p = Process.Start(psi); var sha = p?.StandardOutput.ReadLine(); p?.WaitForExit(1000); return string.IsNullOrWhiteSpace(sha) ? null : sha.Trim();
        }
        catch { return null; }
    }
}
