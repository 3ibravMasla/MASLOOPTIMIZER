# ============================================================
# NewLanguage.ps1
# Creates a new language by copying the EN modules and
# rewriting LanguageCode / LanguageName:
#   Languages\{CODE}\{code}_*.json   (code = CODE lowercase)
# Usage:
#   .\Tools\NewLanguage.ps1 -Code de
#   .\Tools\NewLanguage.ps1 -Code pl -Name "Polski"
# Afterwards run ValidateTranslations.ps1 to verify key coverage.
# ============================================================

param(
    [Parameter(Mandatory = $true)][string]$Code,
    [string]$Name = ""
)

$ErrorActionPreference = "Stop"

$Root      = Split-Path -Parent $PSScriptRoot
$LangDir   = Join-Path $Root "Languages"
$SourceDir = Join-Path $LangDir "EN"

$Code = $Code.Trim().ToUpperInvariant()
if ($Code -notmatch '^[A-Z]{2,3}$') {
    throw "Invalid language code '$Code' (expected 2-3 latin letters, e.g. de, pl, fr)."
}
if ($Code -eq "EN" -or $Code -eq "UA") {
    throw "Code '$Code' is a built-in language. Use a new code (e.g. de, pl)."
}

if (-not (Test-Path $SourceDir)) { throw "Source language EN not found: $SourceDir" }

$TargetDir = Join-Path $LangDir $Code
if (Test-Path $TargetDir) { throw "Target language folder already exists: $TargetDir" }
New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null

if ([string]::IsNullOrWhiteSpace($Name)) {
    $KnownNames = @{
        "DE" = "Deutsch"; "FR" = "Francais"; "PL" = "Polski"; "ES" = "Espanol";
        "IT" = "Italiano"; "PT" = "Portugues"; "NL" = "Nederlands"; "TR" = "Turkce";
        "CS" = "Cestina"; "SK" = "Slovencina"; "HU" = "Magyar"; "RO" = "Romana";
        "SV" = "Svenska"; "DA" = "Dansk"; "FI" = "Suomi"; "NO" = "Norsk";
        "EL" = "Ellinika"; "BG" = "Balgarski"; "HR" = "Hrvatski"; "SR" = "Srpski";
    }
    $Name = if ($KnownNames.ContainsKey($Code)) { $KnownNames[$Code] } else { $Code }
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$prefix    = $Code.ToLowerInvariant()

$files = Get-ChildItem -Path $SourceDir -Filter "en_*.json" -File
if ($files.Count -eq 0) { throw "No EN modules found in $SourceDir" }

$generated = 0
foreach ($f in $files) {
    $moduleName = $f.Name.Substring(3)              # "en_App.json" -> "App.json"
    $targetName = "$prefix`_$moduleName"
    $targetPath = Join-Path $TargetDir $targetName

    $json = Get-Content -Path $f.FullName -Raw -Encoding UTF8 | ConvertFrom-Json

    if ($null -ne $json.PSObject.Properties['LanguageCode']) { $json.LanguageCode = $Code }
    if ($null -ne $json.PSObject.Properties['LanguageName']) { $json.LanguageName = $Name }

    $content = ConvertTo-Json -InputObject $json -Depth 100
    $null = $content | ConvertFrom-Json              # syntax validation

    $tmp = $targetPath + ".tmp"
    [System.IO.File]::WriteAllText($tmp, $content, $utf8NoBom)
    if ((Get-Item $tmp).Length -eq 0) { throw "Empty tmp output: $tmp" }
    Move-Item -Path $tmp -Destination $targetPath -Force
    $generated++
}

Write-Host "New language '$Code' ($Name) created: $generated modules in $TargetDir"
Write-Host "Run: .\Tools\ValidateTranslations.ps1"
