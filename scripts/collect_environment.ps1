param(
    [string]$Output = "results/environment-manifest.json"
)

$ErrorActionPreference = "Stop"
$dotnet = Get-Command dotnet -ErrorAction Stop
$os = Get-CimInstance Win32_OperatingSystem
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$computer = Get-CimInstance Win32_ComputerSystem
$gitSha = (git rev-parse HEAD).Trim()
$gitStatus = @(git status --short)
$diffText = (git diff --binary | Out-String)
$sha256 = [System.Security.Cryptography.SHA256]::Create()
$diffHash = ([BitConverter]::ToString($sha256.ComputeHash([Text.Encoding]::UTF8.GetBytes($diffText)))).Replace("-", "").ToLowerInvariant()
$runtimeRoot = "${env:ProgramFiles(x86)}\Microsoft\EdgeWebView\Application"
$webView2 = Get-ChildItem $runtimeRoot -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
    Sort-Object { [version]$_.Name } -Descending |
    Select-Object -First 1
$powerMode = (powercfg /getactivescheme | Out-String).Trim()

$manifest = [ordered]@{
    captured_at_utc = [DateTimeOffset]::UtcNow.ToString("O")
    windows = [ordered]@{
        caption = $os.Caption
        version = $os.Version
        build = $os.BuildNumber
    }
    hardware = [ordered]@{
        cpu = $cpu.Name.Trim()
        physical_memory_bytes = [long]$computer.TotalPhysicalMemory
        logical_processors = [int]$computer.NumberOfLogicalProcessors
    }
    dotnet = [ordered]@{
        sdk_version = (dotnet --version).Trim()
        runtimes = @(dotnet --list-runtimes)
    }
    webview2_runtime_version = if ($webView2) { $webView2.Name } else { "unknown" }
    build_configuration = "Release"
    architecture = "x64"
    git = [ordered]@{
        commit_sha = $gitSha
        worktree_clean = $gitStatus.Count -eq 0
        status = $gitStatus
        tracked_diff_sha256 = $diffHash
    }
    power_mode = $powerMode
    repository = (Get-Location).Path
}

$parent = Split-Path -Parent $Output
if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
$manifest | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 $Output
Write-Output $Output
