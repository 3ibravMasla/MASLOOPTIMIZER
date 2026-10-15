<#
.SYNOPSIS
    Baseline/regression resource sampler for the MASLOOPTIMIZER process.

.DESCRIPTION
    Every N seconds writes process metrics: CPU%, accumulated CPU seconds, Working Set,
    Private Bytes, Handles, Threads, GDI/User objects, IO Read/Write bytes.
    Output: CSV in Tools\PerfOut plus one line in summary.csv.

    Purpose: Phase 0 of plans/PERFORMANCE_BACKGROUND_STABILITY_PLAN.md.
    Run as ADMINISTRATOR (GetGuiResources for a foreign process).
    ASCII-only on purpose: Windows PowerShell 5.1 mis-reads non-BOM UTF-8 scripts,
    which corrupts parsing of non-ASCII string literals.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File Tools\PerfSampler.ps1 -Minutes 30 -Label idle_visible

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File Tools\PerfSampler.ps1 -Seconds 60 -IntervalSeconds 2 -Label quick_check
#>
param(
    [string]$ProcessName = 'MASLOOPTIMIZER',
    [int]$Minutes = 0,
    [int]$Seconds = 30,
    [int]$IntervalSeconds = 5,
    [string]$Label = 'idle',
    [string]$OutDir = (Join-Path $PSScriptRoot 'PerfOut')
)

$ErrorActionPreference = 'Stop'

# Invariant culture: numbers must use a dot as decimal separator, otherwise the CSV
# becomes ambiguous on locales with a comma decimal separator (e.g. de-DE).
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture

if (-not ('PerfSamplerGui' -as [type])) {
    Add-Type -Name 'PerfSamplerGui' -MemberDefinition @'
[DllImport("user32.dll")]
public static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);
'@
}

$procs = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)
if ($procs.Count -eq 0) {
    Write-Error "Process '$ProcessName' not found. Start the application and retry."
    exit 2
}
if ($procs.Count -gt 1) {
    Write-Warning "Found $($procs.Count) processes '$ProcessName' - sampling the first one (PID $($procs[0].Id))."
}
$proc = $procs[0]

$durationSec = if ($Minutes -gt 0) { $Minutes * 60 } else { $Seconds }
$cores = [Environment]::ProcessorCount
$samples = New-Object System.Collections.Generic.List[object]

Write-Host ("Sampling '{0}' (PID {1}): {2} s, interval {3} s, cores {4}" -f $ProcessName, $proc.Id, $durationSec, $IntervalSeconds, $cores)
Write-Host 'Do not interrupt: CSV and SUMMARY are written only when the run completes.'

$start = Get-Date
$prevCpu = $proc.TotalProcessorTime.TotalSeconds
$prevTime = $start

while (((Get-Date) - $start).TotalSeconds -lt $durationSec) {
    Start-Sleep -Seconds $IntervalSeconds

    try { $proc.Refresh() } catch { break }
    if ($proc.HasExited) {
        Write-Warning 'Process exited - sampling stopped.'
        break
    }

    $now = Get-Date
    $cpuNow = $proc.TotalProcessorTime.TotalSeconds
    $cpuDelta = $cpuNow - $prevCpu
    $wallDelta = ($now - $prevTime).TotalSeconds

    $cpuPct = 0.0
    if ($wallDelta -gt 0) {
        $cpuPct = [Math]::Round(($cpuDelta / $wallDelta / $cores) * 100, 3)
    }
    $prevCpu = $cpuNow
    $prevTime = $now

    $gdi = -1
    $usr = -1
    try {
        $gdi = [uint32][PerfSamplerGui]::GetGuiResources($proc.Handle, 0)
        $usr = [uint32][PerfSamplerGui]::GetGuiResources($proc.Handle, 1)
    }
    catch { }

    $ioRead = 0
    $ioWrite = 0
    try {
        $cim = Get-CimInstance -ClassName Win32_Process -Filter "ProcessId = $($proc.Id)" -ErrorAction SilentlyContinue
        if ($cim) {
            $ioRead = [uint64]$cim.ReadTransferCount
            $ioWrite = [uint64]$cim.WriteTransferCount
        }
    }
    catch { }

    $sample = [pscustomobject]@{
        Timestamp       = $now.ToString('yyyy-MM-dd HH:mm:ss')
        CpuPercent      = $cpuPct
        CpuTotalSeconds = [Math]::Round($cpuNow, 3)
        WorkingSetMB    = [Math]::Round($proc.WorkingSet64 / 1MB, 2)
        PrivateMB       = [Math]::Round($proc.PrivateMemorySize64 / 1MB, 2)
        Handles         = $proc.HandleCount
        Threads         = $proc.Threads.Count
        GdiObjects      = $gdi
        UserObjects     = $usr
        IoReadBytes     = $ioRead
        IoWriteBytes    = $ioWrite
    }
    $samples.Add($sample)

    Write-Host ("[{0}] CPU {1}%  WS {2} MB  Handles {3}" -f $now.ToString('HH:mm:ss'), $cpuPct, $sample.WorkingSetMB, $sample.Handles)
}

if ($samples.Count -eq 0) {
    Write-Warning 'No samples collected (process exited?).'
    exit 3
}

if (-not (Test-Path $OutDir)) {
    New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
}

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$csvPath = Join-Path $OutDir ("perf_{0}_{1}.csv" -f $Label, $stamp)

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add(($samples[0].PSObject.Properties.Name -join ','))
foreach ($s in $samples) {
    $lines.Add((($s.PSObject.Properties | ForEach-Object { $_.Value }) -join ','))
}
[System.IO.File]::WriteAllLines($csvPath, $lines, (New-Object System.Text.UTF8Encoding($false)))

$first = $samples[0]
$last = $samples[$samples.Count - 1]
$cpuStats = $samples | Measure-Object -Property CpuPercent -Average -Maximum
$wsStats = $samples | Measure-Object -Property WorkingSetMB -Minimum -Maximum
$cpuSeconds = [Math]::Round($last.CpuTotalSeconds - $first.CpuTotalSeconds, 3)
$handleDelta = $last.Handles - $first.Handles
$wsDelta = [Math]::Round($last.WorkingSetMB - $first.WorkingSetMB, 2)
$ioWriteDelta = $last.IoWriteBytes - $first.IoWriteBytes

$summaryLine = "{0}; durationMin={1:N1}; samples={2}; cpuAvgPct={3:N3}; cpuMaxPct={4:N3}; cpuSeconds={5}; wsMinMB={6:N2}; wsMaxMB={7:N2}; wsDeltaMB={8}; handleDelta={9}; ioWriteDeltaBytes={10}" -f $Label, ($durationSec / 60.0), $samples.Count, $cpuStats.Average, $cpuStats.Maximum, $cpuSeconds, $wsStats.Minimum, $wsStats.Maximum, $wsDelta, $handleDelta, $ioWriteDelta

$summaryPath = Join-Path $OutDir 'summary.csv'
if (-not (Test-Path $summaryPath)) {
    [System.IO.File]::AppendAllText($summaryPath, "label; metrics" + [Environment]::NewLine, (New-Object System.Text.UTF8Encoding($false)))
}
[System.IO.File]::AppendAllText($summaryPath, $summaryLine + [Environment]::NewLine, (New-Object System.Text.UTF8Encoding($false)))

Write-Host ''
Write-Host "CSV: $csvPath"
Write-Host "SUMMARY: $summaryLine"
