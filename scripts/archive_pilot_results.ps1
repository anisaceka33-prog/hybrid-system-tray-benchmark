param(
    [string]$Source = "results/raw",
    [string]$Destination = "results/smoke/legacy-raw"
)

$ErrorActionPreference = "Stop"
$files = @(Get-ChildItem $Source -File | Where-Object { $_.Name -ne '.gitkeep' -and $_.Name -notlike 'final_e*_*.csv' })
if ($files.Count -eq 0) {
    Write-Output "No legacy pilot files found."
    exit 0
}
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
foreach ($file in $files) {
    Move-Item -LiteralPath $file.FullName -Destination (Join-Path $Destination $file.Name)
}
Write-Output "Moved $($files.Count) pilot files to $Destination; no files were deleted."
