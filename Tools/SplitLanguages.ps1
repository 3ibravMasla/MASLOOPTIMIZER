# ============================================================
# SplitLanguages.ps1
# Splits legacy Languages\uk.json / Languages\en.json into the
# modular structure:
#   Languages\UA\ua_*.json   +   Languages\EN\en_*.json
# Legacy files are removed after a successful split.
# Flat key counts are verified against the source (1592/1368).
# ============================================================

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$LangDir = Join-Path $Root "Languages"
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

$Modules = @(
    @{ Name = 'App';              Sections = @('LanguageCode','LanguageName','Common','Header','Sidebar','Footer','Update','Categories','Risk') }
    @{ Name = 'TweakEngine';      Sections = @('Tweaks') }
    @{ Name = 'DebloatEngine';    Sections = @('Debloat') }
    @{ Name = 'ToolsEngine';      Sections = @('Tools') }
    @{ Name = 'CleanerEngine';    Sections = @('Cleaner') }
    @{ Name = 'DnsEngine';        Sections = @('Dns') }
    @{ Name = 'StartupEngine';    Sections = @('Startup') }
    @{ Name = 'GameModeEngine';   Sections = @('GameMode') }
    @{ Name = 'MsiEngine';        Sections = @('Msi') }
    @{ Name = 'DiagnosticEngine'; Sections = @('Diagnostic') }
    @{ Name = 'NetworkEngine';    Sections = @() }
    @{ Name = 'PresetEngine';     Sections = @() }
)

# Recursively counts flat leaf keys (mirrors C# FlattenJsonElement).
function Get-FlatKeyCount {
    param($obj, [string]$prefix)
    if ($obj -is [string]) { return 1 }
    if ($obj -is [PSCustomObject]) {
        $n = 0
        foreach ($p in $obj.PSObject.Properties) {
            $k = if ($prefix) { "$prefix.$($p.Name)" } else { $p.Name }
            $n += Get-FlatKeyCount $p.Value $k
        }
        return $n
    }
    if ($obj -is [System.Collections.IEnumerable]) {
        $n = 0
        foreach ($it in $obj) { $n += Get-FlatKeyCount $it $prefix }
        return $n
    }
    return 1
}

function Write-Utf8NoBomFile([string]$path, [string]$content) {
    $dir = Split-Path -Parent $path
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $tmp = $path + '.tmp'
    [System.IO.File]::WriteAllText($tmp, $content, $utf8NoBom)
    if ((Get-Item $tmp).Length -eq 0) { throw "Empty tmp output: $tmp" }
    Move-Item -Path $tmp -Destination $path -Force
}

$generated = @()

foreach ($lang in @('UA','EN')) {
    $srcName = if ($lang -eq 'UA') { 'uk.json' } else { 'en.json' }
    $srcPath = Join-Path $LangDir $srcName
    if (-not (Test-Path $srcPath)) { throw "Missing source language file: $srcPath" }
    $json = Get-Content $srcPath -Raw -Encoding UTF8 | ConvertFrom-Json

    $prefix = $lang.ToLowerInvariant()
    $targetDir = Join-Path $LangDir $lang
    if (-not (Test-Path $targetDir)) { New-Item -ItemType Directory -Path $targetDir -Force | Out-Null }

    $sourceTotal = Get-FlatKeyCount $json ''
    $moduleTotal = 0

    foreach ($m in $Modules) {
        $content = [ordered]@{}
        foreach ($s in $m.Sections) {
            $sectionValue = $json.$s
            if ($null -eq $sectionValue) { throw "Section '$s' not found in $srcName" }
            $content[$s] = $sectionValue
            $moduleTotal += Get-FlatKeyCount $sectionValue $s
        }
        # Language code must match the folder name (legacy 'UK' -> 'UA')
        if ($content.Contains('LanguageCode')) { $content['LanguageCode'] = $lang }

        $fileName = "$prefix`_$($m.Name).json"
        $filePath = Join-Path $targetDir $fileName
        if ($content.Count -eq 0) {
            Write-Utf8NoBomFile $filePath '{  }'
        } else {
            Write-Utf8NoBomFile $filePath (ConvertTo-Json -InputObject $content -Depth 100)
        }
        $generated += $filePath
    }

    if ($sourceTotal -ne $moduleTotal) {
        throw "Key count mismatch for $lang : source=$sourceTotal modules=$moduleTotal"
    }
    Write-Host "$lang split OK: $sourceTotal flat keys"
}

# Remove legacy single-file languages after a successful split
foreach ($f in @('uk.json','en.json')) {
    $p = Join-Path $LangDir $f
    if (Test-Path $p) { Remove-Item -Path $p -Force }
}

Write-Host "Generated $($generated.Count) module files."

