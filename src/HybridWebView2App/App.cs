using System.Windows;
using System.IO;
using System.Diagnostics;
using WpfControls = System.Windows.Controls;
using WinForms = System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WebView2SystemTrayBenchmark.Shared;

namespace HybridWebView2App;

public sealed class App : System.Windows.Application
{
    private WinForms.NotifyIcon? _tray; private Window? _window; private WebView2? _webView; private bool _allowClose; private LifecycleMode _mode = LifecycleMode.Reuse; private readonly LifecycleController _lifecycle = new(); private readonly DeterministicBusinessService _service = new(); private readonly HighResolutionLogger _log = new(Path.Combine(AppContext.BaseDirectory, "hybrid-events.jsonl"));
    // benchmark orchestration args
    private string? _benchmarkExperiment;
    private int _benchmarkIterations = 0;
    private int _benchmarkPayload = 0;
    private bool _benchmarkRunning;
    private TaskCompletionSource<string>? _benchmarkResultTcs;
    [STAThread] public static void Main(string[] args) => new App().Run();
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _mode = e.Args.Any(a => a.Equals("--lifecycle=destroy", StringComparison.OrdinalIgnoreCase)) ? LifecycleMode.Destroy : LifecycleMode.Reuse;
        // parse benchmark orchestration args (if any)
        foreach (var a in e.Args)
        {
            if (a.StartsWith("--experiment=", StringComparison.OrdinalIgnoreCase)) _benchmarkExperiment = a.Split('=', 2)[1];
            if (a.StartsWith("--iterations=", StringComparison.OrdinalIgnoreCase) && int.TryParse(a.Split('=', 2)[1], out var it)) _benchmarkIterations = it;
            if (a.StartsWith("--payload=", StringComparison.OrdinalIgnoreCase) && int.TryParse(a.Split('=', 2)[1], out var p)) _benchmarkPayload = p;
        }

        _log.Event("process_started"); CreateTray(); if (e.Args.Contains("--benchmark")) _ = ShowWindowAsync();
    }
    private void CreateTray() { _tray = new WinForms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Visible = true, Text = "Hybrid WebView2 Benchmark" }; var menu = new WinForms.ContextMenuStrip(); menu.Items.Add("Open", null, async (_, _) => await ShowWindowAsync()); menu.Items.Add("Exit", null, (_, _) => Shutdown()); _tray.ContextMenuStrip = menu; _tray.DoubleClick += async (_, _) => await ShowWindowAsync(); }
    private async Task ShowWindowAsync()
    {
        if (_window == null) { _lifecycle.BeginInitialize(); _window = new Window { Title = "Hybrid WebView2 App", Width = 420, Height = 360, ResizeMode = ResizeMode.NoResize }; _window.Closing += OnClosing; _webView = new WebView2(); _window.Content = _webView; }
        _window.Show(); _window.Activate(); if (_webView!.CoreWebView2 == null) await InitializeWebViewAsync(); else { if (_lifecycle.State == LifecycleState.Hidden) _lifecycle.MarkVisible(); _log.Event("ui_visible"); }
    }
    private async Task InitializeWebViewAsync()
    {
        _log.Event("webview_environment_start"); var env = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(Path.GetTempPath(), "WebView2SystemTrayBenchmark", Environment.ProcessId.ToString())); _log.Event("webview_environment_ready"); await _webView!.EnsureCoreWebView2Async(env); _log.Event("ensure_core_ready"); _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived; _webView.CoreWebView2.Navigate(new Uri(Path.Combine(AppContext.BaseDirectory, "Web", "index.html")).AbsoluteUri);
    }
    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var message = BridgeMessageRouter.Deserialize(e.WebMessageAsJson);
            if (message.Type == "benchmark-result")
            {
                _benchmarkResultTcs?.TrySetResult(message.Payload);
                return;
            }

            var response = new BridgeMessageRouter(_service).Handle(message);
            _webView!.CoreWebView2.PostWebMessageAsJson(BridgeMessageRouter.Serialize(response));
            if (message.Type == "frontend-ready" && _lifecycle.State == LifecycleState.Initializing)
            {
                _lifecycle.MarkVisible(); _log.Event("ui_ready");
                // if running a benchmark, start orchestration
                if (!string.IsNullOrEmpty(_benchmarkExperiment) && !_benchmarkRunning)
                {
                    _benchmarkRunning = true;
                    _ = RunBridgeBenchmarkAsync();
                }
                // signal lifecycle reopen waits
                _frontendReadyTcs?.TrySetResult(true);
            }

            if (message.Type == "close-ui") HideWindow();
        }
        catch (Exception ex) { _log.Event("failure", ex.Message); }
    }

    private TaskCompletionSource<bool>? _frontendReadyTcs;

    private async Task<string> ExecuteBridgeOperationAsync(string payload)
    {
        var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _benchmarkResultTcs = result;
        var script = $"window.__bench.runOperation({System.Text.Json.JsonSerializer.Serialize(payload)}).then(result => window.chrome.webview.postMessage({{ id: crypto.randomUUID(), protocolVersion: 1, type: 'benchmark-result', payload: JSON.stringify(result) }}));";
        await _webView!.CoreWebView2.ExecuteScriptAsync(script);
        var completed = await Task.WhenAny(result.Task, Task.Delay(TimeSpan.FromSeconds(30)));
        _benchmarkResultTcs = null;
        if (completed != result.Task) throw new TimeoutException("benchmark result timeout");
        return await result.Task;
    }

    private async Task RunBridgeBenchmarkAsync()
    {
        try
        {
            // small delay to let UI settle
            await Task.Delay(200);
            var experiment = _benchmarkExperiment ?? string.Empty;
            var iterations = Math.Max(1, _benchmarkIterations == 0 ? 1 : _benchmarkIterations);
            var payloadSize = Math.Max(0, _benchmarkPayload == 0 ? 1024 : _benchmarkPayload);

            if (experiment == "E2")
            {
                // Resource states: capture snapshot, idle CPU, interaction CPU
                var proc = Process.GetCurrentProcess();
                var snapshot = ProcessMeasurement.Capture(proc);
                _log.Event("resources_snapshot", System.Text.Json.JsonSerializer.Serialize(new { totalWorkingSet = snapshot.TotalWorkingSetBytes, totalPrivate = snapshot.TotalPrivateMemoryBytes, processCount = snapshot.Processes.Count }));

                var idleCpu = await ProcessMeasurement.MeasureCpuPercentAsync(proc, TimeSpan.FromSeconds(5));
                _log.Event("resources_idle_cpu", System.Text.Json.JsonSerializer.Serialize(new { cpuPercent = idleCpu }));

                // perform a short interaction workload and sample CPU during it
                var interactionPayload = DeterministicBusinessService.GeneratePayload(payloadSize);
                var cpuSampleTask = ProcessMeasurement.MeasureCpuPercentAsync(proc, TimeSpan.FromSeconds(1));
                for (var i = 0; i < Math.Min(5, iterations); i++)
                {
                    var script = $"window.__bench.runOperation({System.Text.Json.JsonSerializer.Serialize(interactionPayload)})";
                    try { await _webView!.CoreWebView2.ExecuteScriptAsync(script); } catch { }
                }
                var interactionCpu = await cpuSampleTask;
                _log.Event("resources_interaction_cpu", System.Text.Json.JsonSerializer.Serialize(new { cpuPercent = interactionCpu }));
                _log.Event("benchmark_complete");
                return;
            }

            if (experiment == "E3")
            {
                // JS -> .NET bridge measurement: record host and JS RTT per operation
                for (var i = 1; i <= iterations; i++)
                {
                    var payload = DeterministicBusinessService.GeneratePayload(payloadSize);
                    var inner = await ExecuteBridgeOperationAsync(payload);
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(inner);
                        var root = doc.RootElement;
                        var jsRtt = root.TryGetProperty("rttMs", out var j) && j.ValueKind == System.Text.Json.JsonValueKind.Number ? j.GetDouble() : 0.0;
                        var hostRtt = root.TryGetProperty("hostRttMs", out var h) && h.ValueKind == System.Text.Json.JsonValueKind.Number ? h.GetDouble() : 0.0;
                        _log.Event("js_to_dotnet", System.Text.Json.JsonSerializer.Serialize(new { cycle = i, jsRttMs = jsRtt, hostRttMs = hostRtt }));
                    }
                    catch (Exception ex)
                    {
                        _log.Event("failure", ex.Message);
                    }
                    await Task.Delay(50);
                }
                _log.Event("benchmark_complete");
                return;
            }

            if (experiment == "E4")
            {
                // JavaScript -> .NET -> JavaScript round-trip RTT
                for (var i = 1; i <= iterations; i++)
                {
                    var payload = DeterministicBusinessService.GeneratePayload(payloadSize);
                    var inner = await ExecuteBridgeOperationAsync(payload);
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(inner);
                        var root = doc.RootElement;
                        var rtt = root.TryGetProperty("rttMs", out var r) && r.ValueKind == System.Text.Json.JsonValueKind.Number ? r.GetDouble() : 0.0;
                        _log.Event("roundtrip_rtt", System.Text.Json.JsonSerializer.Serialize(new { cycle = i, rttMs = rtt }));
                    }
                    catch (Exception ex)
                    {
                        _log.Event("failure", ex.Message);
                    }
                    await Task.Delay(50);
                }
                _log.Event("benchmark_complete");
                return;
            }

            if (experiment == "E5")
            {
                // Payload scaling: test sizes
                var sizes = new[] { 1024, 10240, 102400, 1048576 };
                foreach (var size in sizes)
                {
                    for (var i = 1; i <= iterations; i++)
                    {
                        var payload = DeterministicBusinessService.GeneratePayload(size);
                        var inner = await ExecuteBridgeOperationAsync(payload);
                        try
                        {
                            using var doc = System.Text.Json.JsonDocument.Parse(inner);
                            var root = doc.RootElement;
                            var rtt = root.TryGetProperty("rttMs", out var r) && r.ValueKind == System.Text.Json.JsonValueKind.Number ? r.GetDouble() : 0.0;
                            _log.Event("payload_rtt", System.Text.Json.JsonSerializer.Serialize(new { size, cycle = i, rttMs = rtt }));
                        }
                        catch (Exception ex)
                        {
                            _log.Event("failure", ex.Message);
                        }
                        await Task.Delay(50);
                    }
                }
                _log.Event("benchmark_complete");
                return;
            }

            if (experiment == "E6")
            {
                // Lifecycle comparison: perform reopen cycles and measure reopen latency
                for (var i = 1; i <= iterations; i++)
                {
                    TaskCompletionSource<bool>? frontendReady = null;
                    if (_mode == LifecycleMode.Reuse)
                    {
                        HideWindow();
                    }
                    else
                    {
                        await DestroyWindowAsync();
                        frontendReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        _frontendReadyTcs = frontendReady;
                    }

                    var sw = Stopwatch.StartNew();
                    await ShowWindowAsync();
                    var completed = frontendReady is null || await Task.WhenAny(frontendReady.Task, Task.Delay(TimeSpan.FromSeconds(30))) == frontendReady.Task;
                    var reopenMs = sw.Elapsed.TotalMilliseconds;
                    _log.Event("lifecycle_reopen", System.Text.Json.JsonSerializer.Serialize(new { cycle = i, reopenMs, ok = completed }));
                    _frontendReadyTcs = null;
                    await Task.Delay(200);
                }
                _log.Event("benchmark_complete");
                return;
            }

            // default: no-op benchmark
            _log.Event("benchmark_complete");
        }
        catch (Exception ex)
        {
            _log.Event("failure", ex.Message);
        }
        finally
        {
            _benchmarkRunning = false;
        }
    }
    private void HideWindow() { if (_window is null) return; if (_mode == LifecycleMode.Reuse) { if (_lifecycle.State == LifecycleState.Visible) _lifecycle.Hide(_mode); _window.Hide(); _log.Event("ui_hidden"); } else { _ = DestroyWindowAsync(); } }
    private async Task DestroyWindowAsync() { if (_lifecycle.State == LifecycleState.Visible) _lifecycle.Hide(_mode); _log.Event("dispose_start"); if (_webView != null) { if (_webView.CoreWebView2 != null) _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived; _webView.Dispose(); _webView = null; } _allowClose = true; _window?.Close(); _window = null; _allowClose = false; _lifecycle.MarkDisposed(); _log.Event("dispose_end"); await Task.CompletedTask; }
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e) { if (!_allowClose) { e.Cancel = true; HideWindow(); } }
    protected override void OnExit(ExitEventArgs e) { _allowClose = true; _webView?.Dispose(); _tray?.Dispose(); _log.Event("process_exit"); _log.Dispose(); base.OnExit(e); }
}
