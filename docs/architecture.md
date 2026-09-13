# Architecture

`NativeWpfBaseline` and `HybridWebView2App` share deterministic business, settings, payload, process, metadata, logging, and result-writing services. The baseline renders equivalent controls with WPF. The hybrid host owns tray and WebView2 lifecycle while the Web folder owns presentation and sends typed JSON messages through `postMessage`.

The `reuse` lifecycle hides the existing window and keeps WebView2 alive. The `destroy` lifecycle disposes the WebView2 instance and recreates the host on the next open. These modes are reported separately in benchmark results.
