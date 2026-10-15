# MeasureColdStart.ps1 - Phase 6.2 cold start measurement.
# ASCII-only on purpose: Windows PowerShell 5.1 fails to parse UTF-8 without BOM
# when the script contains non-ASCII text.
#
# Measures "process start -> tray/widget ready" using the [startup-ready] marker
# written by App.xaml.cs to app.log (DEBUG level):
#   "Start (widget): gotovnist za N ms [startup-ready]"
# The number is measured inside the process, so the file flush delay (up to ~15 s)
# does not affect the result - the script only waits for the line to appear.
#
# Run from an ELEVATED PowerShell: the app manifest requires administrator.

param(
    [string]$ExePath = "",
    [int]$Iterations = 3,
    [int]$TimeoutSec = 60,
    [string]$LogPath = "$env:ProgramData\MASLOOPTIMIZER\Logs\app.log"
)

$ErrorActionPreference = "Stop"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "ERROR: run this script from an elevated PowerShell (app manifest requires administrator)." -ForegroundColor Red
    exit 2
}

if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $candidates = @(
        (Join-Path $PSScriptRoot "PerfOut\pub_compressed\MASLOOPTIMIZER.exe"),
        (Join-Path $PSScriptRoot "PerfOut\pub_uncompressed\MASLOOPTIMIZER.exe"),
        (Join-Path $PSScriptRoot "..\bin\Release\net8.0-windows10.0.22621.0\win-x64\publish\MASLOOPTIMIZER.exe")
    )
    $ExePath = ($candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1)
}

if (-not $ExePath -or -not (Test-Path -LiteralPath $ExePath)) {
    Write-Host "ERROR: exe not found. Pass -ExePath <path to MASLOOPTIMIZER.exe>." -ForegroundColor Red
    exit 3
}

# The marker line contains Ukrainian text; only the ASCII marker and the number
# before it are matched. Regex escapes (\u043c\u0441 = Cyrillic "ms") keep this file ASCII.
$markerPattern = '(\d+)\s*\u043c\u0441\s*\[startup-ready\]'

function Get-MarkerMatches {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    $text = Get-Content -LiteralPath $Path -Raw -ErrorAction SilentlyContinue
    if (-not $text) { return @() }
    return [regex]::Matches($text, $markerPattern)
}

Write-Host ("Exe:    {0}" -f $ExePath)
$exeSizeMb = [math]::Round((Get-Item -LiteralPath $ExePath).Length / 1MB, 1)
Write-Host ("Size:   {0} MB" -f $exeSizeMb)
Write-Host ("Runs:   {0}" -f $Iterations)
Write-Host ("Log:    {0}" -f $LogPath)

# Ensure a clean single instance before measuring.
Get-Process -Name "MASLOOPTIMIZER" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 800

$results = @()
for ($i = 1; $i -le $Iterations; $i++) {
    $before = (Get-MarkerMatches -Path $LogPath).Count

    $proc = Start-Process -FilePath $ExePath -ArgumentList "--widget" -PassThru
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $ms = $null

    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 400
        $found = Get-MarkerMatches -Path $LogPath
        if ($found.Count -gt $before) {
            $ms = [int]$found[$found.Count - 1].Groups[1].Value
            break
        }
    }

    if ($null -ne $ms) {
        Write-Host ("Run {0}: {1} ms" -f $i, $ms)
        $results += $ms
    } else {
        Write-Host ("Run {0}: TIMEOUT (no [startup-ready] marker within {1} s)" -f $i, $TimeoutSec) -ForegroundColor Yellow
    }

    Get-Process -Id $proc.Id -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
}

if ($results.Count -gt 0) {
    $avg = [math]::Round(($results | Measure-Object -Average).Average, 0)
    $min = ($results | Measure-Object -Minimum).Minimum
    $max = ($results | Measure-Object -Maximum).Maximum
    Write-Host ("RESULT: AVG {0} ms | MIN {1} ms | MAX {2} ms | successful runs {3}" -f $avg, $min, $max, $results.Count) -ForegroundColor Green
    exit 0
}

Write-Host "ERROR: no successful runs." -ForegroundColor Red
exit 1
