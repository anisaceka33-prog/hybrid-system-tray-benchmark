using System.Windows;
using System.IO;
using WpfControls = System.Windows.Controls;
using WinForms = System.Windows.Forms;
using WebView2SystemTrayBenchmark.Shared;

namespace NativeWpfBaseline;

public sealed class App : System.Windows.Application
{
    private WinForms.NotifyIcon? _tray; private Window? _window; private bool _allowClose; private readonly LifecycleController _lifecycle = new();
    private readonly DeterministicBusinessService _service = new(); private readonly HighResolutionLogger _log = new(Path.Combine(AppContext.BaseDirectory, "native-events.jsonl"));
    [STAThread] public static void Main(string[] args) => new App().Run();
    protected override void OnStartup(StartupEventArgs e) { base.OnStartup(e); _log.Event("process_started"); CreateTray(); CreateWindow(); if (e.Args.Contains("--benchmark")) ShowWindow(); }
    private void CreateTray() { _tray = new WinForms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Visible = true, Text = "Native WPF Benchmark" }; var menu = new WinForms.ContextMenuStrip(); menu.Items.Add("Open", null, (_, _) => ShowWindow()); menu.Items.Add("Exit", null, (_, _) => Shutdown()); _tray.ContextMenuStrip = menu; _tray.DoubleClick += (_, _) => ShowWindow(); }
    private void CreateWindow()
    {
        _lifecycle.BeginInitialize(); _window = new Window { Title = "Native WPF Baseline", Width = 420, Height = 360, ResizeMode = ResizeMode.NoResize };
        var panel = new WpfControls.StackPanel { Margin = new Thickness(18) }; var status = new WpfControls.TextBlock { Text = "Status: Ready", Margin = new Thickness(0, 0, 0, 12) }; var input = new WpfControls.TextBox { Text = "benchmark input", Margin = new Thickness(0, 0, 0, 8) }; var payload = new WpfControls.ComboBox { ItemsSource = new[] { 1024, 10240, 102400, 1048576 }, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 8) }; var output = new WpfControls.TextBox { IsReadOnly = true, Height = 120, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true }; var run = new WpfControls.Button { Content = "Run Operation", Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 8) }; run.Click += (_, _) => { var r = _service.SimulatedBusinessOperation(input.Text); output.Text = $"{r.Output}\nPayload bytes: {payload.SelectedItem}"; status.Text = "Status: Operation complete"; }; var close = new WpfControls.Button { Content = "Close / Hide", Padding = new Thickness(8) }; close.Click += (_, _) => HideWindow(); panel.Children.Add(status); panel.Children.Add(input); panel.Children.Add(payload); panel.Children.Add(run); panel.Children.Add(output); panel.Children.Add(close); _window.Content = panel; _window.Closing += OnClosing; _window.Loaded += (_, _) => { _lifecycle.MarkVisible(); _log.Event("ui_ready"); }; }
    private void ShowWindow() { if (_window is null || _lifecycle.State is LifecycleState.Disposed) CreateWindow(); _window!.Show(); _window.Activate(); if (_lifecycle.State == LifecycleState.Hidden) _lifecycle.MarkVisible(); _log.Event("ui_visible"); }
    private void HideWindow() { if (_window is null) return; if (_lifecycle.State == LifecycleState.Visible) _lifecycle.Hide(LifecycleMode.Reuse); _window.Hide(); _log.Event("ui_hidden"); }
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e) { if (!_allowClose) { e.Cancel = true; HideWindow(); } }
    protected override void OnExit(ExitEventArgs e) { _allowClose = true; _tray?.Dispose(); _window?.Close(); _log.Event("process_exit"); _log.Dispose(); base.OnExit(e); }
}
