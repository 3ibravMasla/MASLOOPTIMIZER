<#
.SYNOPSIS
    In-game background impact measurement scenario (Phase 0 of the plan).

.DESCRIPTION
    Thin wrapper over PerfSampler.ps1: measures MASLOOPTIMIZER resources while the user
    plays. This script does NOT measure FPS; compare FPS with the game's built-in
    benchmark before/after changes (same settings, same scenario).

    Order:
      1. Start the app in the needed mode (main / --widget).
      2. Run this script as administrator.
      3. Start the game and play the chosen scenario (e.g. 20 min).
      4. Inspect CSV/SUMMARY in Tools\PerfOut.

    Budget (see plans/PERFORMANCE_BACKGROUND_STABILITY_PLAN.md, section 4):
      during a game the process must have no network requests and no disk writes,
      CPU below 0.1%; in quiet mode telemetry/animations are suspended.

    ASCII-only on purpose: Windows PowerShell 5.1 mis-reads non-BOM UTF-8 scripts.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File Tools\PerfGameCheck.ps1 -Minutes 20 -Label game_session1
#>
param(
    [string]$ProcessName = 'MASLOOPTIMIZER',
    [int]$Minutes = 20,
    [int]$IntervalSeconds = 5,
    [string]$Label = ('game_' + (Get-Date -Format 'yyyyMMdd_HHmmss'))
)

$sampler = Join-Path $PSScriptRoot 'PerfSampler.ps1'
if (-not (Test-Path $sampler)) {
    Write-Error "Not found: $sampler"
    exit 2
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Warning 'Not running elevated - GDI/User objects may be unavailable.'
}

Write-Host 'Preparation done. Launch the game now; sampling starts automatically.'
Start-Sleep -Seconds 5

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $sampler -ProcessName $ProcessName -Minutes $Minutes -IntervalSeconds $IntervalSeconds -Label $Label
exit $LASTEXITCODE
