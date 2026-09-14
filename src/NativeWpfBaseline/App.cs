using System.Windows;
using System.IO;
using System.Diagnostics;
using WpfControls = System.Windows.Controls;
using WinForms = System.Windows.Forms;
using WebView2SystemTrayBenchmark.Shared;

namespace NativeWpfBaseline;

public sealed class App : System.Windows.Application
{
    private WinForms.NotifyIcon? _tray; private Window? _window; private bool _allowClose; private readonly LifecycleController _lifecycle = new();
    private readonly DeterministicBusinessService _service = new(); private readonly HighResolutionLogger _log = new(Path.Combine(AppContext.BaseDirectory, "native-events.jsonl"));
    private string? _benchmarkExperiment;
    private int _benchmarkIterations = 1;
    private int _benchmarkPayload = 1024;
    [STAThread] public static void Main(string[] args) => new App().Run();
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        foreach (var arg in e.Args)
        {
            if (arg.StartsWith("--experiment=", StringComparison.OrdinalIgnoreCase)) _benchmarkExperiment = arg.Split('=', 2)[1];
            if (arg.StartsWith("--iterations=", StringComparison.OrdinalIgnoreCase) && int.TryParse(arg.Split('=', 2)[1], out var iterations)) _benchmarkIterations = iterations;
            if (arg.StartsWith("--payload=", StringComparison.OrdinalIgnoreCase) && int.TryParse(arg.Split('=', 2)[1], out var payload)) _benchmarkPayload = payload;
        }
        _log.Event("process_started"); CreateTray(); CreateWindow(); if (e.Args.Contains("--benchmark")) ShowWindow();
    }
    private void CreateTray() { _tray = new WinForms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Visible = true, Text = "Native WPF Benchmark" }; var menu = new WinForms.ContextMenuStrip(); menu.Items.Add("Open", null, (_, _) => ShowWindow()); menu.Items.Add("Exit", null, (_, _) => Shutdown()); _tray.ContextMenuStrip = menu; _tray.DoubleClick += (_, _) => ShowWindow(); }
    private void CreateWindow()
    {
        _lifecycle.BeginInitialize(); _window = new Window { Title = "Native WPF Baseline", Width = 420, Height = 360, ResizeMode = ResizeMode.NoResize };
        var panel = new WpfControls.StackPanel { Margin = new Thickness(18) }; var status = new WpfControls.TextBlock { Text = "Status: Ready", Margin = new Thickness(0, 0, 0, 12) }; var input = new WpfControls.TextBox { Text = "benchmark input", Margin = new Thickness(0, 0, 0, 8) }; var payload = new WpfControls.ComboBox { ItemsSource = new[] { 1024, 10240, 102400, 1048576 }, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 8) }; var output = new WpfControls.TextBox { IsReadOnly = true, Height = 120, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true }; var run = new WpfControls.Button { Content = "Run Operation", Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 8) }; run.Click += (_, _) => { var r = _service.SimulatedBusinessOperation(input.Text); output.Text = $"{r.Output}\nPayload bytes: {payload.SelectedItem}"; status.Text = "Status: Operation complete"; }; var close = new WpfControls.Button { Content = "Close / Hide", Padding = new Thickness(8) }; close.Click += (_, _) => HideWindow(); panel.Children.Add(status); panel.Children.Add(input); panel.Children.Add(payload); panel.Children.Add(run); panel.Children.Add(output); panel.Children.Add(close); _window.Content = panel; _window.Closing += OnClosing; _window.Loaded += async (_, _) => { _lifecycle.MarkVisible(); _log.Event("ui_ready"); if (_benchmarkExperiment == "E2") await RunResourceBenchmarkAsync(); }; }

    private async Task RunResourceBenchmarkAsync()
    {
        await Task.Delay(200);
        var process = Process.GetCurrentProcess();
        var snapshot = ProcessMeasurement.Capture(process);
        _log.Event("resources_snapshot", System.Text.Json.JsonSerializer.Serialize(new { totalWorkingSet = snapshot.TotalWorkingSetBytes, totalPrivate = snapshot.TotalPrivateMemoryBytes, processCount = snapshot.Processes.Count, webView2ProcessCount = 0 }));
        var idleCpu = await ProcessMeasurement.MeasureCpuPercentAsync(process, TimeSpan.FromSeconds(5));
        _log.Event("resources_idle_cpu", System.Text.Json.JsonSerializer.Serialize(new { cpuPercent = idleCpu }));
        var interactionPayload = DeterministicBusinessService.GeneratePayload(_benchmarkPayload);
        var cpuTask = ProcessMeasurement.MeasureCpuPercentAsync(process, TimeSpan.FromSeconds(1));
        for (var i = 0; i < Math.Max(5, _benchmarkIterations); i++) _service.SimulatedBusinessOperation(interactionPayload);
        var interactionCpu = await cpuTask;
        _log.Event("resources_interaction_cpu", System.Text.Json.JsonSerializer.Serialize(new { cpuPercent = interactionCpu }));
        _log.Event("benchmark_complete");
    }
    private void ShowWindow() { if (_window is null || _lifecycle.State is LifecycleState.Disposed) CreateWindow(); _window!.Show(); _window.Activate(); if (_lifecycle.State == LifecycleState.Hidden) _lifecycle.MarkVisible(); _log.Event("ui_visible"); }
    private void HideWindow() { if (_window is null) return; if (_lifecycle.State == LifecycleState.Visible) _lifecycle.Hide(LifecycleMode.Reuse); _window.Hide(); _log.Event("ui_hidden"); }
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e) { if (!_allowClose) { e.Cancel = true; HideWindow(); } }
    protected override void OnExit(ExitEventArgs e) { _allowClose = true; _tray?.Dispose(); _window?.Close(); _log.Event("process_exit"); _log.Dispose(); base.OnExit(e); }
}
