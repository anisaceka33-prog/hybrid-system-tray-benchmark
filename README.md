# WebView2 System Tray Benchmark

A reproducible Windows/.NET 8 experiment for comparing equivalent native WPF and vanilla JavaScript UI hosted by Microsoft WebView2. 

## Architecture

- Native: System Tray -> WPF/.NET UI -> Shared deterministic services
- Hybrid: System Tray -> WPF/.NET host -> WebView2 -> HTML/CSS/JavaScript -> isolated JSON web-message bridge -> Shared services

## Prerequisites

Windows 10/11 x64, .NET 8 SDK, Visual Studio 2022 with Desktop development with .NET, Microsoft Edge WebView2 Runtime, and Python 3.11+ for analysis. Measurements must use Release/x64 and a quiet machine.

## Build

```powershell
dotnet restore WebView2SystemTrayBenchmark.sln
dotnet build WebView2SystemTrayBenchmark.sln -c Release -p:Platform=x64
```

## Run

```powershell
dotnet run -c Release --project src/NativeWpfBaseline
dotnet run -c Release --project src/HybridWebView2App -- --lifecycle=reuse
dotnet run -c Release --project src/HybridWebView2App -- --lifecycle=destroy
```

## Benchmark

```powershell
dotnet run -c Release --project benchmarks/BenchmarkRunner -- --experiment=startup-webview2 --iterations=30 --warmup=10 --payload=1024 --output=results/raw
```

Use `--executable`, `--lifecycle`, `--wait-ms`, and `--output` to adapt orchestration. The runner writes timestamped CSV files and never overwrites an earlier run. Raw observations are under `results/raw`; processed summaries belong under `results/processed`.

## Analysis

```powershell
python scripts/analyze_results.py "results/raw/*.csv" --out results/processed
```

The script reports n, mean, median, standard deviation, min/max, 95% CI, and possible outlier counts. 

## Measurement design

Hybrid readiness is the explicit `frontend-ready` message after DOM initialization. Native readiness is the visible WPF UI marker. Reuse hides the existing WebView2; destroy disposes and reconstructs it. CPU is measured from process CPU-time deltas over a fixed wall interval and normalized by logical processors. Memory retains process-level rows and aggregates attributable snapshots; WebView2 process attribution is inherently limited when runtimes are shared.

## Limitations

Scheduling, antivirus, background applications, OS and disk caches, JIT, WebView2 runtime sharing, process attribution, thermal/power state, and machine noise can affect observations. Run repeated sessions, record system metadata, keep payload preparation outside timed bridge sections, and report the exact readiness and lifecycle definitions.

## Thesis execution checklist

- [ ] Clean/reboot or document machine state.
- [ ] Record Windows, .NET, WebView2, hardware, architecture, commit, and build configuration.
- [ ] Build Release/x64.
- [ ] Run cold startup, warm reopen, memory, idle CPU, interaction CPU, process-count, and bridge payload experiments.
- [ ] Preserve every raw CSV and event log.
- [ ] Analyze descriptively before selecting inferential tests.
- [ ] Report limitations and deviations.
