# 🧈 MASLOOPTIMIZER

![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D4?style=for-the-badge&logo=windows)
![.NET](https://img.shields.io/badge/.NET-8.0%20WPF-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![Version](https://img.shields.io/badge/Release-v0.4.6-00FF9D?style=for-the-badge)
![License](https://img.shields.io/badge/License-MIT-F59E0B?style=for-the-badge)
![Language](https://img.shields.io/badge/Code-C%23-512BD4?style=for-the-badge&logo=csharp)

**Комплексна утиліта з відкритим вихідним кодом для глибокого налаштування, очищення, діагностики та максимальної оптимізації Windows 10/11.**

Один дашборд, який замінює пачку окремих тулів: кіберспортивний Game Mode, перемикач апаратних переривань **MSI**, 224 системні твіки, мережевий та DNS-оптимізатор, деблоат UWP, чистильник диска, менеджер автозапуску, бібліотека софту через `winget`, профілі живлення, бекап (VSS + реєстр) та глибока апаратна діагностика. Все це — на C# / WPF / .NET 8.

<img width="1072" height="896" alt="image_2026-08-31_02-01-46" src="https://github.com/user-attachments/assets/b2324b95-8103-4078-a200-18f7d4596d4f" />
<img width="998" height="721" alt="image_2026-08-31_02-01-14" src="https://github.com/user-attachments/assets/60cddf51-1363-4c4b-b5b6-fcaec13f3742" />
<img width="1919" height="1025" alt="image_2026-08-31_02-00-53" src="https://github.com/user-attachments/assets/eb83c0c7-13fb-4c25-a68c-dced7e049e71" />


---

## 📑 Зміст

1. [⚠️ Дисклеймер](#-дисклеймер-сувора-відмова-від-відповідальності)
2. [🤖 Філософія проєкту: Vibe-Coding](#-філософія-проєкту-vibe-coding)
3. [🚀 Розширений функціонал](#-розширений-функціонал)
4. [🏗️ Архітектура та технічний стек](#️-архітектура-та-технічний-стек)
5. [🔧 Збірка з вихідного коду](#-збірка-з-вихідного-коду)
6. [⚠️ Що зараз працює НЕ дуже добре](#-що-зараз-працює-не-дуже-добре-known-issues)
7. [🛣️ Roadmap](#️-roadmap)
8. [🤝 Підтримка проєкту](#-підтримка-проєкту)

---

## ⚠️ ДИСКЛЕЙМЕР (СУВОРА ВІДМОВА ВІД ВІДПОВІДАЛЬНОСТІ)

> Ця програма розробляється **виключно для особистого користування автора** як експериментальний інструмент. Вона здійснює **глибоке втручання в ядро операційної системи**: модифікує системний реєстр (`HKLM`/`HKCU`), керує апаратними перериваннями **IRQ/MSI** на PCI-шині, змінює стан критичних служб, схеми живлення, пріоритети та CPU-affinity процесів через низькорівневі NT API (`NtSetSystemInformation`, `NtSetInformationProcess`, `NtSetTimerResolution`).
>
> **АВТОР НЕ НЕСЕ ЖОДНОЇ ВІДПОВІДАЛЬНОСТІ ЗА БУДЬ-ЯКІ НАСЛІДКИ ВИКОРИСТАННЯ MASLOOPTIMIZER.**
>
> Використовуючи цю програму, ви **повністю та безумовно берете на себе всі ризики**, включно з:
>
> * 🟥 Виникненням критичних помилок системи та **«синіх екранів» смерті (BSOD)**;
> * 🟥 Пошкодженням файлової системи або **неповоротною втратою особистих даних**;
> * 🟥 Зниженням продуктивності, апаратною нестабільністю або **виходом обладнання з ладу**;
> * 🟥 Порушенням правил/ліцензійних угод для програм та ігор (VAC, Anti-Cheat) — це **не чит** і працює на рівні ОС, але політики античитів ми не контролюємо.

**ПЕРЕД натисканням кнопки «Застосувати» ЗАВЖДИ:**
1. Створюйте **точку відновлення Windows** — у програмі є кнопка «🛡️ Точка VSS»;
2. Робіть **експорт реєстру** (кнопка «💾 Бекап реєстру»);
3. Робіть резервні копії важливих файлів.

> **ВИ ВИКОРИСТОВУЄТЕ ЦЕЙ СОФТ ВИКЛЮЧНО НА СВІЙ СТРАХ І РИЗИК!** Жодних гарантій сумісності, стабільності чи безпеки даних.

---

## 🤖 Філософія проєкту: Vibe-Coding

На ринку майже немає гідних, **безкоштовних і повністю відкритих** програм для глибокого налаштування ПК, які задовольняли б потреби power-user'ів, геймерів та ентузіастів оверклоку. Тому я створив власну.

Цей проєкт — яскравий представник підходу **Vibe-Coding**:

> 🧑💻 **Людина — архітектор та ідейний натхненник.** Усі ідеї, логіка роботи Windows «під капотом», розуміння того, які твіки реально працюють, а які — маркетинг, а також вектор розвитку належать людині.
>
> 🤖 **ШІ — кодер, рефактор та UI-інженер.** Безпосереднє написання синтаксису C#, генерація XAML, рефакторинг та дрібна імплементація виконуються у тісній колаборації з AI-асистентами.

Такий симбіоз дозволяє втілювати складні низькорівневі ідеї (NT Native API, WMI, MSI/IRQ) у реальний робочий інструмент із неймовірною швидкістю — і тримати код чистим: атомарні снапшоти, rollback, захист від PID re-use, гігієна дескрипторів.

🐛 **Знайшли баг?** Буду дуже радий фідбеку. Створюйте **Issues** у цьому репозиторії, якщо помітили помилку, або пропонуйте круті ідеї для покращення!

---

## 🚀 Розширений функціонал

### 🎮 Game Mode — кіберспортивний рушій реального часу (`GameModeEngine.cs`)

Працює **«на льоту», без перезавантаження системи**, для стабілізації 1% Low, підвищення FPS та зниження Input Lag:

* **Розумний CPU Affinity** — автоматичний аналіз топології ядер через `GetLogicalProcessorInformationEx`:
  * **AMD Ryzen X3D** → ігри прив'язуються виключно до **CCD0 з 3D V-Cache** (маска береться з першого L3-кешу);
  * **Intel 12+ gen (hybrid)** → тільки **P-Cores** (Efficiency Class ≤ 1);
  * **Класичні CPU** → повна маска + примусове вимкнення **Core Parking** (`powercfg CPMINCORES = 100`).
* **Ізоляція аудіо-стека** — процес `audiodg.exe` переводиться на **High**-пріоритет і фіксується на останньому логічному ядрі (усунення тріску, DPC-затримок та мікрофризів звуку).
* **Трифазне очищення Standby RAM** — нативний виклик `NtSetSystemInformation(SystemMemoryListInformation)`: `MemoryFlushModifiedList` → `MemoryPurgeStandbyList` → `MemoryPurgeLowPriorityStandbyList`. Замір обсягу — через лічильники `Standby Cache Core/Normal Priority/Reserve` (чесна метрика, а не «доступна пам'ять»). Потребує привілеїв `SeProfileSingleProcessPrivilege` + `SeIncreaseQuotaPrivilege` (активуються через `AdjustTokenPrivileges`).
* **Smart Background Demotion** — демоція фонових застосунків (Chrome, Edge, Discord, Steam, Telegram, Spotify, Epic, Battle.net тощо): `BelowNormal` + `IO Low` + **Page Priority = Very Low** (`NtSetInformationProcess`), скидання робочого набору (`EmptyWorkingSet`) та зміщення на останні 2–4 ядра. Відновлення — **виключно з кешу стану**, із захистом від **PID re-use** (звірка `StartTime`).
* **Game Process Boost** — ігровому процесу у фокусі: `High`-пріоритет, `IO High`, **Page Priority = 5**, примусове вимкнення **Power Throttling** (`PROCESS_POWER_THROTTLING_STATE`) та affinity на ігрову маску. Continuous **Game Watcher** (`PeriodicTimer`, 3 с) відстежує foreground-вікно через `GetForegroundWindow`/`GetWindowThreadProcessId`.
* **Кіберспортивний таймер** — `NtSetTimerResolution` на **0.5 мс** (мінімальний безпечний ліміт Windows), коректне зняття запиту при деактивації.
* **Блокування сну** — `SetThreadExecutionState(ES_SYSTEM_REQUIRED | ES_CONTINUOUS)` на виділеному Long-Running Task.
* **Зупинка критичних фонових служб** (SysMain, DoSvc, WpnService, wuauserv, Spooler, DiagTrack) з відновленням з кешу `ServiceController`.
* **Power Plan** — активація **Ultimate Performance** через `PowerSetActiveScheme` (`powrprof.dll`), fallback High Performance, або створення схеми через `powercfg -duplicatescheme` (прихована схема) з видаленням при деактивації.
* **MMCSS / GameBar реєстр** — `GPU Priority = 8`, `Priority = 6`, `Scheduling Category = High`, `SFIO Priority = High`, `SystemResponsiveness = 0`, `NetworkThrottlingIndex = 0xFFFFFFFF`, авто-вмикання Windows Game Mode.
* **Атомарний state-manager** — снапшот реєстру перед втручанням + повний rollback при збої (у тому числі «відкат вже застосованого»).

### ⚡ MSI Utility — апаратні переривання (`MsiEngine.cs`)

Вбудований аналог популярних MSI-інструментів для усунення конфліктів на шині PCI та зниження затримок:

* Автоматичне сканування **GPU, NVMe, USB, аудіо та мережевих контролерів** (детект через реєстр + WMI-фолбек `Win32_PnPEntity`).
* Переведення обладнання з повільних line-based переривань (IRQ) у векторний режим **Message Signaled Interrupts (MSI)**: запис `MSISupported` + `MessageSignaledInterrupt` + `MessageNumberLimit` + субключ пріоритету + **affinity mask**.
* Вбудований апаратний **«Чорний список»** (`VEN_1969` Atheros, `VEN_1102` Creative, `VEN_1B21` ASMedia) — блокує зміну MSI для контролерів, які гарантовано викликають BSOD.
* Збагачення даних: визначення поточного **IRQ/вектора** через асоціацію `Win32_PnPAllocatedResource → Win32_IRQResource`, підстановка вендор-бейджів та HID-мапінг мишей/клавіатур через `Win32_USBControllerDevice`.
* **1-Click Gaming MSI** — розумна стратегія: MSI+High застосовується лише до активного мережевого адаптера (`Win32_NetworkAdapter WHERE NetEnabled=TRUE`), системного NVMe, GPU та зовнішнього USB-аудіо.
* **Повний бекап і відновлення** початкового MSI-стану кожного пристрою.

### 🛡️ База твіків TweakEngine та JSON-пресети (`TweakEngine.cs`)

* **224 системні твіки** з вшитого `tweaks.bundle.json` (v2.0), розподілені за рівнями ризику:
  * 🟢 **116 Safe** · 🟠 **37 Medium** · 🔴 **16 High** · 🎨 **55 UI**;
* Кожен твік — це декларативна структура `RegistryAction` / `ServiceAction` / `CommandAction` з **Check/Apply/Restore** логікою та живим статусом «Застосовано / Ні»;
* Виконання через реєстр-API, `ServiceController` та перевірені `cmd`/`PowerShell` виклики з контролем `ExitCode == 0`;
* **Експорт / імпорт власних JSON-профілів** оптимізації та готові 1-Click паки: **⚡ Safe Pack** і **⚡ Safe Maslo Pack**.

### 🌐 Network & DNS Optimizer (`NetworkEngine.cs` + `DnsEngine.cs`)

* **TCP-стек Windows**: вимкнення **алгоритму Nagle** (`TCPNoDelay=1`, `TcpAckFrequency=1`, `TcpDelAckTicks=0`, `TcpInitialRTT=300`) пер-адаптерно; `netsh int tcp set global`: `autotuninglevel=normal`, `ecncapability=disabled`, `timestamps=disabled`, `rss=enabled`, `rsc=disabled`, `maxsynretransmissions=2`, **TCP Fast Open**, congestion provider **CTCP (Compound TCP)**;
* **QoS розблокування**: зняття резервування 20% каналу (`NetworkThrottlingIndex = 0xFFFFFFFF`);
* **NIC power-saving off**: вимкнення **EEE** (Energy Efficient Ethernet), LSO та інших затримок адаптера — зі снапшотом для відновлення;
* **Вбудований DNS-бенчмарк**: каталог з 30+ пресетів (Cloudflare, Google, Quad9, OpenDNS, Neustar, AdGuard, NextDNS, Yandex, Comodo, Level3…) з групами Speed / Security / Gaming, **паралельний замір пінгу** та 1-Click застосування найшвидшого + резервна копія оригінальних DNS;
* **Reset Network Stack** — скидання стека до стану Windows (для випадку, якщо твіки дали негативний ефект).

### ⚡ Профілі живлення та дисплей (`PowerEngine.cs`)

* Збереження та примусове застосування схем живлення (Eco / Balanced / High Performance / **Ultra Performance**);
* **Зміна частоти оновлення монітора** на льоту через `EnumDisplaySettingsExA` / `ChangeDisplaySettingsExA`;
* Детект ноутбуків та «розумний» пропуск агресивних твіків на батареї.

### 🗑️ Debloat, Очищення, Автозапуск та Софт

* **Debloat UWP** (`DebloatEngine.cs`): видалення **29 вбудованих пакетів** (Copilot, Cortana, Feedback Hub, Clipchamp, Solitaire, Xbox-компаньйони, Zune Music/Video…) через `Remove-AppxPackage`, з відновленням через Microsoft Store (`StoreId`);
* **Глибоке очищення** (`CleanerEngine.cs`): **19 категорій** — кеш шейдерів GPU (DX/NVIDIA/AMD/Intel/Vulkan), temp-файли, кеш Windows Update, логи CBS, Delivery Optimization, кошик (`SHQUERYRBINFO`), кеші браузерів, Prefetch, `Windows.old`, `WinSxS` через **DISM**, кеші розробника — з безпечним та розширеним режимом;
* **Менеджер автозапуску** (`StartupEngine.cs`): Registry Run (user/system), **заплановані завдання** (`schtasks`) та папки автозапуску;
* **Бібліотека софту** (`ToolsEngine.cs`): каталог з **116 утиліт** (HWiNFO, CPU-Z, GPU-Z, MSI Afterburner, FanControl, Ryzen Master, PresentMon, QuickCPU, AIDA64…) з **тихим встановленням через `winget`**, прямими лінками, пакетами VC++ Redist та DirectX Web, а також MAS-активацією Windows.

### 🛟 Backup & Rollback (`BackupEngine.cs`)

* Створення **точки відновлення VSS** через PowerShell з обробкою помилок;
* **Повний експорт реєстру** (список критичних ключів + `regedit`-файли) у timestamp-папки, **відновлення** з них, пошук бекапів на вторинних дисках;
* Перегляд, сортування та видалення бекапів через окреме вікно відновлення.

### 📊 Діагностика, HUD-віджет та інтерфейс

* **Глибока апаратна діагностика** (`DiagnosticEngine.cs`): WMI (`Win32_Processor`, `Win32_PhysicalMemory`, `Win32_VideoController`, `Win32_Tpm`, `Win32_DiskDrive` з **S.M.A.R.T.**), сенсори через **LibreHardwareMonitorLib** (температури ядер CPU/GPU, VRAM, hotspot), **ATI ADL** (`atiadlxx.dll`) для Radeon, статуси **Secure Boot / VBS / TPM**, ReBAR, PCIe-лінк, модулі RAM зі швидкістю — і **генерація текстового звіту**;
* **Плаваючий HUD-віджет** (`WidgetWindow`): телеметрія **CPU / RAM / GPU** з неоновим дизайном, швидкі дії (Restart Explorer, Purge RAM, Shutdown, Task Manager);
* **30 тем застосунку + 30 HUD-тем віджета** (`ThemeEngine.cs`) — від `Cyberpunk 2077`, `Matrix`, `Tron` до `Windows 11 Light` та High-Contrast; авто-мапінг теми віджета під тему застосунку;
* **Локалізація** (`LocalizationManager.cs`): **Українська / Англійська** з fallback-ланцюгом `поточна → EN → UA → ключ` та збереженням вибору;
* **Tray + CLI-режими** (`TrayManager`): згортання в трей, автозапуск віджета (`--widget`) та тихий фоновий режим (`--silent`);
* **Auto-Update** (`UpdateManager.cs`): перевірка нових версій через **GitHub Releases API** + завантаження/встановлення з прогресом;
* **Crash-dumps**: глобальні хендлери `DispatcherUnhandledException` / `AppDomain.UnhandledException` / `TaskScheduler.UnobservedTaskException` пишуть `crash_*.txt` у папку логів.

---

## 🏗️ Архітектура та технічний стек

```
MASLOOPTIMIZER (C# / WPF, .NET 8, single-file self-contained, win-x64)
├── Engines/      # 14 бізнес-рушіїв: GameMode, Msi, Network, Dns, Tweak, Preset,
│                 # Cleaner, Debloat, Startup, Tools, Power, Diagnostic, Backup, Monitor
├── Managers/     # ThemeEngine, LocalizationManager, SettingsManager, TrayManager,
│                 # UpdateManager, AppLogger, AppPaths
├── Models/       # TweakModel, PowerSnapshotModel...
├── Views/        # MainWindow, WidgetWindow, DiagnosticWindow, SettingsWindow,
│                 # RestoreWindow, SafetyWindow, LogWindow
├── Languages/    # UA/EN — JSON-локалізація (EmbeddedResource)
└── tweaks.bundle.json  # 224 твіки (EmbeddedResource)
```

**Залежності (NuGet):** `LibreHardwareMonitorLib 0.9.6` · `System.Management 10.0.2` (WMI) · `System.ServiceProcess.ServiceController 8.0.0`.

**Низькорівневий шар (P/Invoke):**

| API | DLL | Для чого |
|---|---|---|
| `NtSetSystemInformation` | `ntdll` | трифазний purge Standby RAM |
| `NtSetTimerResolution` / `NtQueryTimerResolution` | `ntdll` | кіберспортивний таймер 0.5 мс |
| `NtSetInformationProcess` / `NtQueryInformationProcess` | `ntdll` | IO Priority, Page Priority, Power Throttling |
| `SetThreadExecutionState` | `kernel32` | блокування сну |
| `GetLogicalProcessorInformationEx` | `kernel32` | топологія ядер (CCD0 / P-Cores / L3) |
| `OpenProcessToken` / `LookupPrivilegeValue` / `AdjustTokenPrivileges` | `advapi32` | активація привілеїв |
| `PowerGetActiveScheme` / `PowerSetActiveScheme` | `powrprof` | схеми живлення |
| `EnumDisplaySettingsExA` / `ChangeDisplaySettingsExA` | `user32` | частота монітора |
| `EmptyWorkingSet` | `psapi` | скидання робочого набору |
| `GetForegroundWindow` / `GetWindowThreadProcessId` | `user32` | фокус-вотчери |
| `SHQueryRecycleBinEx` | `shell32` | очищення кошика |
| `ADL_Main_Control_Create` та ін. | `atiadlxx` | датчики Radeon (температура, активність) |
| WMI `Win32_*` | `System.Management` | PCI/IRQ, диски, TPM, пам'ять, мережа |

**Якість коду:** важкі операції — виключно через `Task.Run()` + `Dispatcher.Invoke()` для UI; снапшот-семантика для всього, що змінюється (реєстр, NIC, служби, MSI, процеси); захист від PID re-use та повторного входу (`_busy`/`lock`); гігієна SCM/процесних хендлів; подієва модель з відписками (`IWeakEventListener`).

---

## 🔧 Збірка з вихідного коду

**Вимоги:** Windows 10 1809+ / 11 (x64), .NET 8 SDK (для публікації; сам білд — self-contained, рантайм не потрібен), **права адміністратора** (`requireAdministrator` у маніфесті — програма не запуститься без UAC).

```powershell
git clone <repo-url>
cd <repo-folder>

# Звичайна збірка
dotnet build -c Release --nologo -v q

# Single-file self-contained публікація (як у релізах)
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```

**CLI-режими:**
```powershell
MASLOOPTIMIZER.exe --widget     # запуск тільки HUD-віджета (автозапуск)
MASLOOPTIMIZER.exe --silent     # повна програма, згорнута в трей
```

> ⚠️ Бінарник не підписаний кодом — Windows SmartScreen може попередити про «невідомого видавця». Це очікувано для самозбірних проєктів.

---

## ⚠️ Що зараз працює НЕ дуже добре (Known Issues)

Проєкт активно допрацьовується. Нижче — чесний технічний стан на поточну версію:

1. **Сумісність Windows 11 24H2.** Microsoft блокує/ігнорує частину legacy-ключів реєстру (особливо `MMCSS`, `SystemResponsiveness`, твіки конфіденційності). Твік може мати статус «Застосовано» (перевірка `Check` успішна), але **не давати фактичного ефекту** на нових збірках через політики `HKCU\Software\Policies` та «реєстрові редиректори» Windows Security. Очікуйте, що частина UI/Privacy-твіків на 24H2 — «плацебо».

2. **MSI Mode — реальний ризик BSOD.** Чорний список покриває лише `VEN_1969` (Atheros), `VEN_1102` (Creative), `VEN_1B21` (ASMedia). Інші старі контролери (SATA RAID, застарілі мережеві чини, аудіо-кодеки) можуть некоректно поводитися з MSI і «відвалитися» або дати синій екран. MSI Utility працює з реєстром драйвера напряму і **не може перехопити помилку драйвера в момент завантаження** — тільки бекап стану до зміни.

3. **Локалізація — частково захардкоджена.** Рушії (Engines) повністю локалізовані через `LocalizationManager`, але **оболонка XAML-вікон** містить 259 хардкод-рядків українською (`MainWindow.xaml` — 153, `DiagnosticWindow` — 47, `WidgetWindow` — 39, `SettingsWindow` — 12…), частина з яких перезаписується в runtime, а частина — ні. EN-файли `NetworkEngine`/`PresetEngine` поки порожні (0 ключів). Крім того, діє політика **«RU → примусово UA + Lock»** — системну російську мову програма навмисно не підтримує.

4. **Теми — лише заміна палітри, без глибокого рестайлінгу.** Палітра обмінюється через `DynamicResource`, але ~45 хардкод-кольорів у `MainWindow.xaml`, `WidgetWindow.xaml`, `TrayManager` (WinForms) та кольорові властивості в Engines (`StatusColor`, `VendorBadge` у `MsiEngine`) **не входять у тему**. При швидкому перемиканні тем можливе «перемигування» кольорів, а трей-меню завжди темне.

5. **Debloat UWP залишає порожні плитки.** Видалення глибоко вбудованих пакетів (особливо XBOX-компаньйонів та `Zune`) може лишати порожні ярлики в меню «Пуск» до перезапуску `explorer.exe`. Реєстр «Restore-через-Store» залежить від доступності пакета в магазині.

6. **Сортування не всюди виведено в UI.** Enums `StartupSortMode`, `CleanerSortMode`, `MsiSortMode`, `BackupSortMode` реалізовані в рушіях, але перемикачі сортування для цих модулів відсутні в інтерфейсі.

7. **Polling-архітектура вотчерів.** Game Watcher опитує foreground-вікно кожні 3 с, Focus Watcher — кожні 10 с (`PeriodicTimer`). Це не event-driven підхід: миттєва реакція на зміну фокуса (наприклад, при Alt+Tab) не гарантується. Свідомо не використовується **RealTime**-пріоритет (використовується `High`) — щоб не ризикувати стабільністю системи.

8. **Інші компроміси.** Single-file self-contained публікація збільшує розмір exe; `--silent`/`--widget` режими не показують помилки активації у UI; частина перевірок `CheckTweakStatusNative` виконує `cmd`/`PowerShell`-команди, що на повільних системах може створювати помітну затримку при оновленні списку твіків.

---

## 🛣️ Roadmap

План розвитку — у [`ROADMAP.md`](ROADMAP.md). Ключові напрями: Native AOT/ReadyToRun публікація, повна локалізація XAML-оболонки, event-driven вотчери, розширення чорного списку MSI, винесення кольорів у тему.

---

## 🤝 Підтримка проєкту

* ⭐ **Поставте зірочку репозиторію**, якщо проєкт корисний — це найкраща мотивація!
* 🐛 Повідомляйте про баги та ідеї через **Issues**.
* 💬 Діліться своїми JSON-пресетами оптимізації.

Розроблено з любов'ю до чистого та швидкого заліза. 🧈

---

## 📜 Ліцензія

Проєкт поширюється за ліцензією **MIT** (див. файл `LICENSE`).

> **Нагадування:** це експериментальний проєкт для особистого користування. Використання на бойових/робочих машинах — **виключно на ваш страх і ризик**. Завжди робіть бекапи.
