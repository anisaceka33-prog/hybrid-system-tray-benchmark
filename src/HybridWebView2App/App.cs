using System.Windows;
using System.IO;
using WpfControls = System.Windows.Controls;
using WinForms = System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WebView2SystemTrayBenchmark.Shared;

namespace HybridWebView2App;

public sealed class App : System.Windows.Application
{
    private WinForms.NotifyIcon? _tray; private Window? _window; private WebView2? _webView; private bool _allowClose; private LifecycleMode _mode = LifecycleMode.Reuse; private readonly LifecycleController _lifecycle = new(); private readonly DeterministicBusinessService _service = new(); private readonly HighResolutionLogger _log = new(Path.Combine(AppContext.BaseDirectory, "hybrid-events.jsonl"));
    [STAThread] public static void Main(string[] args) => new App().Run();
    protected override void OnStartup(StartupEventArgs e) { base.OnStartup(e); _mode = e.Args.Any(a => a.Equals("--lifecycle=destroy", StringComparison.OrdinalIgnoreCase)) ? LifecycleMode.Destroy : LifecycleMode.Reuse; _log.Event("process_started"); CreateTray(); if (e.Args.Contains("--benchmark")) _ = ShowWindowAsync(); }
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
        try { var message = BridgeMessageRouter.Deserialize(e.WebMessageAsJson); var response = new BridgeMessageRouter(_service).Handle(message); _webView!.CoreWebView2.PostWebMessageAsJson(BridgeMessageRouter.Serialize(response)); if (message.Type == "frontend-ready" && _lifecycle.State == LifecycleState.Initializing) { _lifecycle.MarkVisible(); _log.Event("ui_ready"); } if (message.Type == "close-ui") HideWindow(); }
        catch (Exception ex) { _log.Event("failure", ex.Message); }
    }
    private void HideWindow() { if (_window is null) return; if (_mode == LifecycleMode.Reuse) { if (_lifecycle.State == LifecycleState.Visible) _lifecycle.Hide(_mode); _window.Hide(); _log.Event("ui_hidden"); } else { _ = DestroyWindowAsync(); } }
    private async Task DestroyWindowAsync() { if (_lifecycle.State == LifecycleState.Visible) _lifecycle.Hide(_mode); _log.Event("dispose_start"); if (_webView != null) { if (_webView.CoreWebView2 != null) _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived; _webView.Dispose(); _webView = null; } _allowClose = true; _window?.Close(); _window = null; _allowClose = false; _lifecycle.MarkDisposed(); _log.Event("dispose_end"); await Task.CompletedTask; }
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e) { if (!_allowClose) { e.Cancel = true; HideWindow(); } }
    protected override void OnExit(ExitEventArgs e) { _allowClose = true; _webView?.Dispose(); _tray?.Dispose(); _log.Event("process_exit"); _log.Dispose(); base.OnExit(e); }
}
