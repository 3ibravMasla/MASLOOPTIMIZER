using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace MASLOOPTIMIZER;

/// <summary>Аргументи події зміни мови.</summary>
public sealed class LanguageChangedEventArgs : EventArgs
{
    public string OldLanguage { get; }
    public string NewLanguage { get; }

    public LanguageChangedEventArgs(string oldLanguage, string newLanguage)
    {
        OldLanguage = oldLanguage;
        NewLanguage = newLanguage;
    }
}

/// <summary>
/// Модульна локалізація.
/// Код мови = ім'я папки (UA, EN, ...). Модулі = {код}_{Module}.json у Languages\{Код}\.
/// Завантаження: EmbeddedResource спочатку, файли на диску перекривають (per-key override).
/// Ланцюг fallback: поточна мова → EN → UA → ключ.
/// </summary>
public class LocalizationManager : INotifyPropertyChanged
{
    private static readonly Lazy<LocalizationManager> _instance = new(() => new LocalizationManager());
    public static LocalizationManager Instance => _instance.Value;

    /// <summary>Базова мова (останній fallback у ланцюжку пошуку).</summary>
    public const string FallbackLanguage = "UA";

    /// <summary>Старий код мови до модульної структури (для сумісності зі збереженими налаштуваннями).</summary>
    private const string LegacyLanguageCode = "UK";

    /// <summary>Кеш плоских словників за кодом мови (модифікуються лише до публікації).</summary>
    private readonly Dictionary<string, Dictionary<string, string>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _languageNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _availableCodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _loggedMissingKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public string CurrentLanguage { get; private set; } = FallbackLanguage;
    public string CurrentLanguageName => GetLanguageName(CurrentLanguage);
    public List<string> AvailableLanguages { get; } = new();

    public static string LanguagesDirectory => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Languages");
    public static IReadOnlyList<string> LanguageSearchDirectories => _languageSearchDirectories;
    private static readonly List<string> _languageSearchDirectories = BuildLanguageSearchDirectories();

    public event EventHandler<LanguageChangedEventArgs>? LanguageChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    private LocalizationManager()
    {
        ScanAvailableLanguages();
        BuildAndCache(FallbackLanguage);
        BuildAndCache("EN");

        // Політика мови запуску (див. ResolveStartupLanguage):
        // RU-система → UA + незмінний замок; заблокована/збережена мова;
        // перший запуск → системна мова або EN.
        string startup = ResolveStartupLanguage();
        if (!HasLanguage(startup)) startup = FallbackLanguage;
        BuildAndCache(startup);
        CurrentLanguage = startup;

        VerifyIntegrity();
    }

    // ====================================================================
    //  ПОЛІТИКА МОВИ ЗАПУСКУ (RU → UA, замок, збережений вибір, система)
    // ====================================================================

    /// <summary>
    /// Визначає мову при кожному старті програми:
    /// 1) системна мова RU → примусово UA + LanguageLocked=true (незмінно);
    /// 2) інакше, якщо мова була заблокована раніше → збережена;
    /// 3) інакше → збережений вибір користувача;
    /// 4) інакше перший запуск → системна мова (якщо є в наборі) або EN.
    /// </summary>
    private string ResolveStartupLanguage()
    {
        try
        {
            string systemLang = GetSystemLanguageCode();

            if (string.Equals(systemLang, "RU", StringComparison.OrdinalIgnoreCase))
            {
                SettingsManager.SaveLanguage("UA");
                SettingsManager.SaveLanguageLocked(true);
                AppLogger.Log("Локалізація: виявлено російськомовну систему — примусово встановлено українську мову (LanguageLocked).", "INFO");
                return "UA";
            }

            if (SettingsManager.ReadLanguageLocked())
            {
                string locked = SettingsManager.ReadLanguage();
                return HasLanguage(locked) ? locked : FallbackLanguage;
            }

            string? saved = SettingsManager.ReadLanguageOptional();
            if (!string.IsNullOrWhiteSpace(saved) && HasLanguage(saved))
            {
                return saved;
            }

            // Перший запуск: системна мова, якщо вона є у доступному наборі; інакше EN.
            if (!string.IsNullOrWhiteSpace(systemLang) && HasLanguage(systemLang))
            {
                SettingsManager.SaveLanguage(systemLang);
                return systemLang;
            }

            SettingsManager.SaveLanguage("EN");
            return "EN";
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Локалізація: помилка визначення мови запуску — {ex.Message}", "ERROR");
            return FallbackLanguage;
        }
    }

    /// <summary>Код системної мови (UA, RU, EN, DE, ...) через P/Invoke GetUserDefaultUILanguage.</summary>
    private static string GetSystemLanguageCode()
    {
        try
        {
            ushort langId = GetUserDefaultUILanguage();
            if (langId == 0) return string.Empty;

            var culture = System.Globalization.CultureInfo.GetCultureInfo(langId);
            string iso = culture.TwoLetterISOLanguageName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(iso)) return string.Empty;
            if (string.Equals(iso, "uk", StringComparison.OrdinalIgnoreCase)) return "UA";
            return iso.ToUpperInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern ushort GetUserDefaultUILanguage();

    // ====================================================================
    //  НОВИЙ API
    // ====================================================================

    /// <summary>Отримує рядок за ключем. Fallback: поточна → EN → UA → ключ.</summary>
    public string T(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return string.Empty;

        if (TryGetFromLanguage(CurrentLanguage, key, out var val)) return val;

        if (!string.Equals("EN", CurrentLanguage, StringComparison.OrdinalIgnoreCase) &&
            TryGetFromLanguage("EN", key, out var en) && !string.IsNullOrEmpty(en))
        {
            return en;
        }

        if (!string.Equals(FallbackLanguage, CurrentLanguage, StringComparison.OrdinalIgnoreCase) &&
            TryGetFromLanguage(FallbackLanguage, key, out var ua) && !string.IsNullOrEmpty(ua))
        {
            return ua;
        }

        LogMissingKey(key);
        return key;
    }

    /// <summary>Отримує рядок у межах модуля: module + "." + key (напр. T("BackupEngine", "Title")).</summary>
    public string T(string module, string key) => T(JoinKey(module, key));

    /// <summary>Безпечний пошук: false замість сирого ключа.</summary>
    public bool TryT(string key, out string value)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            if (TryGetFromLanguage(CurrentLanguage, key, out var v) && !string.IsNullOrEmpty(v))
            {
                value = v;
                return true;
            }

            if (!string.Equals("EN", CurrentLanguage, StringComparison.OrdinalIgnoreCase) &&
                TryGetFromLanguage("EN", key, out var en) && !string.IsNullOrEmpty(en))
            {
                value = en;
                return true;
            }

            if (!string.Equals(FallbackLanguage, CurrentLanguage, StringComparison.OrdinalIgnoreCase) &&
                TryGetFromLanguage(FallbackLanguage, key, out var ua) && !string.IsNullOrEmpty(ua))
            {
                value = ua;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    /// <summary>Безпечний пошук у межах модуля.</summary>
    public bool TryT(string module, string key, out string value) => TryT(JoinKey(module, key), out value);

    /// <summary>Доступ до рядків модуля через індексатор: For("BackupEngine")["Title"].</summary>
    public ModuleStrings For(string module) => new(module ?? string.Empty);

    /// <summary>Форматує рядок за ключем (string.Format).</summary>
    public string Format(string key, params object[] args)
    {
        try
        {
            string template = T(key);
            return args is null || args.Length == 0 ? template : string.Format(template, args);
        }
        catch
        {
            return T(key);
        }
    }

    /// <summary>Форматує рядок у межах модуля.</summary>
    public string Format(string module, string key, params object[] args) => Format(JoinKey(module, key), args);

    // ====================================================================
    //  СУМІСНІСТЬ З ПОПЕРЕДНЬОЮ ВЕРСІЄЮ
    // ====================================================================

    /// <summary>Застарілий індексатор (еквівалент T(key)).</summary>
    public string this[string key] => T(key);

    /// <summary>Застарілий TryGet (еквівалент TryT).</summary>
    public bool TryGet(string key, out string value) => TryT(key, out value);

    /// <summary>Локалізований текст твіка; fallback — оригінал з tweaks.bundle.json.</summary>
    public string GetTweakText(string tweakId, string field, string bundleText)
    {
        if (string.IsNullOrWhiteSpace(tweakId)) return bundleText;
        return TryT($"Tweaks.{tweakId}.{field}", out var v) ? v : bundleText;
    }

    // ====================================================================
    //  КЕРУВАННЯ МОВАМИ
    // ====================================================================

    /// <summary>Завантажує мову (нормалізація UK→UA; lock блокує зміну).</summary>
    public void LoadLanguage(string langCode, bool persist = true)
    {
        string requested = NormalizeCode(langCode);
        if (requested.Length == 0) requested = FallbackLanguage;

        if (SettingsManager.ReadLanguageLocked() &&
            !string.Equals(requested, CurrentLanguage, StringComparison.OrdinalIgnoreCase))
        {
            AppLogger.Log($"Локалізація: зміна мови заблокована (LanguageLocked) — залишаємо {CurrentLanguage}", "WARN");
            return;
        }

        if (!HasLanguage(requested))
        {
            AppLogger.Log($"Локалізація: мову '{requested}' не знайдено, fallback на {FallbackLanguage}", "WARN");
            requested = FallbackLanguage;
        }

        string old = CurrentLanguage;
        BuildAndCache(requested);
        CurrentLanguage = requested;

        if (persist) SettingsManager.SaveLanguage(requested);

        if (!string.Equals(old, requested, StringComparison.OrdinalIgnoreCase))
        {
            LanguageChanged?.Invoke(this, new LanguageChangedEventArgs(old, requested));
        }

        OnPropertyChanged("Item[]");
        OnPropertyChanged(nameof(CurrentLanguage));
        OnPropertyChanged(nameof(CurrentLanguageName));
    }

    /// <summary>Код наступної доступної мови для циклічного перемикання (без зміни).</summary>
    public string NextLanguage()
    {
        if (SettingsManager.ReadLanguageLocked()) return CurrentLanguage;
        if (AvailableLanguages.Count == 0) return FallbackLanguage;

        int index = AvailableLanguages.IndexOf(CurrentLanguage);
        int next = index < 0 ? 0 : (index + 1) % AvailableLanguages.Count;
        return AvailableLanguages[next];
    }

    /// <summary>Відображувана назва мови (LanguageName з App-модуля) або код.</summary>
    public string GetLanguageName(string code)
    {
        string c = NormalizeCode(code);
        return _languageNames.TryGetValue(c, out var name) && !string.IsNullOrWhiteSpace(name) ? name : c;
    }

    /// <summary>Сканує доступні мови: папки Languages\{Код} + EmbeddedResource Languages.{Код}.*.</summary>
    public void ScanAvailableLanguages()
    {
        _availableCodes.Clear();
        _languageNames.Clear();

        foreach (var code in ScanLanguageCodes())
        {
            if (!string.IsNullOrWhiteSpace(code)) _availableCodes.Add(code);
        }

        // Гарантовані вбудовані мови
        _availableCodes.Add(FallbackLanguage);
        _availableCodes.Add("EN");

        foreach (var code in _availableCodes)
        {
            _languageNames[code] = ResolveLanguageName(code);
        }

        AvailableLanguages.Clear();
        AvailableLanguages.AddRange(_availableCodes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
    }

    public bool HasLanguage(string code)
    {
        string c = NormalizeCode(code);
        return !string.IsNullOrEmpty(c) &&
               (_availableCodes.Contains(c) || ScanLanguageCodes().Contains(c));
    }

    // ====================================================================
    //  ВНУТРІШНЯ ЛОГІКА ЗАВАНТАЖЕННЯ
    // ====================================================================

    private bool TryGetFromLanguage(string code, string key, out string value)
    {
        if (_cache.TryGetValue(code, out var dict) && dict.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v))
        {
            value = v;
            return true;
        }
        value = string.Empty;
        return false;
    }

    private void LogMissingKey(string key)
    {
        lock (_loggedMissingKeys)
        {
            if (_loggedMissingKeys.Add(key))
            {
                AppLogger.Log($"Локалізація: пропущений ключ '{key}'", "WARN");
            }
        }
    }

    private void BuildAndCache(string code)
    {
        lock (_gate)
        {
            if (_cache.ContainsKey(code)) return;
            _cache[code] = BuildDictionary(code);
        }
    }

    /// <summary>Об'єднує всі модулі мови у плоский словник з перевіркою дублікатів.</summary>
    private Dictionary<string, string> BuildDictionary(string code)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var module in GetModuleNames(code))
        {
            var local = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // EmbeddedResource спочатку...
            string? embedded = ReadEmbeddedModule(code, module);
            if (!string.IsNullOrWhiteSpace(embedded))
            {
                FlattenModule(embedded, code, module, local);
            }

            // ...потім диск перекриває (per-key override)
            string? diskPath = FindModuleFile(code, module);
            if (diskPath != null)
            {
                try
                {
                    string diskJson = File.ReadAllText(diskPath);
                    if (!string.IsNullOrWhiteSpace(diskJson))
                    {
                        FlattenModule(diskJson, code, module, local);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"Локалізація: помилка читання модуля {code}/{module} з диску: {ex.Message}", "ERROR");
                }
            }

            foreach (var kv in local)
            {
                if (!result.TryAdd(kv.Key, kv.Value))
                {
                    AppLogger.Log($"Локалізація: дублікат ключа '{kv.Key}' між модулями ({code})", "WARN");
                }
            }
        }

        return result;
    }

    private static void FlattenModule(string json, string code, string module, Dictionary<string, string> target)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            FlattenJsonElement(doc.RootElement, string.Empty, target);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Локалізація: помилка парсингу модуля {code}/{module}: {ex.Message}", "ERROR");
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

    /// <summary>Імена модулів (без мовного префікса) для мови: embedded ∪ disk.</summary>
    private IEnumerable<string> GetModuleNames(string code)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string prefix = code.ToLowerInvariant() + "_";
        var assembly = Assembly.GetExecutingAssembly();

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (TryParseEmbeddedModule(resourceName, code, prefix, out var module)) names.Add(module);
        }

        foreach (var dir in LanguageSearchDirectories)
        {
            try
            {
                string langDir = Path.Combine(dir, code);
                if (!Directory.Exists(langDir)) continue;

                foreach (var f in Directory.GetFiles(langDir, prefix + "*.json"))
                {
                    string baseName = Path.GetFileNameWithoutExtension(f);
                    if (baseName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        string module = baseName.Substring(prefix.Length);
                        if (module.Length > 0) names.Add(module);
                    }
                }
            }
            catch { }
        }

        return names;
    }

    /// <summary>Читає вшитий модуль {code}/{prefix}_{module}.json (EmbeddedResource).</summary>
    private static string? ReadEmbeddedModule(string code, string module)
    {
        string prefix = code.ToLowerInvariant() + "_";
        string expected = $"Languages.{code}.{prefix}{module}.json";
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(r => r.EndsWith(expected, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(resourceName)) return null;

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Шукає модуль на диску у всіх зареєстрованих папках Languages\{Код}\.</summary>
    private static string? FindModuleFile(string code, string module)
    {
        string prefix = code.ToLowerInvariant() + "_";
        string fileName = prefix + module + ".json";
        foreach (var dir in LanguageSearchDirectories)
        {
            try
            {
                string candidate = Path.Combine(dir, code, fileName);
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }
        return null;
    }

    /// <summary>JSON модуля: спершу disk (override), інакше embedded.</summary>
    private static string? LoadModuleJson(string code, string module)
    {
        string? diskPath = FindModuleFile(code, module);
        if (diskPath != null)
        {
            try { return File.ReadAllText(diskPath); } catch { }
        }
        return ReadEmbeddedModule(code, module);
    }

    /// <summary>Назва мови з App-модуля (LanguageName), fallback — код.</summary>
    private string ResolveLanguageName(string code)
    {
        try
        {
            if (_cache.TryGetValue(code, out var dict) &&
                dict.TryGetValue("LanguageName", out var cached) && !string.IsNullOrWhiteSpace(cached))
            {
                return cached;
            }

            string? json = LoadModuleJson(code, "App");
            if (!string.IsNullOrWhiteSpace(json))
            {
                string name = ParseLanguageName(json);
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
        }
        catch { }
        return code;
    }

    private static string ParseLanguageName(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("LanguageName", out var name) && name.ValueKind == JsonValueKind.String)
            {
                return name.GetString() ?? string.Empty;
            }
        }
        catch { }
        return string.Empty;
    }

    /// <summary>Всі коди мов: папки Languages\{Код} + embedded Languages.{Код}.*.</summary>
    private static IEnumerable<string> ScanLanguageCodes()
    {
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in LanguageSearchDirectories)
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    string code = Path.GetFileName(sub).Trim().ToUpperInvariant();
                    if (string.IsNullOrWhiteSpace(code)) continue;
                    string prefix = code.ToLowerInvariant() + "_";
                    if (Directory.GetFiles(sub, prefix + "*.json").Length > 0) codes.Add(code);
                }
            }
            catch { }
        }

        var assembly = Assembly.GetExecutingAssembly();
        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (TryParseEmbeddedLanguageCode(resourceName, out var code)) codes.Add(code);
        }

        return codes;
    }

    /// <summary>Парсить {ns}.Languages.{Код}.{module}.json → код мови.</summary>
    private static bool TryParseEmbeddedLanguageCode(string resourceName, out string code)
    {
        code = string.Empty;
        const string marker = ".Languages.";
        int idx = resourceName.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return false;

        string rest = resourceName.Substring(idx + marker.Length);
        int dot = rest.IndexOf('.');
        if (dot <= 0) return false;

        string c = rest.Substring(0, dot).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(c)) return false;
        code = c;
        return true;
    }

    /// <summary>Парсить {ns}.Languages.{Код}.{prefix}{module}.json → module (без префікса).</summary>
    private static bool TryParseEmbeddedModule(string resourceName, string code, string prefix, out string module)
    {
        module = string.Empty;
        const string marker = ".Languages.";
        int idx = resourceName.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return false;

        string rest = resourceName.Substring(idx + marker.Length);
        int dot = rest.IndexOf('.');
        if (dot <= 0) return false;
        if (!string.Equals(rest.Substring(0, dot), code, StringComparison.OrdinalIgnoreCase)) return false;

        string filePart = rest.Substring(dot + 1);
        if (!filePart.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;

        string baseName = filePart.Substring(0, filePart.Length - ".json".Length);
        if (!baseName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        module = baseName.Substring(prefix.Length);
        return module.Length > 0;
    }

    private static string JoinKey(string module, string key)
        => string.IsNullOrWhiteSpace(module) ? key : $"{module}.{key}";

    private static string NormalizeCode(string? code)
    {
        string c = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (c.Length == 0) return FallbackLanguage;
        if (string.Equals(c, LegacyLanguageCode, StringComparison.OrdinalIgnoreCase)) return FallbackLanguage;
        return c;
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

    /// <summary>Само-перевірка: ключі-маркери мають бути у вбудованих модулях.</summary>
    private void VerifyIntegrity()
    {
        try
        {
            bool uaOk = _cache.TryGetValue(FallbackLanguage, out var ua) && ua.ContainsKey("Common.AllCategories");
            bool enOk = _cache.TryGetValue("EN", out var en) && en.ContainsKey("Common.AllCategories");

            if (!uaOk || !enOk)
            {
                AppLogger.Log(
                    "Локалізація: вбудовані мовні модулі не завантажено! Перевірте EmbeddedResource Languages/**/*.json",
                    "ERROR");
            }
        }
        catch { }
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Доступ до рядків одного модуля: For("BackupEngine")["Title"].</summary>
public sealed class ModuleStrings
{
    private readonly string _module;

    internal ModuleStrings(string module) => _module = module;

    public string this[string key] => LocalizationManager.Instance.T(_module, key);
    public bool TryGet(string key, out string value) => LocalizationManager.Instance.TryT(_module, key, out value);
    public string Format(string key, params object[] args) => LocalizationManager.Instance.Format(_module, key, args);
}

