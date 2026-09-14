using System.Diagnostics;
using System.Text.Json;
using WebView2SystemTrayBenchmark.Shared;

internal static class FinalExperiments
{
    public static async Task RunE1Async(RunnerConfig cfg, ResultWriter writer, string gitCommit)
    {
        var records = new List<MeasurementRecord>();
        for (var index = 0; index < cfg.Warmup + cfg.Sessions; index++)
        {
            var measured = index >= cfg.Warmup;
            var cycle = index - cfg.Warmup + 1;
            var runId = Guid.NewGuid().ToString("N");
            var sessionId = Guid.NewGuid().ToString("N");
            var eventFile = ProgramHelper.GetEventFilePath(cfg.Executable);
            var eventStart = ProgramHelper.GetEventFileLength(eventFile);
            Process? process = null;
            var started = Stopwatch.GetTimestamp();
            try
            {
                process = Start(cfg, "E1", runId, sessionId, 1, 0);
                var ready = await ProgramHelper.WaitForFreshEventAsync(eventFile, "ui_ready", Timeout(cfg), eventStart);
                if (measured)
                {
                    records.Add(Record(cfg, gitCommit, runId, sessionId, cycle, "process-to-ui", "fresh_process_tti_ms", Stopwatch.GetElapsedTime(started).TotalMilliseconds, "ms", ready));
                }
            }
            catch (Exception ex)
            {
                if (measured) records.Add(Failure(cfg, gitCommit, runId, sessionId, cycle, "process-to-ui", "fresh_process_tti_ms", ex.Message));
            }
            finally
            {
                await StopAsync(process);
            }
        }

        Console.WriteLine(writer.WriteMeasurements(records, cfg.Output, "final_e1"));
    }

    public static async Task RunE2Async(RunnerConfig cfg, ResultWriter writer, string gitCommit)
    {
        var records = new List<MeasurementRecord>();
        for (var session = 1; session <= cfg.Sessions; session++)
        {
            var runId = Guid.NewGuid().ToString("N");
            var sessionId = Guid.NewGuid().ToString("N");
            var eventFile = ProgramHelper.GetEventFilePath(cfg.Executable);
            var eventStart = ProgramHelper.GetEventFileLength(eventFile);
            Process? process = null;
            try
            {
                process = Start(cfg, "E2", runId, sessionId, cfg.Iterations, cfg.Warmup);
                if (!await ProgramHelper.WaitForFreshEventAsync(eventFile, "ui_ready", Timeout(cfg), eventStart))
                {
                    records.Add(Failure(cfg, gitCommit, runId, sessionId, session, "visible-stable", "startup_failed", "ui_ready timeout"));
                    continue;
                }
                if (!await ProgramHelper.WaitForFreshEventAsync(eventFile, "benchmark_complete", Timeout(cfg), eventStart))
                {
                    records.Add(Failure(cfg, gitCommit, runId, sessionId, session, "resources", "benchmark_failed", "benchmark_complete timeout"));
                    continue;
                }

                foreach (var item in ReadDetails(eventFile, eventStart))
                {
                    if (item.Name == "resources_snapshot")
                    {
                        Add(records, cfg, gitCommit, runId, sessionId, session, "visible-stable", "working_set_bytes", item.Detail, "totalWorkingSet", "bytes");
                        Add(records, cfg, gitCommit, runId, sessionId, session, "visible-stable", "private_bytes", item.Detail, "totalPrivate", "bytes");
                        Add(records, cfg, gitCommit, runId, sessionId, session, "process-topology", "process_count", item.Detail, "processCount", "count");
                        Add(records, cfg, gitCommit, runId, sessionId, session, "process-topology", "webview2_process_count", item.Detail, "webView2ProcessCount", "count");
                    }
                    else if (item.Name == "resources_idle_cpu")
                    {
                        Add(records, cfg, gitCommit, runId, sessionId, session, "idle", "cpu_percent", item.Detail, "cpuPercent", "percent");
                    }
                    else if (item.Name == "resources_interaction_cpu")
                    {
                        Add(records, cfg, gitCommit, runId, sessionId, session, "scripted-interaction", "cpu_percent", item.Detail, "cpuPercent", "percent");
                    }
                }
                EnsureRows(records, cfg, gitCommit, runId, sessionId, session, 6, "resources");
            }
            catch (Exception ex)
            {
                records.Add(Failure(cfg, gitCommit, runId, sessionId, session, "resources", "error", ex.Message));
            }
            finally
            {
                await StopAsync(process);
            }
        }

        Console.WriteLine(writer.WriteMeasurements(records, cfg.Output, "final_e2"));
    }

    public static Task RunE3Async(RunnerConfig cfg, ResultWriter writer, string gitCommit) =>
        RunBridgeSessionsAsync(cfg, writer, gitCommit, "E3", "final_e3", (context, detail) =>
        {
            var cycle = detail.GetProperty("cycle").GetInt32();
            context.Records.Add(Record(context.Config, context.Commit, context.RunId, context.SessionId, cycle, "js-to-dotnet", "js_to_dotnet_js_rtt_ms", detail.GetProperty("jsRttMs").GetDouble(), "ms", true));
            context.Records.Add(Record(context.Config, context.Commit, context.RunId, context.SessionId, cycle, "js-to-dotnet", "js_to_dotnet_host_rtt_ms", detail.GetProperty("hostRttMs").GetDouble(), "ms", true));
        });

    public static Task RunE4Async(RunnerConfig cfg, ResultWriter writer, string gitCommit) =>
        RunBridgeSessionsAsync(cfg, writer, gitCommit, "E4", "final_e4", (context, detail) =>
        {
            var cycle = detail.GetProperty("cycle").GetInt32();
            context.Records.Add(Record(context.Config, context.Commit, context.RunId, context.SessionId, cycle, "js-dotnet-js", "roundtrip_rtt_ms", detail.GetProperty("rttMs").GetDouble(), "ms", true));
        });

    public static Task RunE5Async(RunnerConfig cfg, ResultWriter writer, string gitCommit) =>
        RunBridgeSessionsAsync(cfg, writer, gitCommit, "E5", "final_e5", (context, detail) =>
        {
            var cycle = detail.GetProperty("cycle").GetInt32();
            var targetBytes = detail.GetProperty("targetSize").GetInt32();
            var actualBytes = detail.GetProperty("actualBytes").GetInt32();
            context.Records.Add(Record(context.Config, context.Commit, context.RunId, context.SessionId, cycle, "js-dotnet-js", "payload_rtt_ms", detail.GetProperty("rttMs").GetDouble(), "ms", true, actualBytes));
            context.Records.Add(Record(context.Config, context.Commit, context.RunId, context.SessionId, cycle, "payload-validation", "payload_actual_bytes", actualBytes, "bytes", actualBytes == targetBytes, targetBytes));
        });

    public static async Task RunE6Async(RunnerConfig cfg, ResultWriter writer, string gitCommit)
    {
        var records = new List<MeasurementRecord>();
        for (var session = 1; session <= cfg.Sessions; session++)
        {
            var runId = Guid.NewGuid().ToString("N");
            var sessionId = Guid.NewGuid().ToString("N");
            var eventFile = ProgramHelper.GetEventFilePath(cfg.Executable);
            var eventStart = ProgramHelper.GetEventFileLength(eventFile);
            Process? process = null;
            try
            {
                process = Start(cfg, "E6", runId, sessionId, cfg.Iterations, cfg.Warmup);
                if (!await WaitForCompletionAsync(cfg, eventFile, eventStart))
                {
                    records.Add(Failure(cfg, gitCommit, runId, sessionId, session, "lifecycle", "lifecycle_reopen_ms", "benchmark timeout"));
                    continue;
                }

                foreach (var item in ReadDetails(eventFile, eventStart).Where(item => item.Name == "lifecycle_reopen"))
                {
                    var cycle = item.Detail.GetProperty("cycle").GetInt32();
                    var ok = item.Detail.GetProperty("ok").GetBoolean();
                    records.Add(Record(cfg, gitCommit, runId, sessionId, cycle, "lifecycle", "lifecycle_reopen_ms", item.Detail.GetProperty("reopenMs").GetDouble(), "ms", ok));
                    Add(records, cfg, gitCommit, runId, sessionId, cycle, "lifecycle", "working_set_bytes", item.Detail, "totalWorkingSet", "bytes", ok);
                    Add(records, cfg, gitCommit, runId, sessionId, cycle, "lifecycle", "private_bytes", item.Detail, "totalPrivate", "bytes", ok);
                    Add(records, cfg, gitCommit, runId, sessionId, cycle, "lifecycle", "process_count", item.Detail, "processCount", "count", ok);
                    Add(records, cfg, gitCommit, runId, sessionId, cycle, "lifecycle", "webview2_process_count", item.Detail, "webView2ProcessCount", "count", ok);
                }
                EnsureRows(records, cfg, gitCommit, runId, sessionId, session, cfg.Iterations * 5, "lifecycle");
            }
            catch (Exception ex)
            {
                records.Add(Failure(cfg, gitCommit, runId, sessionId, session, "lifecycle", "error", ex.Message));
            }
            finally
            {
                await StopAsync(process);
            }
        }

        Console.WriteLine(writer.WriteMeasurements(records, cfg.Output, "final_e6"));
    }

    private static async Task RunBridgeSessionsAsync(RunnerConfig cfg, ResultWriter writer, string gitCommit, string experiment, string stem, Action<ParseContext, JsonElement> addRows)
    {
        var records = new List<MeasurementRecord>();
        var expectedPerSession = experiment == "E3" ? cfg.Iterations * 2 : experiment == "E5" ? cfg.Iterations * 8 : cfg.Iterations;
        for (var session = 1; session <= cfg.Sessions; session++)
        {
            var runId = Guid.NewGuid().ToString("N");
            var sessionId = Guid.NewGuid().ToString("N");
            var eventFile = ProgramHelper.GetEventFilePath(cfg.Executable);
            var eventStart = ProgramHelper.GetEventFileLength(eventFile);
            Process? process = null;
            try
            {
                process = Start(cfg, experiment, runId, sessionId, cfg.Iterations, cfg.Warmup);
                if (!await WaitForCompletionAsync(cfg, eventFile, eventStart))
                {
                    records.Add(Failure(cfg, gitCommit, runId, sessionId, session, "bridge", "benchmark_failed", "benchmark timeout"));
                    continue;
                }

                var eventName = experiment == "E3" ? "js_to_dotnet" : experiment == "E4" ? "roundtrip_rtt" : "payload_rtt";
                var context = new ParseContext(cfg, gitCommit, runId, sessionId, records);
                foreach (var item in ReadDetails(eventFile, eventStart).Where(item => item.Name == eventName)) addRows(context, item.Detail);
                EnsureRows(records, cfg, gitCommit, runId, sessionId, session, expectedPerSession, "bridge");
            }
            catch (Exception ex)
            {
                records.Add(Failure(cfg, gitCommit, runId, sessionId, session, "bridge", "error", ex.Message));
            }
            finally
            {
                await StopAsync(process);
            }
        }

        Console.WriteLine(writer.WriteMeasurements(records, cfg.Output, stem));
    }

    private static Process Start(RunnerConfig cfg, string experiment, string runId, string sessionId, int iterations, int warmup)
    {
        var arguments = $"--benchmark --experiment={experiment} --run-id={runId} --session-id={sessionId} --lifecycle={cfg.Lifecycle} --payload={cfg.Payload} --iterations={iterations} --warmup={warmup}";
        return Process.Start(new ProcessStartInfo(cfg.Executable, arguments) { UseShellExecute = true }) ?? throw new InvalidOperationException($"Could not start {cfg.Executable}");
    }

    private static async Task<bool> WaitForCompletionAsync(RunnerConfig cfg, string eventFile, long eventStart)
    {
        return await ProgramHelper.WaitForFreshEventAsync(eventFile, "ui_ready", Timeout(cfg), eventStart)
            && await ProgramHelper.WaitForFreshEventAsync(eventFile, "benchmark_complete", Timeout(cfg), eventStart);
    }

    private static IEnumerable<EventDetail> ReadDetails(string eventFile, long eventStart)
    {
        foreach (var line in ProgramHelper.ReadEventLines(eventFile, eventStart))
        {
            JsonDocument? outer = null;
            JsonDocument? detail = null;
            try
            {
                outer = JsonDocument.Parse(line);
                if (!outer.RootElement.TryGetProperty("eventName", out var nameElement)) continue;
                if (!outer.RootElement.TryGetProperty("detail", out var detailElement) || detailElement.ValueKind != JsonValueKind.String) continue;
                detail = JsonDocument.Parse(detailElement.GetString() ?? "{}");
                yield return new EventDetail(nameElement.GetString() ?? string.Empty, detail.RootElement.Clone());
            }
            finally
            {
                detail?.Dispose();
                outer?.Dispose();
            }
        }
    }

    private static void Add(List<MeasurementRecord> records, RunnerConfig cfg, string commit, string runId, string sessionId, int cycle, string direction, string metric, JsonElement detail, string property, string unit, bool ok = true)
    {
        if (detail.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number)
        {
            records.Add(Record(cfg, commit, runId, sessionId, cycle, direction, metric, value.GetDouble(), unit, ok));
        }
    }

    private static void EnsureRows(List<MeasurementRecord> records, RunnerConfig cfg, string commit, string runId, string sessionId, int cycle, int expected, string direction)
    {
        var count = records.Count(record => record.SessionId == sessionId);
        if (count != expected) records.Add(Failure(cfg, commit, runId, sessionId, cycle, direction, "row_count_mismatch", $"expected {expected}, found {count}"));
    }

    private static MeasurementRecord Record(RunnerConfig cfg, string commit, string runId, string sessionId, int cycle, string direction, string metric, double value, string unit, bool ok, int? payload = null) =>
        new(runId, sessionId, DateTimeOffset.UtcNow, Variant(cfg), cfg.Lifecycle, payload ?? cfg.Payload, direction, metric, value, unit, cycle, ok ? "ok" : "failed", ok ? null : "measurement failed", commit, Environment.Version.ToString());

    private static MeasurementRecord Failure(RunnerConfig cfg, string commit, string runId, string sessionId, int cycle, string direction, string metric, string reason) =>
        new(runId, sessionId, DateTimeOffset.UtcNow, Variant(cfg), cfg.Lifecycle, cfg.Payload, direction, metric, 0, string.Empty, cycle, "failed", reason, commit, Environment.Version.ToString());

    private static string Variant(RunnerConfig cfg) => cfg.Executable.Contains("Native", StringComparison.OrdinalIgnoreCase) ? "native" : "hybrid";
    private static TimeSpan Timeout(RunnerConfig cfg) => TimeSpan.FromSeconds(cfg.TimeoutSeconds);

    private static async Task StopAsync(Process? process)
    {
        if (process is null) return;
        try
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync();
        }
        catch { }
        finally { process.Dispose(); }
    }

    private sealed record ParseContext(RunnerConfig Config, string Commit, string RunId, string SessionId, List<MeasurementRecord> Records);
    private sealed record EventDetail(string Name, JsonElement Detail);
}