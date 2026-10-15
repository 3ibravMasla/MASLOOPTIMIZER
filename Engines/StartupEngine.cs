using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;
using Brush = System.Windows.Media.Brush;
using Microsoft.Win32;

namespace MASLOOPTIMIZER;

public enum StartupSortMode
{
    Default,
    EnabledFirst,
    DisabledFirst,
    NameAscending,
    NameDescending,
    Category,
    Source
}

public class StartupStats
{
    public int Total { get; set; }
    public int Enabled { get; set; }
    public int Disabled => Total - Enabled;
    public int UserAppsCount { get; set; }
    public int HardwareDriversCount { get; set; }
    public int UpdatersCount { get; set; }
    public int TasksCount { get; set; }
    public double OptimizationPercentage => Total > 0 ? Math.Round((Disabled / (double)Total) * 100, 1) : 0;
}

/// <summary>Запис автозапуску з чистими метаданими (шлях, видавець, джерело, вплив).</summary>
public class StartupItem : INotifyPropertyChanged, IWeakEventListener
{
    public StartupItem()
    {
        // Слабка підписка на зміну мови: статичний синглтон не утримує об'єкт.
        PropertyChangedEventManager.AddListener(LocalizationManager.Instance, this, string.Empty);
    }

    bool IWeakEventListener.ReceiveWeakEvent(Type managerType, object sender, EventArgs e)
    {
        if (managerType == typeof(PropertyChangedEventManager))
        {
            OnLocalizationChanged(sender, (PropertyChangedEventArgs)e);
            return true;
        }

        return false;
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ButtonText));
        OnPropertyChanged(nameof(SafetyBadge));
        OnPropertyChanged(nameof(ProtectedBadge));
        OnPropertyChanged(nameof(CategoryLocalized));
        OnPropertyChanged(nameof(SourceLocalized));
        OnPropertyChanged(nameof(StartupImpactLocalized));
        OnPropertyChanged(nameof(OpenFolderText));
    }

    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Publisher { get; set; } = "Невідомий видавець";
    public string Source { get; set; } = "Реєстр (HKCU)";
    public string Category { get; set; } = "Програми користувача";
    public string KeyPath { get; set; } = string.Empty;
    public string DisabledKeyPath { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string TaskPath { get; set; } = string.Empty;
    public string EntryType { get; set; } = "Reg"; // Reg, File, Task

    /// <summary>Чистий шлях до виконуваного файлу (для перегляду та оцінки впливу).</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>Оцінений вплив на старт: High / Medium / Low.</summary>
    public string StartupImpact { get; set; } = "Low";

    /// <summary>true для HKCU та папки користувача; false для HKLM та спільної папки.</summary>
    public bool IsUserScope { get; set; } = true;

    public bool IsCritical { get; set; } = false;

    /// <summary>Чіп-група джерела для фільтрації: user / system / task / folder.</summary>
    public string SourceGroup
    {
        get
        {
            if (EntryType == "Task" ||
                Source.Contains("Планувальник", StringComparison.OrdinalIgnoreCase) ||
                Source.Contains("Task", StringComparison.OrdinalIgnoreCase))
            {
                return "task";
            }
            if (EntryType == "File" ||
                Source.Contains("Папка", StringComparison.OrdinalIgnoreCase) ||
                Source.Contains("Startup Folder", StringComparison.OrdinalIgnoreCase))
            {
                return "folder";
            }
            if (IsUserScope ||
                Source.Contains("HKCU", StringComparison.OrdinalIgnoreCase) ||
                Source.Contains("CurrentUser", StringComparison.OrdinalIgnoreCase))
            {
                return "user";
            }
            return "system";
        }
    }

    public string CategoryLocalized
        => LocalizationManager.Instance.TryGet($"Categories.{Category}", out var cat) && !string.IsNullOrWhiteSpace(cat)
            ? cat
            : Category;

    public string SourceLocalized
    {
        get
        {
            string key = SourceGroup switch
            {
                "user" => "Startup.SourceUserRun",
                "system" => "Startup.SourceSystemRun",
                "task" => "Startup.SourceTaskScheduler",
                "folder" => "Startup.SourceStartupFolder",
                _ => "Startup.SourceUserRun"
            };
            return LocalizationManager.Instance.TryGet(key, out var src) && !string.IsNullOrWhiteSpace(src) ? src : Source;
        }
    }

    public string StartupImpactLocalized
    {
        get
        {
            string key = StartupImpact switch
            {
                "High" => "Startup.ImpactHigh",
                "Medium" => "Startup.ImpactMedium",
                _ => "Startup.ImpactLow"
            };
            return LocalizationManager.Instance.TryGet(key, out var txt) && !string.IsNullOrWhiteSpace(txt) ? txt : StartupImpact;
        }
    }

    public string OpenFolderText => LocalizationManager.Instance["Startup.BtnOpenFolder"];

    private bool _isEnabled = true;
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(ButtonText));
                OnPropertyChanged(nameof(ButtonBg));
            }
        }
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy != value)
            {
                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ButtonText));
            }
        }
    }

    public string StatusText => IsEnabled
        ? LocalizationManager.Instance["Startup.StatusActive"]
        : LocalizationManager.Instance["Startup.StatusPaused"];
    public Brush StatusColor => IsEnabled ? ThemeEngine.Brush("SuccessBrush") : ThemeEngine.Brush("StatusNeutralBrush");
    public Brush ImpactColor => StartupImpact switch
    {
        "High" => ThemeEngine.Brush("DangerBrush"),
        "Medium" => ThemeEngine.Brush("StatusNeutralBrush"),
        _ => ThemeEngine.Brush("SuccessBrush")
    };

    public string SafetyBadge => IsCritical
        ? LocalizationManager.Instance["Startup.BadgeProtected"]
        : LocalizationManager.Instance["Startup.BadgeSafe"];

    /// <summary>Статус захищеного системного процесу (Protected).</summary>
    public string ProtectedBadge => IsCritical
        ? LocalizationManager.Instance["Startup.StatusProtected"]
        : string.Empty;

    public string ButtonText
    {
        get
        {
            var loc = LocalizationManager.Instance;
            if (IsBusy) return loc["Common.Busy"];
            if (IsCritical) return loc["Startup.BtnProtected"];
            return IsEnabled ? loc["Startup.BtnPause"] : loc["Startup.BtnEnable"];
        }
    }

    public Brush ButtonBg => IsCritical ? ThemeEngine.Brush("StatusNeutralBrush") : (IsEnabled ? ThemeEngine.Brush("DangerBrush") : ThemeEngine.Brush("SuccessBrush"));

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class StartupEngine
{
    private static readonly string[] CriticalSystemNames = new[]
    {
        "SecurityHealth", "WindowsDefender", "ctfmon", "SystemTray", "IgfxTray",
        "RTHDVCPL", "Audiodg", "Explorer", "StartMenuExperienceHost"
    };

    private static readonly string[] HardwareKeywords = new[]
    {
        "nvidia", "nvvsvc", "nvcontainer", "amd", "radeon", "realtek", "intel",
        "razer", "logitech", "lghub", "steelseries", "corsair", "icue", "asus",
        "armoury", "msi", "dragoncenter", "nahimic", "soundblaster", "creative"
    };

    private static readonly string[] UpdaterKeywords = new[]
    {
        "update", "updater", "googleupdate", "edgeupdate", "adobeupdate",
        "onedriveupdate", "autoupdate", "helper"
    };

    private static readonly string[] HeavyImpactApps = new[]
    {
        "steam", "epicgames", "epic games", "discord", "adobe", "teams",
        "spotify", "onedrive", "telegram", "dropbox", "docker"
    };

    // StartupApproved: 0x02 / 0x06 = увімкнено, 0x03 = вимкнено (стандарт Task Manager Win10/11).
    private static readonly byte[] ApprovedEnabledData = { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
    private static readonly byte[] ApprovedDisabledData = { 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

    private const string ApprovedRunUser = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedRunMachine = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedRun32Machine = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
    private const string ApprovedStartupFolder = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    #region Отримання записів автозапуску

    public static async Task<List<StartupItem>> GetStartupEntriesAsync()
    {
        return await Task.Run(GetStartupEntries);
    }

    public static List<StartupItem> GetStartupEntries()
    {
        var list = new List<StartupItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        const string runPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string runOncePath = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
        const string runDisabled = @"Software\Microsoft\Windows\CurrentVersion\Run_Disabled";
        const string runOnceDisabled = @"Software\Microsoft\Windows\CurrentVersion\RunOnce_Disabled";

        const string hklmRun = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string hklmRunOnce = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";
        const string hklmRunDisabled = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run_Disabled";
        const string hklmRunOnceDisabled = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce_Disabled";

        const string wowRun = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
        const string wowRunOnce = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce";
        const string wowRunDisabled = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run_Disabled";
        const string wowRunOnceDisabled = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce_Disabled";

        // 1. HKCU Run / RunOnce
        ReadRegistryKey(Registry.CurrentUser, runPath, runDisabled, "Реєстр (HKCU)", true, ApprovedRunUser, null, seen, list);
        ReadRegistryKey(Registry.CurrentUser, runOncePath, runOnceDisabled, "Реєстр (HKCU)", true, ApprovedRunUser, null, seen, list);

        // 2. HKLM Run / RunOnce (64-bit)
        ReadRegistryKey(Registry.LocalMachine, hklmRun, hklmRunDisabled, "Реєстр (HKLM 64-bit)", false, ApprovedRunMachine, null, seen, list);
        ReadRegistryKey(Registry.LocalMachine, hklmRunOnce, hklmRunOnceDisabled, "Реєстр (HKLM 64-bit)", false, ApprovedRunMachine, null, seen, list);

        // 3. HKLM WOW6432Node Run / RunOnce (32-bit)
        ReadRegistryKey(Registry.LocalMachine, wowRun, wowRunDisabled, "Реєстр (HKLM 32-bit)", false, ApprovedRun32Machine, null, seen, list);
        ReadRegistryKey(Registry.LocalMachine, wowRunOnce, wowRunOnceDisabled, "Реєстр (HKLM 32-bit)", false, ApprovedRun32Machine, null, seen, list);

        // 4. Застарілі ключі *_Disabled, куди застосунок переносив записи раніше.
        ReadRegistryKey(Registry.CurrentUser, runDisabled, runPath, "Реєстр (HKCU)", true, ApprovedRunUser, false, seen, list);
        ReadRegistryKey(Registry.LocalMachine, hklmRunDisabled, hklmRun, "Реєстр (HKLM 64-bit)", false, ApprovedRunMachine, false, seen, list);
        ReadRegistryKey(Registry.LocalMachine, wowRunDisabled, wowRun, "Реєстр (HKLM 32-bit)", false, ApprovedRun32Machine, false, seen, list);

        // 5. Папки автозапуску (User та Common)
        ScanStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Папка Автозапуску (User)", true, list);
        ScanStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Папка Автозапуску (System)", false, list);

        // 6. Сторонні завдання Планувальника (швидке читання XML, без schtasks).
        ScanScheduledTasks(list);

        return list.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    #endregion

    #region Контекстне сортування, фільтрація та статистика

    public static IEnumerable<StartupItem> GetFilteredAndSortedEntries(
        IEnumerable<StartupItem> sourceList,
        string? category = null,
        string? searchQuery = null,
        StartupSortMode sortMode = StartupSortMode.Default,
        string? sourceGroup = null)
    {
        var query = sourceList.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(category) && !category.Equals("Всі", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(e => string.Equals(e.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(sourceGroup) && !sourceGroup.Equals("Всі", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(e => string.Equals(e.SourceGroup, sourceGroup, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            string q = searchQuery.Trim();
            query = query.Where(e =>
                e.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                e.Command.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                e.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                e.Source.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        return sortMode switch
        {
            StartupSortMode.EnabledFirst => query.OrderByDescending(e => e.IsEnabled).ThenBy(e => e.Name),
            StartupSortMode.DisabledFirst => query.OrderBy(e => e.IsEnabled).ThenBy(e => e.Name),
            StartupSortMode.NameAscending => query.OrderBy(e => e.Name),
            StartupSortMode.NameDescending => query.OrderByDescending(e => e.Name),
            StartupSortMode.Category => query.OrderBy(e => e.Category).ThenBy(e => e.Name),
            StartupSortMode.Source => query.OrderBy(e => e.Source).ThenBy(e => e.Name),
            _ => query.OrderBy(e => e.Name)
        };
    }

    /// <summary>Чіп-групи джерел автозапуску: user / system / task / folder (All перший).</summary>
    public static IReadOnlyList<string> GetSourceGroups() => new[] { "Всі", "user", "system", "task", "folder" };

    public static List<string> GetCategories(IEnumerable<StartupItem> sourceList)
    {
        var categories = sourceList.Select(e => e.Category).Distinct().OrderBy(c => c).ToList();
        categories.Insert(0, "Всі");
        return categories;
    }

    public static StartupStats GetStatistics(IEnumerable<StartupItem> sourceList)
    {
        var list = sourceList.ToList();
        return new StartupStats
        {
            Total = list.Count,
            Enabled = list.Count(e => e.IsEnabled),
            UserAppsCount = list.Count(e => e.Category == "Програми користувача"),
            HardwareDriversCount = list.Count(e => e.Category == "Драйвери & Залізо"),
            UpdatersCount = list.Count(e => e.Category == "Фонові оновлювачі"),
            TasksCount = list.Count(e => e.Category == "Планувальник завдань")
        };
    }

    #endregion

    #region Зчитування реєстру, папок та планувальника

    private static void ReadRegistryKey(
        RegistryKey root,
        string subKey,
        string altKey,
        string source,
        bool isUser,
        string approvedSub,
        bool? forceEnabled,
        HashSet<string> seen,
        List<StartupItem> list)
    {
        try
        {
            using var key = root.OpenSubKey(subKey, false);
            if (key == null) return;

            foreach (var name in key.GetValueNames())
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (name.StartsWith("PS", StringComparison.OrdinalIgnoreCase)) continue;

                string cmd = key.GetValue(name)?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(cmd)) continue;
                if (!seen.Add($"{source}|{name}")) continue;

                bool enabled;
                if (forceEnabled.HasValue)
                {
                    enabled = forceEnabled.Value;
                }
                else if (!TryGetStartupApprovedEnabled(root, approvedSub, name, out enabled))
                {
                    // Немає запису StartupApproved — перевіряємо застарілий ключ *_Disabled.
                    bool inLegacy = false;
                    try
                    {
                        using var alt = root.OpenSubKey(altKey, false);
                        inLegacy = alt?.GetValue(name) != null;
                    }
                    catch { }
                    enabled = !inLegacy;
                }

                string exe = ExtractExecutablePath(cmd);
                var (cat, isCrit, fallbackPub) = ClassifyStartupItem(name, cmd);
                string pub = ResolvePublisher(exe);

                list.Add(new StartupItem
                {
                    Name = name,
                    Command = cmd,
                    Source = source,
                    Category = cat,
                    KeyPath = subKey,
                    DisabledKeyPath = altKey,
                    EntryType = "Reg",
                    IsEnabled = enabled,
                    IsCritical = isCrit,
                    IsUserScope = isUser,
                    ExecutablePath = exe,
                    Publisher = string.IsNullOrWhiteSpace(pub) ? fallbackPub : pub,
                    StartupImpact = EstimateImpact(name, exe)
                });
            }
        }
        catch { }
    }

    private static void ScanStartupFolder(string folderPath, string source, bool isUser, List<StartupItem> list)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return;

        try
        {
            var dir = new DirectoryInfo(folderPath);
            var root = isUser ? Registry.CurrentUser : Registry.LocalMachine;

            foreach (var file in dir.GetFiles())
            {
                if (file.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) ||
                    file.Name.Contains("AutorunsDisabled", StringComparison.OrdinalIgnoreCase))
                    continue;

                bool legacyDisabled = file.Extension.Equals(".disabled", StringComparison.OrdinalIgnoreCase);
                string cleanName = legacyDisabled ? Path.GetFileNameWithoutExtension(file.Name) : file.Name;

                if (!TryGetStartupApprovedEnabled(root, ApprovedStartupFolder, cleanName, out bool enabled))
                {
                    enabled = !legacyDisabled;
                }

                string target = TryResolveShortcutTarget(file.FullName);
                string cmd = string.IsNullOrWhiteSpace(target) ? file.FullName : target;
                var (cat, isCrit, fallbackPub) = ClassifyStartupItem(cleanName, cmd);
                string pub = ResolvePublisher(target);

                list.Add(new StartupItem
                {
                    Name = cleanName,
                    Command = cmd,
                    Source = source,
                    Category = cat,
                    FilePath = file.FullName,
                    EntryType = "File",
                    IsEnabled = enabled,
                    IsCritical = isCrit,
                    IsUserScope = isUser,
                    ExecutablePath = target,
                    Publisher = string.IsNullOrWhiteSpace(pub) ? fallbackPub : pub,
                    StartupImpact = EstimateImpact(cleanName, target)
                });
            }
        }
        catch { }
    }

    /// <summary>
    /// Швидке (<20 мс) читання сторонніх задач Планувальника: XML-файли у System32\Tasks
    /// з пропуском піддерева \Microsoft. Як резерв — реєстровий кеш TaskCache\Tree.
    /// </summary>
    private static void ScanScheduledTasks(List<StartupItem> list)
    {
        try
        {
            string tasksRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Tasks");
            if (Directory.Exists(tasksRoot))
            {
                foreach (var file in EnumerateTaskFiles(tasksRoot))
                {
                    var item = TryBuildTaskItem(tasksRoot, file);
                    if (item != null) list.Add(item);
                }
            }

            if (!list.Any(e => e.EntryType == "Task"))
            {
                ScanScheduledTasksFromRegistry(list);
            }
        }
        catch { }
    }

    /// <summary>Рекурсивно перелічує файли задач, повністю пропускаючи каталог \Microsoft.</summary>
    private static IEnumerable<string> EnumerateTaskFiles(string root)
    {
        var result = new List<string>();

        void Recurse(string dir)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
                {
                    result.Add(file);
                }
            }
            catch { }

            try
            {
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    string name = Path.GetFileName(sub);
                    if (string.Equals(name, "Microsoft", StringComparison.OrdinalIgnoreCase)) continue;
                    Recurse(sub);
                }
            }
            catch { }
        }

        Recurse(root);
        return result;
    }

    private static StartupItem? TryBuildTaskItem(string tasksRoot, string file)
    {
        try
        {
            string xml = File.ReadAllText(file);
            if (string.IsNullOrWhiteSpace(xml)) return null;

            var doc = XDocument.Parse(xml);
            var exec = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Exec");
            if (exec == null) return null;

            string command = exec.Elements().FirstOrDefault(e => e.Name.LocalName == "Command")?.Value?.Trim() ?? string.Empty;
            string args = exec.Elements().FirstOrDefault(e => e.Name.LocalName == "Arguments")?.Value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(command)) return null;

            string fullCommand = string.IsNullOrWhiteSpace(args) ? command : $"{command} {args}";

            bool enabled = true;
            var settings = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Settings");
            var enabledEl = settings?.Elements().FirstOrDefault(e => e.Name.LocalName == "Enabled");
            if (enabledEl != null && bool.TryParse(enabledEl.Value.Trim(), out bool parsed)) enabled = parsed;

            string rel = Path.GetRelativePath(tasksRoot, file).Replace('/', '\\');
            string cleanName = Path.GetFileName(rel);
            var (_, isCrit, fallbackPub) = ClassifyStartupItem(cleanName, fullCommand);
            string exe = ExtractExecutablePath(command);
            string pub = ResolvePublisher(exe);

            return new StartupItem
            {
                Name = cleanName,
                Command = fullCommand,
                Source = "Планувальник завдань",
                Category = "Планувальник завдань",
                TaskPath = "\\" + rel,
                EntryType = "Task",
                IsEnabled = enabled,
                IsCritical = isCrit,
                IsUserScope = false,
                ExecutablePath = exe,
                Publisher = string.IsNullOrWhiteSpace(pub) ? fallbackPub : pub,
                StartupImpact = EstimateImpact(cleanName, exe)
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Резервний шлях: перелік задач із реєстрового кешу TaskCache\Tree (без \Microsoft).</summary>
    private static void ScanScheduledTasksFromRegistry(List<StartupItem> list)
    {
        try
        {
            using var tree = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\TaskCache\Tree", false);
            if (tree == null) return;

            void Recurse(RegistryKey parent, string path)
            {
                foreach (var name in parent.GetSubKeyNames())
                {
                    if (string.IsNullOrEmpty(path) && string.Equals(name, "Microsoft", StringComparison.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        using var node = parent.OpenSubKey(name, false);
                        if (node == null) continue;

                        string full = path + "\\" + name;
                        if (node.GetValue("Id") is string)
                        {
                            // Наявність значення Id означає, що вузол — реальна задача (а не тека).
                            list.Add(new StartupItem
                            {
                                Name = name,
                                Command = string.Empty,
                                Source = "Планувальник завдань",
                                Category = "Планувальник завдань",
                                TaskPath = full,
                                EntryType = "Task",
                                IsEnabled = true,
                                IsCritical = false,
                                IsUserScope = false,
                                StartupImpact = "Low"
                            });
                        }

                        Recurse(node, full);
                    }
                    catch { }
                }
            }

            Recurse(tree, string.Empty);
        }
        catch { }
    }

    private static bool TryGetStartupApprovedEnabled(RegistryKey root, string approvedSubKey, string valueName, out bool enabled)
    {
        enabled = true;
        try
        {
            using var key = root.OpenSubKey(approvedSubKey, false);
            if (key?.GetValue(valueName) is byte[] data && data.Length > 0)
            {
                // 0x03 = вимкнено; 0x02 / 0x06 = увімкнено.
                enabled = data[0] != 0x03;
                return true;
            }
        }
        catch { }
        return false;
    }

    private static (string Category, bool IsCritical, string Publisher) ClassifyStartupItem(string name, string command)
    {
        string lowerName = name.ToLowerInvariant();
        string lowerCmd = command.ToLowerInvariant();

        // 1. Критичні системні
        if (CriticalSystemNames.Any(c => lowerName.Contains(c.ToLowerInvariant()) || lowerCmd.Contains(c.ToLowerInvariant())))
        {
            return ("Драйвери & Залізо", true, "Microsoft Windows / Security Core");
        }

        // 2. Драйвери та софт для заліза
        if (HardwareKeywords.Any(h => lowerName.Contains(h) || lowerCmd.Contains(h)))
        {
            string pub = lowerCmd.Contains("nvidia") ? "NVIDIA Corporation" :
                         lowerCmd.Contains("amd") || lowerCmd.Contains("radeon") ? "Advanced Micro Devices" :
                         lowerCmd.Contains("realtek") ? "Realtek Semiconductor" :
                         lowerCmd.Contains("intel") ? "Intel Corporation" : "Виробник обладнання";

            return ("Драйвери & Залізо", false, pub);
        }

        // 3. Фонові апдейтери
        if (UpdaterKeywords.Any(u => lowerName.Contains(u) || lowerCmd.Contains(u)))
        {
            return ("Фонові оновлювачі", false, "Служба оновлення");
        }

        // 4. Звичайні користувацькі програми
        return ("Програми користувача", false, "Сторонній додаток");
    }

    /// <summary>Витягує чистий шлях до .exe з командного рядка (з підтримкою лапок і %env%).</summary>
    private static string ExtractExecutablePath(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return string.Empty;

        try
        {
            string cmd = command.Trim();

            if (cmd.StartsWith("\"", StringComparison.Ordinal))
            {
                int end = cmd.IndexOf('"', 1);
                if (end > 1)
                {
                    return Environment.ExpandEnvironmentVariables(cmd.Substring(1, end - 1));
                }
            }

            int space = cmd.IndexOf(' ');
            string first = space > 0 ? cmd.Substring(0, space) : cmd;
            return Environment.ExpandEnvironmentVariables(first.Trim('"'));
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>Назва компанії-видавця з метаданих файлу; порожньо, якщо недоступно.</summary>
    private static string ResolvePublisher(string? exePath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
            {
                var vi = FileVersionInfo.GetVersionInfo(exePath);
                if (!string.IsNullOrWhiteSpace(vi.CompanyName)) return vi.CompanyName;
                if (!string.IsNullOrWhiteSpace(vi.ProductName)) return vi.ProductName;
                if (!string.IsNullOrWhiteSpace(vi.FileDescription)) return vi.FileDescription;
            }
        }
        catch { }
        return string.Empty;
    }

    /// <summary>Оцінює вплив на старт за відомими «важкими» застосунками та розміром файлу.</summary>
    private static string EstimateImpact(string name, string exePath)
    {
        string lower = (name + " " + exePath).ToLowerInvariant();
        if (HeavyImpactApps.Any(k => lower.Contains(k))) return "High";

        try
        {
            if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
            {
                long len = new FileInfo(exePath).Length;
                if (len > 40L * 1024 * 1024) return "High";
                if (len > 8L * 1024 * 1024) return "Medium";
            }
        }
        catch { }

        return "Low";
    }

    /// <summary>Best-effort читання цільового шляху ярлика .lnk через WScript.Shell (лише рефлексія).</summary>
    private static string TryResolveShortcutTarget(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return string.Empty;

            object? shell = Activator.CreateInstance(type);
            if (shell == null) return string.Empty;

            object? lnk = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
            if (lnk == null) return string.Empty;

            object? target = lnk.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, lnk, null);
            return target as string ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    #endregion

    #region Керування станом (Вимкнення / Увімкнення) — миттєво, без повного пересканування

    /// <summary>
    /// Змінює стан одного запису у фоні та оновлює <see cref="StartupItem.IsEnabled"/> на місці.
    /// Повний скан <see cref="GetStartupEntriesAsync"/> НЕ виконується (відгук UI <10 мс, без блимання списку).
    /// </summary>
    public static async Task<bool> ToggleStartupStateAsync(StartupItem item, bool enable)
    {
        if (item.IsCritical) return false;

        item.IsBusy = true;
        bool success = await Task.Run(() => ApplyToggle(item, enable));

        if (success)
        {
            item.IsEnabled = enable;
            AppLogger.Log($"Автозапуск: [{item.Name}] {(enable ? "увімкнено" : "призупинено")}", "SUCCESS");
        }

        item.IsBusy = false;
        return success;
    }

    private static bool ApplyToggle(StartupItem item, bool enable)
    {
        try
        {
            switch (item.EntryType)
            {
                case "Reg":
                    return ToggleRegistry(item, enable);
                case "File":
                    return ToggleStartupFile(item, enable);
                case "Task":
                    return ToggleScheduledTask(item, enable);
                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Помилка зміни автозапуску для {item.Name}: {ex.Message}", "ERROR");
            return false;
        }
    }

    private static bool ToggleRegistry(StartupItem item, bool enable)
    {
        var root = item.IsUserScope ? Registry.CurrentUser : Registry.LocalMachine;

        string approvedSub = item.KeyPath.Contains("WOW6432Node", StringComparison.OrdinalIgnoreCase)
            ? ApprovedRun32Machine
            : (item.IsUserScope ? ApprovedRunUser : ApprovedRunMachine);

        if (enable)
        {
            // Чиста міграція: повертаємо значення з *_Disabled назад у робочий ключ.
            try
            {
                using var disabled = root.OpenSubKey(item.DisabledKeyPath, true);
                if (disabled?.GetValue(item.Name) != null)
                {
                    disabled.DeleteValue(item.Name, false);
                }
            }
            catch { }

            using (var active = root.CreateSubKey(item.KeyPath))
            {
                if (active != null && active.GetValue(item.Name) == null)
                {
                    active.SetValue(item.Name, item.Command);
                }
            }

            WriteStartupApproved(root, approvedSub, item.Name, true);
            return true;
        }

        // Вимкнення: нативний стандарт Windows (StartupApproved), запис лишається в Run.
        try
        {
            using var legacy = root.OpenSubKey(item.DisabledKeyPath, true);
            if (legacy?.GetValue(item.Name) != null)
            {
                legacy.DeleteValue(item.Name, false);
            }
        }
        catch { }

        WriteStartupApproved(root, approvedSub, item.Name, false);
        return true;
    }

    private static bool ToggleStartupFile(StartupItem item, bool enable)
    {
        var root = item.IsUserScope ? Registry.CurrentUser : Registry.LocalMachine;
        string approvedSub = item.IsUserScope
            ? ApprovedStartupFolder
            : @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

        string approvedName = Path.GetFileName(item.FilePath);
        if (approvedName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
        {
            approvedName = approvedName.Substring(0, approvedName.Length - 9);
        }

        if (enable && item.FilePath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
        {
            // Міграція зі старого підходу «перейменування .disabled».
            string target = item.FilePath.Substring(0, item.FilePath.Length - 9);
            try
            {
                if (File.Exists(item.FilePath))
                {
                    File.Move(item.FilePath, target, true);
                    item.FilePath = target;
                }
            }
            catch { }
        }

        WriteStartupApproved(root, approvedSub, approvedName, enable);
        return true;
    }

    private static bool ToggleScheduledTask(StartupItem item, bool enable)
    {
        if (string.IsNullOrWhiteSpace(item.TaskPath)) return false;

        string action = enable ? "/change /enable" : "/change /disable";
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = $"{action} /tn \"{item.TaskPath}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        using var proc = Process.Start(psi);
        proc?.WaitForExit(3000);
        return proc?.ExitCode == 0;
    }

    private static void WriteStartupApproved(RegistryKey root, string approvedSub, string valueName, bool enabled)
    {
        try
        {
            using var key = root.CreateSubKey(approvedSub);
            key?.SetValue(valueName, enabled ? ApprovedEnabledData : ApprovedDisabledData, RegistryValueKind.Binary);
        }
        catch { }
    }

    /// <summary>Відкриває Провідник із виділеним виконуваним файлом запису.</summary>
    public static void OpenFileLocation(StartupItem item)
    {
        try
        {
            string? target = null;

            if (!string.IsNullOrWhiteSpace(item.ExecutablePath) && File.Exists(item.ExecutablePath))
            {
                target = item.ExecutablePath;
            }
            else if (!string.IsNullOrWhiteSpace(item.FilePath))
            {
                string plain = item.FilePath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                    ? item.FilePath.Substring(0, item.FilePath.Length - 9)
                    : item.FilePath;
                if (File.Exists(plain)) target = plain;
                else if (File.Exists(item.FilePath)) target = item.FilePath;
            }

            if (!string.IsNullOrWhiteSpace(target))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{target}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "/select,\"C:\\Windows\\System32\\Tasks\"",
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Не вдалося відкрити розташування файлу для {item.Name}: {ex.Message}", "WARN");
        }
    }

    #endregion
}
