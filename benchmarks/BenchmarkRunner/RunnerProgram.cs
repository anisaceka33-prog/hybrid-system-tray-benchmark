using System.Diagnostics;
using System.Text.Json;
using System.Globalization;
using WebView2SystemTrayBenchmark.Shared;

namespace BenchmarkRunner;

public static class RunnerProgram
{
    // Entry method called from CLI (Program.cs remains unchanged). Use this helper to run extended experiments.
    public static async Task<int> RunAsync(string[] args)
    {
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
    }

    // Minimal copies of RunE1/E2 that delegate to the same behavior as Program.cs but using ProgramHelper wait helper.
    private static async Task RunE1Async(RunnerConfig cfg, ResultWriter writer, string gitCommit)
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
                var ready = await ProgramHelper.WaitForFreshEventAsync(eventFile, "ui_ready", TimeSpan.FromSeconds(cfg.TimeoutSeconds));
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

    private static async Task RunE2Async(RunnerConfig cfg, ResultWriter writer, string gitCommit)
    {
        var records = new List<MeasurementRecord>();
        for (var i = 0; i < cfg.Warmup + cfg.Iterations; i++)
        {
            var runId = Guid.NewGuid().ToString("N"); var sessionId = Guid.NewGuid().ToString("N"); Process? process = null;
            try
            {
                var psi = new ProcessStartInfo(cfg.Executable, $"--benchmark --experiment=E2 --run-id={runId} --session-id={sessionId} --lifecycle={cfg.Lifecycle} --payload={cfg.Payload}") { UseShellExecute = true };
                process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {cfg.Executable}");
                var eventFile = Path.Combine(Path.GetDirectoryName(process.MainModule?.FileName) ?? Environment.CurrentDirectory, cfg.Executable.Contains("Native", StringComparison.OrdinalIgnoreCase) ? "native-events.jsonl" : "hybrid-events.jsonl");
                var ready = await ProgramHelper.WaitForFreshEventAsync(eventFile, "ui_ready", TimeSpan.FromSeconds(cfg.TimeoutSeconds));
                if (!ready) { records.Add(new MeasurementRecord(runId, sessionId, DateTimeOffset.UtcNow, cfg.Executable.Contains("Native", StringComparison.OrdinalIgnoreCase) ? "native" : "hybrid", cfg.Lifecycle, cfg.Payload, "resources", "startup_failed", 0, "", i + 1, "failed", "ui_ready timeout", gitCommit, Environment.Version.ToString())); continue; }

                var proc = Process.GetProcessById(process.Id);
                var workingSet = proc.WorkingSet64; var privateBytes = proc.PrivateMemorySize64;
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
}
