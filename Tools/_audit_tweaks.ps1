# Tweak audit extractor: compact dump + static anomaly checks (no writes to system)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$bundlePath = Join-Path $root 'tweaks.bundle.json'

$json = Get-Content -Raw -Encoding UTF8 $bundlePath | ConvertFrom-Json
$out = New-Object System.Collections.Generic.List[string]

$validKinds = @('String', 'DWord', 'QWord', 'MultiString', 'ExpandString', 'Binary')
$validStartup = @('Disabled', 'Manual', 'Automatic')

$svcMissing = New-Object System.Collections.Generic.List[string]
$flags = New-Object System.Collections.Generic.List[string]
$targetMap = @{}
$idx = 0

$out.Add("== TWEAKS: $($json.Tweaks.Count) | Version $($json.Version) ==")

foreach ($t in $json.Tweaks) {
    $idx++
    $acts = New-Object System.Collections.Generic.List[string]
    $tflags = New-Object System.Collections.Generic.List[string]

    if ($t.RegistryActions) {
        foreach ($a in $t.RegistryActions) {
            $vn = [string]$a.ValueName
            $vnShow = if ([string]::IsNullOrEmpty($vn)) { '<default>' } else { $vn }
            $kind = [string]$a.ValueKind
            $apply = [string]$a.ApplyValue
            if ($a.ApplyValue -is [bool]) { $apply = $a.ApplyValue.ToString().ToLower() }
            if ($null -eq $a.ApplyValue) { $apply = '<null>' }

            if ($a.DeleteOnRestore) { $restore = 'DELETE-VALUE' }
            elseif ($null -ne $a.RestoreValue) { $restore = [string]$a.RestoreValue }
            else { $restore = 'NONE-BROKEN' ; $tflags.Add('SILENT-RESTORE') }

            $key = "$($a.Hive)\$($a.KeyPath)::$vn"
            if (-not $targetMap.ContainsKey($key)) { $targetMap[$key] = New-Object System.Collections.Generic.List[string] }
            $targetMap[$key].Add("$($t.Id)=$apply")

            if ($validKinds -notcontains $kind) { $tflags.Add("KIND:$kind") }
            $acts.Add("$($a.Hive)\$($a.KeyPath) :: $vnShow [$kind] = '$apply' -> $restore")

            if ([string]::IsNullOrEmpty($vn) -and [string]::IsNullOrEmpty($apply)) { $tflags.Add('EMPTY-DEFAULT-WRITE') }
            if ($a.KeyPath -match '\\NameSpace\\') { $tflags.Add('NAMESPACE-KEY(needs key delete)') }
            if ($a.KeyPath -match '\\shell\\' -and [string]::IsNullOrEmpty($vn)) { $tflags.Add('SHELL-VERB-KEY(restore must delete key)') }
            if ($a.KeyPath -match 'Interfaces$') { $tflags.Add('PARENT-KEY-Interfaces') }
            if ($a.KeyPath -match 'PowerSettings') { $tflags.Add('POWER-SETTINGS-PATH') }
        }
    }

    if ($t.ServiceActions) {
        foreach ($s in $t.ServiceActions) {
            $exists = Test-Path ("HKLM:\SYSTEM\CurrentControlSet\Services\" + $s.ServiceName)
            if (-not $exists) { $svcMissing.Add("$($t.Id) => $($s.ServiceName)") ; $tflags.Add("SVC-MISSING:$($s.ServiceName)") }
            if ($validStartup -notcontains [string]$s.ApplyStartup) { $tflags.Add("SVC-APPLY-BAD:$($s.ApplyStartup)") }
            if ($validStartup -notcontains [string]$s.RestoreStartup) { $tflags.Add("SVC-RESTORE-BAD:$($s.RestoreStartup)") }
            $stop = if ($s.StopOnApply) { 'stop' } else { '-' }
            $acts.Add("SVC $($s.ServiceName): $($s.ApplyStartup)/$($s.RestoreStartup) [$stop] exists=$exists")
        }
    }

    if ($t.CommandAction) {
        $acts.Add("CMD check: $($t.CommandAction.CheckCmd)  EXPECT<<$($t.CommandAction.CheckExpected)>>")
        $acts.Add("CMD apply: $($t.CommandAction.ApplyCmd)")
        $acts.Add("CMD restore: $($t.CommandAction.RestoreCmd)")
    }

    if ($t.CheckScript) { $acts.Add("SCR check: $($t.CheckScript)") }
    if ($t.ApplyScript) { $acts.Add("SCR apply: $($t.ApplyScript)") }
    if ($t.RestoreScript) { $acts.Add("SCR restore: $($t.RestoreScript)") }

    $type = [string]$t.Type
    $declared = @()
    if ($t.RegistryActions) { $declared += 'Registry' }
    if ($t.ServiceActions) { $declared += 'Service' }
    if ($t.CommandAction) { $declared += 'Command' }
    if ($t.CheckScript -or $t.ApplyScript -or $t.RestoreScript) { $declared += 'Script' }
    if ($declared.Count -eq 0) { $tflags.Add('NO-ACTIONS') }
    elseif ($declared -notcontains $type) { $tflags.Add("TYPE-MISMATCH(declared=$type actual=$($declared -join '+'))") }
    if ($declared.Count -gt 1) { $tflags.Add("MULTI-SOURCE($($declared -join '+'))") }

    $line = ("{0:d3}|{1}|{2}|{3}| " -f $idx, $t.Id, $t.Risk, $type) + ($acts -join ' ;; ')
    if ($tflags.Count -gt 0) {
        $line += "   <<FLAGS: " + (($tflags | Select-Object -Unique) -join ', ') + ">>"
    }
    $out.Add($line)
}

$dupes = New-Object System.Collections.Generic.List[string]
foreach ($k in $targetMap.Keys) {
    if ($targetMap[$k].Count -gt 1) {
        $dupes.Add("$k  <== " + ($targetMap[$k] -join ' | '))
    }
}

# Localization coverage for tweak Ids
$locMissing = New-Object System.Collections.Generic.List[string]
foreach ($lang in @(@('UA', 'Languages\UA\ua_TweakEngine.json'), @('EN', 'Languages\EN\en_TweakEngine.json'))) {
    $p = Join-Path $root $lang[1]
    if (-not (Test-Path $p)) { $locMissing.Add("$($lang[0]): FILE MISSING") ; continue }
    $locJson = Get-Content -Raw -Encoding UTF8 $p | ConvertFrom-Json
    $props = @()
    if ($locJson.Tweaks) { $props = $locJson.Tweaks.PSObject.Properties.Name }
    foreach ($t in $json.Tweaks) {
        if ($props -notcontains $t.Id) { $locMissing.Add("$($lang[0]): missing Id $($t.Id)") ; continue }
        $node = $locJson.Tweaks.$($t.Id)
        foreach ($f in @('Name', 'Description', 'Benefits', 'SideEffects', 'Category')) {
            if (-not ($node.PSObject.Properties.Name -contains $f) -or [string]::IsNullOrWhiteSpace([string]$node.$f)) {
                $locMissing.Add("$($lang[0]): $($t.Id).$f EMPTY")
            }
        }
    }
}

$out.Add('')
$out.Add("== DUPLICATE REGISTRY TARGETS: $($dupes.Count) ==")
$out.AddRange($dupes)
$out.Add('')
$out.Add("== MISSING SERVICES: $($svcMissing.Count) ==")
$out.AddRange($svcMissing)
$out.Add('')
$out.Add("== LOCALIZATION GAPS: $($locMissing.Count) ==")
$out.AddRange($locMissing)

$outPath = Join-Path $PSScriptRoot '_tweaks_dump.txt'
[System.IO.File]::WriteAllLines($outPath, $out, (New-Object System.Text.UTF8Encoding($false)))

Write-Output ("tweaks={0} dupes={1} svcMissing={2} locGaps={3} lines={4}" -f $json.Tweaks.Count, $dupes.Count, $svcMissing.Count, $locMissing.Count, $out.Count)
exit 0
