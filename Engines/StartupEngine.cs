using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
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

public class StartupEntry : INotifyPropertyChanged
{
    public StartupEntry()
    {
        LocalizationManager.Instance.PropertyChanged += OnLocalizationChanged;
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ButtonText));
        OnPropertyChanged(nameof(SafetyBadge));
        OnPropertyChanged(nameof(ProtectedBadge));
        OnPropertyChanged(nameof(CategoryLocalized));
        OnPropertyChanged(nameof(SourceLocalized));
    }

    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Publisher { get; set; } = "Невідомий видавець";
    public string Source { get; set; } = "Реєстр (Run)";
    public string Category { get; set; } = "Програми користувача";
    public string KeyPath { get; set; } = string.Empty;
    public string DisabledKeyPath { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string TaskPath { get; set; } = string.Empty;
    public string EntryType { get; set; } = "Reg"; // Reg, File, Task
    public bool IsCritical { get; set; } = false;

    /// <summary>Чіп-група джерела для фільтрації: user / system / task / folder.</summary>
    public string SourceGroup
    {
        get
        {
            if (Source.Contains("Планувальник", StringComparison.OrdinalIgnoreCase) ||
                Source.Contains("Task", StringComparison.OrdinalIgnoreCase) ||
                EntryType == "Task")
            {
                return "task";
            }
            if (Source.Contains("Папка", StringComparison.OrdinalIgnoreCase) ||
                Source.Contains("Startup Folder", StringComparison.OrdinalIgnoreCase) ||
                EntryType == "File")
            {
                return "folder";
            }
            if (Source.Contains("HKCU", StringComparison.OrdinalIgnoreCase) ||
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
    public string StatusColor => IsEnabled ? "#107C41" : "#2A2D3D";
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

    public string ButtonBg => IsCritical ? "#334155" : (IsEnabled ? "#C42B1C" : "#107C41");

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

    #region Отримання записів автозапуску

    public static async Task<List<StartupEntry>> GetStartupEntriesAsync()
    {
        return await Task.Run(() => GetStartupEntries());
    }

    public static List<StartupEntry> GetStartupEntries()
    {
        var list = new List<StartupEntry>();

        // 1. Реєстр HKCU (Користувач)
        ReadRegistryRun(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", @"Software\Microsoft\Windows\CurrentVersion\Run_Disabled", "Реєстр (HKCU)", true, list);
        ReadRegistryRun(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run_Disabled", @"Software\Microsoft\Windows\CurrentVersion\Run", "Реєстр (HKCU)", false, list);

        // 2. Реєстр HKLM 64-bit (Система)
        ReadRegistryRun(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run_Disabled", "Реєстр (HKLM 64-bit)", true, list);
        ReadRegistryRun(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run_Disabled", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "Реєстр (HKLM 64-bit)", false, list);

        // 3. Реєстр HKLM 32-bit (WOW6432Node)
        ReadRegistryRun(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run_Disabled", "Реєстр (HKLM 32-bit)", true, list);
        ReadRegistryRun(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run_Disabled", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Реєстр (HKLM 32-bit)", false, list);

        // 4. Папки автозапуску (User та Common)
        ScanStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Папка Автозапуску (User)", list);
        ScanStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Папка Автозапуску (System)", list);

        // 5. Сторонні завдання Планувальника (Task Scheduler)
        ScanScheduledTasks(list);

        return list.OrderBy(x => x.Name).ToList();
    }

    #endregion

    #region Контекстне сортування, фільтрація та статистика

    public static IEnumerable<StartupEntry> GetFilteredAndSortedEntries(
        IEnumerable<StartupEntry> sourceList,
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

    public static List<string> GetCategories(IEnumerable<StartupEntry> sourceList)
    {
        var categories = sourceList.Select(e => e.Category).Distinct().OrderBy(c => c).ToList();
        categories.Insert(0, "Всі");
        return categories;
    }

    public static StartupStats GetStatistics(IEnumerable<StartupEntry> sourceList)
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

    private static void ReadRegistryRun(RegistryKey root, string subKey, string targetAltKey, string source, bool isEnabled, List<StartupEntry> list)
    {
        try
        {
            using var key = root.OpenSubKey(subKey, false);
            if (key == null) return;

            foreach (var name in key.GetValueNames())
            {
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith("PS", StringComparison.OrdinalIgnoreCase))
                    continue;

                string cmd = key.GetValue(name)?.ToString() ?? string.Empty;
                var (cat, isCrit, pub) = ClassifyStartupItem(name, cmd);

                list.Add(new StartupEntry
                {
                    Name = name,
                    Command = cmd,
                    Publisher = pub,
                    Source = source,
                    Category = cat,
                    KeyPath = subKey,
                    DisabledKeyPath = targetAltKey,
                    IsEnabled = isEnabled,
                    IsCritical = isCrit,
                    EntryType = "Reg"
                });
            }
        }
        catch { }
    }

    private static void ScanStartupFolder(string folderPath, string source, List<StartupEntry> list)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return;

        try
        {
            var dir = new DirectoryInfo(folderPath);
            foreach (var file in dir.GetFiles())
            {
                if (file.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) ||
                    file.Name.Contains("AutorunsDisabled", StringComparison.OrdinalIgnoreCase))
                    continue;

                bool isDis = file.Extension.Equals(".disabled", StringComparison.OrdinalIgnoreCase);
                string cleanName = isDis ? Path.GetFileNameWithoutExtension(file.Name) : file.Name;
                var (cat, isCrit, pub) = ClassifyStartupItem(cleanName, file.FullName);

                list.Add(new StartupEntry
                {
                    Name = cleanName,
                    Command = file.FullName,
                    Publisher = pub,
                    Source = source,
                    Category = cat,
                    FilePath = file.FullName,
                    IsEnabled = !isDis,
                    IsCritical = isCrit,
                    EntryType = "File"
                });
            }
        }
        catch { }
    }

    private static void ScanScheduledTasks(List<StartupEntry> list)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = "/query /fo CSV /v /nh",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return;

            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(2500);

            using var reader = new StringReader(output);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("\"")) continue;

                var parts = line.Split(new[] { "\",\"" }, StringSplitOptions.None)
                                .Select(p => p.Trim('"')).ToArray();

                if (parts.Length >= 9)
                {
                    string taskName = parts[1].Trim();
                    string state = parts[3].Trim();
                    string taskToRun = parts[8].Trim();

                    // Відсікаємо критичні системні завдання Windows
                    if (taskName.StartsWith(@"\Microsoft\Windows", StringComparison.OrdinalIgnoreCase) ||
                        taskName.StartsWith(@"\Microsoft\XblGameSave", StringComparison.OrdinalIgnoreCase) ||
                        string.IsNullOrWhiteSpace(taskToRun) ||
                        taskToRun.Equals("N/A", StringComparison.OrdinalIgnoreCase))
                        continue;

                    bool isEnabled = !state.Equals("Disabled", StringComparison.OrdinalIgnoreCase);
                    string cleanName = Path.GetFileName(taskName);
                    var (cat, isCrit, pub) = ClassifyStartupItem(cleanName, taskToRun);

                    list.Add(new StartupEntry
                    {
                        Name = cleanName,
                        Command = taskToRun,
                        Publisher = pub,
                        Source = "Планувальник завдань",
                        Category = "Планувальник завдань",
                        TaskPath = taskName,
                        IsEnabled = isEnabled,
                        IsCritical = isCrit,
                        EntryType = "Task"
                    });
                }
            }
        }
        catch { }
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

    #endregion

    #region Керування станом (Вимкнення / Увімкнення)

    public static bool ToggleStartupState(StartupEntry item)
    {
        if (item.IsCritical) return false;
        item.IsBusy = true;

        try
        {
            bool success = false;
            string stateStr = item.IsEnabled ? "призупинено" : "увімкнено";

            if (item.EntryType == "Reg")
            {
                var root = item.Source.Contains("HKCU") ? Registry.CurrentUser : Registry.LocalMachine;

                if (item.IsEnabled)
                {
                    using (var dKey = root.CreateSubKey(item.DisabledKeyPath))
                    {
                        dKey.SetValue(item.Name, item.Command);
                    }
                    using (var aKey = root.OpenSubKey(item.KeyPath, true))
                    {
                        aKey?.DeleteValue(item.Name, false);
                    }
                    item.IsEnabled = false;
                    success = true;
                }
                else
                {
                    using (var aKey = root.CreateSubKey(item.KeyPath))
                    {
                        aKey.SetValue(item.Name, item.Command);
                    }
                    using (var dKey = root.OpenSubKey(item.DisabledKeyPath, true))
                    {
                        dKey?.DeleteValue(item.Name, false);
                    }
                    item.IsEnabled = true;
                    success = true;
                }
            }
            else if (item.EntryType == "File")
            {
                if (item.IsEnabled)
                {
                    string target = item.FilePath + ".disabled";
                    if (File.Exists(item.FilePath))
                    {
                        File.Move(item.FilePath, target, true);
                        item.FilePath = target;
                        item.IsEnabled = false;
                        success = true;
                    }
                }
                else
                {
                    string target = item.FilePath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                        ? item.FilePath.Substring(0, item.FilePath.Length - 9)
                        : item.FilePath;

                    if (File.Exists(item.FilePath))
                    {
                        File.Move(item.FilePath, target, true);
                        item.FilePath = target;
                        item.IsEnabled = true;
                        success = true;
                    }
                }
            }
            else if (item.EntryType == "Task")
            {
                string action = item.IsEnabled ? "/change /disable" : "/change /enable";
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"{action} /tn \"{item.TaskPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var proc = Process.Start(psi);
                proc?.WaitForExit(2000);

                if (proc?.ExitCode == 0)
                {
                    item.IsEnabled = !item.IsEnabled;
                    success = true;
                }
            }

            if (success)
            {
                AppLogger.Log($"Автозапуск: [{item.Name}] успішно {stateStr}", "SUCCESS");
            }

            item.IsBusy = false;
            return success;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Помилка зміни автозапуску для {item.Name}: {ex.Message}", "ERROR");
        }

        item.IsBusy = false;
        return false;
    }

    #endregion
}