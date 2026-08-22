# Build-TweaksManifest.ps1
# Автоматичний парсер усіх .ps1 твіків у єдиний JSON-бандл для C#

$tweaksFolder = Join-Path $PSScriptRoot "tweaks"
$outputJson = Join-Path $PSScriptRoot "tweaks.bundle.json"

if (-not (Test-Path $tweaksFolder)) {
    Write-Error "Папку tweaks не знайдено за шляхом: $tweaksFolder"
    exit
}

$allFiles = Get-ChildItem -Path $tweaksFolder -Recurse -Filter "*.ps1"
$tweakList = [System.Collections.Generic.List[PSCustomObject]]::new()

Write-Host "🔍 Сканування твіків (знайдено: $($allFiles.Count) файлів)..." -ForegroundColor Cyan

foreach ($file in $allFiles) {
    try {
        $rawCode = [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
        
        # Виконуємо скрипт твіка для зчитування його об'єкта метаданих
        $module = [scriptblock]::Create($rawCode).InvokeReturnAsIs()

        if ($module -and $module.Id) {
            # Визначаємо Risk
            $risk = "Safe"
            if ($file.DirectoryName -like "*0_UI*") { $risk = "UI" }
            elseif ($file.DirectoryName -like "*1_Safe*") { $risk = "Safe" }
            elseif ($file.DirectoryName -like "*2_Medium*") { $risk = "Medium" }
            elseif ($file.DirectoryName -like "*3_High*") { $risk = "High" }

            # Визначаємо категорію
            $category = $module.Category
            if (-not $category) {
                $parentName = Split-Path -Leaf (Split-Path -Parent $file.FullName)
                $category = switch -Wildcard ($parentName) {
                    "*Privacy*"     { "Приватність & Телеметрія" }
                    "*Gaming*"      { "GPU & Геймінг" }
                    "*CPU*"         { "Процесор & Живлення" }
                    "*Storage*"     { "Накопичувачі & SSD" }
                    "*Explorer*"    { "Провідник & QoL" }
                    "*Network*"     { "Мережа & Пінг" }
                    "*Input*"       { "Периферія & Введення" }
                    "*Updates*"     { "Оновлення & Обслуговування" }
                    "*Services*"    { "Системні служби" }
                    "*Security*"    { "Безпека системи" }
                    Default         { "Загальні твіки" }
                }
            }

            $tweakObj = [ordered]@{
                Id          = $module.Id
                Name        = $module.Name
                Category    = $category
                Risk        = $risk
                Description = if ($module.Description) { $module.Description } else { $module.Desc }
                Benefits    = if ($module.Benefits) { $module.Benefits } else { "" }
                SideEffects = if ($module.SideEffects) { $module.SideEffects } else { "" }
                CheckScript = if ($module.CheckStatus) { $module.CheckStatus.ToString().Trim() } else { "" }
                ApplyScript = if ($module.Apply) { $module.Apply.ToString().Trim() } else { "" }
                RestoreScript = if ($module.Restore) { $module.Restore.ToString().Trim() } else { "" }
            }

            $tweakList.Add([PSCustomObject]$tweakObj)
            Write-Host "  [✓] Додано: $($module.Name) ($risk)" -ForegroundColor Green
        }
    }
    catch {
        Write-Warning "Помилка обробки $($file.Name): $_"
    }
}

$payload = @{
    Version     = "1.0"
    GeneratedAt = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
    TotalCount  = $tweakList.Count
    Tweaks      = $tweakList
} | ConvertTo-Json -Depth 6

[System.IO.File]::WriteAllText($outputJson, $payload, [System.Text.Encoding]::UTF8)
Write-Host "`n🚀 Успішно! Згенеровано $outputJson (Всього твіків: $($tweakList.Count))" -ForegroundColor Cyan