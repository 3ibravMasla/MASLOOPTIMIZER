$ErrorActionPreference = 'Stop'
$root = 'c:\Users\MasloDe\Desktop\projekt\WinOptimizer\v0.3.4\Languages'
$out = 'c:\Users\MasloDe\Desktop\projekt\WinOptimizer\v0.3.4\Tools\_task_report.txt'
$sb = New-Object System.Text.StringBuilder

function Flatten-Json($obj, $prefix, [System.Collections.Generic.Dictionary[string,string]]$dict) {
    if ($obj -is [System.Management.Automation.PSCustomObject]) {
        foreach ($p in $obj.PSObject.Properties) {
            $k = if ([string]::IsNullOrEmpty($prefix)) { $p.Name } else { "$prefix.$($p.Name)" }
            Flatten-Json $p.Value $k $dict
        }
    }
    elseif ($obj -is [System.Array]) {
        # arrays not expected in translation files; index them
        for ($i=0; $i -lt $obj.Count; $i++) {
            $k = "$prefix[$i]"
            Flatten-Json $obj[$i] $k $dict
        }
    }
    elseif ($null -ne $obj) {
        $dict[$prefix] = [string]$obj
    }
}

function Load-Flat([string]$path) {
    $d = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([System.StringComparer]::OrdinalIgnoreCase)
    if (-not (Test-Path $path)) { return $d }
    $txt = Get-Content -Raw -LiteralPath $path
    if ([string]::IsNullOrWhiteSpace($txt)) { return $d }
    try {
        $j = $txt | ConvertFrom-Json
        Flatten-Json $j '' $d
    } catch {
        $d['__PARSE_ERROR__'] = $_.Exception.Message
    }
    return $d
}

function Get-Placeholders([string]$s) {
    $m = [regex]::Matches($s, '\{(\d+)([^}]*)\}')
    $nums = @()
    foreach ($x in $m) { $nums += [int]$x.Groups[1].Value }
    return ($nums | Sort-Object) -join ','
}

$enDir = Join-Path $root 'EN'
$uaDir = Join-Path $root 'UA'

$enFiles = Get-ChildItem -LiteralPath $enDir -Filter '*.json' | Sort-Object Name
$uaFiles = Get-ChildItem -LiteralPath $uaDir -Filter '*.json' | Sort-Object Name

[void]$sb.AppendLine("=== LANGUAGE FILE INVENTORY ===")
foreach ($f in $enFiles) {
    $raw = Get-Content -Raw -LiteralPath $f.FullName
    $empty = [string]::IsNullOrWhiteSpace($raw.Trim().Trim('{','}').Trim())
    [void]$sb.AppendLine("EN  {0,-30} {1,8} B  empty={2}" -f $f.Name, $f.Length, $empty)
}
foreach ($f in $uaFiles) {
    $raw = Get-Content -Raw -LiteralPath $f.FullName
    $empty = [string]::IsNullOrWhiteSpace($raw.Trim().Trim('{','}').Trim())
    [void]$sb.AppendLine("UA  {0,-30} {1,8} B  empty={2}" -f $f.Name, $f.Length, $empty)
}

[void]$sb.AppendLine("")
[void]$sb.AppendLine("=== KEY PARITY (EN vs UA) ===")
$enMap = @{}
$uaMap = @{}
foreach ($f in $enFiles) { $enMap[$f.Name] = $f.FullName }
foreach ($f in $uaFiles) { $uaMap[$f.Name] = $f.FullName }

$allNames = @($enMap.Keys + $uaMap.Keys) | Sort-Object -Unique

foreach ($name in $allNames) {
    $enPath = $enMap[$name]
    $uaPath = $uaMap[$name]
    $enDict = if ($enPath) { Load-Flat $enPath } else { $null }
    $uaDict = if ($uaPath) { Load-Flat $uaPath } else { $null }

    if (-not $enPath) { [void]$sb.AppendLine("MODULE $name : MISSING EN FILE"); continue }
    if (-not $uaPath) { [void]$sb.AppendLine("MODULE $name : MISSING UA FILE"); continue }

    $enKeys = $enDict.Keys | Where-Object { $_ -ne '__PARSE_ERROR__' }
    $uaKeys = $uaDict.Keys | Where-Object { $_ -ne '__PARSE_ERROR__' }

    $enSet = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    $uaSet = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($k in $enKeys) { [void]$enSet.Add($k) }
    foreach ($k in $uaKeys) { [void]$uaSet.Add($k) }

    $onlyEn = $enKeys | Where-Object { -not $uaSet.Contains($_) }
    $onlyUa = $uaKeys | Where-Object { -not $enSet.Contains($_) }

    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("MODULE $name : EN=$($enSet.Count) UA=$($uaSet.Count) | onlyEN=$(@($onlyEn).Count) onlyUA=$(@($onlyUa).Count)")
    if ($enDict.ContainsKey('__PARSE_ERROR__')) { [void]$sb.AppendLine("   EN PARSE ERROR: $($enDict['__PARSE_ERROR__'])") }
    if ($uaDict.ContainsKey('__PARSE_ERROR__')) { [void]$sb.AppendLine("   UA PARSE ERROR: $($uaDict['__PARSE_ERROR__'])") }
}

[void]$sb.AppendLine("")
[void]$sb.AppendLine("=== PLACEHOLDER MISMATCHES (common keys) ===")
foreach ($name in $allNames) {
    $enPath = $enMap[$name]
    $uaPath = $uaMap[$name]
    if (-not $enPath -or -not $uaPath) { continue }
    $enDict = Load-Flat $enPath
    $uaDict = Load-Flat $uaPath
    $common = $enDict.Keys | Where-Object { $uaDict.ContainsKey($_) -and $_ -ne '__PARSE_ERROR__' }
    foreach ($k in ($common | Sort-Object)) {
        $phEn = Get-Placeholders $enDict[$k]
        $phUa = Get-Placeholders $uaDict[$k]
        if ($phEn -ne $phUa) {
            [void]$sb.AppendLine("PLACEHOLDER  $name :: $k")
            [void]$sb.AppendLine("    EN: $($enDict[$k])")
            [void]$sb.AppendLine("    UA: $($uaDict[$k])")
        }
    }
}

Set-Content -LiteralPath $out -Value $sb.ToString() -Encoding UTF8
Write-Output "DONE"
