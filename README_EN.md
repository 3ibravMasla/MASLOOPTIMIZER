# 🧈 MASLOOPTIMIZER

![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D4?style=for-the-badge&logo=windows)
![.NET](https://img.shields.io/badge/.NET-8.0%20WPF-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![Version](https://img.shields.io/badge/Release-v0.4.6-00FF9D?style=for-the-badge)
![License](https://img.shields.io/badge/License-MIT-F59E0B?style=for-the-badge)
![Language](https://img.shields.io/badge/Code-C%23-512BD4?style=for-the-badge&logo=csharp)

> 🌐 **English** · [🇺🇦 Українська версія](README.md)

**An open-source all-in-one utility for deep Windows 10/11 tuning, cleaning, diagnostics and maximum optimization.**

A single dashboard that replaces a pile of separate tools: an esports-grade Game Mode, a hardware **MSI** interrupt switcher, 224 system tweaks, a network & DNS optimizer, UWP debloat, a disk cleaner, a startup manager, a software library via `winget`, power profiles, backup (VSS + registry) and deep hardware diagnostics. All built with C# / WPF / .NET 8.

<img width="1919" height="1025" alt="image_2026-08-31_02-00-53" src="https://github.com/user-attachments/assets/eb83c0c7-13fb-4c25-a68c-dced7e049e71" />
<img width="998" height="721" alt="image_2026-08-31_02-01-14" src="https://github.com/user-attachments/assets/60cddf51-1363-4c4b-b5b6-fcaec13f3742" />
<img width="1072" height="896" alt="image_2026-08-31_02-01-46" src="https://github.com/user-attachments/assets/b2324b95-8103-4078-a200-18f7d4596d4f" />

---

## 📑 Table of Contents

1. [⚠️ Disclaimer](#-disclaimer)
2. [🤖 Project Philosophy: Vibe-Coding](#-project-philosophy-vibe-coding)
3. [🚀 Feature Highlights](#-feature-highlights)
4. [🏗️ Architecture & Tech Stack](#️-architecture--tech-stack)
5. [🔧 Building from Source](#-building-from-source)
6. [⚠️ Known Issues](#-known-issues)
7. [🛣️ Roadmap](#️-roadmap)
8. [🤝 Support](#-support)

---

## ⚠️ DISCLAIMER (STRICT LIMITATION OF LIABILITY)

> This program is being developed **exclusively for the author's personal use** as an experimental tool. It performs **deep interference with the operating system core**: it modifies the system registry (`HKLM`/`HKCU`), manages hardware **IRQ/MSI** interrupts on the PCI bus, alters the state of critical services, power plans, process priorities and CPU affinity through low-level NT APIs (`NtSetSystemInformation`, `NtSetInformationProcess`, `NtSetTimerResolution`).
>
> **THE AUTHOR ASSUMES NO RESPONSIBILITY WHATSOEVER FOR ANY CONSEQUENCES OF USING MASLOOPTIMIZER.**
>
> By using this program, you **fully and unconditionally accept all risks**, including:
>
> * 🟥 Critical system errors and **"Blue Screens of Death" (BSOD)**;
> * 🟥 File-system corruption or **irreversible loss of personal data**;
> * 🟥 Performance degradation, hardware instability or **hardware failure**;
> * 🟥 Breaching rules/license agreements of programs and games (VAC, Anti-Cheat) — this is **not a cheat** and works at the OS level, but we do not control anti-cheat policies.

**ALWAYS, before pressing "Apply":**
1. Create a **Windows restore point** — there is a "🛡️ VSS Point" button in the app;
2. Make a **registry export** ("💾 Registry Backup" button);
3. Back up important files.

> **YOU USE THIS SOFTWARE ENTIRELY AT YOUR OWN RISK!** No guarantees of compatibility, stability or data safety.

---

## 🤖 Project Philosophy: Vibe-Coding

There are almost no decent, **free and fully open-source** deep system-tuning utilities on the market that satisfy the needs of power users, gamers and overclocking enthusiasts. So I built my own.

This project is a vivid example of **Vibe-Coding**:

> 🧑💻 **The human is the architect and the visionary.** All ideas, the logic of how Windows works "under the hood", the understanding of which tweaks actually work and which are marketing, and the development vector belong to the human.
>
> 🤖 **The AI is the coder, the refactorer and the UI engineer.** The direct writing of C# syntax, XAML generation, refactoring and minor implementation are done in close collaboration with AI assistants.

This symbiosis brings complex low-level ideas (NT Native API, WMI, MSI/IRQ) into a real, working tool at incredible speed — while keeping the code clean: atomic snapshots, rollback, PID re-use protection, handle hygiene.

🐛 **Found a bug?** I'd really appreciate your feedback. Create **Issues** in this repository if you notice an error, or suggest cool ideas for improvement!

---

## 🚀 Feature Highlights

### 🎮 Game Mode — real-time esports engine (`GameModeEngine.cs`)

Works **on the fly, without a system reboot**, to stabilize 1% lows, raise FPS and reduce input lag:

* **Smart CPU Affinity** — automatic core topology analysis via `GetLogicalProcessorInformationEx`:
  * **AMD Ryzen X3D** → games are pinned exclusively to **CCD0 with 3D V-Cache** (the mask is taken from the first L3 cache);
  * **Intel 12th gen+ (hybrid)** → only **P-Cores** (Efficiency Class ≤ 1);
  * **Classic CPUs** → full mask + forced **Core Parking** disable (`powercfg CPMINCORES = 100`).
* **Audio stack isolation** — the `audiodg.exe` process is raised to **High** priority and pinned to the last logical core (eliminates crackling, DPC latency and audio micro-stutters).
* **Three-phase Standby RAM purge** — native `NtSetSystemInformation(SystemMemoryListInformation)` call: `MemoryFlushModifiedList` → `MemoryPurgeStandbyList` → `MemoryPurgeLowPriorityStandbyList`. The freed amount is measured via the `Standby Cache Core/Normal Priority/Reserve` counters (an honest metric, not "available memory"). Requires `SeProfileSingleProcessPrivilege` + `SeIncreaseQuotaPrivilege` (enabled via `AdjustTokenPrivileges`).
* **Smart Background Demotion** — demotes background apps (Chrome, Edge, Discord, Steam, Telegram, Spotify, Epic, Battle.net, etc.): `BelowNormal` + `IO Low` + **Page Priority = Very Low** (`NtSetInformationProcess`), working set flush (`EmptyWorkingSet`) and shifting them to the last 2–4 cores. Restoration is **exclusively from the state cache**, with **PID re-use** protection (`StartTime` verification).
* **Game Process Boost** — for the foreground game process: `High` priority, `IO High`, **Page Priority = 5**, forced **Power Throttling** disable (`PROCESS_POWER_THROTTLING_STATE`) and affinity to the gaming mask. A continuous **Game Watcher** (`PeriodicTimer`, 3 s) tracks the foreground window via `GetForegroundWindow`/`GetWindowThreadProcessId`.
* **Esports timer** — `NtSetTimerResolution` down to **0.5 ms** (the minimum safe Windows limit), with proper request release on deactivation.
* **Sleep blocking** — `SetThreadExecutionState(ES_SYSTEM_REQUIRED | ES_CONTINUOUS)` on a dedicated Long-Running Task.
* **Critical background services stop** (SysMain, DoSvc, WpnService, wuauserv, Spooler, DiagTrack) with restoration from the `ServiceController` cache.
* **Power Plan** — activates **Ultimate Performance** via `PowerSetActiveScheme` (`powrprof.dll`), falls back to High Performance, or creates the hidden scheme via `powercfg -duplicatescheme` (removed on deactivation).
* **MMCSS / GameBar registry** — `GPU Priority = 8`, `Priority = 6`, `Scheduling Category = High`, `SFIO Priority = High`, `SystemResponsiveness = 0`, `NetworkThrottlingIndex = 0xFFFFFFFF`, auto-enable of the Windows Game Mode.
* **Atomic state manager** — registry snapshot before interference + full rollback on failure (including rollback of already-applied changes).

### ⚡ MSI Utility — hardware interrupts (`MsiEngine.cs`)

A built-in analog of popular MSI tools for resolving PCI-bus conflicts and lowering latency:

* Automatic scanning of **GPU, NVMe, USB, audio and network controllers** (registry-based detection + WMI fallback `Win32_PnPEntity`).
* Switching hardware from slow line-based interrupts (IRQ) to vector mode **Message Signaled Interrupts (MSI)**: writes `MSISupported` + `MessageSignaledInterrupt` + `MessageNumberLimit` + a priority subkey + **affinity mask**.
* Built-in hardware **blacklist** (`VEN_1969` Atheros, `VEN_1102` Creative, `VEN_1B21` ASMedia) — blocks MSI switching for controllers that are guaranteed to cause BSOD.
* Data enrichment: current **IRQ/vector** detection via the `Win32_PnPAllocatedResource → Win32_IRQResource` association, vendor badges, and HID mapping of mice/keyboards via `Win32_USBControllerDevice`.
* **1-Click Gaming MSI** — a smart strategy: MSI+High is applied only to the active network adapter (`Win32_NetworkAdapter WHERE NetEnabled=TRUE`), the system NVMe, the GPU and external USB audio.
* **Full backup & restore** of each device's original MSI state.

### 🛡️ TweakEngine database & JSON presets (`TweakEngine.cs`)

* **224 system tweaks** from the embedded `tweaks.bundle.json` (v2.0), split by risk level:
  * 🟢 **116 Safe** · 🟠 **37 Medium** · 🔴 **16 High** · 🎨 **55 UI**;
* Each tweak is a declarative `RegistryAction` / `ServiceAction` / `CommandAction` structure with **Check/Apply/Restore** logic and a live "Applied / Not applied" status;
* Execution via registry APIs, `ServiceController` and verified `cmd`/`PowerShell` calls with `ExitCode == 0` checks;
* **Export / import of your own JSON optimization profiles** and ready-made 1-Click packs: **⚡ Safe Pack** and **⚡ Safe Maslo Pack**.

### 🌐 Network & DNS Optimizer (`NetworkEngine.cs` + `DnsEngine.cs`)

* **Windows TCP stack**: disables **Nagle's algorithm** (`TCPNoDelay=1`, `TcpAckFrequency=1`, `TcpDelAckTicks=0`, `TcpInitialRTT=300`) per-adapter; `netsh int tcp set global`: `autotuninglevel=normal`, `ecncapability=disabled`, `timestamps=disabled`, `rss=enabled`, `rsc=disabled`, `maxsynretransmissions=2`, **TCP Fast Open**, congestion provider **CTCP (Compound TCP)**;
* **QoS unlock**: removes the 20% bandwidth reservation (`NetworkThrottlingIndex = 0xFFFFFFFF`);
* **NIC power-saving off**: disables **EEE** (Energy Efficient Ethernet), LSO and other adapter latencies — with a snapshot for restoration;
* **Built-in DNS benchmark**: a catalog of 30+ presets (Cloudflare, Google, Quad9, OpenDNS, Neustar, AdGuard, NextDNS, Yandex, Comodo, Level3…) with Speed / Security / Gaming groups, **parallel ping measurement** and 1-Click apply of the fastest one + a backup of the original DNS settings;
* **Reset Network Stack** — resets the stack to the Windows defaults (for cases when the tweaks had a negative effect).

### ⚡ Power & Display profiles (`PowerEngine.cs`)

* Saves and force-applies power plans (Eco / Balanced / High Performance / **Ultra Performance**);
* **On-the-fly monitor refresh rate override** via `EnumDisplaySettingsExA` / `ChangeDisplaySettingsExA`;
* Laptop detection and "smart" skipping of aggressive tweaks on battery.

### 🗑️ Debloat, Cleaning, Startup & Software

* **UWP Debloat** (`DebloatEngine.cs`): removes **29 built-in packages** (Copilot, Cortana, Feedback Hub, Clipchamp, Solitaire, Xbox companions, Zune Music/Video…) via `Remove-AppxPackage`, with restoration through the Microsoft Store (`StoreId`);
* **Deep cleaning** (`CleanerEngine.cs`): **19 categories** — GPU shader caches (DX/NVIDIA/AMD/Intel/Vulkan), temp files, Windows Update cache, CBS logs, Delivery Optimization, Recycle Bin (`SHQUERYRBINFO`), browser caches, Prefetch, `Windows.old`, `WinSxS` via **DISM**, dev caches — with Safe and Advanced modes;
* **Startup manager** (`StartupEngine.cs`): Registry Run (user/system), **scheduled tasks** (`schtasks`) and startup folders;
* **Software library** (`ToolsEngine.cs`): a catalog of **116 utilities** (HWiNFO, CPU-Z, GPU-Z, MSI Afterburner, FanControl, Ryzen Master, PresentMon, QuickCPU, AIDA64…) with **silent install via `winget`**, direct links, VC++ Redist & DirectX Web packages, and a Windows MAS activation helper.

### 🛟 Backup & Rollback (`BackupEngine.cs`)

* **VSS restore point** creation via PowerShell with error handling;
* **Full registry export** (a list of critical keys + `regedit` files) into timestamped folders, **restoration** from them, discovery of backups on secondary drives;
* Browsing, sorting and deleting backups through a dedicated restore window.

### 📊 Diagnostics, HUD Widget & Interface

* **Deep hardware diagnostics** (`DiagnosticEngine.cs`): WMI (`Win32_Processor`, `Win32_PhysicalMemory`, `Win32_VideoController`, `Win32_Tpm`, `Win32_DiskDrive` with **S.M.A.R.T.**), sensors via **LibreHardwareMonitorLib** (CPU/GPU core temps, VRAM, hotspot), **ATI ADL** (`atiadlxx.dll`) for Radeon, **Secure Boot / VBS / TPM** statuses, ReBAR, PCIe link, RAM modules with speed — and **text report generation**;
* **Floating HUD widget** (`WidgetWindow`): **CPU / RAM / GPU** telemetry with a neon design, quick actions (Restart Explorer, Purge RAM, Shutdown, Task Manager);
* **30 app themes + 30 widget HUD themes** (`ThemeEngine.cs`) — from `Cyberpunk 2077`, `Matrix`, `Tron` to `Windows 11 Light` and High-Contrast; automatic widget theme mapping to the app theme;
* **Localization** (`LocalizationManager.cs`): **Ukrainian / English** with a fallback chain `current → EN → UA → key` and saved selection;
* **Tray + CLI modes** (`TrayManager`): minimize to tray, widget autostart (`--widget`) and silent background mode (`--silent`);
* **Auto-update** (`UpdateManager.cs`): new-version checks via the **GitHub Releases API** + download/install with progress;
* **Crash dumps**: global `DispatcherUnhandledException` / `AppDomain.UnhandledException` / `TaskScheduler.UnobservedTaskException` handlers write `crash_*.txt` into the logs folder.

---

## 🏗️ Architecture & Tech Stack

```
MASLOOPTIMIZER (C# / WPF, .NET 8, single-file self-contained, win-x64)
├── Engines/      # 14 business engines: GameMode, Msi, Network, Dns, Tweak, Preset,
│                 # Cleaner, Debloat, Startup, Tools, Power, Diagnostic, Backup, Monitor
├── Managers/     # ThemeEngine, LocalizationManager, SettingsManager, TrayManager,
│                 # UpdateManager, AppLogger, AppPaths
├── Models/       # TweakModel, PowerSnapshotModel...
├── Views/        # MainWindow, WidgetWindow, DiagnosticWindow, SettingsWindow,
│                 # RestoreWindow, SafetyWindow, LogWindow
├── Languages/    # UA/EN — JSON localization (EmbeddedResource)
└── tweaks.bundle.json  # 224 tweaks (EmbeddedResource)
```

**Dependencies (NuGet):** `LibreHardwareMonitorLib 0.9.6` · `System.Management 10.0.2` (WMI) · `System.ServiceProcess.ServiceController 8.0.0`.

**Low-level layer (P/Invoke):**

| API | DLL | Purpose |
|---|---|---|
| `NtSetSystemInformation` | `ntdll` | three-phase Standby RAM purge |
| `NtSetTimerResolution` / `NtQueryTimerResolution` | `ntdll` | 0.5 ms esports timer |
| `NtSetInformationProcess` / `NtQueryInformationProcess` | `ntdll` | IO Priority, Page Priority, Power Throttling |
| `SetThreadExecutionState` | `kernel32` | sleep blocking |
| `GetLogicalProcessorInformationEx` | `kernel32` | core topology (CCD0 / P-Cores / L3) |
| `OpenProcessToken` / `LookupPrivilegeValue` / `AdjustTokenPrivileges` | `advapi32` | privilege enabling |
| `PowerGetActiveScheme` / `PowerSetActiveScheme` | `powrprof` | power plans |
| `EnumDisplaySettingsExA` / `ChangeDisplaySettingsExA` | `user32` | monitor refresh rate |
| `EmptyWorkingSet` | `psapi` | working set flush |
| `GetForegroundWindow` / `GetWindowThreadProcessId` | `user32` | focus watchers |
| `SHQueryRecycleBinEx` | `shell32` | Recycle Bin emptying |
| `ADL_Main_Control_Create` and others | `atiadlxx` | Radeon sensors (temperature, activity) |
| WMI `Win32_*` | `System.Management` | PCI/IRQ, disks, TPM, memory, network |

**Code quality:** heavy operations run exclusively through `Task.Run()` + `Dispatcher.Invoke()` for the UI; snapshot semantics for everything that changes (registry, NIC, services, MSI, processes); PID re-use and re-entrancy protection (`_busy`/`lock`); SCM/process handle hygiene; event model with unsubscription (`IWeakEventListener`).

---

## 🔧 Building from Source

**Requirements:** Windows 10 1809+ / 11 (x64), .NET 8 SDK (for publishing; the build itself is self-contained — no runtime needed), **administrator rights** (`requireAdministrator` in the manifest — the app won't start without UAC).

```powershell
git clone <repo-url>
cd <repo-folder>

# Regular build
dotnet build -c Release --nologo -v q

# Single-file self-contained publish (like in releases)
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```

**CLI modes:**
```powershell
MASLOOPTIMIZER.exe --widget     # launch only the HUD widget (autostart)
MASLOOPTIMIZER.exe --silent     # full program, minimized to tray
```

> ⚠️ The binary is not code-signed — Windows SmartScreen may warn about an "unknown publisher". That's expected for self-built projects.

---

## ⚠️ Known Issues

The project is under active development. Here is the honest technical state as of the current version:

1. **Windows 11 24H2 compatibility.** Microsoft blocks/ignores some legacy registry keys (especially `MMCSS`, `SystemResponsiveness`, privacy tweaks). A tweak may show "Applied" status (the `Check` succeeds) but **have no real effect** on new builds due to `HKCU\Software\Policies` policies and Windows Security "registry redirectors". Expect some UI/Privacy tweaks to be "placebo" on 24H2.

2. **MSI Mode — real BSOD risk.** The blacklist covers only `VEN_1969` (Atheros), `VEN_1102` (Creative), `VEN_1B21` (ASMedia). Other old controllers (SATA RAID, legacy network chips, audio codecs) may misbehave with MSI and "drop out" or cause a blue screen. The MSI Utility writes the driver registry directly and **cannot catch a driver error at load time** — only a state backup before the change.

3. **Localization — partially hardcoded.** The engines are fully localized via `LocalizationManager`, but the **XAML window shell** contains 259 hardcoded Ukrainian strings (`MainWindow.xaml` — 153, `DiagnosticWindow` — 47, `WidgetWindow` — 39, `SettingsWindow` — 12…), some of which are rewritten at runtime and some not. The EN files for `NetworkEngine`/`PresetEngine` are still empty (0 keys). Also, there is a deliberate **"RU → force UA + Lock"** policy — the program intentionally does not support the Russian system language.

4. **Themes — palette swap only, no deep restyling.** The palette is swapped via `DynamicResource`, but ~45 hardcoded colors in `MainWindow.xaml`, `WidgetWindow.xaml`, `TrayManager` (WinForms) and color properties in the engines (`StatusColor`, `VendorBadge` in `MsiEngine`) **are not part of the theme**. Rapid theme switching may cause colors to "flicker", and the tray menu is always dark.

5. **UWP Debloat leaves empty tiles.** Removing deeply embedded packages (especially Xbox companions and `Zune`) may leave empty Start-menu shortcuts until `explorer.exe` is restarted. The "Restore via Store" logic depends on the package being available in the Store.

6. **Sorting is not exposed everywhere in the UI.** The `StartupSortMode`, `CleanerSortMode`, `MsiSortMode` and `BackupSortMode` enums are implemented in the engines, but there are no sorting switchers for these modules in the interface.

7. **Watcher polling architecture.** The Game Watcher polls the foreground window every 3 s and the Focus Watcher every 10 s (`PeriodicTimer`). This is not an event-driven approach: instant reaction to a focus change (e.g., on Alt+Tab) is not guaranteed. **RealTime** priority is deliberately not used (`High` is used instead) to avoid risking system stability.

8. **Other trade-offs.** The single-file self-contained publish increases the exe size; `--silent`/`--widget` modes don't show activation errors in the UI; part of the `CheckTweakStatusNative` checks run `cmd`/`PowerShell` commands, which on slow systems can cause a noticeable delay when refreshing the tweak list.

---

## 🛣️ Roadmap

The development plan is in [`ROADMAP.md`](ROADMAP.md). Key directions: Native AOT/ReadyToRun publish, full XAML shell localization, event-driven watchers, extending the MSI blacklist, moving colors into the theme.

---

## 🤝 Support

* ⭐ **Star the repository** if you find the project useful — it's the best motivation!
* 🐛 Report bugs and ideas via **Issues**.
* 💬 Share your JSON optimization presets.

Made with love for clean and fast hardware. 🧈

---

## 📜 License

The project is distributed under the **MIT** license (see the `LICENSE` file).

> **Reminder:** this is an experimental project for personal use. Using it on production/work machines is **entirely at your own risk**. Always make backups.
