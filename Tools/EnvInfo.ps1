<#
.SYNOPSIS
    Environment facts collector for the Phase 8 matrix of the performance plan.

.DESCRIPTION
    Collects OS build, CPU topology, RAM, GPU, physical disks and elevation state,
    writes a JSON snapshot into Tools\PerfOut and appends one summary line to
    Tools\PerfOut\env_matrix.csv. Run it on every test environment (Win10/Win11,
    weak/strong hardware); results feed plans/PERFORMANCE_REPORT.md.

    ASCII-only on purpose: Windows PowerShell 5.1 mis-reads non-BOM UTF-8 scripts.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File Tools\EnvInfo.ps1 -Tag weak-win10
#>
param(
    [string]$OutDir = (Join-Path $PSScriptRoot 'PerfOut'),
    [string]$Tag = 'env'
)

$ErrorActionPreference = 'Stop'

# Invariant culture so CSV numbers stay dot-separated on comma-decimal locales.
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }

$os  = Get-CimInstance Win32_OperatingSystem
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$cs  = Get-CimInstance Win32_ComputerSystem

$gpus = @()
try {
    $gpus = @(Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue | ForEach-Object {
        [pscustomobject]@{ Name = $_.Name; Driver = $_.DriverVersion }
    })
} catch { }

$disks = @()
try {
    $disks = @(Get-PhysicalDisk -ErrorAction SilentlyContinue | ForEach-Object {
        [pscustomobject]@{ Name = $_.FriendlyName; Media = [string]$_.MediaType; Bus = [string]$_.BusType; SizeGB = [math]::Round($_.Size / 1GB) }
    })
} catch { }

$id = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($id)
$elevated = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$snap = [ordered]@{
    TimestampUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss')
    Tag          = $Tag
    OS           = $os.Caption
    OSVersion    = $os.Version
    OSBuild      = $os.BuildNumber
    Arch         = $os.OSArchitecture
    Elevated     = $elevated
    CPU          = $cpu.Name.Trim()
    CPUCores     = $cpu.NumberOfCores
    CPUThreads   = $cpu.NumberOfLogicalProcessors
    CPUMaxMHz    = $cpu.MaxClockSpeed
    RamGB        = [math]::Round($cs.TotalPhysicalMemory / 1GB, 1)
    Gpus         = $gpus
    Disks        = $disks
    PowerShell   = $PSVersionTable.PSVersion.ToString()
}

$jsonPath = Join-Path $OutDir ("env_{0}_{1}.json" -f $Tag, $stamp)
[IO.File]::WriteAllText($jsonPath, ($snap | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))

$gpuLine  = ($gpus  | ForEach-Object { $_.Name + ' (drv ' + $_.Driver + ')' }) -join ' | '
$diskLine = ($disks | ForEach-Object { $_.Name + ' ' + $_.Media + '/' + $_.Bus + ' ' + $_.SizeGB + 'GB' }) -join ' | '

$line = "{0}; tag={1}; os={2} build={3}; cpu={4} ({5}C/{6}T); ramGB={7}; gpu={8}; disks={9}; elevated={10}" -f `
    $stamp, $Tag, $os.Caption, $os.BuildNumber, $cpu.Name.Trim(), $cpu.NumberOfCores, $cpu.NumberOfLogicalProcessors, `
    $snap.RamGB, $gpuLine, $diskLine, $elevated

$csvPath = Join-Path $OutDir 'env_matrix.csv'
if (-not (Test-Path $csvPath)) {
    [IO.File]::AppendAllText($csvPath, "stamp; summary" + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))
}
[IO.File]::AppendAllText($csvPath, $line + [Environment]::NewLine, (New-Object Text.UTF8Encoding($false)))

Write-Host $line
Write-Host "JSON: $jsonPath"
