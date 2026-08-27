using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace MASLOOPTIMIZER;

public class LocalizationManager : INotifyPropertyChanged
{
    private static readonly Lazy<LocalizationManager> _instance = new(() => new LocalizationManager());
    public static LocalizationManager Instance => _instance.Value;

    /// <summary>Код базової мови — мова оригінальних текстів у tweaks.bundle.json.</summary>
    public const string FallbackLanguage = "UK";

    private Dictionary<string, string> _currentStrings = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _fallbackStrings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _languageNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Надійний вбудований словник-fallback на випадок відсутності або пошкодження JSON-файлів.</summary>
    private static readonly IReadOnlyDictionary<string, string> BuiltInUk = CreateBuiltIn("UK");
    private static readonly IReadOnlyDictionary<string, string> BuiltInEn = CreateBuiltIn("EN");

    public string CurrentLanguage { get; private set; } = FallbackLanguage;
    public string CurrentLanguageName => GetLanguageName(CurrentLanguage);
    public List<string> AvailableLanguages { get; } = new();

    public static string LanguagesDirectory => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Languages");

    /// <summary>Папки пошуку мовних файлів (без дублікатів, регістронезалежно).</summary>
    public static IReadOnlyList<string> LanguageSearchDirectories => _languageSearchDirectories;
    private static readonly List<string> _languageSearchDirectories = BuildLanguageSearchDirectories();

    private LocalizationManager()
    {
        ScanAvailableLanguages();
        LoadFallbackLanguage();
        // Читаємо збережену мову з налаштувань; fallback — UK.
        LoadLanguage(ReadSavedLanguage(), persist: false);
    }

    /// <summary>
    /// Отримує рядок за ключем. Ланцюжок fallback:
    /// поточна мова → uk.json → назва ключа.
    /// </summary>
    public string this[string key]
    {
        get
        {
            if (string.IsNullOrWhiteSpace(key)) return string.Empty;
            if (_currentStrings.TryGetValue(key, out var val) && !string.IsNullOrEmpty(val))
            {
                return val;
            }
            if (_fallbackStrings.TryGetValue(key, out var fb) && !string.IsNullOrEmpty(fb))
            {
                return fb;
            }
            if (TryGetBuiltIn(CurrentLanguage, key, out var builtIn) && !string.IsNullOrEmpty(builtIn))
            {
                return builtIn;
            }
            if (TryGetBuiltIn(FallbackLanguage, key, out var builtInFallback) && !string.IsNullOrEmpty(builtInFallback))
            {
                return builtInFallback;
            }
            return key; // Останній fallback — назва ключа
        }
    }

    /// <summary>
    /// Безпечний пошук: повертає false замість сирого ключа, якщо рядка немає.
    /// Для опціональних ключів (напр. Categories.* у мовних файлах).
    /// </summary>
    public bool TryGet(string key, out string value)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            if (_currentStrings.TryGetValue(key, out var val) && !string.IsNullOrEmpty(val))
            {
                value = val;
                return true;
            }
            if (_fallbackStrings.TryGetValue(key, out var fb) && !string.IsNullOrEmpty(fb))
            {
                value = fb;
                return true;
            }
            if (TryGetBuiltIn(CurrentLanguage, key, out var builtIn) && !string.IsNullOrEmpty(builtIn))
            {
                value = builtIn;
                return true;
            }
            if (TryGetBuiltIn(FallbackLanguage, key, out var builtInFallback) && !string.IsNullOrEmpty(builtInFallback))
            {
                value = builtInFallback;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    /// <summary>
    /// Отримує локалізований текст твіка. Ключ у мовному файлі:
    /// Tweaks.{id}.Name / Tweaks.{id}.Description.
    /// Якщо переклад відсутній у поточній мові та в uk.json —
    /// повертається оригінальний текст з tweaks.bundle.json.
    /// </summary>
    public string GetTweakText(string tweakId, string field, string bundleText)
    {
        if (string.IsNullOrWhiteSpace(tweakId)) return bundleText;
        string key = $"Tweaks.{tweakId}.{field}";
        if (_currentStrings.TryGetValue(key, out var val) && !string.IsNullOrEmpty(val))
        {
            return val;
        }
        if (_fallbackStrings.TryGetValue(key, out var fb) && !string.IsNullOrEmpty(fb))
        {
            return fb;
        }
        return bundleText; // Fallback на оригінальний текст з бандлу
    }

    public void ScanAvailableLanguages()
    {
        AvailableLanguages.Clear();
        _languageNames.Clear();

        try
        {
            foreach (var dir in LanguageSearchDirectories)
            {
                if (!Directory.Exists(dir)) continue;

                foreach (var file in Directory.GetFiles(dir, "*.json"))
                {
                    string code = Path.GetFileNameWithoutExtension(file).ToUpperInvariant();
                    if (string.IsNullOrWhiteSpace(code) || AvailableLanguages.Contains(code))
                    {
                        continue;
                    }

                    AvailableLanguages.Add(code);
                    _languageNames[code] = ReadLanguageName(file);
                }
            }
        }
        catch { }

        if (AvailableLanguages.Count == 0)
        {
            AvailableLanguages.AddRange(new[] { "UK", "EN" });
            _languageNames["UK"] = "Українська";
            _languageNames["EN"] = "English";
        }

        foreach (var code in AvailableLanguages)
        {
            if (!_languageNames.TryGetValue(code, out var name) || string.IsNullOrWhiteSpace(name))
            {
                _languageNames[code] = code;
            }
        }

        AvailableLanguages.Sort(StringComparer.OrdinalIgnoreCase);
    }

    public void LoadLanguage(string langCode, bool persist = true)
    {
        string requested = (langCode ?? string.Empty).Trim().ToUpperInvariant();
        if (requested.Length == 0) requested = FallbackLanguage;

        // Безпечний пошук файлу в усіх папках; якщо відсутній — падаємо на базову мову.
        string? filePath = FindLanguageFile(requested);
        if (filePath == null)
        {
            requested = FallbackLanguage;
            filePath = FindLanguageFile(requested);
        }

        CurrentLanguage = requested;
        _currentStrings.Clear();
        if (filePath != null)
        {
            LoadLanguageFile(filePath, _currentStrings);
        }

        if (persist) SaveLanguage(requested);

        OnPropertyChanged("Item[]");
        OnPropertyChanged(nameof(CurrentLanguage));
        OnPropertyChanged(nameof(CurrentLanguageName));
    }

    /// <summary>Повертає відображувану назву мови (LanguageName з файлу) або сам код.</summary>
    public string GetLanguageName(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return string.Empty;
        return _languageNames.TryGetValue(code, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : code;
    }

    /// <summary>Повертає код наступної доступної мови для циклічного перемикання.</summary>
    public string NextLanguage()
    {
        if (AvailableLanguages.Count == 0) return FallbackLanguage;
        int index = AvailableLanguages.IndexOf(CurrentLanguage);
        int next = index < 0 ? 0 : (index + 1) % AvailableLanguages.Count;
        return AvailableLanguages[next];
    }

    private string ReadSavedLanguage()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile)) return FallbackLanguage;

            using var doc = JsonDocument.Parse(File.ReadAllText(AppPaths.SettingsFile));
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("Language", out var lang) &&
                lang.ValueKind == JsonValueKind.String)
            {
                string code = lang.GetString()!.Trim().ToUpperInvariant();
                if (!string.IsNullOrWhiteSpace(code) && FindLanguageFile(code) != null)
                {
                    return code;
                }
            }
        }
        catch { }

        return FallbackLanguage;
    }

    private static void SaveLanguage(string code)
    {
        try
        {
            AppPaths.EnsureDirectories();

            var options = new JsonSerializerOptions { WriteIndented = true };
            var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

            if (File.Exists(AppPaths.SettingsFile))
            {
                try
                {
                    var existing = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(AppPaths.SettingsFile));
                    if (existing != null)
                    {
                        foreach (var kv in existing) settings[kv.Key] = kv.Value;
                    }
                }
                catch { }
            }

            settings["Language"] = JsonSerializer.SerializeToElement(code);
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(settings, options));
        }
        catch { }
    }

    private static string ReadLanguageName(string filePath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(filePath));
            if (doc.RootElement.TryGetProperty("LanguageName", out var name) && name.ValueKind == JsonValueKind.String)
            {
                return name.GetString() ?? string.Empty;
            }
        }
        catch { }

        return string.Empty;
    }

    /// <summary>Завантажує базовий (uk) словник для fallback-пошуку рядків.</summary>
    private void LoadFallbackLanguage()
    {
        _fallbackStrings.Clear();
        string? filePath = FindLanguageFile(FallbackLanguage);
        if (filePath != null)
        {
            LoadLanguageFile(filePath, _fallbackStrings);
        }
    }

    /// <summary>Читає JSON-файл мови та розгортає його у плоский словник ключ→рядок.</summary>
    private static void LoadLanguageFile(string filePath, Dictionary<string, string> target)
    {
        if (!File.Exists(filePath)) return;

        try
        {
            string json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json);
            FlattenJsonElement(doc.RootElement, string.Empty, target);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Помилка завантаження мовного файлу {filePath}: {ex.Message}", "ERROR");
        }
    }

    private static void FlattenJsonElement(JsonElement element, string prefix, Dictionary<string, string> target)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                string nextKey = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                FlattenJsonElement(prop.Value, nextKey, target);
            }
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            target[prefix] = element.GetString() ?? string.Empty;
        }
    }

    /// <summary>Шукає файл мови у всіх зареєстрованих папках пошуку.</summary>
    private static string? FindLanguageFile(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        string fileName = code.Trim().ToLowerInvariant() + ".json";
        foreach (var dir in LanguageSearchDirectories)
        {
            try
            {
                string candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }
        return null;
    }

    /// <summary>Формує список папок пошуку мовних файлів (без дублікатів).</summary>
    private static List<string> BuildLanguageSearchDirectories()
    {
        var dirs = new List<string>();

        AddDirectory(dirs, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Languages"));

        try { AddDirectory(dirs, Path.Combine(AppPaths.Root, "Languages")); } catch { }

        try
        {
            string? exeDir = Path.GetDirectoryName(Environment.ProcessPath);
            if (!string.IsNullOrWhiteSpace(exeDir))
            {
                AddDirectory(dirs, Path.Combine(exeDir, "Languages"));
            }
        }
        catch { }

        return dirs;
    }

    private static void AddDirectory(List<string> dirs, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (dirs.Any(d => string.Equals(d, path, StringComparison.OrdinalIgnoreCase))) return;
        dirs.Add(path);
    }

    private static bool TryGetBuiltIn(string code, string key, out string value)
    {
        var dict = string.Equals(code, "EN", StringComparison.OrdinalIgnoreCase) ? BuiltInEn : BuiltInUk;
        if (dict.TryGetValue(key, out var found) && !string.IsNullOrEmpty(found))
        {
            value = found;
            return true;
        }
        value = string.Empty;
        return false;
    }

    public string Format(string key, params object[] args)
    {
        try
        {
            string template = this[key];
            return string.Format(template, args);
        }
        catch
        {
            return this[key];
        }
    }

    /// <summary>
    /// Повертає вбудований словник fallback для заданої мови.
    /// Використовується лише якщо відповідний JSON-файл відсутній або пошкоджений.
    /// </summary>
    private static IReadOnlyDictionary<string, string> CreateBuiltIn(string code)
    {
        bool en = string.Equals(code, "EN", StringComparison.OrdinalIgnoreCase);
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Add(string key, string uk, string enValue) => d[key] = en ? enValue : uk;

        // Header
        Add("Header.Title", "MASLOOPTIMIZER", "MASLOOPTIMIZER");
        Add("Header.CoreVersion", "v0.3.3", "v0.3.3");
        Add("Header.BtnSpecs", "🔍 Специфікації ПК", "🔍 PC Specs");
        Add("Header.BtnVss", "🛡️ Точка VSS", "🛡️ VSS Point");
        Add("Header.BtnBackup", "💾 Бекап реєстру", "💾 Registry Backup");
        Add("Header.BtnRestore", "🔄 Відкат", "🔄 Rollback");
        Add("Header.BtnUpdates", "🚀 Оновлення", "🚀 Updates");
        Add("Header.BtnWidget", "📌 Віджет", "📌 Widget");
        Add("Header.BtnBackupMenu", "🛡️ Бекап & VSS", "🛡️ Backup & VSS");
        Add("Header.BtnSettings", "⚙️ Налаштування", "⚙️ Settings");
        Add("Header.BadgeHealth", "🏆 Health Score", "🏆 Health Score");
        Add("Header.BtnTheme", "🎨 Тема", "🎨 Theme");
        Add("Header.BtnDarkTheme", "🌙 Темна тема", "🌙 Dark Theme");
        Add("Header.BtnLightTheme", "☀️ Світла тема", "☀️ Light Theme");
        Add("Header.Language", "🌐 Мова", "🌐 Language");
        Add("Header.BadgeOS", "OS:", "OS:");
        Add("Header.BadgeCPU", "CPU:", "CPU:");
        Add("Header.BadgeGPU", "GPU:", "GPU:");
        Add("Header.BadgeRAM", "RAM:", "RAM:");
        Add("Header.BadgeDisk", "C:", "C:");
        Add("Header.BadgeTweaks", "Database:", "Database:");

        // Common
        Add("Common.AllCategories", "Всі", "All");
        Add("Common.Busy", "⏳ Обробка...", "⏳ Processing...");
        Add("Common.Apply", "⚡ Застосувати", "⚡ Apply");
        Add("Common.Restore", "↩️ Відновити", "↩️ Restore");
        Add("Common.Installing", "⏳ Встановлення...", "⏳ Installing...");
        Add("Common.Cleaning", "⏳ Очищення...", "⏳ Cleaning...");
        Add("Common.Loading", "⏳ Завантаження...", "⏳ Loading...");
        Add("Common.Error", "⚠️ Помилка", "⚠️ Error");
        Add("Common.Success", "✅ Успішно", "✅ Success");
        Add("Common.SortLabel", "Сортування:", "Sort by:");
        Add("Common.SortDefault", "За замовчуванням", "Default");
        Add("Common.SortAppliedFirst", "🟢 Застосовані спочатку", "🟢 Applied first");
        Add("Common.SortUnappliedFirst", "⚪ Стандартні спочатку", "⚪ Standard first");
        Add("Common.SortInstalledFirst", "🟢 Встановлені спочатку", "🟢 Installed first");
        Add("Common.SortUninstalledFirst", "⚪ Видалені спочатку", "⚪ Removed first");
        Add("Common.SortNotInstalledFirst", "⚪ Не встановлені", "⚪ Not installed");
        Add("Common.SortRisk", "⚠️ За рівнем ризику", "⚠️ By risk level");
        Add("Common.SortName", "🔤 За назвою (А-Я)", "🔤 By name (A-Z)");
        Add("Common.TweaksCount", "{0} твіків", "{0} tweaks");
        Add("Common.StatusOptimized", "🟢 ОПТИМІЗОВАНО", "🟢 OPTIMIZED");
        Add("Common.StatusStandard", "⚪ СТАНДАРТ", "⚪ STANDARD");
        Add("Common.UnitBytes", "Байт", "B");
        Add("Common.UnitKB", "КБ", "KB");
        Add("Common.UnitMB", "МБ", "MB");
        Add("Common.UnitGB", "ГБ", "GB");
        Add("Common.SystemStorage", "Системне сховище", "System storage");

        // Sidebar
        Add("Sidebar.SearchPlaceholder", "🔍 Пошук...", "🔍 Search...");
        Add("Sidebar.NavUI", "Твіки системи/Корисне", "System Tweaks / QoL");
        Add("Sidebar.NavSafe", "Безпечні твіки", "Safe Tweaks");
        Add("Sidebar.NavMedium", "Помірні твіки", "Medium Tweaks");
        Add("Sidebar.NavHigh", "Небезпечні твіки", "Advanced Tweaks");
        Add("Sidebar.NavDns", "DNS Оптимізатор", "DNS Optimizer");
        Add("Sidebar.NavDebloat", "Деблоат UWP програм", "UWP Debloat Manager");
        Add("Sidebar.NavStartup", "Менеджер автозапуску", "Startup Manager");
        Add("Sidebar.NavTools", "Інструменти та Софт", "Tools & Software");
        Add("Sidebar.NavCleaner", "Очищення дисків", "Disk Cleaner");
        Add("Sidebar.NavGameMode", "🎮 Ігровий режим", "🎮 Game Mode");
        Add("Sidebar.NavNetwork", "🌐 Мережа", "🌐 Network");
        Add("Sidebar.AutomationTitle", "АВТОМАТИЗАЦІЯ", "AUTOMATION");
        Add("Sidebar.BtnSafePack", "⚡ 1-Click Safe Pack", "⚡ 1-Click Safe Pack");
        Add("Sidebar.BtnMasloPack", "⚡ 1-Click Safe Maslo Pack", "⚡ 1-Click Safe Maslo Pack");
        Add("Sidebar.BtnGameBoost", "🚀 1-Click Game Boost", "🚀 1-Click Game Boost");
        Add("Sidebar.BtnGameBoostBusy", "⏳ Boost...", "⏳ Boost...");
        Add("Sidebar.BtnPresets", "📦 Пресети (JSON)", "📦 Presets (JSON)");
        Add("Sidebar.BtnSafePackTip", "Застосувати всі безпечні твіки одним кліком", "Apply all safe tweaks in one click");
        Add("Sidebar.BtnMasloPackTip", "Комплексна оптимізація: безпечні твіки + деблоат мотлоху", "Full optimization: safe tweaks + bloatware debloat");
        Add("Sidebar.BtnGameBoostTip", "Миттєвий буст FPS: очищення кешу пам'яті та зупинка фонових служб", "Instant FPS boost: clears memory cache and stops background services");
        Add("Sidebar.BtnPresetsTip", "Менеджер пресетів: зберегти конфіг або розгорнути збережений профіль", "Preset manager: save config or deploy a saved profile");
        Add("Sidebar.MiSaveProfile", "💾 Зберегти конфіг", "💾 Save Config");
        Add("Sidebar.MiLoadProfile", "📂 Завантажити пресет", "📂 Load Preset");
        Add("Update.ToastTitle", "Знайдено нове оновлення MASLOOPTIMIZER v{0}!", "A new MASLOOPTIMIZER update v{0} is available!");
        Add("Update.ToastSub", "Натисніть «Оновити зараз», щоб завантажити і встановити останню версію програми.", "Click \"Update Now\" to download and install the latest version of the app.");
        Add("Update.BtnNow", "🚀 Оновити зараз", "🚀 Update Now");
        Add("Update.BtnClose", "✕ Закрити", "✕ Close");
        Add("Update.DownloadingTitle", "Завантаження оновлення MASLOOPTIMIZER v{0}...", "Downloading MASLOOPTIMIZER update v{0}...");
        Add("Update.DownloadProgress", "Завантаження: {0}%", "Downloading: {0}%");
        Add("Update.DownloadDone", "Оновлення завантажено. Програма перезапуститься автоматично.", "Update downloaded. The app will restart automatically.");

        // Dns
        Add("Dns.Title", "🌐 ОПТИМІЗАТОР МЕРЕЖЕВИХ ЗАТРИМОК DNS", "🌐 DNS NETWORK LATENCY OPTIMIZER");
        Add("Dns.Description", "Паралельний вимір пінгу та 1-Click активація найшвидших DNS-серверів", "Parallel ping measurement and 1-Click fastest DNS activation");
        Add("Dns.BtnFastest", "⚡ Найшвидший DNS", "⚡ Fastest DNS");
        Add("Dns.BtnReset", "🔄 Скинути до DHCP", "🔄 Reset to DHCP");
        Add("Dns.StatusActive", "🟢 АКТИВНИЙ", "🟢 ACTIVE");
        Add("Dns.StatusAvailable", "⚪ ДОСТУПНИЙ", "⚪ AVAILABLE");
        Add("Dns.StatusTimeout", "🔴 ТАЙМАУТ", "🔴 TIMEOUT");
        Add("Dns.BtnApply", "⚡ Застосувати", "⚡ Apply");
        Add("Dns.BtnActive", "✓ Активний", "✓ Active");

        // Debloat
        Add("Debloat.Title", "📦 МЕНЕДЖЕР ДЕБЛОАТУ UWP ПРОГРАМ WINDOWS", "📦 WINDOWS UWP DEBLOAT MANAGER");
        Add("Debloat.Description", "Видалення вбудованого мотлоху, телеметрії та реклами", "Remove bloatware, telemetry, and promotional items");
        Add("Debloat.BtnRescan", "🔄 Пересканувати", "🔄 Rescan");
        Add("Debloat.BtnUninstall", "🗑️ Видалити", "🗑️ Uninstall");
        Add("Debloat.BtnRestore", "↩️ Відновити", "↩️ Restore");
        Add("Debloat.StatusInstalled", "🟢 ВСТАНОВЛЕНО", "🟢 INSTALLED");
        Add("Debloat.StatusNotInstalled", "⚪ НЕМАЄ В СИСТЕМІ", "⚪ NOT IN SYSTEM");
        Add("Debloat.StatsSummary", "Встановлено: {0} | Видалено: {1} з {2} UWP-компонентів", "Installed: {0} | Removed: {1} of {2} UWP apps");

        // Tools
        Add("Tools.Title", "🛠️ БІБЛІОТЕКА НЕОБХІДНОГО СОФТУ ТА ДІАГНОСТИКИ", "🛠️ ESSENTIAL SOFTWARE & DIAGNOSTICS");
        Add("Tools.Description", "Тихе встановлення офіційних інструментів через Winget та прямі лінки", "Silent installation of official utilities via Winget and direct sources");
        Add("Tools.BtnRescan", "🔄 Оновити статус", "🔄 Update Status");
        Add("Tools.BtnInstall", "⬇️ Встановити", "⬇️ Install");
        Add("Tools.BtnInstalled", "✓ Встановлено", "✓ Installed");
        Add("Tools.BtnSite", "🌐 Сайт", "🌐 Website");
        Add("Tools.StatsSummary", "Виявлено в системі: {0} | Доступно для встановлення: {1} з {2} утиліт", "Detected in system: {0} | Available: {1} of {2} tools");
        Add("Tools.BtnActivate", "⚡ Активація", "⚡ Activate");
        Add("Tools.BtnInstallAll", "⚡ Встановити все", "⚡ Install all");
        Add("Tools.BtnUpdateDx", "⚡ Оновити DirectX", "⚡ Update DirectX");

        // Cleaner
        Add("Cleaner.Title", "🧹 ГЛИБОКЕ ОЧИЩЕННЯ ТА ЗВІЛЬНЕННЯ SSD", "🧹 DEEP SSD CLEANING & DISK CLEANER");
        Add("Cleaner.Description", "Безпечне очищення кешів, тимчасових файлів та звільнення місця на системному диску", "Safely clean caches, temporary files and free up space on the system drive");
        Add("Cleaner.Calculating", "Підрахунок зайнятого простору...", "Calculating space...");
        Add("Cleaner.FoundTotal", "Виявлено для очищення: {0}", "Detected for cleanup: {0}");
        Add("Cleaner.BtnCleanAll", "⚡ 1-Click Очистити все", "⚡ 1-Click Clean All");
        Add("Cleaner.BtnRescan", "🔄 Пересканувати", "🔄 Rescan");
        Add("Cleaner.BtnClean", "🧹 Очистити", "🧹 Clean");

        // Startup
        Add("Startup.Title", "🚀 МЕНЕДЖЕР АВТОЗАПУСКУ WINDOWS", "🚀 WINDOWS STARTUP MANAGER");
        Add("Startup.Description", "Керування автозавантаженням та фоновими завданнями", "Manage autostart apps and scheduled tasks");
        Add("Startup.BtnRescan", "🔄 Оновити", "🔄 Refresh");
        Add("Startup.BtnPause", "⏸️ Призупинити", "⏸️ Pause");
        Add("Startup.BtnEnable", "▶️ Увімкнути", "▶️ Enable");
        Add("Startup.StatusActive", "🟢 АКТИВНО", "🟢 ACTIVE");
        Add("Startup.StatusPaused", "⚪ ПРИЗУПИНЕНО", "⚪ PAUSED");
        Add("Startup.BtnProtected", "🔒 Захищено", "🔒 Protected");

        // GameMode
        Add("GameMode.Title", "🎮 ІГРОВИЙ РЕЖИМ", "🎮 GAME MODE");
        Add("GameMode.Description", "Пріоритет GPU/CPU, зупинка фонових служб та звільнення пам'яті для максимального FPS", "GPU/CPU priority, stop background services and free memory for maximum FPS");
        Add("GameMode.BtnActivate", "⚡ Активувати", "⚡ Activate");
        Add("GameMode.BtnDeactivate", "🔄 Деактивувати", "🔄 Deactivate");
        Add("GameMode.StatusActive", "🟢 АКТИВНИЙ", "🟢 ACTIVE");
        Add("GameMode.StatusInactive", "⚪ НЕАКТИВНИЙ", "⚪ INACTIVE");

        // Msi
        Add("Msi.Title", "⚡ MSI MODE ДЛЯ PCI-ПРИСТРОЇВ", "⚡ MSI MODE FOR PCI DEVICES");
        Add("Msi.Description", "Векторні переривання (Message Signaled Interrupts) для GPU, NVMe та мережевих адаптерів", "Message Signaled Interrupts for GPU, NVMe and network adapters");
        Add("Msi.BtnScan", "🔍 Сканувати PCI", "🔍 Scan PCI");
        Add("Msi.BtnEnable", "⚡ Увімкнути MSI", "⚡ Enable MSI");
        Add("Msi.BtnDisable", "↩️ Вимкнути MSI", "↩️ Disable MSI");
        Add("Msi.BtnOptimize", "🚀 1-Click Gaming MSI", "🚀 1-Click Gaming MSI");
        Add("Msi.BtnRestore", "↩️ Відновити стандартний режим", "↩️ Restore default mode");
        Add("Msi.StatusSupported", "🟢 MSI MODE", "🟢 MSI MODE");
        Add("Msi.StatusNotSupported", "⚪ LINE-BASED", "⚪ LINE-BASED");

        // Health
        Add("Health.Title", "🩺 ДІАГНОСТИКА ІГРОВОЇ СИСТЕМИ", "🩺 GAMING SYSTEM DIAGNOSTICS");
        Add("Health.Description", "Аудит параметрів GPU/CPU/SSD/мережі та 1-Click виправлення проблем", "Audit GPU/CPU/SSD/network settings and 1-Click fixes");
        Add("Health.BtnScan", "🔍 Запустити аудит", "🔍 Run Audit");
        Add("Health.BtnFix", "⚡ 1-Click Виправити все", "⚡ 1-Click Fix All");
        Add("Health.StatusOptimal", "🟢 ОПТИМАЛЬНО", "🟢 OPTIMAL");
        Add("Health.StatusWarning", "⚠️ ПОТРЕБУЄ УВАГИ", "⚠️ NEEDS ATTENTION");
        Add("Health.StatsSummary", "Оцінка: {0}% | Оптимально: {1} з {2} параметрів", "Score: {0}% | Optimal: {1} of {2} settings");

        // Footer
        Add("Footer.ReadyStatus", "Готово до оптимізації (.NET 8 Core)", "Ready for optimization (.NET 8 Core)");
        Add("Footer.BtnLogs", "📊 Історія дій", "📊 Action History");
        Add("Footer.Author", "by 3ibravMasla", "by 3ibravMasla");
        Add("Footer.Scanning", "Перевірка активних параметрів системи...", "Checking active system parameters...");
        Add("Footer.Analyzing", "Аналіз: {0}", "Analyzing: {0}");
        Add("Footer.ScanDone", "Діагностика завершена. Система готова до роботи.", "Diagnostics complete. System is ready.");


        // DNS — чіпси, сортування та статуси
        Add("Dns.ChipAll", "🌟 Всі", "🌟 All");
        Add("Dns.ChipSpeed", "⚡ Швидкі", "⚡ Fast");
        Add("Dns.ChipSecurity", "🛡️ Безпечні / З блокуванням реклами", "🛡️ Secure / Ad-Blocking");
        Add("Dns.ChipGaming", "🎮 Геймінг", "🎮 Gaming");
        Add("Dns.SortLabel", "Сортування:", "Sort by:");
        Add("Dns.SortFastest", "⚡ Найменший пінг спочатку", "⚡ Lowest ping first");
        Add("Dns.SortName", "🔤 За алфавітом", "🔤 By name (A-Z)");
        Add("Dns.PingTimeout", "Таймаут", "Timeout");
        Add("Dns.Measuring", "Замір пінгу DNS-серверів...", "Measuring DNS server pings...");
        Add("Dns.Measured", "DNS-сервери відсортовано за найменшим пінгом.", "DNS servers sorted by lowest ping.");
        Add("Dns.SearchingFastest", "Пошук найшвидшого DNS...", "Searching for fastest DNS...");
        Add("Dns.ApplyDone", "DNS встановлено: {0}", "DNS applied: {0}");
        Add("Dns.ApplyFailed", "Помилка встановлення DNS.", "Failed to apply DNS.");
        Add("Dns.ResetDone", "DNS успішно повернуто до початкового стану (DHCP).", "DNS successfully restored to original state (DHCP).");
        Add("Dns.ResetFailed", "Помилка відновлення DNS.", "Failed to restore DNS.");
        Add("Dns.FastestApplied", "Встановлено найшвидший DNS: {0} ({1} ms)", "Fastest DNS applied: {0} ({1} ms)");
        Add("Dns.FastestFound", "Найшвидший сервер: {0}\nЗатримка (Ping): {1} ms\n\nСервер успішно активовано!", "Fastest server: {0}\nLatency (Ping): {1} ms\n\nServer activated successfully!");

        // Startup — чіпси, джерела, статуси
        Add("Startup.ChipAll", "🌟 Всі", "🌟 All");
        Add("Startup.ChipUser", "📝 Програми користувача (HKCU Run)", "📝 User Apps (HKCU Run)");
        Add("Startup.ChipSystem", "🖥️ Системні (HKLM Run)", "🖥️ System (HKLM Run)");
        Add("Startup.ChipTasks", "⏰ Планувальник завдань", "⏰ Task Scheduler");
        Add("Startup.ChipFolder", "📁 Папка автозавантаження", "📁 Startup Folder");
        Add("Startup.BadgeProtected", "🛡️ СИСТЕМНИЙ", "🛡️ SYSTEM");
        Add("Startup.BadgeSafe", "⚡ БЕЗПЕЧНО", "⚡ SAFE");
        Add("Startup.StatusProtected", "🔒 ЗАХИЩЕНО", "🔒 PROTECTED");
        Add("Startup.SourceUserRun", "Реєстр (HKCU)", "Registry (HKCU)");
        Add("Startup.SourceSystemRun", "Реєстр (HKLM)", "Registry (HKLM)");
        Add("Startup.SourceTaskScheduler", "Планувальник завдань", "Task Scheduler");
        Add("Startup.SourceStartupFolder", "Папка автозавантаження", "Startup Folder");
        Add("Startup.ListRefreshed", "Список автозавантаження оновлено.", "Startup list refreshed.");
        Add("Startup.ListRescanned", "Список автозавантаження перескановано.", "Startup list rescanned.");
        Add("Startup.ToggleDone", "Автозапуск для {0}: {1}", "Autostart for {0}: {1}");
        Add("Startup.EnabledWord", "Увімкнено", "Enabled");
        Add("Startup.PausedWord", "Призупинено", "Paused");

        // Cleaner — статуси, бейджі, повідомлення
        Add("Cleaner.SafeBadge", "🟢 БЕЗПЕЧНО", "🟢 SAFE");
        Add("Cleaner.ManualBadge", "🟡 РУЧНИЙ РЕЖИМ", "🟡 MANUAL MODE");
        Add("Cleaner.Scanning", "Сканування дискового простору...", "Scanning disk space...");
        Add("Cleaner.ScanDone", "Аналіз кешів завершено.", "Cache analysis completed.");
        Add("Cleaner.CleaningItem", "Очищення: {0}...", "Cleaning: {0}...");
        Add("Cleaner.Freed", "Звільнено: {0}", "Freed: {0}");
        Add("Cleaner.FoundSummary", "Виявлено для очищення: {0}", "Detected for cleanup: {0}");
        Add("Cleaner.CleanedOne", "Очищено: {0} (+{1})", "Cleaned: {0} (+{1})");
        Add("Cleaner.CleanDone", "Очищення завершено!", "Cleaning completed!");
        Add("Cleaner.TotalFreed", "Успішно звільнено: {0}", "Successfully freed: {0}");
        Add("Cleaner.ConfirmAll", "Очистити всі виявлені безпечні кеші та файли?", "Clean all detected safe caches and files?");
        Add("Cleaner.ConfirmAllTitle", "1-Click Очищення", "1-Click Cleanup");

        // Game Mode & MSI
        Add("GameMode.ToggleOn", "🎮 Game Mode: ON", "🎮 Game Mode: ON");
        Add("GameMode.ToggleOff", "🎮 Game Mode: OFF", "🎮 Game Mode: OFF");
        Add("GameMode.Activating", "⏳ Активація Game Mode...", "⏳ Activating Game Mode...");
        Add("GameMode.Deactivating", "⏳ Деактивація Game Mode...", "⏳ Deactivating Game Mode...");
        Add("GameMode.BtnPurge", "🧹 Очистити Standby RAM", "🧹 Purge Standby RAM");
        Add("GameMode.HowItWorksTitle", "📖 ЯК ЦЕ ПРАЦЮЄ", "📖 HOW IT WORKS");
        Add("GameMode.HowItWorks", "Game Mode зупиняє некритичні фонові служби (Windows Search, SysMain/Superfetch, телеметрія, апдейтери), піднімає пріоритет активного процесу до 100% (Realtime-клас) та перемикає систему на схему живлення Ultimate Performance (або High Performance) для мінімальних фризів і максимального FPS.", "Game Mode stops non-critical background services (Windows Search, SysMain/Superfetch, telemetry, updaters), raises the active process priority to 100% (Realtime class) and switches the system to the Ultimate Performance power plan (or High Performance) for minimal stutters and maximum FPS.");
        Add("GameMode.StandbyTitle", "🧹 STAND BY RAM PURGE", "🧹 STANDBY RAM PURGE");
        Add("GameMode.StandbyDesc", "Викликає нативний системний виклик NtSetSystemInformation (SystemMemoryListInformation → MemoryPurgeStandbyList), який скидає модифіковані та резервні (standby) сторінки пам'яті. Це усуває мікрофризи та звільняє оперативну пам'ять без перезавантаження.", "Calls the native system API NtSetSystemInformation (SystemMemoryListInformation → MemoryPurgeStandbyList), which flushes modified and standby memory pages. This eliminates micro-stutters and frees RAM without a reboot.");
        Add("GameMode.PurgeBusy", "⏳...", "⏳...");
        Add("GameMode.PurgeFreed", "✓ {0:N0} MB", "✓ {0:N0} MB");
        Add("GameMode.PurgeError", "⚠️ Не вдалося", "⚠️ Failed");
        Add("GameMode.PurgeOk", "Standby RAM очищено: {0:N0} MB", "Standby RAM purged: {0:N0} MB");
        Add("GameMode.PurgeFail", "Очищення Standby RAM не вдалося (потрібні права адміністратора).", "Standby RAM purge failed (administrator rights required).");

        Add("Msi.PriorityLabel", "Пріоритет:", "Priority:");
        Add("Msi.ProtectedDevice", "🔒 Захищено", "🔒 Protected");
        Add("Msi.ScanBusy", "⏳ Сканування...", "⏳ Scanning...");
        Add("Msi.Scanning", "Сканування PCI-пристроїв...", "Scanning PCI devices...");
        Add("Msi.StatsFormat", "Пристроїв: {0} | MSI Mode: {1} ({2}%)", "Devices: {0} | MSI Mode: {1} ({2}%)");
        Add("Msi.OptimizeDone", "1-Click Gaming MSI: оптимізовано {0} пристроїв.", "1-Click Gaming MSI: optimized {0} devices.");
        Add("Msi.RestoreDone", "Відновлено стандартний режим для {0} пристроїв.", "Restored default mode for {0} devices.");
        Add("Msi.ToggleFail", "Не вдалося змінити MSI для {0}.", "Failed to change MSI for {0}.");
        Add("Msi.TooltipTitle", "MSI Mode (Message Signaled Interrupts)", "MSI Mode (Message Signaled Interrupts)");
        Add("Msi.TooltipWhat", "MSI Mode — сучасний механізм переривань, коли пристрій надсилає повідомлення-переривання через шину PCIe замість загальної лінії IRQ. Це знижує затримки та навантаження на CPU під час ігор.", "MSI Mode is a modern interrupt mechanism where the device sends message interrupts over the PCIe bus instead of a shared IRQ line. It reduces latency and CPU load during gaming.");
        Add("Msi.TooltipPriority", "Пріоритет High означає, що переривання пристрою обробляються з найвищим пріоритетом у черзі DPC, що дає мінімальний ввід-вивід та плавний FPS.", "High priority means device interrupts are handled with the highest priority in the DPC queue, providing minimal I/O latency and smooth FPS.");
        Add("Msi.TooltipLimit", "Ліміт переривань (MessageNumberLimit) — кількість векторів переривань, які пристрій може використовувати для розпаралелювання обробки IRQ між ядрами CPU.", "Interrupt limit (MessageNumberLimit) is the number of interrupt vectors the device can use to parallelize IRQ processing across CPU cores.");

        // Diagnostic
        Add("Diagnostic.Title", "🔍 АПАРАТНИЙ АУДИТ ТА СЕНСОРИ ТЕЛЕМЕТРІЇ", "🔍 HARDWARE AUDIT & SYSTEM TELEMETRY");
        Add("Diagnostic.Subtitle", "Детальна інформація про апаратні вузли, термозони, кеш, контролери та підсистеми ПК", "Detailed information about hardware nodes, thermal zones, cache, controllers and PC subsystems");
        Add("Diagnostic.LiveBadge", "Live Telemetry", "Live Telemetry");
        Add("Diagnostic.CpuSection", "🔥 ПРОЦЕСОР (CPU) & ТЕРМОЗОНИ", "🔥 PROCESSOR (CPU) & THERMAL ZONES");
        Add("Diagnostic.GpuSection", "🎮 ВІДЕОКАРТА (GPU) & МОНІТОРИ", "🎮 VIDEO CARD (GPU) & MONITORS");
        Add("Diagnostic.RamSection", "⚡ ОПЕРАТИВНА ПАМ'ЯТЬ (RAM)", "⚡ RANDOM ACCESS MEMORY (RAM)");
        Add("Diagnostic.StorageSection", "💾 НАКОПИЧУВАЧІ ТА ТОМИ", "💾 STORAGE & VOLUMES");
        Add("Diagnostic.BoardSection", "🎛️ ПЛАТА, BIOS ТА МЕРЕЖА", "🎛️ BOARD, BIOS & NETWORK");
        Add("Diagnostic.LblModel", "Модель:", "Model:");
        Add("Diagnostic.LblConfig", "Конфігурація:", "Configuration:");
        Add("Diagnostic.LblSocket", "Сокет:", "Socket:");
        Add("Diagnostic.LblBaseFreq", "Базова частота:", "Base clock:");
        Add("Diagnostic.LblMaxFreq", "Макс. частота:", "Max clock:");
        Add("Diagnostic.LblCache", "Кеш L3 / L2:", "L3 / L2 cache:");
        Add("Diagnostic.LblVirtual", "Віртуалізація:", "Virtualization:");
        Add("Diagnostic.LblGpuModel", "Модель GPU:", "GPU model:");
        Add("Diagnostic.LblGpuVram", "Відеопам'ять:", "VRAM:");
        Add("Diagnostic.LblGpuBus", "Шина & ReBAR:", "Bus & ReBAR:");
        Add("Diagnostic.LblGpuDriver", "Драйвер:", "Driver:");
        Add("Diagnostic.LblGpuClockPower", "Частота / Споживання:", "Clock / Power:");
        Add("Diagnostic.LblGpuFan", "Кулери (Fan):", "Fans:");
        Add("Diagnostic.LblGpuDisplays", "Підключені дисплеї:", "Connected displays:");
        Add("Diagnostic.LblRamCapacity", "Обсяг пам'яті:", "Capacity:");
        Add("Diagnostic.LblRamLoad", "Завантаження RAM:", "RAM load:");
        Add("Diagnostic.LblRamSlots", "Слоти DIMM:", "DIMM slots:");
        Add("Diagnostic.LblRamModules", "Встановлені модулі:", "Installed modules:");
        Add("Diagnostic.LblBoardModel", "Материнська плата:", "Motherboard:");
        Add("Diagnostic.LblBios", "Версія BIOS:", "BIOS version:");
        Add("Diagnostic.LblNetAdapter", "Мережевий адаптер:", "Network adapter:");
        Add("Diagnostic.LblNetIp", "IPv4 / Пінг шлюзу:", "IPv4 / Gateway ping:");
        Add("Diagnostic.LblSecurity", "Безпека:", "Security:");
        Add("Diagnostic.BtnRefresh", "🔄 Оновити", "🔄 Refresh");
        Add("Diagnostic.BtnCopy", "📋 Скопіювати повний звіт", "📋 Copy full report");
        Add("Diagnostic.BtnSave", "💾 Зберегти звіт (.txt)", "💾 Save report (.txt)");
        Add("Diagnostic.BtnClose", "Закрити", "Close");
        Add("Diagnostic.CpuTempFormat", "Пакет: {0}", "Package: {0}");
        Add("Diagnostic.VrmTempFormat", "VRM: {0}", "VRM: {0}");
        Add("Diagnostic.BoardTempFormat", "Плата: {0}", "Board: {0}");
        Add("Diagnostic.GpuCoreTempFormat", "GPU: {0}", "GPU: {0}");
        Add("Diagnostic.GpuHotspotTempFormat", "Hotspot: {0}", "Hotspot: {0}");
        Add("Diagnostic.GpuVramTempFormat", "VRAM: {0}", "VRAM: {0}");
        Add("Diagnostic.CoresFormat", "{0} ядер / {1} потоків", "{0} cores / {1} threads");
        Add("Diagnostic.L3L2Format", "L3: {0} | L2: {1}", "L3: {0} | L2: {1}");
        Add("Diagnostic.FreeRamFormat", "{0} {1} (Вільно: {2})", "{0} {1} (Free: {2})");
        Add("Diagnostic.LoadFormat", "{0} ГБ ({1}%)", "{0} GB ({1}%)");
        Add("Diagnostic.SlotsFormat", "{0} з {1} слотів", "{0} of {1} slots");
        Add("Diagnostic.ModulesEmpty", "Модулі не визначені", "Modules not detected");
        Add("Diagnostic.PrimaryDisplay", "Основний монітор", "Primary monitor");
        Add("Diagnostic.DisplayPrimary", "Головний", "Primary");
        Add("Diagnostic.LocalDisk", "Локальний диск", "Local disk");
        Add("Diagnostic.VolumeFormat", "📁 {0}\\ [{1}] — {2} вільно з {3} ({4}%, {5})", "📁 {0}\\ [{1}] — {2} free of {3} ({4}%, {5})");
        Add("Diagnostic.DiskFormat", "• {0} — {1} ({2}) | {3}", "• {0} — {1} ({2}) | {3}");
        Add("Diagnostic.MtsUnit", "MT/s", "MT/s");
        Add("Diagnostic.MhzUnit", "GHz", "GHz");
        Add("Diagnostic.GpuIntegrated", "Вбудована (iGPU)", "Integrated (iGPU)");
        Add("Diagnostic.GpuDiscrete", "Дискретна (dGPU)", "Discrete (dGPU)");
        Add("Diagnostic.DriveNvme", "NVMe SSD", "NVMe SSD");
        Add("Diagnostic.DriveSataSsd", "SATA SSD", "SATA SSD");
        Add("Diagnostic.DriveHdd", "HDD", "HDD");
        Add("Diagnostic.DriveUsb", "USB-накопичувач", "USB drive");
        Add("Diagnostic.SmartStatus", "S.M.A.R.T.: {0}", "S.M.A.R.T.: {0}");
        Add("Diagnostic.SecureBootOn", "Увімкнено (UEFI)", "Enabled (UEFI)");
        Add("Diagnostic.SecureBootOff", "Вимкнено", "Disabled");
        Add("Diagnostic.VbsOn", "Увімкнено (VBS/Core Isolation)", "Enabled (VBS/Core Isolation)");
        Add("Diagnostic.VbsOff", "Вимкнено (Gaming Boost Mode)", "Disabled (Gaming Boost Mode)");
        Add("Diagnostic.TpmReady", "TPM 2.0 (Готовий)", "TPM 2.0 (Ready)");
        Add("Diagnostic.CpuVirtualOn", "Увімкнено (AMD-V / VT-x)", "Enabled (AMD-V / VT-x)");
        Add("Diagnostic.CpuVirtualOff", "Вимкнено в BIOS / не визначено", "Disabled in BIOS / not detected");
        Add("Diagnostic.NoResponse", "Немає відповіді", "No response");
        Add("Diagnostic.DirectConnection", "Пряме підключення", "Direct connection");
        Add("Diagnostic.UptimeFormat", "{0}д {1}год {2}хв", "{0}d {1}h {2}m");
        Add("Diagnostic.ErrorTitle", "⚠️ Помилка", "⚠️ Error");
        Add("Diagnostic.ErrorCollect", "Помилка опитування сенсорів: {0}", "Sensor polling error: {0}");

        Add("Diagnostic.ReportCopied", "Повний апаратний звіт успішно скопійовано в буфер обміну!", "Full hardware report copied to clipboard!");
        Add("Diagnostic.ReportSaved", "Звіт успішно збережено:\n{0}", "Report saved successfully:\n{0}");
        Add("Diagnostic.SaveError", "Помилка збереження файлу: {0}", "Failed to save file: {0}");
        Add("Diagnostic.ReportSeparator", "=========================================================================", "=========================================================================");
        Add("Diagnostic.ReportTitle", "MASLOOPTIMIZER // АПАРАТНИЙ АУДИТ ТА СЕНСОРИ ТЕЛЕМЕТРІЇ СИСТЕМИ", "MASLOOPTIMIZER // HARDWARE AUDIT & SYSTEM TELEMETRY");
        Add("Diagnostic.ReportDate", "Дата аудиту:", "Audit date:");
        Add("Diagnostic.ReportOs", "Операційна система:", "Operating system:");
        Add("Diagnostic.ReportUptime", "Час роботи (Uptime):", "Uptime:");
        Add("Diagnostic.ReportPower", "Схема живлення:", "Power plan:");
        Add("Diagnostic.ReportSecurity", "Безпека:", "Security:");
        Add("Diagnostic.ReportCpu", "🔥 [ПРОЦЕСОР, ТЕМПЕРАТУРИ ТА КЕШ]", "🔥 [PROCESSOR, TEMPERATURES & CACHE]");
        Add("Diagnostic.ReportCpuModel", "Модель CPU:", "CPU model:");
        Add("Diagnostic.ReportCpuConfig", "Конфігурація:", "Configuration:");
        Add("Diagnostic.ReportCpuCache", "Кеш пам'ять:", "Cache:");
        Add("Diagnostic.ReportGpu", "🎮 [ВІДЕОКАРТА ТА МОНІТОРИ]", "🎮 [VIDEO CARD & MONITORS]");
        Add("Diagnostic.ReportGpuVram", "Відеопам'ять (VRAM):", "VRAM:");
        Add("Diagnostic.ReportGpuTemps", "Температури GPU:", "GPU temperatures:");
        Add("Diagnostic.ReportGpuBus", "Шина та ReBAR:", "Bus & ReBAR:");
        Add("Diagnostic.ReportGpuDriver", "Драйвер / Fan / W:", "Driver / Fan / W:");
        Add("Diagnostic.ReportGpuDisplays", "Дисплеї:", "Displays:");
        Add("Diagnostic.ReportRam", "⚡ [ОПЕРАТИВНА ПАМ'ЯТЬ]", "⚡ [RANDOM ACCESS MEMORY]");
        Add("Diagnostic.ReportRamCapacity", "Обсяг / Тип:", "Capacity / Type:");
        Add("Diagnostic.ReportRamUsage", "Використання RAM:", "RAM usage:");
        Add("Diagnostic.ReportRamModules", "Модулі:", "Modules:");
        Add("Diagnostic.ReportStorage", "💾 [НАКОПИЧУВАЧІ ТА ТОМИ]", "💾 [STORAGE & VOLUMES]");
        Add("Diagnostic.ReportBoard", "🎛️ [МАТЕРИНСЬКА ПЛАТА]", "🎛️ [MOTHERBOARD]");
        Add("Diagnostic.ReportBoardInfo", "Плата:", "Board:");
        Add("Diagnostic.ReportBios", "Версія BIOS:", "BIOS version:");
        Add("Diagnostic.ReportNetwork", "🌐 [МЕРЕЖА]", "🌐 [NETWORK]");
        Add("Diagnostic.ReportNetAdapter", "Мережевий адаптер:", "Network adapter:");
        Add("Diagnostic.ReportNetIp", "IPv4 / Шлюз:", "IPv4 / Gateway:");

        // Categories (твікі, DNS, автозапуск, очищення)
        Add("Categories.Швидкість & Геймінг", "Швидкість & Геймінг", "Speed & Gaming");
        Add("Categories.Блокування реклами", "Блокування реклами", "Ad-Blocking");
        Add("Categories.Безпека & Захист", "Безпека & Захист", "Security & Protection");
        Add("Categories.Приватність", "Приватність", "Privacy");
        Add("Categories.Сімейний захист", "Сімейний захист", "Family Protection");
        Add("Categories.Драйвери & Залізо", "Драйвери & Залізо", "Drivers & Hardware");
        Add("Categories.Фонові оновлювачі", "Фонові оновлювачі", "Background Updaters");
        Add("Categories.Програми користувача", "Програми користувача", "User Applications");
        Add("Categories.Ігри & GPU", "Ігри & GPU", "Games & GPU");
        Add("Categories.Безпечний кеш", "Безпечний кеш", "Safe cache");
        Add("Categories.Браузери & Додатки", "Браузери & Додатки", "Browsers & Apps");
        Add("Categories.Дампи & Логи (Manual)", "Дампи & Логи (Manual)", "Dumps & Logs (Manual)");
        Add("Categories.Системне стиснення", "Системне стиснення", "System compression");





        return d;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}