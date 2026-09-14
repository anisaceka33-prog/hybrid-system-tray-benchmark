using System.Diagnostics;
using WebView2SystemTrayBenchmark.Shared;

var cfg = RunnerConfig.Parse(args);
if (cfg.ShowHelp)
{
    Console.WriteLine(RunnerConfig.HelpText);
    return 0;
}

cfg.Validate();

var gitCommit = GitHelper.GetCommitSha() ?? "unknown";

var writer = new ResultWriter();
Directory.CreateDirectory(cfg.Output);

switch (cfg.Experiment)
{
    case "E1":
        await FinalExperiments.RunE1Async(cfg, writer, gitCommit);
        break;
    case "E2":
        await FinalExperiments.RunE2Async(cfg, writer, gitCommit);
        break;
    case "E3":
        await FinalExperiments.RunE3Async(cfg, writer, gitCommit);
        break;
    case "E4":
        await FinalExperiments.RunE4Async(cfg, writer, gitCommit);
        break;
    case "E5":
        await FinalExperiments.RunE5Async(cfg, writer, gitCommit);
        break;
    case "E6":
        await FinalExperiments.RunE6Async(cfg, writer, gitCommit);
        break;
    default:
        Console.Error.WriteLine($"Unknown experiment '{cfg.Experiment}'. Valid values: E1, E2, E3, E4, E5, E6.");
        return 2;
}

return 0;

sealed class RunnerConfig
{
          public static string HelpText => @"Usage: --experiment=E1|E2|E3|E4|E5|E6 --executable=path --sessions=1 --iterations=1 --warmup=0 --payload=1024 --lifecycle=reuse --output=results/raw --timeout-seconds=30
Options:
    --experiment    E1|E2|E3|E4|E5|E6 (required)
  --executable    path to executable (default: HybridWebView2App.exe)
      --sessions      independent process sessions (default: 1)
      --iterations    recorded measurements per session (default: 1)
      --warmup        unrecorded warmup measurements per session (default: 0)
  --payload       payload bytes (default: 1024)
  --lifecycle     reuse|destroy (default: reuse)
  --output        output directory (default: results/raw)
  --timeout-seconds timeout for awaiting events (default: 30)
  --help, -h      show this help";

    public string Experiment { get; init; } = "E1";
    public string Executable { get; init; } = "HybridWebView2App.exe";
    public int Sessions { get; init; } = 1;
    public int Iterations { get; init; } = 1;
    public int Warmup { get; init; } = 0;
    public int Payload { get; init; } = 1024;
    public string Lifecycle { get; init; } = "reuse";
    public string Output { get; init; } = Path.Combine(Environment.CurrentDirectory, "results", "raw");
    public int TimeoutSeconds { get; init; } = 30;
    public bool ShowHelp { get; init; }

    public static RunnerConfig Parse(string[] args)
    {
        var dict = args.Where(a => a.StartsWith("--") || a == "-h" || a == "--help").ToDictionary(a => a.StartsWith("--") ? a[2..].Split('=', 2)[0] : a, a => a.StartsWith("--") ? (a.Contains('=') ? a.Split('=', 2)[1] : "true") : "true", StringComparer.OrdinalIgnoreCase);
        if (dict.ContainsKey("-h") || dict.ContainsKey("--help")) return new RunnerConfig { ShowHelp = true };
        return new RunnerConfig
        {
            Experiment = dict.TryGetValue("experiment", out var e) ? e : "E1",
            Executable = dict.TryGetValue("executable", out var ex) ? ex : "HybridWebView2App.exe",
            Sessions = dict.TryGetValue("sessions", out var s) && int.TryParse(s, out var sv) ? sv : 1,
            Iterations = dict.TryGetValue("iterations", out var it) && int.TryParse(it, out var iv) ? iv : 1,
            Warmup = dict.TryGetValue("warmup", out var w) && int.TryParse(w, out var wv) ? wv : 0,
            Payload = dict.TryGetValue("payload", out var p) && int.TryParse(p, out var pv) ? pv : 1024,
            Lifecycle = dict.TryGetValue("lifecycle", out var l) ? l : "reuse",
            Output = dict.TryGetValue("output", out var o) ? o : Path.Combine(Environment.CurrentDirectory, "results", "raw"),
            TimeoutSeconds = dict.TryGetValue("timeout-seconds", out var t) && int.TryParse(t, out var tv) ? tv : 30
        };
    }

    public void Validate()
    {
        if (Sessions <= 0) throw new ArgumentOutOfRangeException(nameof(Sessions));
        if (Iterations <= 0) throw new ArgumentOutOfRangeException(nameof(Iterations));
        if (Warmup < 0) throw new ArgumentOutOfRangeException(nameof(Warmup));
        if (Payload < 0) throw new ArgumentOutOfRangeException(nameof(Payload));
        if (Lifecycle != "reuse" && Lifecycle != "destroy") throw new ArgumentException("lifecycle must be 'reuse' or 'destroy'");
    }
}

static class GitHelper
{
    public static string? GetCommitSha()
    {
        try
        {
            var psi = new ProcessStartInfo("git", "rev-parse HEAD") { RedirectStandardOutput = true, UseShellExecute = false };
            using var p = Process.Start(psi); var sha = p?.StandardOutput.ReadLine(); p?.WaitForExit(1000); return string.IsNullOrWhiteSpace(sha) ? null : sha.Trim();
        }
        catch { return null; }
    }
}
