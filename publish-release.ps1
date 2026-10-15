# ============================================================
# publish-release.ps1 — 1-Click packaging of MASLOOPTIMIZER v0.6.0
#
# Builds the self-contained win-x64 release, verifies that the full tweak
# payload (70 .bat scripts + tweaks.bundle.json) is present, strips debug
# artifacts and packs everything into the GitHub-Releases ZIP.
#
# The ZIP contains a SINGLE ROOT FOLDER ("MASLOOPTIMIZER/") so extracting it
# does not scatter ~200 files into the current directory. The auto-updater
# handles this layout via UpdateManager.ResolveExtractedRoot().
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File .\publish-release.ps1              # modular (default)
#   powershell -ExecutionPolicy Bypass -File .\publish-release.ps1 -SingleFile  # 1 exe + Scripts/
#
# NOTE: the default (modular) layout is what TweakEngine expects; -SingleFile is
# an experimental, visually cleaner layout — measure cold start (Tools\MeasureColdStart.ps1)
# before shipping it as the main release.
# ============================================================

[CmdletBinding()]
param(
    # Bundle managed assemblies into a single exe (Scripts/ and tweaks.bundle.json
    # stay loose thanks to ExcludeFromSingleFile in the .csproj).
    [switch]$SingleFile
)

$ErrorActionPreference = "Stop"

$Root           = $PSScriptRoot
$Version        = "0.6.0"
$PublishRoot    = Join-Path $Root "publish"
$OutDir         = Join-Path $PublishRoot "MASLOOPTIMIZER"
$ZipPath        = Join-Path $PublishRoot "MASLOOPTIMIZER-v$Version-win-x64.zip"
$ExpectedTweaks = 70

$Layout          = if ($SingleFile) { "single-file (exe bundled)" } else { "modular (folder)" }
$SingleFileValue = if ($SingleFile.IsPresent) { "true" } else { "false" }

Write-Host "=== MASLOOPTIMIZER v$Version release packaging ===" -ForegroundColor Cyan
Write-Host "    Layout: $Layout" -ForegroundColor Cyan

# 1. Clean previous outputs ----------------------------------------------
if (Test-Path $PublishRoot) {
    Write-Host "[1/5] Cleaning previous publish output..." -ForegroundColor Yellow
    Remove-Item -Path $PublishRoot -Recurse -Force
}
New-Item -Path $OutDir -ItemType Directory -Force | Out-Null

# 2. Build & publish -----------------------------------------------------
# Modular by default: TweakEngine launches Scripts\Tweaks\*.bat from disk, so the
# release must stay a real folder tree. -SingleFile only bundles managed DLLs.
Write-Host "[2/5] Publishing (Release / win-x64 / self-contained / $Layout)..." -ForegroundColor Yellow

$publishArgs = @(
    "$Root\MASLOOPTIMIZER.csproj",
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=$SingleFileValue",
    # Keep native runtime libraries loose: avoids first-run extraction into %TEMP%\.net
    # and keeps cold start deterministic.
    "-p:IncludeNativeLibrariesForSelfExtract=false",
    "-o", $OutDir
)
dotnet publish @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# 3. Verify the payload --------------------------------------------------
Write-Host "[3/5] Verifying payload (exe + bundle + tweak scripts)..." -ForegroundColor Yellow

$exe = Join-Path $OutDir "MASLOOPTIMIZER.exe"
if (-not (Test-Path $exe)) { throw "Missing MASLOOPTIMIZER.exe in publish output." }

$bundle = Join-Path $OutDir "tweaks.bundle.json"
if (-not (Test-Path $bundle)) { throw "Missing tweaks.bundle.json in publish output (check ExcludeFromSingleFile)." }

$tweaksRoot = Join-Path $OutDir "Scripts\Tweaks"
if (-not (Test-Path $tweaksRoot)) { throw "Missing Scripts\Tweaks folder in publish output (check ExcludeFromSingleFile)." }

$publishedTweaks = @(Get-ChildItem -Path $tweaksRoot -Recurse -Filter "*.bat" -File -ErrorAction SilentlyContinue)
if ($publishedTweaks.Count -ne $ExpectedTweaks) {
    throw "Expected $ExpectedTweaks tweak .bat scripts, but found $($publishedTweaks.Count)."
}
Write-Host ("    OK: MASLOOPTIMIZER.exe + tweaks.bundle.json + {0} tweak scripts." -f $publishedTweaks.Count) -ForegroundColor Green

# 4. Strip debug / runtime artifacts (keep .xbf compiled XAML — required by WPF) ----
Write-Host "[4/5] Removing debug artifacts (.pdb / doc .xml / createdump.exe)..." -ForegroundColor Yellow
Get-ChildItem -Path $OutDir -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { ($_.Extension -in ".pdb", ".xml") -or ($_.Name -ieq "createdump.exe") } |
    Remove-Item -Force -ErrorAction SilentlyContinue

$remaining = @(Get-ChildItem -Path $OutDir -Recurse -File -ErrorAction SilentlyContinue)
Write-Host ("    Payload now: {0} files." -f $remaining.Count) -ForegroundColor Gray

# 5. Pack the release ZIP (single root folder) ---------------------------
Write-Host "[5/5] Packing $([System.IO.Path]::GetFileName($ZipPath))..." -ForegroundColor Yellow
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
# -Path $OutDir (NOT "$OutDir\*") => the archive root is the "MASLOOPTIMIZER" folder,
# so extraction produces one clean directory instead of ~200 loose files.
Compress-Archive -Path $OutDir -DestinationPath $ZipPath -CompressionLevel Optimal

$zipInfo = Get-Item $ZipPath
Write-Host ""
Write-Host "=== DONE ===" -ForegroundColor Green
Write-Host ("Release ZIP: {0}" -f $zipInfo.FullName) -ForegroundColor Green
Write-Host ("Size: {0:N2} MB" -f ($zipInfo.Length / 1MB)) -ForegroundColor Green
Write-Host ("Layout inside ZIP: MASLOOPTIMIZER\ ($($remaining.Count) files)") -ForegroundColor Green
