# Static verification of patched tweaks.bundle.json (ASCII-only, read-only)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$json = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'tweaks.bundle.json') | ConvertFrom-Json

$problems = New-Object System.Collections.Generic.List[string]
$stats = [ordered]@{
    tweaks = $json.Tweaks.Count
    version = $json.Version
    registry = 0; service = 0; command = 0; scriptOnly = 0
    refreshShell = 0; checkScript = 0
    delKeyOnApply = 0; delKeyOnRestore = 0; wildcardPaths = 0
    noActions = 0; silentRestore = 0; shellVerbNoKeyDel = 0; namespaceNoKeyDel = 0; parentInterfaces = 0
}

$targets = @{}
foreach ($t in $json.Tweaks) {
    $hasReg = ($t.RegistryActions -and $t.RegistryActions.Count -gt 0)
    $hasSvc = ($t.ServiceActions -and $t.ServiceActions.Count -gt 0)
    $hasCmd = ($t.CommandAction -ne $null)
    $hasScript = ($t.ApplyScript -or $t.CheckScript)
    if ($hasReg) { $stats.registry++ }
    if ($hasSvc) { $stats.service++ }
    if ($hasCmd) { $stats.command++ }
    if (-not ($hasReg -or $hasSvc -or $hasCmd)) { if ($hasScript) { $stats.scriptOnly++ } else { $stats.noActions++; $problems.Add("NO-ACTIONS: $($t.Id)") } }
    if ($t.RefreshShell) { $stats.refreshShell++ }
    if ($t.CheckScript) { $stats.checkScript++ }

    # перевірка узгодженості типу
    $declared = @()
    if ($hasReg) { $declared += 'Registry' }
    if ($hasSvc) { $declared += 'Service' }
    if ($hasCmd -or $hasScript) { $declared += 'Command' }
    if ($declared.Count -gt 0 -and ($declared -notcontains $t.Type)) { $problems.Add("TYPE-MISMATCH($($t.Type)): $($t.Id) -> $($declared -join '+')") }

    if ($hasReg) {
        foreach ($a in $t.RegistryActions) {
            if ($a.DeleteKeyOnApply) { $stats.delKeyOnApply++ }
            if ($a.DeleteKeyOnRestore) { $stats.delKeyOnRestore++ }
            if ($a.KeyPath -match '[\*\?]') { $stats.wildcardPaths++ }

            if ($a.KeyPath -match '\\shell\\' -and [string]::IsNullOrEmpty([string]$a.ValueName) -and -not $a.DeleteKeyOnRestore) {
                $stats.shellVerbNoKeyDel++
                $problems.Add("SHELL-VERB без DeleteKeyOnRestore: $($t.Id)")
            }
            if ($a.KeyPath -match '\\NameSpace\\' -and -not $a.DeleteKeyOnApply) {
                $stats.namespaceNoKeyDel++
                $problems.Add("NAMESPACE-ключ не видаляється: $($t.Id)")
            }
            if ($a.KeyPath -match '\\Interfaces$') {
                $stats.parentInterfaces++
                $problems.Add("батьківський ключ Interfaces: $($t.Id)")
            }
            if ($null -eq $a.ApplyValue -and -not $a.DeleteKeyOnApply) {
                $problems.Add("немає ApplyValue: $($t.Id)")
            }
            elseif (-not $a.DeleteOnRestore -and $null -eq $a.RestoreValue -and -not $a.DeleteKeyOnApply -and -not $a.DeleteKeyOnRestore -and -not $t.CheckScript) {
                $stats.silentRestore++
                $problems.Add("SILENT-RESTORE: $($t.Id)")
            }

            $key = "$($a.Hive)\$($a.KeyPath)::$($a.ValueName)"
            if (-not $targets.ContainsKey($key)) { $targets[$key] = @() }
            $targets[$key] += $t.Id
        }
    }
}
$dupes = @($targets.Keys | Where-Object { $targets[$_].Count -gt 1 })

$stats.GetEnumerator() | ForEach-Object { Write-Output ("{0,-18} = {1}" -f $_.Key, $_.Value) }
Write-Output ("duplicateTargets   = {0}" -f $dupes.Count)
if ($dupes.Count) { $dupes | ForEach-Object { Write-Output ("  dup: " + $_) } }
Write-Output ("problems           = {0}" -f $problems.Count)
$problems | Select-Object -Unique | ForEach-Object { Write-Output ("  " + $_) }
exit 0
