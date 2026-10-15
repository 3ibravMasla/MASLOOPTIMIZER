<#
.SYNOPSIS
    Verifies system state rollback after a Game Mode activate/deactivate cycle.

.DESCRIPTION
    Phase 1.3 of plans/PERFORMANCE_BACKGROUND_STABILITY_PLAN.md.
    Takes a snapshot (active power scheme, Core Parking AC/DC, status and start mode of
    Game Mode candidate services, GameBar registry values) and compares two snapshots.

    Procedure:
      1. Elevated:  .\Tools\VerifyGameModeRollback.ps1 -Action Save -Tag before
      2. In the app: enable Game Mode, wait for activation, disable Game Mode.
      3. Elevated:  .\Tools\VerifyGameModeRollback.ps1 -Action Save -Tag after
      4. Compare:   .\Tools\VerifyGameModeRollback.ps1 -Action Compare
         Defaults: Tools\PerfOut\gm_rollback_before.json / gm_rollback_after.json

    Expectation after Phase 1: no differences (except services Windows restarted itself).

    ASCII-only on purpose: Windows PowerShell 5.1 mis-reads non-BOM UTF-8 scripts.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File Tools\VerifyGameModeRollback.ps1 -Action Save -Tag before
#>
param(
    [ValidateSet('Save', 'Compare')]
    [string]$Action = 'Save',
    [string]$Tag = 'before',
    [string]$Path,
    [string]$BeforePath,
    [string]$AfterPath,
    [string]$OutDir = (Join-Path $PSScriptRoot 'PerfOut')
)

$ErrorActionPreference = 'Stop'

function Get-TextHash([string]$text) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($text)
        return ([System.BitConverter]::ToString($sha.ComputeHash($bytes)) -replace '-', '')
    }
    finally {
        $sha.Dispose()
    }
}

function Get-GameModeSnapshot {
    $snap = [ordered]@{}
    $snap.Timestamp = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')

    # Active power scheme GUID
    $scheme = (powercfg /getactivescheme) 2>$null
    if ($scheme -match '([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})') {
        $snap.ActiveScheme = $Matches[1]
    }

    # Fingerprint of ALL visible processor power settings of the active scheme.
    # Any drift (including Core Parking if the scheme exposes it) changes this hash.
    $procOut = (powercfg /query SCHEME_CURRENT SUB_PROCESSOR) 2>$null
    $procText = ($procOut -join [Environment]::NewLine)
    $snap.ProcessorSettingsHash = Get-TextHash $procText

    # Core Parking (0cc5b647-c1df-4637-891a-dec35c318583) may be hidden/absent in
    # custom schemes - then the targeted query prints only the scheme header and the
    # values stay $null. Regex parsing is language-independent (hex is not localized).
    $park = (powercfg /query SCHEME_CURRENT SUB_PROCESSOR 0cc5b647-c1df-4637-891a-dec35c318583) 2>$null
    $hex = [regex]::Matches(($park -join [Environment]::NewLine), '0x([0-9a-fA-F]{1,8})')
    if ($hex.Count -ge 1) { $snap.CoreParkingAc = [Convert]::ToInt64($hex[0].Groups[1].Value, 16) }
    if ($hex.Count -ge 2) { $snap.CoreParkingDc = [Convert]::ToInt64($hex[1].Groups[1].Value, 16) }

    # Candidate Game Mode services: status and start mode
    $svc = [ordered]@{}
    foreach ($name in @('SysMain', 'DoSvc', 'WpnService', 'wuauserv', 'Spooler', 'DiagTrack')) {
        $s = Get-Service -Name $name -ErrorAction SilentlyContinue
        if ($null -ne $s) {
            $startMode = ''
            try {
                $cim = Get-CimInstance -ClassName Win32_Service -Filter "Name='$name'" -ErrorAction SilentlyContinue
                if ($cim) { $startMode = $cim.StartMode }
            }
            catch { }
            $svc[$name] = [ordered]@{ Status = $s.Status.ToString(); StartMode = $startMode }
        }
    }
    $snap.Services = $svc

    # Windows GameBar registry (HKCU)
    $reg = [ordered]@{}
    foreach ($v in @('AutoGameModeEnabled', 'AllowAutoGameMode')) {
        $val = $null
        try { $val = (Get-ItemProperty -Path 'HKCU:\Software\Microsoft\GameBar' -Name $v -ErrorAction Stop).$v } catch { }
        $reg[$v] = $val
    }
    $snap.GameBarRegistry = $reg

    return $snap
}

if ($Action -eq 'Save') {
    if (-not (Test-Path $OutDir)) {
        New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
    }
    if (-not $Path) {
        $Path = Join-Path $OutDir ("gm_rollback_{0}.json" -f $Tag)
    }

    $snap = Get-GameModeSnapshot
    $json = $snap | ConvertTo-Json -Depth 6
    [System.IO.File]::WriteAllText($Path, $json, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Snapshot saved: $Path"
    exit 0
}

if ($Action -eq 'Compare') {
    # Defaults make the documented Save-Save-Compare workflow work without explicit paths.
    if (-not $BeforePath) { $BeforePath = Join-Path $OutDir 'gm_rollback_before.json' }
    if (-not $AfterPath) { $AfterPath = Join-Path $OutDir 'gm_rollback_after.json' }
    if (-not (Test-Path $BeforePath) -or -not (Test-Path $AfterPath)) {
        Write-Error 'One of the snapshot files does not exist.'
        exit 2
    }

    $a = Get-Content -Raw -Path $BeforePath | ConvertFrom-Json
    $b = Get-Content -Raw -Path $AfterPath | ConvertFrom-Json
    $diffs = New-Object System.Collections.Generic.List[string]

    if ($a.ActiveScheme -ne $b.ActiveScheme) {
        $diffs.Add("ActiveScheme: $($a.ActiveScheme) -> $($b.ActiveScheme)")
    }
    if ($a.ProcessorSettingsHash -ne $b.ProcessorSettingsHash) {
        $diffs.Add("ProcessorSettingsHash: $($a.ProcessorSettingsHash) -> $($b.ProcessorSettingsHash)")
    }
    if ($a.CoreParkingAc -ne $b.CoreParkingAc) {
        $diffs.Add("CoreParkingAc: $($a.CoreParkingAc) -> $($b.CoreParkingAc)")
    }
    if ($a.CoreParkingDc -ne $b.CoreParkingDc) {
        $diffs.Add("CoreParkingDc: $($a.CoreParkingDc) -> $($b.CoreParkingDc)")
    }

    foreach ($name in $a.Services.PSObject.Properties.Name) {
        $sa = $a.Services.$name
        $sb = $b.Services.$name
        if ($null -eq $sb) { continue }
        if ($sa.Status -ne $sb.Status) {
            $diffs.Add("Service $name Status: $($sa.Status) -> $($sb.Status)")
        }
        if ($sa.StartMode -ne $sb.StartMode) {
            $diffs.Add("Service $name StartMode: $($sa.StartMode) -> $($sb.StartMode)")
        }
    }

    foreach ($v in $a.GameBarRegistry.PSObject.Properties.Name) {
        $va = $a.GameBarRegistry.$v
        $vb = $b.GameBarRegistry.$v
        if ($va -ne $vb) {
            $diffs.Add("GameBar.${v}: $va -> $vb")
        }
    }

    if ($diffs.Count -eq 0) {
        Write-Host 'OK: system state matches - Game Mode rollback is correct.'
        exit 0
    }

    Write-Warning "Differences found after rollback ($($diffs.Count)):"
    foreach ($d in $diffs) { Write-Host " - $d" }
    Write-Host ''
    Write-Host 'Note: service Status may legitimately differ if Windows restarted it.'
    exit 1
}
