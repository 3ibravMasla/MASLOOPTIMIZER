# 🧈 MASLOOPTIMIZER

![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D4?style=for-the-badge&logo=windows)
![.NET](https://img.shields.io/badge/.NET-8.0%20WPF-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![Version](https://img.shields.io/badge/Release-v0.6.0-00FF9D?style=for-the-badge)
![License](https://img.shields.io/badge/License-MIT-F59E0B?style=for-the-badge)
![Language](https://img.shields.io/badge/Code-C%23-512BD4?style=for-the-badge&logo=csharp)

> 🌐 **English** · [🇺🇦 Українська версія](README.md)

**An open-source all-in-one utility for deep Windows 10/11 tuning, cleaning, diagnostics and maximum optimization.**

A single dashboard that replaces a pile of separate tools: **70 modular `.bat` tweaks** across 7 categories, a DNS optimizer with **37 presets** and **DoH**, UWP debloat, a startup manager, a software catalog via `winget`, disk cleanup with an analyzer, backups (VSS + registry), deep hardware diagnostics and a floating neon **HUD widget**. All built with C# / WPF / .NET 8.

<img width="1919" height="1025" alt="image_2026-08-31_02-00-53" src="https://github.com/user-attachments/assets/eb83c0c7-13fb-4c25-a68c-dced7e049e71" />
<img width="998" height="721" alt="image_2026-08-31_02-01-14" src="https://github.com/user-attachments/assets/60cddf51-1363-4c4b-b5b6-fcaec13f3742" />
<img width="1072" height="896" alt="image_2026-08-31_02-01-46" src="https://github.com/user-attachments/assets/b2324b95-8103-4078-a200-18f7d4596d4f" />

---

## 📑 Table of Contents

1. [⚠️ Disclaimer](#-disclaimer)
2. [⬇️ Download & Installation](#️-download--installation)
3. [🤖 Project Philosophy: Vibe-Coding](#-project-philosophy-vibe-coding)
4. [🚀 Feature Highlights](#-feature-highlights)
5. [🏗️ Architecture & Tech Stack](#️-architecture--tech-stack)
6. [🔧 Building from Source](#-building-from-source)
7. [⚠️ Known Issues](#-known-issues)
8. [🛣️ Roadmap](#️-roadmap)
9. [🤝 Support](#-support)

---

## ⚠️ DISCLAIMER (STRICT LIMITATION OF LIABILITY)

> This program is being developed **exclusively for the author's personal use** as an experimental tool. It performs **deep interference with the operating system**: it modifies the system registry (`HKLM`/`HKCU`), alters power schemes and the state of critical services, rewrites network stack and DNS settings, removes built-in Windows packages (`Remove-AppxPackage`) and **permanently deletes files** (caches, logs, recycle bin) in cleanup mode.
>
> **THE AUTHOR ASSUMES NO RESPONSIBILITY WHATSOEVER FOR ANY CONSEQUENCES OF USING MASLOOPTIMIZER.**
>
> By using this program, you **fully and unconditionally accept all risks**, including:
>
> - 🟥 Critical system errors and **"Blue Screens of Death" (BSOD)**;
> - 🟥 File-system corruption or **irreversible loss of personal data**;
> - 🟥 Performance degradation, hardware instability or **hardware failure**;
> - 🟥 Breaching rules/license agreements of programs and games (VAC, Anti-Cheat) — this is **not a cheat** and works at the OS level, but we do not control anti-cheat policies.

**ALWAYS, before pressing "Apply":**

1. Create a **Windows restore point** — there is a "🛡️ VSS Point" button in the app;
2. Make a **registry export** ("💾 Registry Backup" button);
3. Back up important files.

> **YOU USE THIS SOFTWARE ENTIRELY AT YOUR OWN RISK!** No guarantees of compatibility, stability or data safety.

---

## ⬇️ Download & Installation

[![Download](https://img.shields.io/badge/%E2%AC%87%EF%B8%8F_Download-latest_release-00FF9D?style=for-the-badge&logo=github)](https://github.com/3ibravMasla/MASLOOPTIMIZER/releases/latest)

**Requirements:** Windows 10 1809+ / Windows 11 (x64) · **administrator rights** (UAC is mandatory) · nothing else to install — the .NET runtime is bundled into the release.

### Installation

1. Download **`MASLOOPTIMIZER-v0.6.0-win-x64.zip`** from the [Releases](https://github.com/3ibravMasla/MASLOOPTIMIZER/releases/latest) page.
2. Extract the archive into a **dedicated folder** (e.g. `C:\MASLOOPTIMIZER`). The app is **portable** — no installer needed.
3. Run `MASLOOPTIMIZER.exe` (confirm the UAC prompt).
4. Optionally enable autostart and create a shortcut in Settings.

> **Where your settings live.** Tweak states, logs, backups and `settings.json` are stored in `%ProgramData%\MASLOOPTIMIZER` and in the registry (`HKCU\Software\MASLOOPTIMIZER`). The application folder itself is **code only**: it can be replaced entirely without losing anything.

### Updating (automatic, starting from 0.6.0)

The app has **built-in auto-update**: it checks GitHub Releases itself and shows an "Update available" banner. After you press the button, the archive is downloaded, the app restarts and **replaces the whole file set** (exe + `Scripts` + `tweaks.bundle.json`) while preserving your settings.

### ⚠️ One-time migration from 0.5.6 to 0.6.0

**Older versions (≤ 0.5.6) will NOT auto-update.** In 0.5.6 the updater could only swap the `.exe` itself, whereas 0.6.0 is a **modular structure** with external `.bat` scripts that simply do not exist in the old build. The migration is therefore **manual and one-time**:

1. Tray → **🛑 Full Exit** (close the old version).
2. Download the 0.6.0 ZIP from the [Releases](https://github.com/3ibravMasla/MASLOOPTIMIZER/releases/latest) page.
3. Extract into a **dedicated folder** (e.g. `C:\MASLOOPTIMIZER`) and run `MASLOOPTIMIZER.exe`.
4. The old folder can be deleted — settings live in `%ProgramData%` and the registry.

From then on, updates arrive on their own through the banner. Going 0.6.0 → 0.6.x is already fully automatic.

---

## 🤖 Project Philosophy: Vibe-Coding

There are almost no decent, **free and fully open-source** tools for deep PC tuning that satisfy power users, gamers and overclocking enthusiasts. So I built my own.

This project is a prime example of the **Vibe-Coding** approach:

> 🧑💻 **The human is the architect and the source of inspiration.** All ideas, Windows "under the hood" logic, the understanding of which tweaks actually work and which are marketing, plus the development direction — all of that belongs to the human.
>
> 🤖 **The AI is the coder, refactorer and UI engineer.** Writing C# syntax, generating XAML, refactoring and small-scale implementation are done in close collaboration with AI assistants.

This symbiosis makes it possible to turn complex low-level ideas (NT Native API, WMI, Task Scheduler, registry) into a real working tool with incredible speed — while keeping the code clean: atomic snapshots, rollback, handle hygiene, and an event-driven model with no polling timers.

🐛 **Found a bug?** Feedback is very welcome. Open an **Issue** in this repository if you spot a problem, or suggest cool ideas for improvement!

---

## 🚀 Feature Highlights

### 🧩 Tweak Engine — the heart of the app (`TweakEngine.cs`)

- **70 modular `.bat` tweaks** across 7 categories of 10 scripts each: **CPU · GPU · Gaming · Telemetry · UI · Security · Power**;
- Every tweak is an **external `.bat` script** with two branches: `apply` and `restore`. The app runs it through `cmd.exe` in the background (no window), captures `stdout/stderr` and treats only `ExitCode == 0` as success;
- Tweak metadata lives in [`tweaks.bundle.json`](tweaks.bundle.json) (**Version 3.1**) — embedded into the exe *and* duplicated next to it, so the catalog can be updated without rebuilding;
- **Risk levels** `Safe` / `Medium` / `High` — they affect sorting and mandatory confirmation;
- **Live status** ("Applied / No") via registry marker checks (`CheckReg*`);
- **Snapshot semantics:** everything that changes captures its current state first, so it can always be rolled back;
- **Batch applying** of several tweaks in one action, plus per-category filtering.

> 💡 Tweaks are **plain text `.bat` files** in [`Scripts/Tweaks/`](Scripts/Tweaks). You can read them, write your own and version them like regular code.

### 🌐 DNS Optimizer (`DnsEngine.cs`)

- A catalog of **37 different presets** (Cloudflare, Google, Quad9, OpenDNS, Neustar, AdGuard, NextDNS, Comodo, Level3…) grouped into **Speed / Security / Gaming**;
- **Multi-sample latency measurement** — 3 probes per provider, ICMP with TCP 53 → 443 fallback and spike trimming (`AggregateLatency`);
- **1-Click "fastest"** — finds and applies the best DNS, backing up the original;
- **DNS-over-HTTPS (DoH)** — on Windows 11 (build ≥ 22000) encryption is added for known providers via `netsh dns add encryption ... autoupgrade=yes udpfallback=no`;
- **🔄 Flush DNS cache** and **Reset DHCP / restore original DNS**.

### 🗑️ Debloat, Cleanup, Startup & Software

- **UWP Debloat** ([`DebloatEngine.cs`](Engines/DebloatEngine.cs)): ~40 built-in bloat items (Copilot, Recall, Cortana, Feedback Hub, Clipchamp, Xbox companions, Zune…) plus **OneDrive**, with batch removal and restoration via the Microsoft Store;
- **Deep cleanup** ([`CleanerEngine.cs`](Engines/CleanerEngine.cs)): **19 categories** — GPU shader caches (DX/NVIDIA/AMD/Intel/Vulkan), temp files, Windows Update cache, CBS logs, Delivery Optimization, recycle bin (`SHQUERYRBINFO`), browser caches, Prefetch, `Windows.old`, `WinSxS` via **DISM** — with safe and manual modes. Plus a **📊 WizTree-style disk analyzer** that can delete heavy folders (optionally to the recycle bin);
- **Startup manager** ([`StartupEngine.cs`](Engines/StartupEngine.cs)): Registry Run/RunOnce (user + system), **Task Scheduler** (XML parsing), Startup folders; disabling goes through `StartupApproved` without deleting entries;
- **Software catalog** ([`ToolsEngine.cs`](Engines/ToolsEngine.cs)): utilities (HWiNFO, CPU-Z, GPU-Z, MSI Afterburner, FanControl, Ryzen Master, PresentMon…) with **silent `winget` installation**, VC++ Redist and DirectX Web packages, Windows MAS activation, and **desktop shortcut creation**.

### 🛟 Backup & Rollback (`BackupEngine.cs`)

- Creating a **VSS restore point** (WMI `SystemRestore` + PowerShell fallback);
- **Registry export** of all critical keys into a timestamped folder with `meta.json`;
- **Backup mirroring** into `%ProgramData%\MASLOOPTIMIZER\Backups` and onto secondary drives (`MASLOOPTIMIZER_Backups`) — so backups survive a system reinstall;
- Import/rollback from a backup, listing, sorting and deletion in a dedicated window.

### 📊 Diagnostics, HUD Widget & Interface

- **Deep hardware diagnostics** ([`DiagnosticEngine.cs`](Engines/DiagnosticEngine.cs)): static audit — CPU, GPU with **64-bit VRAM**, RAM with **DIMM slots**, disks with **S.M.A.R.T.**, TPM, **Secure Boot / VBS**, ReBAR, PCIe link. Plus a **live sensor session** via `LibreHardwareMonitorLib` (the Ring0 driver is opened once, sensors polled every **1.5 s**);
- **Text hardware report** — can be saved or copied;
- **Floating HUD widget** ([`WidgetWindow.xaml`](Views/WidgetWindow.xaml)): CPU / RAM / GPU / disk C: / network ↓↑ telemetry plus **uptime**, and quick actions (Restart Explorer, Purge RAM, Shutdown, Task Manager);
- **Standalone RAM purge** ([`MonitorEngine.PurgeStandbyMemoryAsync()`](Engines/MonitorEngine.cs:196)): `EmptyWorkingSet` across all processes + `NtSetSystemInformation` (FlushModified → PurgeStandby → PurgeLowPriorityStandby) with privilege activation; it reports the **actually freed megabytes** (an honest delta, not "available memory");
- **🧠 Quiet mode**: [`AppActivityCoordinator`](Managers/AppActivityCoordinator.cs:16) is the single source of truth about system state (widget visibility, fullscreen game, session lock, on-battery). It updates **only from events** (`SetWinEventHook`, `SystemEvents`) — no timers. While gaming, telemetry sleeps, no new CLIs are spawned, and update checks are deferred;
- **30 app themes + 30 widget HUD themes** ([`ThemeEngine.cs`](Managers/ThemeEngine.cs)) — from `Cyberpunk 2077`, `Matrix`, `Tron` to `Windows 11 Light` and High-Contrast, with automatic widget theme mapping;
- **Localization** ([`LocalizationManager.cs`](Managers/LocalizationManager.cs)): **Ukrainian / English** with a fallback chain `current → EN → UA → key` and persisted choice;
- **Tray** ([`TrayManager.cs`](Managers/TrayManager.cs)): open the optimizer, show the widget, purge RAM, empty the recycle bin, session hygiene (DNS + clipboard) and full exit;
- **CLI modes**: `--widget` (HUD only), `--silent` (minimized to tray);
- **Auto-update** ([`UpdateManager.cs`](Managers/UpdateManager.cs)): GitHub Releases API checks with throttling (at most once per 24 h) and modular **`.zip` update** with progress;
- **Crash dumps**: global `DispatcherUnhandledException` / `AppDomain.UnhandledException` / `TaskScheduler.UnobservedTaskException` handlers write `crash_*.txt` into the logs folder.

---

## 🏗️ Architecture & Tech Stack

```
MASLOOPTIMIZER (C# / WPF, .NET 8, self-contained, win-x64, modular ZIP)
├── Engines/      # 9 business engines: TweakEngine (core), Cleaner, Debloat,
│                 # Diagnostic, Dns, Startup, Tools, Backup, Monitor
├── Managers/     # 8 services: AppPaths, AppLogger, LocalizationManager,
│                 # SettingsManager, ThemeEngine, AppActivityCoordinator,
│                 # TrayManager, UpdateManager
├── Models/       # TweakModel (Id, ScriptPath, Risk, CheckReg*, RegistryActions…)
├── Views/        # MainWindow (6 tabs), WidgetWindow, DiagnosticWindow,
│                 # SettingsWindow, RestoreWindow, SafetyWindow, LogWindow
├── Languages/    # UA/EN — JSON localization (EmbeddedResource)
├── Scripts/Tweaks/<Category>/*.bat   # 70 .bat tweaks (7 categories × 10)
└── tweaks.bundle.json                # metadata for 70 tweaks (Version 3.1)
```

**Main window — 6 tabs:** `Tweaks` · `DNS` · `Debloat` · `Startup` · `Software` · `Cleanup`.

**Dependencies (NuGet):** `LibreHardwareMonitorLib 0.9.6` (Ring0 sensors) · `System.Management 10.0.2` (WMI) · `System.ServiceProcess.ServiceController 8.0.0` (services).

**Low-level layer (P/Invoke):**

| API                                                                   | DLL                 | Purpose                                          |
| --------------------------------------------------------------------- | ------------------- | ------------------------------------------------ |
| `NtSetSystemInformation`                                              | `ntdll`             | three-phase standby RAM purge                    |
| `GlobalMemoryStatusEx`                                                | `kernel32`          | honest measurement of freed memory               |
| `OpenProcessToken` / `LookupPrivilegeValue` / `AdjustTokenPrivileges` | `advapi32`          | privilege activation for the purge               |
| `EmptyWorkingSet`                                                     | `psapi`             | resetting process working sets                   |
| `SetWinEventHook`                                                     | `user32`            | event-driven fullscreen game detection           |
| `SHQueryRecycleBinEx`                                                 | `shell32`           | recycle bin size before cleanup                  |
| WMI `Win32_*` (`Processor`, `PhysicalMemory`, `VideoController`, `Tpm`, `DiskDrive`…) | `System.Management` | hardware audit, S.M.A.R.T., TPM |

**Code quality:** heavy work runs exclusively through `Task.Run()` with a return to the UI via `Dispatcher`; snapshot semantics for everything that changes (registry, services, DNS, NIC); re-entrancy protection (`lock` / `_busy` / `SemaphoreSlim` — including single-flight in `UpdateManager`); process and SCM handle hygiene in `finally`; an event model with unsubscription (`IWeakEventListener`) so static singletons never hold on to UI objects.

---

## 🔧 Building from Source

**Requirements:** Windows 10 1809+ / 11 (x64), **.NET 8 SDK**, **administrator rights** (`requireAdministrator` in the manifest — the app will not start without UAC).

```powershell
git clone https://github.com/3ibravMasla/MASLOOPTIMIZER.git
cd MASLOOPTIMIZER

# Regular build
dotnet build -c Release --nologo -v q

# Modular self-contained publish (as in releases):
# the runtime is bundled, but Scripts/ stay as real .bat files on disk
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=false -o .\publish\MASLOOPTIMIZER

# 1-click release ZIP packaging
powershell -ExecutionPolicy Bypass -File .\publish-release.ps1
```

> ⚠️ **Why not single-file.** `TweakEngine` launches external `.bat` scripts **from disk** at `AppDomain.CurrentDomain.BaseDirectory\Scripts\Tweaks\...`, and the tweak catalog and languages are loaded as separate files. Hence the release is a **modular folder**, not a single exe.

**CLI modes:**

```powershell
MASLOOPTIMIZER.exe --widget     # HUD widget only (autostart)
MASLOOPTIMIZER.exe --silent     # full app, minimized to tray
```

> ⚠️ The binary is not code-signed — Windows SmartScreen may warn about an "unknown publisher". This is expected for self-built projects.

---

## ⚠️ Known Issues

The project is under active development. Here is an honest technical status for the current version:

1. **Windows 11 24H2 compatibility.** Microsoft blocks/ignores some legacy registry keys (especially `MMCSS`, `SystemResponsiveness`, privacy tweaks). A tweak may report "Applied" (the `Check` succeeds) while producing **no actual effect** on newer builds, due to `HKCU\Software\Policies` policies and Windows Security registry redirects. Expect some UI/Privacy tweaks to be "placebo" on 24H2.

2. **Batch tweak application is not fast.** Each `.bat` runs sequentially in its own `cmd.exe` process with a **30 s** timeout; batch-applying all 70 scripts naturally takes a long time, and `Check` verification for some tweaks additionally invokes `cmd`/`PowerShell`.

3. **Localization is partially hardcoded.** The engines and metadata are fully localized through `LocalizationManager`, but the **XAML window shell** still contains some hardcoded Ukrainian strings that are not re-rendered on language change. There is also a deliberate **"RU → force UA + Lock"** policy — system Russian is intentionally not supported.

4. **Themes are palette swaps only, without deep restyling.** The palette is swapped via `DynamicResource`, but some colors in `MainWindow.xaml`, `WidgetWindow.xaml` and `TrayManager` (WinForms) **are not part of the theme** — rapid theme switching may cause flicker, and the tray menu is always dark.

5. **UWP debloat leaves empty tiles.** Removing deeply embedded packages (especially the XBOX companions and `Zune`) may leave blank shortcuts in the Start menu until `explorer.exe` is restarted. Store-based restoration depends on package availability in the store.

6. **Sorting is not exposed everywhere in the UI.** The `StartupSortMode`, `CleanerSortMode` and `BackupSortMode` enums are implemented in the engines, but sorting switches for those modules are missing from the interface.

7. **The disk analyzer runs on demand.** Heavy scans (`ScanDriveHeavyItemsAsync`) walk the whole file system — on an HDD this can take minutes and noticeably load the disk.

8. **Other trade-offs.** Modular publishing yields ~200 files in the release folder (mitigated by a ZIP with a single root folder); `--silent`/`--widget` do not surface activation errors in the UI; MAS activation depends on a third-party project and may be flagged by antivirus.

---

## 🛣️ Roadmap

Development directions (current tasks live in the repository **Issues**):

- 🔤 **Full XAML shell localization** — move the remaining hardcoded strings into JSON dictionaries;
- 🧩 **Tweak catalog expansion** — new categories and scripts beyond the current 70;
- ⚡ **Faster batch mode** — parallel execution of independent `.bat` scripts while preserving snapshot semantics;
- 🎨 **Colors into the theme** — remove the remaining hardcoded colors from XAML and the WinForms tray;
- ✍️ **Code signing** — to eliminate SmartScreen warnings.

---

## 🤝 Support

- ⭐ **Star the repository** if the project is useful — it is the best motivation!
- 🐛 Report bugs and ideas through **Issues**.
- 🔧 Share your own `.bat` tweaks — the catalog is easy to extend.

Built with love for clean and fast hardware. 🧈

---

## 📜 License

This project is distributed under the **MIT** license (see the [`LICENSE`](LICENSE) file).

> **Reminder:** this is an experimental project for personal use. Using it on production/work machines is **entirely at your own risk**. Always make backups.
