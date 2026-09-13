using System.Diagnostics;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;

namespace WebView2SystemTrayBenchmark.Shared;

public sealed class DeterministicBusinessService
{
    private readonly string _settingsPath;
    public DeterministicBusinessService(string? settingsPath = null) => _settingsPath = settingsPath ?? Path.Combine(Path.GetTempPath(), "webview2-benchmark-settings.json");
    public ApplicationInfo GetApplicationInfo() => new("WebView2 System Tray Benchmark", Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0", Environment.Is64BitProcess ? "x64" : "x86", Environment.Version.ToString());
    public AppSettings GetSettings() => File.Exists(_settingsPath) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath)) ?? new() : new();
    public void SaveSettings(AppSettings settings) => File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings));
    public static string GeneratePayload(int sizeBytes)
    {
        if (sizeBytes < 0) throw new ArgumentOutOfRangeException(nameof(sizeBytes));
        const string seed = "0123456789abcdef";
        return string.Concat(Enumerable.Repeat(seed, (sizeBytes + seed.Length - 1) / seed.Length))[..sizeBytes];
    }
    public static int ActualUtf8Bytes(string payload) => Encoding.UTF8.GetByteCount(payload);
    public static string Sha256(string payload) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    public string EchoPayload(string payload) => payload;
    public OperationResult SimulatedBusinessOperation(string input) { var hash = input.Aggregate(17, (current, c) => unchecked(current * 31 + c)); return new("SimulatedBusinessOperation", $"{input.Length}:{Math.Abs(hash)}", DateTimeOffset.UtcNow); }
}

public sealed class HighResolutionLogger : IDisposable
{
    private readonly Stopwatch _clock = Stopwatch.StartNew(); private readonly StreamWriter _writer; private readonly object _gate = new();
    public HighResolutionLogger(string path) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); _writer = new(path, append: true) { AutoFlush = true }; }
    public void Event(string name, string? detail = null) { lock (_gate) _writer.WriteLine(JsonSerializer.Serialize(new { eventName = name, elapsedMs = _clock.Elapsed.TotalMilliseconds, timestampUtc = DateTimeOffset.UtcNow, detail })); }
    public void Dispose() => _writer.Dispose();
}

public sealed class ResultWriter
{
    public string WriteMeasurements(IEnumerable<MeasurementRecord> records, string directory, string stem) => WriteCsv(records, directory, stem);
    public string WriteCsv(IEnumerable<MeasurementRecord> records, string directory, string stem)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{stem}_{DateTime.UtcNow:yyyy-MM-ddTHHmmssfffZ}_{Guid.NewGuid():N}.csv");
        using var writer = new StreamWriter(path);
        writer.WriteLine("run_id,session_id,timestamp,variant,lifecycle,payload_bytes,direction,metric,value,unit,cycle_index,status,failure_reason,build_commit,runtime_version");
        foreach (var r in records)
        {
            // Explicitly format every field to avoid dependence on thread culture
            var fields = new string[]
            {
                Q(r.RunId),
                Q(r.SessionId),
                // ISO 8601 round-trip format, culture invariant
                Q(r.TimestampUtc.ToString("O", CultureInfo.InvariantCulture)),
                Q(r.Variant),
                Q(r.Lifecycle),
                // integers formatted invariantly
                r.PayloadBytes.ToString(CultureInfo.InvariantCulture),
                Q(r.Direction),
                Q(r.Metric),
                // floating point value must use invariant culture and full precision
                r.Value.ToString("G17", CultureInfo.InvariantCulture),
                Q(r.Unit),
                r.CycleIndex.ToString(CultureInfo.InvariantCulture),
                Q(r.Status),
                Q(r.FailureReason ?? string.Empty),
                Q(r.BuildCommit ?? string.Empty),
                Q(r.RuntimeVersion ?? string.Empty)
            };

            writer.WriteLine(string.Join(',', fields));
        }

        return path;
    }

    private static string Q(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        // If field contains special CSV characters, quote and escape quotes
        var needsQuote = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        var escaped = value.Replace("\"", "\"\"");
        return needsQuote ? $"\"{escaped}\"" : escaped;
    }
}
