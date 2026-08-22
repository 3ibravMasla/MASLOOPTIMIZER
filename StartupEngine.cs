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

public class StartupEntry : INotifyPropertyChanged
{
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Source { get; set; } = "Реєстр (Run)";
    public string Category { get; set; } = "Реєстр (Run)";
    public string KeyPath { get; set; } = string.Empty;
    public string DisabledKeyPath { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string TaskPath { get; set; } = string.Empty;
    public string EntryType { get; set; } = "Reg"; // Reg, File, Task

    private bool _isEnabled = true;
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(ButtonText));
            OnPropertyChanged(nameof(ButtonBg));
        }
    }

    public string StatusText => IsEnabled ? "🟢 АКТИВНО" : "⚪ ПРИЗУПИНЕНО";
    public string StatusColor => IsEnabled ? "#107C41" : "#2A2D3D";
    public string ButtonText => IsEnabled ? "⏸️ Призупинити" : "▶️ Увімкнути";
    public string ButtonBg => IsEnabled ? "#C42B1C" : "#107C41";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class StartupEngine
{
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

        // 4. Папки автозапуску (Користувач та Загальносистемна)
        ScanStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Папка Автозапуску (User)", list);
        ScanStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Папка Автозапуску (System)", list);

        // 5. Сторонні завдання Планувальника (Task Scheduler)
        ScanScheduledTasks(list);

        return list.OrderBy(x => x.Name).ToList();
    }

    private static void ReadRegistryRun(RegistryKey root, string subKey, string targetAltKey, string source, bool isEnabled, List<StartupEntry> list)
    {
        try
        {
            using var key = root.OpenSubKey(subKey, false);
            if (key != null)
            {
                foreach (var name in key.GetValueNames())
                {
                    if (string.IsNullOrWhiteSpace(name) || name.StartsWith("PS", StringComparison.OrdinalIgnoreCase)) 
                        continue;

                    string cmd = key.GetValue(name)?.ToString() ?? string.Empty;
                    list.Add(new StartupEntry
                    {
                        Name = name,
                        Command = cmd,
                        Source = source,
                        Category = "Реєстр (Run)",
                        KeyPath = subKey,
                        DisabledKeyPath = targetAltKey,
                        IsEnabled = isEnabled,
                        EntryType = "Reg"
                    });
                }
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

                list.Add(new StartupEntry
                {
                    Name = cleanName,
                    Command = file.FullName,
                    Source = source,
                    Category = "Папки автозапуску",
                    FilePath = file.FullName,
                    IsEnabled = !isDis,
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

                // Парсимо CSV вивід schtasks
                var parts = line.Split(new[] { "\",\"" }, StringSplitOptions.None)
                                .Select(p => p.Trim('"')).ToArray();

                if (parts.Length >= 9)
                {
                    string taskName = parts[1].Trim();
                    string state = parts[3].Trim();
                    string taskToRun = parts[8].Trim();

                    // Фільтруємо системні та порожні завдання Windows
                    if (taskName.StartsWith(@"\Microsoft\Windows", StringComparison.OrdinalIgnoreCase) ||
                        taskName.StartsWith(@"\Microsoft\XblGameSave", StringComparison.OrdinalIgnoreCase) ||
                        string.IsNullOrWhiteSpace(taskToRun) ||
                        taskToRun.Equals("N/A", StringComparison.OrdinalIgnoreCase))
                        continue;

                    bool isEnabled = !state.Equals("Disabled", StringComparison.OrdinalIgnoreCase);

                    list.Add(new StartupEntry
                    {
                        Name = Path.GetFileName(taskName),
                        Command = taskToRun,
                        Source = "Планувальник завдань",
                        Category = "Планувальник завдань",
                        TaskPath = taskName,
                        IsEnabled = isEnabled,
                        EntryType = "Task"
                    });
                }
            }
        }
        catch { }
    }

    public static bool ToggleStartupState(StartupEntry item)
    {
        try
        {
            if (item.EntryType == "Reg")
            {
                var root = item.Source.Contains("HKCU") ? Registry.CurrentUser : Registry.LocalMachine;

                if (item.IsEnabled)
                {
                    // Переносимо з Run у Run_Disabled
                    using (var dKey = root.CreateSubKey(item.DisabledKeyPath))
                    {
                        dKey.SetValue(item.Name, item.Command);
                    }
                    using (var aKey = root.OpenSubKey(item.KeyPath, true))
                    {
                        aKey?.DeleteValue(item.Name, false);
                    }
                    item.IsEnabled = false;
                }
                else
                {
                    // Повертаємо з Run_Disabled у Run
                    using (var aKey = root.CreateSubKey(item.DisabledKeyPath))
                    {
                        aKey.SetValue(item.Name, item.Command);
                    }
                    using (var dKey = root.OpenSubKey(item.KeyPath, true))
                    {
                        dKey?.DeleteValue(item.Name, false);
                    }
                    item.IsEnabled = true;
                }
                return true;
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
                        return true;
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
                        return true;
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
                    return true;
                }
            }
        }
        catch { }

        return false;
    }
}