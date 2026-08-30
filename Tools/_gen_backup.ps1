# ============================================================
# _gen_backup.ps1
# BackupEngine localization module generator.
#
# Extracts Ukrainian UI strings from existing XAML/CS sources
# (RestoreWindow, SafetyWindow, BackupEngine, MainWindow backup
# handlers) and writes:
#   Languages\UA\ua_BackupEngine.json
#   Languages\EN\en_BackupEngine.json
#
# RULES:
#   - This script source is pure ASCII. Ukrainian text is NEVER
#     typed here: it is read from the source files (valid UTF-8).
#   - The English map is authored below (ASCII + emoji via
#     [char]::ConvertFromUtf32 / codepoints).
#   - Atomic safe-swap writes; UTF-8 without BOM.
# ============================================================

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$LangRoot = Join-Path $Root "Languages"
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

$Sources = @{
    "RestoreWindow.xaml"     = Join-Path $Root "Views\RestoreWindow.xaml"
    "RestoreWindow.xaml.cs"  = Join-Path $Root "Views\RestoreWindow.xaml.cs"
    "SafetyWindow.xaml"      = Join-Path $Root "Views\SafetyWindow.xaml"
    "SafetyWindow.xaml.cs"   = Join-Path $Root "Views\SafetyWindow.xaml.cs"
    "BackupEngine.cs"        = Join-Path $Root "Engines\BackupEngine.cs"
    "MainWindow.xaml.cs"     = Join-Path $Root "Views\MainWindow.xaml.cs"
}

$contentCache = @{}
function Get-SourceContent([string]$file) {
    if (-not $contentCache.ContainsKey($file)) {
        $path = $Sources[$file]
        if (-not (Test-Path $path)) { throw "Missing source file: $path" }
        $contentCache[$file] = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    }
    return $contentCache[$file]
}

# --- transforms -------------------------------------------------

function Convert-CSharpLiteral([string]$s) {
    # C# escaped backslash -> single backslash
    $s = $s -replace '\\\\', '\'
    # C# newline escape -> real LF
    $s = $s -replace '\\n', "`n"
    # {someVar} / {SomeMethod(args)} -> {0}, {1}, ... in order of appearance
    $sb = New-Object System.Text.StringBuilder
    $i = 0
    $pos = 0
    foreach ($m in [regex]::Matches($s, '\{[^}]*\}')) {
        [void]$sb.Append($s.Substring($pos, $m.Index - $pos))
        [void]$sb.Append('{' + $i + '}')
        $i++
        $pos = $m.Index + $m.Length
    }
    [void]$sb.Append($s.Substring($pos))
    return $sb.ToString()
}

function Convert-Disclaimer([string]$s) {
    $s = $s -replace '<LineBreak\s*/>', "`n"
    $s = [regex]::Replace($s, '<[^>]+>', '')
    $s = $s -replace '[ \t]{2,}', ' '
    $s = $s -replace ' ?\r?\n ?', "`n"
    $s = $s -replace "`n{3,}", "`n`n"
    return $s.Trim()
}

# --- region slicing ---------------------------------------------

function Get-Region([string]$content, [string]$fromPattern, [string]$toPattern) {
    $fm = [regex]::Match($content, $fromPattern)
    if (-not $fm.Success) { throw "Region start not found: $fromPattern" }
    $start = $fm.Index + $fm.Length
    $tm = [regex]::Match($content.Substring($start), $toPattern)
    if (-not $tm.Success) { throw "Region end not found: $toPattern" }
    return $content.Substring($start, $tm.Index)
}

# --- extraction --------------------------------------------------

function Extract-Text([hashtable]$entry, [string]$key) {
    $content = Get-SourceContent $entry.File
    if ($entry.ContainsKey('From')) {
        $content = Get-Region $content $entry.From $entry.To
    }
    $matches = [regex]::Matches($content, $entry.Pattern)
    $idx = if ($entry.ContainsKey('Index')) { [int]$entry.Index } else { 0 }
    if ($idx -ge $matches.Count) {
        throw "Extraction failed for key '$key': pattern matched $($matches.Count) time(s), need index $idx"
    }
    $group = if ($entry.ContainsKey('Group')) { [int]$entry.Group } else { 1 }
    if ($group -ge $matches[$idx].Groups.Count) {
        throw "Extraction failed for key '$key': group $group missing"
    }
    $val = [string]$matches[$idx].Groups[$group].Value
    if ([string]::IsNullOrWhiteSpace($val)) {
        throw "Extraction failed for key '$key': empty value"
    }
    switch ($entry.Transform) {
        'csharp' { return Convert-CSharpLiteral $val }
        'disc'   { return Convert-Disclaimer $val }
        default  { return $val }
    }
}

# --- extraction table (key -> source rule) ----------------------
# Structural patterns + positional Index. The script fails loudly
# if a pattern/index no longer matches the current source files.

$Extract = @{
    # ---- RestoreWindow.xaml ----
    'BackupEngine.Title'                = @{ File='RestoreWindow.xaml';    Pattern='(?s)<Window x:Class="MASLOOPTIMIZER.RestoreWindow".*?Title="([^"]+)"' }
    'BackupEngine.RestoreTitle'         = @{ File='RestoreWindow.xaml';    Pattern='<TextBlock Text="([^"]+)" FontSize="15" FontWeight="Black"' }
    'BackupEngine.RestoreSubtitle'      = @{ File='RestoreWindow.xaml';    Pattern='<TextBlock Text="([^"]+)" FontSize="11.5" Foreground="{DynamicResource TextSecondary}"' }
    'BackupEngine.EmptyNotice'          = @{ File='RestoreWindow.xaml';    Pattern='x:Name="EmptyBackupsNotice" Text="([^"]+)"' }
    'BackupEngine.CreatedFormat'        = @{ File='RestoreWindow.xaml';    Pattern='Binding FormattedDate, StringFormat=''([^'']+)''' }
    'BackupEngine.BtnRestore'           = @{ File='RestoreWindow.xaml';    Pattern='<Button Content="([^"]+)" Background="#0078D4"' }
    'BackupEngine.BtnDelete'            = @{ File='RestoreWindow.xaml';    Pattern='<Button Content="([^"]+)" Background="{DynamicResource ActionBtnBg}"' }
    'BackupEngine.BtnSystemRestore'     = @{ File='RestoreWindow.xaml';    Pattern='<Button Content="([^"]+)" Style="{StaticResource HeaderActionBtn}" Margin="0,0,8,0"' }
    'BackupEngine.BtnOpenFolder'        = @{ File='RestoreWindow.xaml';    Pattern='<Button Content="([^"]+)" Style="{StaticResource HeaderActionBtn}" Click="BtnOpenFolder_Click"' }
    'BackupEngine.BtnClose'             = @{ File='RestoreWindow.xaml';    Pattern='<Button Grid.Column="2" Content="([^"]+)" Style="{StaticResource HeaderActionBtn}" Click="BtnClose_Click"' }

    # ---- RestoreWindow.xaml.cs (interpolated MessageBox calls, in order) ----
    'BackupEngine.LoadError'            = @{ File='RestoreWindow.xaml.cs'; Pattern='MessageBox\.Show\(\$"([^"]*)",\s*"([^"]+)"'; Index=0; Group=1; Transform='csharp' }
    'BackupEngine.ErrorTitle'           = @{ File='RestoreWindow.xaml.cs'; Pattern='MessageBox\.Show\(\$"([^"]*)",\s*"([^"]+)"'; Index=0; Group=2 }
    'BackupEngine.ConfirmRestoreMessage'= @{ File='RestoreWindow.xaml.cs'; Pattern='MessageBox\.Show\(\$"([^"]*)",\s*"([^"]+)"'; Index=1; Group=1; Transform='csharp' }
    'BackupEngine.ConfirmRestoreTitle'  = @{ File='RestoreWindow.xaml.cs'; Pattern='MessageBox\.Show\(\$"([^"]*)",\s*"([^"]+)"'; Index=1; Group=2 }
    'BackupEngine.RestoreFail'          = @{ File='RestoreWindow.xaml.cs'; Pattern='MessageBox\.Show\(\$"([^"]*)",\s*"([^"]+)"'; Index=2; Group=1; Transform='csharp' }
    'BackupEngine.RestoreFailTitle'     = @{ File='RestoreWindow.xaml.cs'; Pattern='MessageBox\.Show\(\$"([^"]*)",\s*"([^"]+)"'; Index=2; Group=2 }
    'BackupEngine.ConfirmDeleteMessage' = @{ File='RestoreWindow.xaml.cs'; Pattern='MessageBox\.Show\(\$"([^"]*)",\s*"([^"]+)"'; Index=3; Group=1; Transform='csharp' }
    'BackupEngine.ConfirmDeleteTitle'   = @{ File='RestoreWindow.xaml.cs'; Pattern='MessageBox\.Show\(\$"([^"]*)",\s*"([^"]+)"'; Index=3; Group=2 }
    'BackupEngine.DeleteError'          = @{ File='RestoreWindow.xaml.cs'; Pattern='MessageBox\.Show\(\$"([^"]*)",\s*"([^"]+)"'; Index=4; Group=1; Transform='csharp' }
    'BackupEngine.SystemRestoreFail'    = @{ File='RestoreWindow.xaml.cs'; Pattern='MessageBox\.Show\(\$"([^"]*)",\s*"([^"]+)"'; Index=5; Group=1; Transform='csharp' }
    'BackupEngine.OpenFolderFail'       = @{ File='RestoreWindow.xaml.cs'; Pattern='MessageBox\.Show\(\$"([^"]*)",\s*"([^"]+)"'; Index=6; Group=1; Transform='csharp' }
    'BackupEngine.RestoreDoneTitle'     = @{ File='RestoreWindow.xaml.cs'; Pattern='MessageBox\.Show\(res\.Message, "([^"]+)"'; Index=0 }

    # ---- SafetyWindow.xaml ----
    'BackupEngine.SafetyTitle'          = @{ File='SafetyWindow.xaml';     Pattern='(?s)<Window x:Class="MASLOOPTIMIZER.SafetyWindow".*?Title="([^"]+)"' }
    'BackupEngine.SafetyHeading'        = @{ File='SafetyWindow.xaml';     Pattern='<TextBlock Text="([^"]+)" FontSize="14.5" FontWeight="Black"' }
    'BackupEngine.SafetySubtitle'       = @{ File='SafetyWindow.xaml';     Pattern='<TextBlock Text="([^"]+)" FontSize="11.5" Foreground="#94A3B8"' }
    'BackupEngine.DisclaimerTitle'      = @{ File='SafetyWindow.xaml';     Pattern='Text="([^"]+)" FontWeight="Black" FontSize="11.5" Foreground="#F87171"' }
    'BackupEngine.DisclaimerBody'       = @{ File='SafetyWindow.xaml';     Pattern='(?s)LineHeight="17" Foreground="#F8FAFC">(.*?)</TextBlock>'; Transform='disc' }
    'BackupEngine.StepVssTitle'         = @{ File='SafetyWindow.xaml';     Pattern='Text="([^"]+)" FontWeight="Bold" FontSize="12.5"'; Index=0 }
    'BackupEngine.StepRegTitle'         = @{ File='SafetyWindow.xaml';     Pattern='Text="([^"]+)" FontWeight="Bold" FontSize="12.5"'; Index=1 }
    'BackupEngine.StepNotDone'          = @{ File='SafetyWindow.xaml';     Pattern='x:Name="TxtRestoreStatus" Text="([^"]+)"' }
    'BackupEngine.BtnCreateRestore'     = @{ File='SafetyWindow.xaml';     Pattern='Content="([^"]+)" Click="BtnCreateRestore_Click"' }
    'BackupEngine.BtnSaveRegistry'      = @{ File='SafetyWindow.xaml';     Pattern='Content="([^"]+)" Click="BtnCreateRegBackup_Click"' }
    'BackupEngine.TxtLogIdle'           = @{ File='SafetyWindow.xaml';     Pattern='x:Name="TxtLog" Grid.Row="3" Text="([^"]+)"' }
    'BackupEngine.BtnExit'              = @{ File='SafetyWindow.xaml';     Pattern='Content="([^"]+)" Background="#241416"' }
    'BackupEngine.BtnProceed'           = @{ File='SafetyWindow.xaml';     Pattern='Content="([^"]+)" IsEnabled="False"' }

    # ---- SafetyWindow.xaml.cs (status/button literals, in file order) ----
    'BackupEngine.StatusCheatSkipped'   = @{ File='SafetyWindow.xaml.cs';  Pattern='TxtRestoreStatus\.Text = "([^"]+)"'; Index=0 }
    'BackupEngine.StatusVssBusy'        = @{ File='SafetyWindow.xaml.cs';  Pattern='TxtRestoreStatus\.Text = "([^"]+)"'; Index=1 }
    'BackupEngine.StatusVssOk'          = @{ File='SafetyWindow.xaml.cs';  Pattern='TxtRestoreStatus\.Text = "([^"]+)"'; Index=2 }
    'BackupEngine.StatusVssLimited'     = @{ File='SafetyWindow.xaml.cs';  Pattern='TxtRestoreStatus\.Text = "([^"]+)"'; Index=3 }
    'BackupEngine.BtnDev'               = @{ File='SafetyWindow.xaml.cs';  Pattern='BtnCreateRestore\.Content = "([^"]+)"'; Index=0 }
    'BackupEngine.BtnCreated'           = @{ File='SafetyWindow.xaml.cs';  Pattern='BtnCreateRestore\.Content = "([^"]+)"'; Index=1 }
    'BackupEngine.BtnSkipped'           = @{ File='SafetyWindow.xaml.cs';  Pattern='BtnCreateRestore\.Content = "([^"]+)"'; Index=2 }
    'BackupEngine.StatusRegBusy'        = @{ File='SafetyWindow.xaml.cs';  Pattern='TxtRegistryStatus\.Text = "([^"]+)"'; Index=1 }
    'BackupEngine.StatusRegOk'          = @{ File='SafetyWindow.xaml.cs';  Pattern='TxtRegistryStatus\.Text = "([^"]+)"'; Index=2 }
    'BackupEngine.StatusRegPartial'     = @{ File='SafetyWindow.xaml.cs';  Pattern='TxtRegistryStatus\.Text = "([^"]+)"'; Index=3 }
    'BackupEngine.BtnSaved'             = @{ File='SafetyWindow.xaml.cs';  Pattern='BtnCreateRegBackup\.Content = "([^"]+)"'; Index=1 }
    'BackupEngine.BtnPartial'           = @{ File='SafetyWindow.xaml.cs';  Pattern='BtnCreateRegBackup\.Content = "([^"]+)"'; Index=2 }
    'BackupEngine.CheatActivated'       = @{ File='SafetyWindow.xaml.cs';  Pattern='TxtLog\.Text = "([^"]+)"'; Index=0 }
    'BackupEngine.StatusAllDone'        = @{ File='SafetyWindow.xaml.cs';  Pattern='TxtLog\.Text = "([^"]+)"'; Index=1 }

    # ---- BackupEngine.cs (result messages, in file order) ----
    'BackupEngine.VssCreated'           = @{ File='BackupEngine.cs';       Pattern='return \(true, "([^"]+)"\);'; Index=0 }
    'BackupEngine.VssPowerShellOk'      = @{ File='BackupEngine.cs';       Pattern='return \(true, "([^"]+)"\);'; Index=1 }
    'BackupEngine.VssError'             = @{ File='BackupEngine.cs';       Pattern='return \(false, \$"([^"]*)"'; Index=0; Transform='csharp' }
    'BackupEngine.VssFailed'            = @{ File='BackupEngine.cs';       Pattern='return \(false, \$"([^"]*)"'; Index=1; Transform='csharp' }
    'BackupEngine.VssServiceBlocked'    = @{ File='BackupEngine.cs';       Pattern='return \(false, "([^"]+)"'; Index=0 }
    'BackupEngine.BackupExportFailed'   = @{ File='BackupEngine.cs';       Pattern='return \(false, "([^"]+)"'; Index=1 }
    'BackupEngine.RestoreFolderMissing' = @{ File='BackupEngine.cs';       Pattern='return \(false, "([^"]+)"'; Index=2 }
    'BackupEngine.RestoreNoRegFiles'    = @{ File='BackupEngine.cs';       Pattern='return \(false, "([^"]+)"'; Index=3 }
    'BackupEngine.RestoreImportFailed'  = @{ File='BackupEngine.cs';       Pattern='return \(false, "([^"]+)"'; Index=4 }
    'BackupEngine.BackupExportError'    = @{ File='BackupEngine.cs';       Pattern='return \(false, \$"([^"]*)"'; Index=2; Transform='csharp' }
    'BackupEngine.BackupSaved'          = @{ File='BackupEngine.cs';       Pattern='return \(true, \$"([^"]*)"'; Index=0; Transform='csharp' }
    'BackupEngine.RestoreImported'      = @{ File='BackupEngine.cs';       Pattern='return \(true, \$"([^"]*)"'; Index=1; Transform='csharp' }

    # ---- MainWindow.xaml.cs (backup handlers region) ----
    'BackupEngine.MainVssConfirm'       = @{ File='MainWindow.xaml.cs'; From='private async void BtnVssPoint_Click'; To='private void BtnRestoreRollback_Click'; Pattern='MessageBox\.Show\("([^"]+)",\s*"([^"]+)"'; Group=1 }
    'BackupEngine.MainVssTitle'         = @{ File='MainWindow.xaml.cs'; From='private async void BtnVssPoint_Click'; To='private void BtnRestoreRollback_Click'; Pattern='MessageBox\.Show\("([^"]+)",\s*"([^"]+)"'; Group=2 }
    'BackupEngine.MainVssBusy'          = @{ File='MainWindow.xaml.cs'; From='private async void BtnVssPoint_Click'; To='private void BtnRestoreRollback_Click'; Pattern='StatusText\.Text = "([^"]+)"'; Index=0 }
    'BackupEngine.MainBackupBusy'       = @{ File='MainWindow.xaml.cs'; From='private async void BtnVssPoint_Click'; To='private void BtnRestoreRollback_Click'; Pattern='StatusText\.Text = "([^"]+)"'; Index=1 }
    'BackupEngine.MainVssResultTitle'   = @{ File='MainWindow.xaml.cs'; From='private async void BtnVssPoint_Click'; To='private void BtnRestoreRollback_Click'; Pattern='MessageBox\.Show\(res\.Message, "([^"]+)"'; Index=0 }
    'BackupEngine.MainBackupResultTitle'= @{ File='MainWindow.xaml.cs'; From='private async void BtnVssPoint_Click'; To='private void BtnRestoreRollback_Click'; Pattern='MessageBox\.Show\(res\.Message, "([^"]+)"'; Index=1 }
}

# --- emoji helpers (pure ASCII source) ---------------------------
function CodePoint([uint32]$codePoint) { return [char]::ConvertFromUtf32($codePoint) }
$eRefresh = CodePoint 0x1F504
$eFolder  = CodePoint 0x1F4C2
$eBolt    = [string][char]0x26A1
$eTrash   = (CodePoint 0x1F5D1) + [string][char]0xFE0F
$eShield  = (CodePoint 0x1F6E1) + [string][char]0xFE0F
$eWarn    = [string][char]0x26A0 + [string][char]0xFE0F
$eCheck   = [string][char]0x2713
$eCross   = [string][char]0x274C
$eDisk    = CodePoint 0x1F4BE
$eRocket  = CodePoint 0x1F680
$eMulX    = [string][char]0x2715
$eHour    = [string][char]0x23F3
$eBullet  = [string][char]0x2022
$eUnlock  = CodePoint 0x1F513

# --- English map (ASCII + emoji) ----------------------------------
$EnMap = @{
    'BackupEngine.Title'                 = 'MASLOOPTIMIZER - Registry & System Restore'
    'BackupEngine.RestoreTitle'          = "$eRefresh REGISTRY & SYSTEM RESTORE"
    'BackupEngine.RestoreSubtitle'       = 'Select a backup point or run Windows System Restore'
    'BackupEngine.EmptyNotice'           = 'No restore points found'
    'BackupEngine.CreatedFormat'         = 'Created: {0}'
    'BackupEngine.BtnRestore'            = "$eBolt Restore"
    'BackupEngine.BtnDelete'             = $eTrash
    'BackupEngine.BtnSystemRestore'      = "$eShield Windows System Restore (VSS UI)"
    'BackupEngine.BtnOpenFolder'         = "$eFolder Open backups folder"
    'BackupEngine.BtnClose'              = 'Close'
    'BackupEngine.LoadError'             = 'Error loading the backup list: {0}'
    'BackupEngine.ErrorTitle'            = 'Error'
    'BackupEngine.ConfirmRestoreMessage' = "Restore all registry parameters from the selected backup?`n`nFolder: {0}`nKeys: {1}"
    'BackupEngine.ConfirmRestoreTitle'   = 'Confirm restore'
    'BackupEngine.RestoreFail'           = 'Failed to restore the registry: {0}'
    'BackupEngine.RestoreFailTitle'      = 'Critical error'
    'BackupEngine.ConfirmDeleteMessage'  = 'Delete this registry backup ({0})?'
    'BackupEngine.ConfirmDeleteTitle'    = 'Delete backup'
    'BackupEngine.DeleteError'           = 'Error deleting backup: {0}'
    'BackupEngine.SystemRestoreFail'     = 'Failed to launch system restore: {0}'
    'BackupEngine.OpenFolderFail'        = 'Failed to open the folder: {0}'
    'BackupEngine.RestoreDoneTitle'      = 'Registry restore'
    'BackupEngine.SafetyTitle'           = 'MASLOOPTIMIZER - Safety Protocol'
    'BackupEngine.SafetyHeading'         = "$eShield DISCLAIMER & SYSTEM PROTECTION"
    'BackupEngine.SafetySubtitle'        = 'Mandatory safety protocol before starting OS optimization'
    'BackupEngine.DisclaimerTitle'       = "$eWarn OFFICIAL WARNING ABOUT RISKS AND TERMS OF USE:"
    'BackupEngine.DisclaimerBody'        = "$eBullet Deep system intervention: The MASLOOPTIMIZER suite performs deep low-level modifications of Windows parameters, including mass editing of HKLM/HKCU registry branches, changing security policies, disabling services and kernel scheduler tuning.`n`n$eBullet Full disclaimer: All optimizations, debloat scripts, cleanups and tweaks are applied exclusively AT YOUR OWN RISK. The developer and project team bear no legal or material responsibility for possible system instability, file damage or antivirus software failures.`n`n$eBullet Mandatory safety protocol: Access to the program is blocked. To start working, create a restore point (VSS) and save a registry backup."
    'BackupEngine.StepVssTitle'          = '1. Create a Windows System Restore point (VSS)'
    'BackupEngine.StepRegTitle'          = '2. Full registry backup (Export all tweak branches)'
    'BackupEngine.StepNotDone'           = "$eCross Not created (Mandatory step)"
    'BackupEngine.BtnCreateRestore'      = "$eBolt Create restore point"
    'BackupEngine.BtnSaveRegistry'       = "$eDisk Save registry"
    'BackupEngine.TxtLogIdle'            = 'Complete both protection steps to unlock the application.'
    'BackupEngine.BtnExit'               = "$eMulX Decline and exit"
    'BackupEngine.BtnProceed'            = "$eRocket I understand the risks - Enter the app"
    'BackupEngine.StatusCheatSkipped'    = "$eCheck Skipped (MASLO cheat code)"
    'BackupEngine.BtnDev'                = "$eCheck DEV"
    'BackupEngine.StatusVssBusy'         = "$eHour Creating restore point... Please wait..."
    'BackupEngine.StatusVssOk'           = "$eCheck Restore point created successfully"
    'BackupEngine.BtnCreated'            = "$eCheck Created"
    'BackupEngine.StatusVssLimited'      = "$eWarn VSS is limited by the system. Step counted."
    'BackupEngine.BtnSkipped'            = "$eWarn Skipped"
    'BackupEngine.StatusRegBusy'         = "$eHour Exporting system registry branches..."
    'BackupEngine.StatusRegOk'           = "$eCheck All branches saved to the backups folder"
    'BackupEngine.BtnSaved'              = "$eCheck Saved"
    'BackupEngine.StatusRegPartial'      = "$eWarn Registry backup saved partially."
    'BackupEngine.BtnPartial'            = "$eWarn Partial"
    'BackupEngine.CheatActivated'        = "$eUnlock 'MASLO' cheat code activated! Full access unlocked."
    'BackupEngine.StatusAllDone'         = "$eCheck All protection measures completed. Optimizer access unlocked."
    'BackupEngine.VssCreated'            = 'Windows System Restore point (VSS) created successfully!'
    'BackupEngine.VssError'              = 'VSS error (Code: {0}). Check that System Protection is enabled in Windows properties.'
    'BackupEngine.VssPowerShellOk'       = 'Windows restore point created via PowerShell!'
    'BackupEngine.VssServiceBlocked'     = 'VSS service is disabled or blocked by Windows protection policies.'
    'BackupEngine.VssFailed'             = 'Error creating VSS restore point: {0}'
    'BackupEngine.BackupSaved'           = 'Registry backup ({0} branches, {1}) saved!'
    'BackupEngine.BackupExportFailed'    = 'Failed to export registry branches.'
    'BackupEngine.BackupExportError'     = 'Registry export error: {0}'
    'BackupEngine.RestoreFolderMissing'  = 'Backup folder not found.'
    'BackupEngine.RestoreNoRegFiles'     = 'No .reg files found in the selected folder.'
    'BackupEngine.RestoreImportFailed'   = 'Failed to import any keys from the selected backup (check administrator rights).'
    'BackupEngine.RestoreImported'       = 'Successfully imported {0} keys from backup [{1}].'
    'BackupEngine.MainVssConfirm'        = 'Create a new Windows System Restore point (VSS)?'
    'BackupEngine.MainVssTitle'          = 'System Protection'
    'BackupEngine.MainVssBusy'           = 'Creating VSS system restore point...'
    'BackupEngine.MainVssResultTitle'    = 'Restore point'
    'BackupEngine.MainBackupBusy'        = 'Creating a registry backup...'
    'BackupEngine.MainBackupResultTitle' = 'Registry backup'
}

# --- build both maps in stable key order -------------------------
$KeyOrder = @($Extract.Keys | Sort-Object)
$uaOut = [ordered]@{}
$enOut = [ordered]@{}
$keyPrefix = 'BackupEngine.'
foreach ($k in $KeyOrder) {
    # The JSON root section 'BackupEngine' already provides the prefix,
    # so map keys must be short names (flattened key = BackupEngine.X).
    $short = if ($k.StartsWith($keyPrefix)) { $k.Substring($keyPrefix.Length) } else { $k }
    $uaOut[$short] = Extract-Text $Extract[$k] $k
    if (-not $EnMap.ContainsKey($k)) { throw "Missing EN text for key '$k'" }
    $enOut[$short] = $EnMap[$k]
}
$extraKeys = @($EnMap.Keys | Where-Object { -not $Extract.ContainsKey($_) })
if ($extraKeys.Count -gt 0) { throw "EN map has unknown keys: $($extraKeys -join ', ')" }

# --- validation ---------------------------------------------------
$uaJson = ConvertTo-Json -InputObject ([ordered]@{ BackupEngine = $uaOut }) -Depth 10
$enJson = ConvertTo-Json -InputObject ([ordered]@{ BackupEngine = $enOut }) -Depth 10
$uaParsed = $uaJson | ConvertFrom-Json
$enParsed = $enJson | ConvertFrom-Json
$uaCount = @($uaParsed.BackupEngine.PSObject.Properties).Count
$enCount = @($enParsed.BackupEngine.PSObject.Properties).Count
if ($uaCount -ne $KeyOrder.Count) { throw "UA JSON key count mismatch: $uaCount vs $($KeyOrder.Count)" }
if ($enCount -ne $KeyOrder.Count) { throw "EN JSON key count mismatch: $enCount vs $($KeyOrder.Count)" }
if ($uaCount -ne $enCount) { throw "UA/EN key count mismatch: $uaCount vs $enCount" }

# --- atomic safe-swap write (UTF-8 no BOM) ------------------------
function Write-Utf8NoBomFile([string]$path, [string]$content) {
    $dir = Split-Path -Parent $path
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $tmp = $path + '.tmp'
    [System.IO.File]::WriteAllText($tmp, $content, $utf8NoBom)
    if ((Get-Item $tmp).Length -eq 0) { throw "Empty tmp output: $tmp" }
    Move-Item -Path $tmp -Destination $path -Force
}

$uaPath = Join-Path $LangRoot 'UA\ua_BackupEngine.json'
$enPath = Join-Path $LangRoot 'EN\en_BackupEngine.json'
Write-Utf8NoBomFile $uaPath $uaJson
Write-Utf8NoBomFile $enPath $enJson

Write-Host "BackupEngine localization module generated: $uaCount keys"
Write-Host "  $uaPath"
Write-Host "  $enPath"

