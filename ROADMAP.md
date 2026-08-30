# MASLOOPTIMIZER — Strategic Roadmap

> Трекінг прогресу розробки. Позначайте виконані пункти через `[x]`.
> Сигнатури модулів див. у `REPO_MAP.md`.

## Milestone 1: Core Performance Engines
- [ ] **GameMode Booster** — `Engines/GameModeEngine.cs`
  - `ActivateGameModeAsync()` / `DeactivateGameModeAsync()` / `ToggleGameModeAsync()`
  - Standby-пам'ять: `PurgeStandbyListAsync()` (NtSetSystemInformation)
  - Демоція фонових процесів: `DemoteProcess(int pid)`, `DemoteBackgroundProcesses()`, `RestoreProcess(...)`
  - Стан/подія: `IsGameModeActive`, `OnGameModeStateChanged`
- [ ] **Network TCP/Latency** — `Engines/NetworkEngine.cs`
  - `OptimizeTcpLatencyAsync(bool isApply)` (Nagle/ACK, автотюнінг, EEE, LSO, QoS, DNS cache, буфери NIC)
- [ ] **Memory/Standby Cleaner** — `Engines/CleanerEngine.cs`, `Engines/MonitorEngine.cs`
  - `CleanItemAsync()`, `CleanAllSafeAsync()`, `CalculateSizesAsync()`
  - Фоновий watcher (`IsWatcherRunning`)

## Milestone 2: Windows System Management
- [ ] **Debloat UWP** — `Engines/DebloatEngine.cs`
  - `ScanInstalledPackagesAsync()`, `UninstallPackageAsync()`, `RestorePackageAsync()`, `RestoreAllDefaultUwpAsync()`
- [ ] **Startup Manager** — `Engines/StartupEngine.cs`
  - Перелік автозавантажень, увімкнення/вимкнення (Registry Run + startup folders)
- [ ] **MSI Interrupt Tooling** — `Engines/MsiEngine.cs`
  - Перемикання line-based / MSI режимів, енумерація PCI-пристроїв (`MsiStats`)
- [ ] **DNS Benchmarking** — `Engines/DnsEngine.cs`
  - Бенчмарк пресетів DNS, сортування fastest-first (`DnsSortMode`)

## Milestone 3: System Integrity & Rollback
- [ ] **VSS Restore Points** — `Engines/BackupEngine.cs`
  - `CreateVssRestorePointAsync(string description)`
- [ ] **Registry Snapshots** — `Engines/BackupEngine.cs`
  - `ExportRegistryBackupAsync()`, `RestoreRegistryFromFolderAsync()`, `GetAvailableBackupsAsync()`
- [ ] **Atomic State-Swapping** — `Engines/PowerEngine.cs`, `Engines/ToolsEngine.cs`, `Engines/TweakEngine.cs`
  - Снапшот оригінальних значень перед зміною + атомарний rollback (registry/services/power plans)

## Milestone 4: WPF UI/UX & Desktop Integration
- [ ] **Cyber Dark/Light Themes** — `Managers/ThemeEngine.cs`
  - 30 тем застосунку + 30 HUD-тем: `ApplyAppTheme()`, `ApplyNextAppTheme()`, `ApplyPreviousAppTheme()`, `Brush(string key)`
- [ ] **Monitoring HUD Widget** — `Views/WidgetWindow.xaml.cs`
  - HUD-віджет моніторингу (FPS/пам'ять/стан), DispatcherTimer
- [ ] **Tray Background Loop** — `Managers/TrayManager.cs`
  - NotifyIcon, фоновий цикл: `Initialize()`, `ToggleWidget()`, `ShowMainWindow()`, `FullExit()`

## Milestone 5: Localization & Data Pipelines
- [ ] **Dynamic Language Switcher** — `Managers/LocalizationManager.cs`, `Managers/SettingsManager.cs`
  - `LocalizationManager.Instance.For(...)`, `ReadLanguage()`/`SaveLanguage()`
  - Fallback-ланцюг: поточна мова → EN → UA → ключ
- [ ] **JSON Bundle Engine** — `tweaks.bundle.json`, `Engines/TweakEngine.cs`, `Engines/PresetEngine.cs`
  - Парсинг та застосування registry/service/command-твіків із вшитого bundle
- [ ] **Offline Fallbacks** — `Languages/**/*.json` (EmbeddedResource у single-file exe)

## Milestone 6: Release Engineering & Deployment
- [ ] **Single-File Publish** — `MASLOOPTIMIZER.csproj`
  - `PublishSingleFile`, `SelfContained`, `EnableCompressionInSingleFile`, `RuntimeIdentifier=win-x64`
- [ ] **Native AOT / ReadyToRun** — `MASLOOPTIMIZER.csproj`
  - `PublishReadyToRun` / AOT-публікація для скорочення старту
- [ ] **Auto-Update Pipeline** — `Managers/UpdateManager.cs`
  - `CheckForUpdateAsync()`, `DownloadAndInstallUpdateAsync()` (поточна версія `0.4.6`)
