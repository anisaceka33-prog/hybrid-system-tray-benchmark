using System.Diagnostics;
using WebView2SystemTrayBenchmark.Shared;

// Additional experiment implementations for E3-E6.
// These are implemented as standalone async methods so the main runner can call them
// once the switch in Program.cs is extended to reference them.

internal static class Experiments
{
    public static async Task RunE3Async(RunnerConfig cfg, ResultWriter writer, string gitCommit)
    {
        // E3: JS -> .NET bridge measurement is handled by the app when launched with --benchmark --experiment=E3
        var records = new List<MeasurementRecord>();
        for (var i = 0; i < cfg.Warmup + cfg.Iterations; i++)
        {
            var runId = Guid.NewGuid().ToString("N"); var sessionId = Guid.NewGuid().ToString("N"); Process? process = null;
            try
            {
                var eventFile = ProgramHelper.GetEventFilePath(cfg.Executable);
                var eventStart = ProgramHelper.GetEventFileLength(eventFile);
                var psi = new ProcessStartInfo(cfg.Executable, $"--benchmark --experiment=E3 --run-id={runId} --session-id={sessionId} --lifecycle={cfg.Lifecycle} --payload={cfg.Payload} --iterations=1") { UseShellExecute = true };
                process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {cfg.Executable}");
                var ready = await ProgramHelper.WaitForFreshEventAsync(eventFile, "ui_ready", TimeSpan.FromSeconds(cfg.TimeoutSeconds), eventStart);
                if (!ready) { records.Add(new MeasurementRecord(runId, sessionId, DateTimeOffset.UtcNow, cfg.Executable.Contains("Native", StringComparison.OrdinalIgnoreCase) ? "native" : "hybrid", cfg.Lifecycle, cfg.Payload, "bridge", "startup_failed", 0, "", i + 1, "failed", "ui_ready timeout", gitCommit, Environment.Version.ToString())); continue; }

                // wait for benchmark_complete
                var completed = await ProgramHelper.WaitForFreshEventAsync(eventFile, "benchmark_complete", TimeSpan.FromSeconds(cfg.TimeoutSeconds), eventStart);
                if (!completed) { records.Add(new MeasurementRecord(runId, sessionId, DateTimeOffset.UtcNow, cfg.Executable.Contains("Native", StringComparison.OrdinalIgnoreCase) ? "native" : "hybrid", cfg.Lifecycle, cfg.Payload, "bridge", "error", 0, "", i + 1, "failed", "benchmark_complete timeout", gitCommit, Environment.Version.ToString())); continue; }

                // parse recorded js_to_dotnet events
                var lines = ProgramHelper.ReadEventLines(eventFile, eventStart).ToArray();
                for (var idx = 0; idx < lines.Length; idx++)
                {
                    try
                    {
                        var doc = System.Text.Json.JsonDocument.Parse(lines[idx]);
                        if (!doc.RootElement.TryGetProperty("eventName", out var en)) continue;
                        if (en.GetString() == "js_to_dotnet")
                        {
                            if (doc.RootElement.TryGetProperty("detail", out var detail))
                            {
                                var d = detail.GetString() ?? "";
                                try
                                {
                                    var meta = System.Text.Json.JsonDocument.Parse(d);
                                    var jsRtt = meta.RootElement.GetProperty("jsRttMs").GetDouble();
                                    var hostRtt = meta.RootElement.GetProperty("hostRttMs").GetDouble();
                                    records.Add(new MeasurementRecord(runId, sessionId, DateTimeOffset.UtcNow, cfg.Executable.Contains("Native", StringComparison.OrdinalIgnoreCase) ? "native" : "hybrid", cfg.Lifecycle, cfg.Payload, "bridge", "js_to_dotnet_js_rtt_ms", jsRtt, "ms", i + 1, "ok", null, gitCommit, Environment.Version.ToString()));
                                    records.Add(new MeasurementRecord(runId, sessionId, DateTimeOffset.UtcNow, cfg.Executable.Contains("Native", StringComparison.OrdinalIgnoreCase) ? "native" : "hybrid", cfg.Lifecycle, cfg.Payload, "bridge", "js_to_dotnet_host_rtt_ms", hostRtt, "ms", i + 1, "ok", null, gitCommit, Environment.Version.ToString()));
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                records.Add(new MeasurementRecord(runId, sessionId, DateTimeOffset.UtcNow, "unknown", cfg.Lifecycle, cfg.Payload, "bridge", "error", 0, "", i + 1, "failed", ex.Message, gitCommit, Environment.Version.ToString()));
            }
            finally { try { if (process is { HasExited: false }) process.Kill(true); } catch { } process?.Dispose(); }
        }

        var path = writer.WriteMeasurements(records, cfg.Output, "e3"); Console.WriteLine(path);
        await Task.CompletedTask;
    }

    public static async Task RunE4Async(RunnerConfig cfg, ResultWriter writer, string gitCommit)
    {
        // E4: JS->.NET->JS round-trip RTT measured by app
        var records = new List<MeasurementRecord>();
        for (var i = 0; i < cfg.Warmup + cfg.Iterations; i++)
        {
            var runId = Guid.NewGuid().ToString("N"); var sessionId = Guid.NewGuid().ToString("N"); Process? process = null;
            try
            {
                var eventFile = ProgramHelper.GetEventFilePath(cfg.Executable);
                var eventStart = ProgramHelper.GetEventFileLength(eventFile);
                var psi = new ProcessStartInfo(cfg.Executable, $"--benchmark --experiment=E4 --run-id={runId} --session-id={sessionId} --lifecycle={cfg.Lifecycle} --payload={cfg.Payload} --iterations=1") { UseShellExecute = true };
                process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {cfg.Executable}");
                var ready = await ProgramHelper.WaitForFreshEventAsync(eventFile, "ui_ready", TimeSpan.FromSeconds(cfg.TimeoutSeconds), eventStart);
                if (!ready) { records.Add(new MeasurementRecord(runId, sessionId, DateTimeOffset.UtcNow, cfg.Executable.Contains("Native", StringComparison.OrdinalIgnoreCase) ? "native" : "hybrid", cfg.Lifecycle, cfg.Payload, "bridge", "startup_failed", 0, "", i + 1, "failed", "ui_ready timeout", gitCommit, Environment.Version.ToString())); continue; }

                var completed = await ProgramHelper.WaitForFreshEventAsync(eventFile, "benchmark_complete", TimeSpan.FromSeconds(cfg.TimeoutSeconds), eventStart);
                if (!completed) { records.Add(new MeasurementRecord(runId, sessionId, DateTimeOffset.UtcNow, cfg.Executable.Contains("Native", StringComparison.OrdinalIgnoreCase) ? "native" : "hybrid", cfg.Lifecycle, cfg.Payload, "bridge", "error", 0, "", i + 1, "failed", "benchmark_complete timeout", gitCommit, Environment.Version.ToString())); continue; }

                // parse roundtrip_rtt events
                var lines = ProgramHelper.ReadEventLines(eventFile, eventStart).ToArray();
                for (var idx = 0; idx < lines.Length; idx++)
                {
                    try
                    {
                        var doc = System.Text.Json.JsonDocument.Parse(lines[idx]);
                        if (!doc.RootElement.TryGetProperty("eventName", out var en)) continue;
                        if (en.GetString() == "roundtrip_rtt")
                        {
                            if (doc.RootElement.TryGetProperty("detail", out var detail))
                            {
                                var d = detail.GetString() ?? "";
                                try
                                {
                                    var meta = System.Text.Json.JsonDocument.Parse(d);
                                    var rtt = meta.RootElement.GetProperty("rttMs").GetDouble();
                                    records.Add(new MeasurementRecord(runId, sessionId, DateTimeOffset.UtcNow, cfg.Executable.Contains("Native", StringComparison.OrdinalIgnoreCase) ? "native" : "hybrid", cfg.Lifecycle, cfg.Payload, "bridge", "roundtrip_rtt_ms", rtt, "ms", i + 1, "ok", null, gitCommit, Environment.Version.ToString()));
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                records.Add(new MeasurementRecord(runId, sessionId, DateTimeOffset.UtcNow, "unknown", cfg.Lifecycle, cfg.Payload, "bridge", "error", 0, "", i + 1, "failed", ex.Message, gitCommit, Environment.Version.ToString()));
            }
            finally { try { if (process is { HasExited: false }) process.Kill(true); } catch { } process?.Dispose(); }
        }

        var path = writer.WriteMeasurements(records, cfg.Output, "e4"); Console.WriteLine(path);
        await Task.CompletedTask;
    }

    public static async Task RunE5Async(RunnerConfig cfg, ResultWriter writer, string gitCommit)
    {
        // E5 placeholder: payload scaling is implemented in app; runner will launch app and parse payload_rtt events
        var records = new List<MeasurementRecord>();
        for (var i = 0; i < cfg.Warmup + cfg.Iterations; i++)
        {
            var runId = Guid.NewGuid().ToString("N"); var sessionId = Guid.NewGuid().ToString("N"); Process? process = null;
            try
            {
                var eventFile = ProgramHelper.GetEventFilePath(cfg.Executable);
                var eventStart = ProgramHelper.GetEventFileLength(eventFile);
                var psi = new ProcessStartInfo(cfg.Executable, $"--benchmark --experiment=E5 --run-id={runId} --session-id={sessionId} --lifecycle={cfg.Lifecycle} --iterations={cfg.Iterations}") { UseShellExecute = true };
                process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {cfg.Executable}");
                var ready = await ProgramHelper.WaitForFreshEventAsync(eventFile, "ui_ready", TimeSpan.FromSeconds(cfg.TimeoutSeconds), eventStart);
                if (!ready) { records.Add(new MeasurementRecord(runId, sessionId, DateTimeOffset.UtcNow, cfg.Executable.Contains("Native", StringComparison.OrdinalIgnoreCase) ? "native" : "hybrid", cfg.Lifecycle, cfg.Payload, "bridge", "startup_failed", 0, "", i + 1, "failed", "ui_ready timeout", gitCommit, Environment.Version.ToString())); continue; }

                var completed = await ProgramHelper.WaitForFreshEventAsync(eventFile, "benchmark_complete", TimeSpan.FromSeconds(cfg.TimeoutSeconds), eventStart);
                if (!completed) { records.Add(new MeasurementRecord(runId, sessionId, DateTimeOffset.UtcNow, cfg.Executable.Contains("Native", StringComparison.OrdinalIgnoreCase) ? "native" : "hybrid", cfg.Lifecycle, cfg.Payload, "bridge", "error", 0, "", i + 1, "failed", "benchmark_complete timeout", gitCommit, Environment.Version.ToString())); continue; }

                var lines = ProgramHelper.ReadEventLines(eventFile, eventStart).ToArray();
                for (var idx = 0; idx < lines.Length; idx++)
                {
                    try
                    {
                        var doc = System.Text.Json.JsonDocument.Parse(lines[idx]);
                        if (!doc.RootElement.TryGetProperty("eventName", out var en)) continue;
                        if (en.GetString() == "payload_rtt")
                        {
                            if (doc.RootElement.TryGetProperty("detail", out var detail))
                            {
                                var d = detail.GetString() ?? "";
                                try
                                {
                                    var meta = System.Text.Json.JsonDocument.Parse(d);
                                    var size = meta.RootElement.GetProperty("size").GetInt32();
                                    var rtt = meta.RootElement.GetProperty("rttMs").GetDouble();
                                    records.Add(new MeasurementRecord(runId, sessionId, DateTimeOffset.UtcNow, cfg.Executable.Contains("Native", StringComparison.OrdinalIgnoreCase) ? "native" : "hybrid", cfg.Lifecycle, size, "bridge", "payload_rtt_ms", rtt, "ms", i + 1, "ok", null, gitCommit, Environment.Version.ToString()));
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                records.Add(new MeasurementRecord(runId, sessionId, DateTimeOffset.UtcNow, "unknown", cfg.Lifecycle, cfg.Payload, "bridge", "error", 0, "", i + 1, "failed", ex.Message, gitCommit, Environment.Version.ToString()));
            }
            finally { try { if (process is { HasExited: false }) process.Kill(true); } catch { } process?.Dispose(); }
        }

        var path = writer.WriteMeasurements(records, cfg.Output, "e5"); Console.WriteLine(path);
        await Task.CompletedTask;
    }

    public static async Task RunE6Async(RunnerConfig cfg, ResultWriter writer, string gitCommit)
    {
        // Measure lifecycle reopen latency reported by the target app.
        var records = new List<MeasurementRecord>();
        for (var i = 0; i < cfg.Warmup + cfg.Iterations; i++)
        {
            var runId = Guid.NewGuid().ToString("N"); var sessionId = Guid.NewGuid().ToString("N"); Process? process = null;
            try
            {
                var eventFile = ProgramHelper.GetEventFilePath(cfg.Executable);
                var eventStart = ProgramHelper.GetEventFileLength(eventFile);
                var psi = new ProcessStartInfo(cfg.Executable, $"--benchmark --experiment=E6 --run-id={runId} --session-id={sessionId} --lifecycle={cfg.Lifecycle} --payload={cfg.Payload} --iterations=1") { UseShellExecute = true };
                process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {cfg.Executable}");
                var ready = await ProgramHelper.WaitForFreshEventAsync(eventFile, "ui_ready", TimeSpan.FromSeconds(cfg.TimeoutSeconds), eventStart);
                if (!ready)
                {
                    records.Add(new MeasurementRecord(runId, sessionId, DateTimeOffset.UtcNow, cfg.Executable.Contains("Native", StringComparison.OrdinalIgnoreCase) ? "native" : "hybrid", cfg.Lifecycle, cfg.Payload, "bridge", "payload_rtt_ms", 0, "ms", i + 1, "failed", "ui_ready timeout", gitCommit, Environment.Version.ToString()));
                    try { if (process is { HasExited: false }) process.Kill(true); } catch { } process?.Dispose();
                    continue;
                }

                var completed = await ProgramHelper.WaitForFreshEventAsync(eventFile, "benchmark_complete", TimeSpan.FromSeconds(cfg.TimeoutSeconds), eventStart);
                if (!completed)
                {
                    records.Add(new MeasurementRecord(runId, sessionId, DateTimeOffset.UtcNow, cfg.Executable.Contains("Native", StringComparison.OrdinalIgnoreCase) ? "native" : "hybrid", cfg.Lifecycle, cfg.Payload, "bridge", "payload_rtt_ms", 0, "ms", i + 1, "failed", "benchmark_complete timeout", gitCommit, Environment.Version.ToString()));
                    try { if (process is { HasExited: false }) process.Kill(true); } catch { } process?.Dispose();
                    continue;
                }

                // Extract lifecycle reopen events generated by this process.
                var values = new List<double>();
                var lines = ProgramHelper.ReadEventLines(eventFile, eventStart).ToArray();
                for (var idx = 0; idx < lines.Length; idx++)
                {
                    try
                    {
                        var doc = System.Text.Json.JsonDocument.Parse(lines[idx]);
                        if (!doc.RootElement.TryGetProperty("eventName", out var en)) continue;
                        if (en.GetString() == "lifecycle_reopen")
                        {
                            if (doc.RootElement.TryGetProperty("detail", out var detail))
                            {
                                var d = detail.GetString() ?? "";
                                try
                                {
                                    var meta = System.Text.Json.JsonDocument.Parse(d);
                                    if (meta.RootElement.TryGetProperty("reopenMs", out var reopenEl) && reopenEl.ValueKind == System.Text.Json.JsonValueKind.Number)
                                    {
                                        values.Add(reopenEl.GetDouble());
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }

                if (values.Count == 0)
                {
                    records.Add(new MeasurementRecord(runId, sessionId, DateTimeOffset.UtcNow, cfg.Executable.Contains("Native", StringComparison.OrdinalIgnoreCase) ? "native" : "hybrid", cfg.Lifecycle, cfg.Payload, "lifecycle", "lifecycle_reopen_ms", 0, "ms", i + 1, "failed", "no_lifecycle_reopen", gitCommit, Environment.Version.ToString()));
                }
                else
                {
                    var mean = values.Average();
                    records.Add(new MeasurementRecord(runId, sessionId, DateTimeOffset.UtcNow, cfg.Executable.Contains("Native", StringComparison.OrdinalIgnoreCase) ? "native" : "hybrid", cfg.Lifecycle, cfg.Payload, "lifecycle", "lifecycle_reopen_ms", mean, "ms", i + 1, "ok", null, gitCommit, Environment.Version.ToString()));
                }
            }
            catch (Exception ex)
            {
                records.Add(new MeasurementRecord(runId, sessionId, DateTimeOffset.UtcNow, "unknown", cfg.Lifecycle, cfg.Payload, "lifecycle", "lifecycle_reopen_ms", 0, "ms", i + 1, "failed", ex.Message, gitCommit, Environment.Version.ToString()));
            }
            finally { try { if (process is { HasExited: false }) process.Kill(true); } catch { } process?.Dispose(); }
        }

        var path = writer.WriteMeasurements(records, cfg.Output, "e6"); Console.WriteLine(path);
        await Task.CompletedTask;
    }
}
