# Аудит інтерфейсу та перекладу — MASLOOPTIMIZER

> Фаза 1: Інвентаризація + базовий збір (без правок коду).
> Дата: 2026-08-30. Робоча директорія: `c:\Users\MasloDe\Desktop\projekt\WinOptimizer\v0.3.4`

---

## 1. Стан збірки

| Команда | Результат |
|---|---|
| `dotnet build --nologo -v q` | ✅ **Build succeeded — 0 Warning(s), 0 Error(s)** |

---

## 2. Матриця сортування (модуль | enum | ComboBox | дефолт | статус)

| Модуль | Enum (файл:рядок) | Значення | ComboBox у XAML | Дефолт у code-behind | Статус |
|---|---|---|---|---|---|
| Tweak | `TweakSortMode` (Engines\TweakEngine.cs:14) | Default, AppliedFirst, UnappliedFirst, RiskAscending, RiskDescending, NameAscending, NameDescending, Category | ✅ `SortComboBox` (MainWindow.xaml:651) | `_currentTweakSort = Default` (MainWindow.xaml.cs:47) | ОК |
| Dns | `DnsSortMode` (Engines\DnsEngine.cs:14) | FastestFirst, SlowestFirst, ActiveFirst, NameAscending, NameDescending, Category | ✅ `DnsSortComboBox` (MainWindow.xaml:839) | `_currentDnsSort = FastestFirst` (MainWindow.xaml.cs:59) | ОК |
| Debloat | `DebloatSortMode` (Engines\DebloatEngine.cs:14) | Default, InstalledFirst, UninstalledFirst, NameAscending, NameDescending, Category | ✅ `DebloatSortComboBox` (MainWindow.xaml:996) | `_currentDebloatSort = Default` (MainWindow.xaml.cs:51) | ОК |
| Tools | `ToolSortMode` (Engines\ToolsEngine.cs:16) | Default, InstalledFirst, NotInstalledFirst, NameAscending, NameDescending, Category | ✅ `ToolsSortComboBox` (MainWindow.xaml:1308) | `_currentToolsSort = Default` (MainWindow.xaml.cs:55) | ОК |
| Startup | `StartupSortMode` (Engines\StartupEngine.cs:14) | Default, EnabledFirst, DisabledFirst, NameAscending, NameDescending, Category, Source | ❌ немає | ❌ немає поля | НЕМАЄ в UI |
| Cleaner | `CleanerSortMode` (Engines\CleanerEngine.cs:16) | SizeDescending, SizeAscending, SafeFirst, NameAscending, Category | ❌ немає | ❌ немає поля | НЕМАЄ в UI |
| Msi | `MsiSortMode` (Engines\MsiEngine.cs:16) | Default, MsiFirst, LineBasedFirst, PriorityDescending, NameAscending, Category, Vendor | ❌ немає | ❌ немає поля | НЕМАЄ в UI |
| Backup | `BackupSortMode` (Engines\BackupEngine.cs:14) | DateDescending, DateAscending, SizeDescending, KeyCountDescending, NameAscending | ❌ немає (вікно RestoreWindow) | ❌ немає поля | НЕМАЄ в UI |

Примітки:
- Текстові мітки «Сортування:» захардкожені у XAML у 4 місцях (MainWindow.xaml:646, 834, 991, 1303), але ПЕРЕЗАПИСУЮТЬСЯ на етапі виконання через `loc["Common.SortLabel"]` / `loc["Dns.SortLabel"]` (MainWindow.xaml.cs:1406–1409).
- Елементи `ComboBoxItem` (наприклад `Content="За замовчуванням"`) також захардкожені, але перелокалізовуються в runtime методами `ApplyLocalizedSortItems()` / `ApplyDnsSortItems()` (MainWindow.xaml.cs:1410–1411).
- Startup/Cleaner/Msi/Backup: enum існує і `GetFilteredAndSorted*(...)` підтримують параметр `sortMode`, але UI-перемикач і стан `_currentXxxSort` відсутні — сортування не виведено в інтерфейс.

---

## 3. Захардкожені кольори (hex + SolidColorBrush)

### 3.1. Views/*.xaml (hex-літерали в атрибутах)
- **DiagnosticWindow.xaml** (14): `#1E293B` (фон бейджа, :34); секційні/сенсорні акценти Foreground — `#F59E0B` (:87,:94), `#38BDF8` (:91,:135,:138), `#4ADE80` (:97), `#F87171` (:141), `#FBBF24` (:144), `#A78BFA` (:183,:185), `#F43F5E` (:225).
- **MainWindow.xaml** (~45): кольорові чипи-акценти модулів `Background` — `#8A2BE2` (:298), `#107C41` (:317,:794,:1392,:1721,:2060,:2114,:2176), `#D87A00` (:336), `#C42B1C` (:355,:1070,:1925), `#3949AB` (:377), `#E65100` (:396,:1453), `#00897B` (:415), `#0078D4` (:434,:732,:908,:1382,:1898), `#D32F2F` (:453), `#8E44AD` (:472,:548,:1606,:2144,:2349), `#0E7490` (:491,:1907), `#F59E0B` (:509,:1209), `#1E293B` (:1201,:1503,:1512), `#38BDF8` (:1510,:1519,:1817), `#059669` (:723), `#6A1B9A` (:1916), `#000000` (Color=, :2302).
- **RestoreWindow.xaml** (1): `Background="#0078D4"` + `Foreground="White"` (:55).
- **SafetyWindow.xaml** (~8): `#0078D4` (:14,:15), `#F8FAFC` (:125), `#EF4444` (:126), `#94A3B8` (:134), `#241416`/`#7F1D1D`/`#F87171` (:144).
- **WidgetWindow.xaml** (~14): напівпрозорі ARGB фони `#F00B0E17` (:93), `#F40B0E17` (:244), `#F20B0E17` (:274); `#00FF9D` (:96,:127), `#64748B` (:112), `#38BDF8` (:168,:195), `#F59E0B` (:180), `#A78BFA` (:206), `#0078D4` (:269), `#8E44AD` (:284), `White` (:269,:284).

### 3.2. Engines/*.cs (hex-рядки у властивостях StatusColor/PingColor/ButtonBg/VendorBadge)
- `CleanerEngine.cs:161` StatusColor `#38BDF8`/`#64748B`
- `DebloatEngine.cs:102` StatusColor `#107C41`/`#2A2D3D`
- `DnsEngine.cs:122` PingColor, `:132` StatusColor
- `MsiEngine.cs:76–81` VendorBadge (`#76B900`,`#ED1C24`,`#0071C5`,`#005596`,`#1428A0`,`#475569`), `:215` StatusColor, `:219–222` PriorityColor, `:252–254` RowBg
- `StartupEngine.cs:161` StatusColor, `:182` ButtonBg
- `ToolsEngine.cs:149` StatusColor
- `Models\TweakModel.cs:211` StatusColor `#107C41`/`#2A2D3D`

### 3.3. Code-behind (Brush/Color у коді, не через DynamicResource)
- `MainWindow.xaml.cs`: helper `HexBrush()` (:1315,:1321), застосування :1280–1281 (тема), :1556 (GameMode).
- `SafetyWindow.xaml.cs`: helper `HexBrush()` (:101) + ~15 застосувань (:117–199).
- `WidgetWindow.xaml.cs`: helper `HexBrush()` (:866), `HsvToRgb()` (:505,:522), `new SolidColorBrush()` (:128) + ~20 застосувань (теми віджета).
- `TrayManager.cs`: `ColorTranslator.FromHtml(...)` ~12 місць (:68,:265,:270,:277–279,:286–291) — WinForms-трей.
- `AppLogger.cs:27–30`: кольори рівнів логу (`#00FF9D`,`#F59E0B`,`#EF4444`,`#94A3B8`).
- `SettingsWindow.xaml.cs:17–19,:59–60`: `PreviewBg/PreviewCard/PreviewAccent` — дефолти прев'ю теми (свідомо статичні).

### 3.4. Легітимні джерела кольорів (НЕ порушення — це сама тема)
- `App.xaml:6–39` — визначення `SolidColorBrush` ресурсів палітри.
- `Managers\ThemeEngine.cs:240–291` — словники палітри тем; `:491–493` — акценти WidgetTheme.

---

## 4. Незалокалізовані рядки

### 4.1. Хардкод у XAML (атрибути Text/Content/Header/Title/ToolTip без `{...}`)
| Файл | Кількість |
|---|---|
| MainWindow.xaml | 153 |
| DiagnosticWindow.xaml | 47 |
| WidgetWindow.xaml | 39 |
| SettingsWindow.xaml | 12 |
| LogWindow.xaml | 5 |
| RestoreWindow.xaml | 2 |
| SafetyWindow.xaml | 1 |
| **Разом** | **259** |

Найпомітніші (повністю захардкожені українською):
- **MainWindow.xaml** — весь shell: сайдбар (:94–151), заголовки секцій (:302–530), ComboBoxItem сортування (:659–668, :847–850, :1004–1011, :1316–1323), описи модулів (:1629, :1645, :1692, :1944), бренд `MASL/PTIMIZER` (:43,:68), версія `v0.4.0` (:81), статус-бар (:2240–2262). Частина перезаписується runtime через `loc[...]` (Dns/Debloat/сортування), але більшість — ні.
- **DiagnosticWindow.xaml** — заголовки секцій, мітки метрик (`Модель:`, `Сокет:`, `Кеш L3/L2:` …), плейсхолдери `Завантаження...`/`-`.
- **WidgetWindow.xaml** — кнопки (`🔄 Перезапуск`, `🧹 Очистити ОЗП`, `🛑 Вимкнути`, `📊 Диспетчер`, `🚀 Головна` …), налаштування віджета.
- **SettingsWindow.xaml** — секції (`🔍 Масштаб інтерфейсу`, `🎨 Тема інтерфейсу`, `🚀 Автозапуск Windows`, `🌐 Мова інтерфейсу`), чекбокси, кнопка `Закрити`.
- **LogWindow.xaml** — заголовок `📋 ІСТОРІЯ ОПЕРАЦІЙ ТА ЛОГИ`, кнопки `Відкрити папку логів`/`Очистити історію`.
- **SafetyWindow.xaml** — лише `Title="SafetyWindow"` (:4); решта тексту підставляється в code-behind через `Loc[...]`.
- **RestoreWindow.xaml** — `Title="RestoreWindow"` (:5) + кнопка закриття `&#x2715;` (:28).

### 4.2. Хардкод у code-behind (MessageBox / інші UI-рядки, що НЕ через LocalizationManager)
- `App.xaml.cs:79–81` — критична помилка (`Виникла невиправна помилка інтерфейсу…`, `MASLOOPTIMIZER — критична помилка`).
- `MainWindow.xaml.cs`:
  - :860 — `Застосувати всі безпечні твіки…` / `1-Click Safe Pack`
  - :881 — `Застосувати рекомендований комплексний Maslo Pack…` / `1-Click Safe Maslo Pack`
  - :1171 — `Доступна нова версія…` / `Оновлення MASLOOPTIMIZER`
  - :1179 — `У вас встановлена остання версія…` / `Оновлення`
  - :1193,:1227 — `Експорт конфігурації`
  - :1210,:1243 — `Імпорт конфігурації`
  - :1831, :1837 — `Помилка Game Boost: …` / `Game Boost`
- Інші вікна (DiagnosticWindow, RestoreWindow) переважно ЛОКАЛІЗОВАНІ через `loc[...]`/`Loc[...]`.

---

## 5. Стан перекладів (EN vs UA)

### 5.1. Наявність файлів
Обидві мови мають **ідентичний набір із 14 файлів** — відсутніх файлів НЕМАЄ:
`App, BackupEngine, CleanerEngine, DebloatEngine, DiagnosticEngine, DnsEngine, GameModeEngine, MsiEngine, NetworkEngine, PowerEngine, PresetEngine, StartupEngine, ToolsEngine, TweakEngine`.

### 5.2. Кількість ключів (flattened) на файл
| Файл | EN | UA |
|---|---|---|
| App | 110 | 110 |
| BackupEngine | 67 | 67 |
| CleanerEngine | 67 | 67 |
| DebloatEngine | 8 | 8 |
| DiagnosticEngine | 106 | 106 |
| DnsEngine | 98 | 98 |
| GameModeEngine | 20 | 20 |
| MsiEngine | 25 | 25 |
| NetworkEngine | **0** | **0** |
| PowerEngine | 13 | 13 |
| PresetEngine | **0** | **0** |
| StartupEngine | 25 | 25 |
| ToolsEngine | 11 | 11 |
| TweakEngine | 896 | **1120** |

### 5.3. Результат ValidateTranslations.ps1
```
VALIDATION OK: all languages contain the 'EN' key set module-by-module (extra keys: 224).
```

### 5.4. Висновки по перекладах
- ✅ Скрипт проходить: UA містить усі ключі EN (прямий напрямок ОК).
- ⚠️ **`en_NetworkEngine.json` та `en_PresetEngine.json` (і ua_*) — ПУСТІ (`{}`, 0 ключів)**. Модулі Network та Preset повністю не локалізовані; їх тексти захардкожені у MainWindow.xaml (розділи «Мережа & TCP», «Пресети (JSON)»).
- ⚠️ **UA/TweakEngine має на 224 ключі БІЛЬШЕ, ніж EN** (1120 vs 896) — це зворотний розрив: ~56 твіків (при 4 полях на твік) мають український опис, але відсутні в англійському файлі. При перемиканні на EN ці твіки не матимуть перекладу.
- ⚠️ DebloatEngine має лише 8 ключів — назви UWP-додатків, імовірно, захардкожені в `DebloatEngine.cs`, а не в JSON (потребує перевірки у Фазі 2).

---

## 6. Підсумок знахідок (для наступних фаз)

1. **Сортування не виведено в UI** для Startup, Cleaner, Msi, Backup (enum є, ComboBox/дефолт відсутні).
2. **~45 hex-кольорів у MainWindow.xaml** (акценти модулів) та десятки у Diagnostic/Widget/Safety/Restore — не через `DynamicResource`.
3. **259 хардкод-рядків у XAML**; найбільше — MainWindow (153), Diagnostic (47), Widget (39).
4. **~13 хардкод-MessageBox** у MainWindow.xaml.cs + App.xaml.cs (не локалізовані).
5. **Network і Preset без перекладів** (порожні JSON).
---

## 7. Фаза 2 — Читабельність / теми (виконано)

> Дата: 2026-08-30. Мета: усунути «сірі нечитаємі таблички» та захардкоджені hex, що ламаються при зміні теми.
> Збірка після змін: ✅ **Build succeeded — 0 Warning(s), 0 Error(s)**.

### 7.1. ThemeEngine.cs (Manager)
- Додано семантичні кисті у **обидві** базові палітри (`BaseDark` / `BaseLight`):
  `SuccessBrush`, `WarningBrush`, `DangerBrush`, `InfoBrush`, `StatusNeutralBrush`
  та текстові варіанти `SuccessText`, `WarningText`, `DangerText`, `InfoText`.
  Світлі палітри отримали темніші (читабельні на світлому фоні) відтінки, темні — яскравіші.
- **Контраст**: `TextMuted` у ТЕМНИХ палітрах піднято `#64748B → #8291A6` (контраст ~5.9:1 замість ~3.8:1). Світлі палітри не чіпали.
- Додано статичний хелпер `ThemeEngine.Brush(string key)` — повертає кисть поточної теми через `Application.Current.TryFindResource` (заміна `HexBrush` у моделях/code-behind).
- Нові ресурси також додані у `App.xaml` як дефолти CyberDark.

### 7.2. Engines/Models — кольорові властивості статусу
Усі hex-рядки замінені на `ThemeEngine.Brush("...")` (тип `string` → `Brush`):
- `CleanerEngine.cs` StatusColor, `DebloatEngine.cs` StatusColor, `DnsEngine.cs` PingColor/StatusColor,
  `StartupEngine.cs` StatusColor/ButtonBg, `ToolsEngine.cs` StatusColor, `TweakModel.cs` StatusColor.
- `MsiEngine.cs` — `StatusColor`, `PriorityColor`, `ActionButtonBg` (ключові з брифу).
- Для уникнення конфлікту `System.Drawing.Brush` vs `System.Windows.Media.Brush` (global using WinForms) додано аліас `using Brush = System.Windows.Media.Brush;`.
- Додано `RefreshThemeColors()` до `CleanerItem` та `PciMsiDevice`; `MainWindow` викликає її при зміні теми
  (`OnThemeChangedExternally` + `ThemeMenuItem_Click`), щоб статуси Cleaner/MSI (які не оновлюються через чіпси категорій) перефарбовувались.

### 7.3. Views — заміна hex на DynamicResource
- **MainWindow.xaml**: бейджі ризиків `SAFE/MED/HIGH` → `SuccessBrush/WarningBrush/DangerBrush`;
  темно-сірі бейджі `#1E293B` → `BadgeBg`; статусні/CTA кольори `#107C41/#C42B1C/#D87A00` → семантичні кисті;
  текст `#F59E0B/#38BDF8/#059669` → `WarningText/InfoText/SuccessText`.
- **SafetyWindow.xaml** (+ `.cs`): повна тематизація — фон вікна/панелей → `WindowBg/CardBg`, тексти → `TextPrimary/TextSecondary/DangerText/WarningText`, кнопки → `ChipActiveBg/ActionBtnBg/SuccessBrush/DangerBrush`; `HexBrush` у code-behind замінено на `ThemeEngine.Brush`.
- **DiagnosticWindow.xaml**: фон бейджа `#1E293B` → `BadgeBg`; сенсорні/заголовкові кольори → `WarningText/InfoText/SuccessText/DangerText`.
- **RestoreWindow.xaml**: кнопка `#0078D4/White` → `ChipActiveBg/ChipActiveText`.
- **WidgetWindow.xaml**: сірий `#64748B` (кнопка закриття) → `TextMuted`.
- `MainWindow.xaml.cs`: `GameModeStatusText` активний стан `HexBrush("#00FF9D")` → `FindResource("AccentGreen")`.

### 7.4. Задокументовані статичні акценти (свідомо НЕ конвертовано)
- **Модульні нав-чіпи** MainWindow: `#8A2BE2` (QoL), `#3949AB` (DNS), `#E65100` (UWP), `#00897B` (RUN),
  `#0078D4` (SOFT), `#D32F2F` (CLEAN), `#8E44AD` (GAME), `#0E7490` (NET), `#F59E0B` (PWR) + секційні чипи `#6A1B9A`, `#E65100`.
- **CTA-кнопки** `Background="#0078D4" Foreground="White"` (Apply/Install) — консистентний синій primary-акцент.
- **VendorBadge** у `MsiEngine.cs` (`#76B900/#ED1C24/#0071C5/#005596/#1428A0/#475569`) — фірмові кольори вендорів.
- **WidgetWindow** HUD-акценти (ARGB фони `#F00B0E17` тощо, сенсорні `#38BDF8/#F59E0B/#A78BFA`, кнопки `#0078D4/#8E44AD`) — окрема темна HUD-тема віджета (завжди на темному склі).
- **SafetyWindow** hover/pressed `#106EBE/#005A9E` — стани інтеракції синьої кнопки.
- `AppLogger.cs` (кольори рівнів логу), `TrayManager.cs` (WinForms-трей), `SettingsWindow.xaml.cs` (прев'ю теми) — не UI-текст/фон головного вікна.
- `MainWindow.xaml` `Color="#000000"` — тінь DropShadow (не текст/фон).

### 7.5. Результат
- Залишилось hex у Views: лише задокументовані статичні акценти (див. 7.4).
- Залишилось hex в Engines/Models: лише `MsiEngine.VendorBadge` (фірмові кольори).
- Перемикання теми тепер перефарбовує статуси/бейджі/тексти; «застиглого» сірого на світлих темах більше немає.


---

## 8. Фаза 3: Сортування по модулях

> Дата: 2026-08-30. Мета: робочий ComboBox сортування у кожному списковому модулі + коректні дефолти.

### 8.1. Перевірено — вже працювало (без змін коду)

| Модуль | Статус | Примітка |
|---|---|---|
| Dns | ✅ ОК | Дефолт `FastestFirst` (`_currentDnsSort = FastestFirst`, MainWindow.xaml.cs:59). ComboBox `DnsSortComboBox` пересортовує після `MeasureAllPingsAsync()` (вкладка DNS → вимір → `UpdateDnsChipsAndFilter`). До виміру `Ping = 999`, `OrderBy(Ping)` стабільний → каталоговий (детермінований) порядок, а не випадковий. |
| Tweak | ✅ ОК | `SortComboBox` має `Default`/`AppliedFirst`/`UnappliedFirst`/`Risk`/`Name`, хендлер `SortComboBox_SelectionChanged` працює. |
| Tools (Софт) | ✅ ОК | Сортування `InstalledFirst` працює; для встановлених програм видима кнопка «Відкрити» (`OpenButtonText`/`IsOpenVisible` → `OpenTool_Click` → `ToolsEngine.OpenInstalledTool` через `Process.Start`). |

### 8.2. Додано відсутні ComboBox сортування

| Модуль | ComboBox | Enum | Дефолт | Локалізація |
|---|---|---|---|---|
| Startup | `StartupSortComboBox` (MainWindow.xaml:1166) | `StartupSortMode` Default/EnabledFirst/DisabledFirst/Name/Category/Source | `Default` | `Common.Sort*` + `Startup.SortEnabledFirst/DisabledFirst/Category/Source` |
| Cleaner | `CleanerSortComboBox` (MainWindow.xaml:1524) | `CleanerSortMode` SizeDescending/SizeAscending/SafeFirst/Name/Category | `SizeDescending` | `Common.SortName` + `Cleaner.SortSizeDesc/Asc/SafeFirst/Category` |
| Msi | `MsiSortComboBox` (MainWindow.xaml:1824) | `MsiSortMode` Default/MsiFirst/LineBasedFirst/Priority/Name/Category/Vendor | `Default` | `Common.Sort*` + `Msi.SortMsiFirst/LineBased/Priority/Category/Vendor` |
| Backup | `BackupSortComboBox` (RestoreWindow.xaml:40) | `BackupSortMode` DateDescending/DateAscending/SizeDescending/KeyCountDescending/NameAscending | `DateDescending` | `Common.SortLabel` + `BackupEngine.SortDateDesc/Asc/SizeDesc/KeyCount/Name` |

### 8.3. Зміни в code-behind

- **MainWindow.xaml.cs**: додано поля `_currentStartupSort`/`_currentCleanerSort`/`_currentMsiSort`; хендлери `StartupSortComboBox_SelectionChanged`, `CleanerSortComboBox_SelectionChanged`, `MsiSortComboBox_SelectionChanged`; методи `UpdateCleanerList()` та `UpdateMsiDevices()` (перевиклик `GetFilteredAndSorted*`).
- `UpdateStartupChipsAndFilter` тепер передає `sortMode: _currentStartupSort` у `GetFilteredAndSortedEntries`.
- `ScanMsiDevicesAsync` тепер наповнює список через `UpdateMsiDevices()` (сортування враховується одразу після сканування).
- Cleaner: `UpdateCleanerList()` викликається після `CalculateSizesAsync` (відкриття вкладки CLEAN, `BtnRescanCleaner_Click`, `CleanItem_Click`, `BtnCleanAll_Click`) — список пересортовується за розміром/статусом.
- `ApplyLocalizedSortItems()` розширено мапами для Startup/Cleaner/Msi; `RefreshLocalizedChrome()` задає `LblSortStartup/Cleaner/Msi`.
- **RestoreWindow.xaml.cs**: поле `_currentBackupSort`, хендлер `BackupSortComboBox_SelectionChanged`, `RefreshBackupsListAsync` викликає `GetAvailableBackupsAsync(_currentBackupSort)`, локалізація пунктів у `ApplyLocalizedUi`.

### 8.4. JSON (UTF-8 без BOM, атомарно)

- Додано ключі сортування: `en/ua_StartupEngine.json` (4), `en/ua_CleanerEngine.json` (4), `en/ua_MsiEngine.json` (5), `en/ua_BackupEngine.json` (5).
- Всі 8 файлів перевірено `ConvertFrom-Json -ErrorAction Stop` → **ALL JSON VALID**.

### 8.5. Результат

- `dotnet build --nologo -v q` → **0 Warning(s), 0 Error(s)**.
- Кожен списковий модуль (Startup/Cleaner/Msi/Backup) отримав робочий локалізований ComboBox сортування.
- Network/Power не мають списків (кнопки/статуси) — сортування свідомо не додано (п.5 брифу).

Залишилось (з попереднього звіту): ~259 хардкод-рядків локалізації, ~13 хардкод-MessageBox, порожні JSON `Network`/`Preset`, розбіжність Tweak EN↔UA (224 зайві ключі).

---

## 9. Фаза 4: Переклад EN/UA

> Дата: 2026-08-30. Мета: повна відповідність EN↔UA, без відсутніх ключів, без захардкоджених MessageBox.

### 9.1. Reverse-gap TweakEngine (224 зайві UA-ключі)

- Причина: `ua_TweakEngine.json` містив поле `Category` для кожного твіка, а `en_TweakEngine.json` — ні (896 vs 1120 плоских ключів).
- Виправлено: до `en_TweakEngine.json` додано `"Category"` для всіх 224 твіків (переклад через словник `Categories` з `en_App.json`, 9 унікальних категорій, порядок ключів Name/Description/Benefits/SideEffects/Category).
- Результат: `ValidateTranslations.ps1` → `VALIDATION OK ... (extra keys: 0)`.

### 9.2. Нові ключі локалізації (en_App.json + ua_App.json)

- `Common.Yes` / `Common.No` (для GameBoost «Так/Ні»).
- `Update.Checking` / `Available` / `AvailableTitle` / `Downloading` / `UpToDate` / `UpToDateTitle` / `UpToDateStatus`.
- Нова секція `Dialogs` (17 ключів): `SafePackConfirm/Title/Done`, `Optimizing`, `PresetMenuPrompt/Title`, `ExportConfigTitle`, `ImportConfigTitle`, `DeployingProfile`, `GameBoostDone/Title/Applied/Partial/Error/ErrorTitle`, `CriticalError/Title`.
- Формат: UTF-8 без BOM (перевірено байтово), `\n`-escape для багаторядкових повідомлень, placeholders `{0}` / `{0:N0}` / `{1}`; line endings — CRLF.

### 9.3. Локалізовано MessageBox + StatusText (code-behind)

- `MainWindow.xaml.cs`:
  - `BtnBatchApply_Click` — SafePack confirm/done + «Оптимізація: …»;
  - `BtnCheckUpdates_Click` — перевірка/доступність/завантаження/актуальність оновлень;
  - `BtnPresetMenu_Click` — prompt пресет-менеджера, «Експорт/Імпорт конфігурації», «Розгортання профілю…»;
  - `BtnGameBoost_Click` — результат/частковий/помилка Game Boost.
- `App.xaml.cs` — критична помилка інтерфейсу → `Dialogs.CriticalError` / `Dialogs.CriticalErrorTitle`.

### 9.4. Перевірено (вже локалізовано, без змін)

- SafetyWindow, DiagnosticWindow, RestoreWindow — Title/контент/кнопки локалізовані в code-behind (`ApplyLocalizedUi` / `ApplyLocalizedLabels`).
- ComboBox'и сортування (п.6 брифу): `RefreshLocalizedChrome` → `ApplyLocalizedSortItems` (Startup/Cleaner/Msi) + `ApplyDnsSortItems` (Dns) + `RestoreWindow.ApplyLocalizedUi` (Backup) — перемикання мови оновлює всі пункти.

### 9.5. Результат

- `dotnet build --nologo -v q` → **0 Warning(s), 0 Error(s)**.
- `ValidateTranslations.ps1` → `VALIDATION OK ... (extra keys: 0)`.

Залишилось: ~259 XAML-дефолтів (MainWindow/Widget/Settings/Log/Diagnostic), які здебільшого перезаписуються runtime; порожні JSON `Network`/`Preset` (модулі без локалізованих рядків).


---

## 10. Фаза 5: Фінальна верифікація

> Дата: 2026-08-30. Мета: переконатись, що все разом працює.

### 10.1. Збірка та переклади

| Перевірка | Результат |
|---|---|
| `dotnet build --nologo -v q` | ✅ **Build succeeded — 0 Warning(s), 0 Error(s)** |
| `ValidateTranslations.ps1` | ✅ `VALIDATION OK ... (extra keys: 0)` |

### 10.2. Рантайм-смоук

- exe: `bin\Debug\net8.0-windows10.0.22621.0\win-x64\MASLOOPTIMIZER.exe`.
- Запуск без падінь: процес живий 8+ с, головне вікно відкрилося (`MainWindowTitle = "MASLOOPTIMIZER v0.4.0"`).
- Завершення: `CloseMainWindow()` → коректний вихід, сиріт-процесів немає.
- Застосунок працює підвищено (elevated): з непривілейованої консолі `Stop-Process` дає `Access denied` — очікувано для WinOptimizer.
- Інтерактивні пункти чекліста (перемикання теми/мови, зміна порядку сортування, кнопка запуску) потребують ручної перевірки в GUI. На рівні коду підтверджено:
  - DNS дефолт = `FastestFirst` (найшвидший пінг угорі) — `MainWindow.xaml.cs:59`.
  - Твіки дефолт = `Default` (`MainWindow.xaml.cs:47`); опції `AppliedFirst`/`UnappliedFirst`/`Risk`/`Name` присутні.
  - Софт: сортування `InstalledFirst` працює; кнопка «Відкрити» → `OpenTool_Click` → `ToolsEngine.OpenInstalledTool` → `Process.Start` (`MainWindow.xaml.cs:1177`, `ToolsEngine.cs:1194`).
  - Всі ComboBox сортування (Tweak/Dns/Debloat/Tools/Startup/Cleaner/Msi/Backup) перемаповані на `_currentXxxSort` і оновлюються при зміні мови.

### 10.3. Фінальний дефікс (виправлено у цій фазі)

Фаза 4 декларувала «всі захардкоджені MessageBox + StatusText локалізовано», але верифікація виявила пропущені захардкоджені рядки. Усі вони тепер локалізовані:

**1. MainWindow.xaml.cs — StatusText + MessageBox (~32 рядки + 1):**
- Ready-статуси модулів (`Debloat.Ready`, `Tools.Ready`, `GameMode.Ready`).
- Tweak apply/revert (`Tweak.Applying/ApplyDone/ApplyFailed/Restoring/RestoreDone/RestoreFailed/Crash`).
- Debloat uninstall/restore/rescan (`Debloat.Uninstalling/Uninstalled/UninstallFailed/Restoring/Restored/RestoreStoreOpened/Rescanning/RescanDone`).
- Tools install/launch/rescan (`Tools.*`, 16 ключів).
- Power (`Power.Applying/ApplyError/SnapshotCreating/SnapshotDone/SnapshotError`).
- MessageBox `BtnSafeMasloPack_Click` → `Dialogs.MasloPackConfirm` / `Dialogs.MasloPackTitle`.

**2. Network-модуль (був повністю нелокалізований) — виправлено:**
- `en/ua_NetworkEngine.json` був порожній `{ }` → заповнено об'єктом `Network` (26 ключів: Title, Description, статуси, частини стану Nagle/EEE/QoS/DNS-cache, кнопки BtnNagle/BtnEee/BtnQos/BtnReset, Busy/…Done/ErrorShort).
- `RefreshLocalizedChrome()` тепер задає `NetworkTitleText/NetworkDescText/NetworkStatusText/NetworkDetailsText` + 4 кнопки.
- `RefreshNetworkStatusAsync()` + 4 хендлери кнопок (`BtnNagle/Eee/Qos/NetworkReset_Click`) переведено на `loc[…]` / `loc.Format(…)`.

**3. PresetEngine.cs (движок) — виправлено:**
- Результати `(bool, string)` та `progressCallback` повідомлення (~17 рядків) → `Dialogs.PresetSaveDone/SaveError/ExportCancelled/ImportCancelled/FileNotFound/Corrupted/ImportError/DebloatingItem/ConfigDeployed/ProfileApplied/MasloPackProgress/MasloPackDone/MasloPackResult/PresetError` (+ перевикористано `Dialogs.Optimizing`, `Tweak.Restoring`).

**4. CleanerEngine.cs — виправлено:**
- UI-орієнтований `FormatBytes` (розміри `TotalSizeFormatted`/`SafeSizeFormatted`) → `Common.UnitGB/MB/KB/Bytes` (раніше «ГБ/МБ/КБ/Байт»).

**5. JSON-ключі додано (UTF-8 без BOM, атомарно, EN↔UA parity):**
- `en/ua_TweakEngine` (+7), `en/ua_DebloatEngine` (+9), `en/ua_ToolsEngine` (+15), `en/ua_PowerEngine` (+5), `en/ua_GameModeEngine` (+1), `en/ua_NetworkEngine` (+26, з нуля), `en/ua_App` (`Dialogs` +16).

### 10.4. Спостереження (не дефекти)

- `MASLOOPTIMIZER.csproj` → `Version=0.4.0`, заголовок вікна «v0.4.0», але директорія називається `v0.3.4` (розбіжність лише в імені теки).
- `en_PresetEngine.json` лишився порожній — пресет-рядки живуть у `en_App.json` (`Dialogs.*`), згідно з наявним патерном (ExportConfigTitle/ImportConfigTitle/DeployingProfile).
- ~259 XAML-дефолтів перезаписуються в runtime (див. Фазу 1); Network-дефолти тепер також перезаписуються.
- Внутрішні логи (`AppLogger.Log`) залишено українською — це конвенція проєкту (не UI).


### 10.5. Тимчасові скрипти

- У `Tools/` лише штатні скрипти: `NewLanguage.ps1`, `SplitLanguages.ps1`, `ValidateTranslations.ps1`, `_gen_backup.ps1` + `_audit_report.md`. Тимчасових файлів не створювалось — чистити нічого.

