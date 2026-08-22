# Publish-Release.ps1
# 1-Click Конвеєр компіляції фінального релізу MASLOOPTIMIZER

$ErrorActionPreference = "Stop"
$projectDir = $PSScriptRoot
$outputDir = Join-Path $projectDir "_build_release"

Write-Host "======================================================" -ForegroundColor Cyan
Write-Host "  🚀 MASLOOPTIMIZER — 1-CLICK RELEASE COMPILER" -ForegroundColor Cyan
Write-Host "======================================================" -ForegroundColor Cyan

# 1. Зупинка відкритого процесу, якщо він запущений
Write-Host "`n[1/4] Перевірка відкритих процесів..." -ForegroundColor Yellow
Stop-Process -Name "MASLOOPTIMIZER" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "MASLOOPTIMIZER_CS" -Force -ErrorAction SilentlyContinue

# 2. Оновлення маніфесту твіків (якщо папка tweaks оновлювалась)
Write-Host "[2/4] Синхронізація бази твіків..." -ForegroundColor Yellow
$tweaksFolder = Join-Path $projectDir "..\v0.3\tweaks"
if (Test-Path $tweaksFolder) {
    $bundleScript = Join-Path $projectDir "Build-TweaksManifest.ps1"
    if (Test-Path $bundleScript) {
        & $bundleScript -tweaksFolder $tweaksFolder
    }
}

# 3. Очищення цільової папки
Write-Host "[3/4] Підготовка директорії випуску..." -ForegroundColor Yellow
if (Test-Path $outputDir) {
    Remove-Item $outputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

# 4. Компіляція автономного Single-File бінарника
Write-Host "[4/4] Компіляція .NET 8 Native x64 Single-File EXE..." -ForegroundColor Yellow

dotnet publish `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:PublishReadyToRun=true `
    -o $outputDir

# Перейменування у фінальний бінарник
$builtExe = Join-Path $outputDir "MASLOOPTIMIZER_CS.exe"
$finalExe = Join-Path $outputDir "MASLOOPTIMIZER.exe"
if (Test-Path $builtExe) {
    Move-Item -Path $builtExe -Destination $finalExe -Force
}

# Копіюємо іконку для ресурсів
$iconSource = Join-Path $projectDir "icon"
if (Test-Path $iconSource) {
    Copy-Item -Path $iconSource -Destination (Join-Path $outputDir "icon") -Recurse -Force
}

Write-Host "`n======================================================" -ForegroundColor Green
Write-Host "  ✅ РЕЛІЗ УСПІШНО ЗІБРАНО!" -ForegroundColor Green
Write-Host "  📂 Шлях: $finalExe" -ForegroundColor Green
Write-Host "  ⚡ Готовий до поширення автономний EXE файл." -ForegroundColor Green
Write-Host "======================================================" -ForegroundColor Green

# Відкриваємо папку з готовим EXE
Start-Process "explorer.exe" -ArgumentList "`"$outputDir`""