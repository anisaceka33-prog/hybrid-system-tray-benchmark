using System.Text.Json.Serialization;

namespace WebView2SystemTrayBenchmark.Shared;

public enum LifecycleMode { Reuse, Destroy }
public enum LifecycleState { NotCreated, Initializing, Visible, Hidden, Disposing, Disposed, Faulted }

public sealed record MeasurementRecord(
    string RunId, string SessionId, DateTimeOffset TimestampUtc, string Variant, string Lifecycle,
    int PayloadBytes, string Direction, string Metric, double Value, string Unit, int CycleIndex,
    string Status, string? FailureReason, string BuildCommit, string RuntimeVersion);

public sealed record ProcessSnapshotRecord(
    string RunId, string SessionId, DateTimeOffset TimestampUtc, int Pid, string ProcessName,
    string ProcessKind, long WorkingSetBytes, long PrivateMemoryBytes, TimeSpan CpuTime);

public sealed record WebViewMessage(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("protocolVersion")] int ProtocolVersion,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("payload")] string Payload);

public sealed record WebViewResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("protocolVersion")] int ProtocolVersion,
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("payload")] string? Payload,
    [property: JsonPropertyName("error")] string? Error);

public sealed record AppSettings(string DisplayName = "Benchmark User", int DefaultPayloadBytes = 1024);
public sealed record ApplicationInfo(string Name, string Version, string Architecture, string Runtime);
public sealed record OperationResult(string Operation, string Output, DateTimeOffset TimestampUtc);
public sealed record SystemMetadata(string Processor, int LogicalProcessors, long PhysicalMemoryBytes, string WindowsVersion, string Architecture, string DotNetVersion, string WebView2SdkVersion, string WebView2RuntimeVersion, string BuildConfiguration, string RepositoryCommit);

public sealed class LifecycleController
{
    public LifecycleState State { get; private set; } = LifecycleState.NotCreated;
    public LifecycleState BeginInitialize() { Require(LifecycleState.NotCreated, LifecycleState.Disposed, LifecycleState.Hidden); State = LifecycleState.Initializing; return State; }
    public LifecycleState MarkVisible() { Require(LifecycleState.Initializing, LifecycleState.Hidden); State = LifecycleState.Visible; return State; }
    public LifecycleState Hide(LifecycleMode mode) { Require(LifecycleState.Visible); State = mode == LifecycleMode.Reuse ? LifecycleState.Hidden : LifecycleState.Disposing; return State; }
    public LifecycleState MarkDisposed() { Require(LifecycleState.Disposing, LifecycleState.Initializing); State = LifecycleState.Disposed; return State; }
    public LifecycleState MarkFaulted() => State = LifecycleState.Faulted;
    private void Require(params LifecycleState[] allowed) { if (!allowed.Contains(State)) throw new InvalidOperationException($"Invalid lifecycle transition from {State}."); }
}
