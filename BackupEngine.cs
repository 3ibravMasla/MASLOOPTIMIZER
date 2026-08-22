using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MASLOOPTIMIZER;

/// <summary>
/// Модель метаданих резервної копії для відображення в UI та вікні відкату
/// </summary>
public class BackupEntry
{
    public string FolderPath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int KeyCount { get; set; }
    public string MachineName { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string FormattedDate => CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
    public string SizeFormatted { get; set; } = "0 КБ";
}

public static class BackupEngine
{
    /// <summary>
    /// Головне захищене системне сховище (ProgramData — не видаляється при чищенні робочого столу)
    /// </summary>
    public static string BackupsDirectory
    {
        get
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string dir = Path.Combine(programData, "MASLOOPTIMIZER", "backups");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>
    /// Локальна портативна папка бекапів поруч із .exe файлом
    /// </summary>
    public static string LocalBackupsDirectory
    {
        get
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>
    /// Автоматичний пошук резервних сховищ на всіх інших підключених дисках (D:\, E:\ тощо)
    /// </summary>
    public static List<string> GetSecondaryDriveBackupDirs()
    {
        var secondaryDirs = new List<string>();
        try
        {
            var otherDrives = DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed &&
                            !d.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase));

            foreach (var drive in otherDrives)
            {
                string secDir = Path.Combine(drive.RootDirectory.FullName, "MASLOOPTIMIZER_Backups");
                secondaryDirs.Add(secDir);
            }
        }
        catch { }
        return secondaryDirs;
    }

    /// <summary>
    /// Повний каталог системних гілок реєстру, які зачіпаються оптимізатором
    /// </summary>
    public static readonly IReadOnlyList<string> AllRegistryKeys = new List<string>
    {
        // --- Системні політики та безпека (HKLM) ---
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows",
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender",
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Spynet",
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows NT\DNSClient",
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization",
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR",
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection",
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\EdgeUI",
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\System",
        @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate",

        // --- Ядро, Планувальник, Пам'ять та Драйвери ---
        @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager",
        @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
        @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\kernel",
        @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Executive",
        @"HKLM\SYSTEM\CurrentControlSet\Control\FileSystem",
        @"HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl",
        @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
        @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers\Scheduler",
        @"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard",
        @"HKLM\SYSTEM\CurrentControlSet\Control\CrashControl",
        @"HKLM\SYSTEM\CurrentControlSet\Control\Remote Assistance",
        @"HKLM\SYSTEM\CurrentControlSet\Control\Power",
        @"HKLM\SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters",
        @"HKLM\SYSTEM\CurrentControlSet\Services\disk\Parameters",

        // --- Системні параметри Windows (HKLM) ---
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons",
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
        @"HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting",
        @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",

        // --- Налаштування профілю користувача (HKCU) ---
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\NamingTemplates",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Search",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\SettingSync",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo",
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\AppHost",
        @"HKCU\Control Panel\Desktop",
        @"HKCU\Control Panel\Mouse",
        @"HKCU\Control Panel\Sound",
        @"HKCU\Control Panel\Accessibility",
        @"HKCU\System\GameConfigStore",
        @"HKCU\Software\Microsoft\GameBar",
        @"HKCU\Software\Microsoft\Clipboard",
        @"HKCU\Software\Microsoft\InputPersonalization",
        @"HKCU\Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy",
        @"HKCU\Software\Microsoft\Windows\Windows Error Reporting",
        @"HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}"
    };

    #region Створення контрольної точки відновлення VSS

    /// <summary>
    /// Створює точку відновлення VSS, знімаючи системне обмеження частоти створення
    /// </summary>
    public static async Task<(bool Success, string Message)> CreateVssRestorePointAsync(string description = "MASLOOPTIMIZER_RestorePoint")
    {
        return await Task.Run(() =>
        {
            try
            {
                // 1. Зняття ліміту частоти створення точок відновлення
                try
                {
                    using var srKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore");
                    srKey?.SetValue("SystemRestorePointCreationFrequency", 0, RegistryValueKind.DWord);
                }
                catch { }

                // 2. Створення точки через WMI SystemRestore
                using var searcher = new ManagementClass(@"\\localhost\root\default:SystemRestore");
                var inParams = searcher.GetMethodParameters("CreateRestorePoint");
                inParams["Description"] = description;
                inParams["RestorePointType"] = 0; // APPLICATION_INSTALL / MODIFY_SETTINGS
                inParams["EventType"] = 100;      // BEGIN_SYSTEM_CHANGE

                var outParams = searcher.InvokeMethod("CreateRestorePoint", inParams, null);
                uint returnCode = (uint)(outParams["ReturnValue"] ?? 1);

                if (returnCode == 0)
                {
                    return (true, "Системну точку відновлення Windows (VSS) успішно створено!");
                }
                else
                {
                    return (false, $"Помилка VSS (Код повернення: {returnCode}). Перевірте, чи увімкнено захист системи.");
                }
            }
            catch (Exception ex)
            {
                // Fallback через PowerShell CLI
                try
                {
                    using var proc = Process.Start(new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -NonInteractive -Command \"Checkpoint-Computer -Description '{description}' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });
                    proc?.WaitForExit(10000);

                    return proc?.ExitCode == 0
                        ? (true, "Точку відновлення Windows створено через PowerShell!")
                        : (false, "Служба VSS вимкнена або заблокована політиками захисту Windows.");
                }
                catch
                {
                    return (false, $"Помилка створення точки VSS: {ex.Message}");
                }
            }
        });
    }

    #endregion

    #region Експорт, Дзеркалювання та Відновлення Реєстру

    /// <summary>
    /// Експорт гілок реєстру в головну системну папку та дзеркальне дублювання на інші диски
    /// </summary>
    public static async Task<(bool Success, string Message, string BackupPath)> ExportRegistryBackupAsync(
        string tweakName = "Full_Tweak_Backup",
        IEnumerable<string>? customKeys = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string folderName = $"{tweakName}_{timestamp}";
                
                // 1. Створення бекапу в ProgramData
                string targetFolder = Path.Combine(BackupsDirectory, folderName);
                Directory.CreateDirectory(targetFolder);

                var keysToExport = customKeys ?? AllRegistryKeys;
                int successCount = 0;

                foreach (var key in keysToExport)
                {
                    string sanitized = key.Replace('\\', '_').Replace('/', '_').Replace(':', '_');
                    string outFile = Path.Combine(targetFolder, $"{sanitized}.reg");

                    using var proc = Process.Start(new ProcessStartInfo
                    {
                        FileName = "reg.exe",
                        Arguments = $"export \"{key}\" \"{outFile}\" /y",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });
                    proc?.WaitForExit(3000);

                    if (File.Exists(outFile) && new FileInfo(outFile).Length > 0)
                    {
                        successCount++;
                    }
                }

                var meta = new BackupEntry
                {
                    FolderPath = targetFolder,
                    Name = folderName,
                    CreatedAt = DateTime.Now,
                    KeyCount = successCount,
                    MachineName = Environment.MachineName,
                    User = Environment.UserName
                };

                string metaJson = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(targetFolder, "meta.json"), metaJson);

                // 2. Дзеркалювання на локальну папку програми та додаткові диски (D:, E:)
                MirrorBackupToStorageLocations(targetFolder, folderName);

                return successCount > 0
                    ? (true, $"Бекап реєстру ({successCount} гілок) збережено у: {folderName}", targetFolder)
                    : (false, "Не вдалося виконати експорт гілок реєстру.", string.Empty);
            }
            catch (Exception ex)
            {
                return (false, $"Помилка експорту реєстру: {ex.Message}", string.Empty);
            }
        });
    }

    private static void MirrorBackupToStorageLocations(string sourceFolder, string folderName)
    {
        try
        {
            // Дзеркало в локальну папку .\backups\
            string localTarget = Path.Combine(LocalBackupsDirectory, folderName);
            CopyDirectory(sourceFolder, localTarget);

            // Дзеркало на зовнішні диски D:\MASLOOPTIMIZER_Backups\
            foreach (var secDir in GetSecondaryDriveBackupDirs())
            {
                if (!Directory.Exists(secDir)) Directory.CreateDirectory(secDir);
                string mirrorTarget = Path.Combine(secDir, folderName);
                CopyDirectory(sourceFolder, mirrorTarget);
            }
        }
        catch { }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) return;

        Directory.CreateDirectory(destinationDir);
        foreach (var file in dir.GetFiles())
        {
            string targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath, true);
        }
    }

    /// <summary>
    /// Відновлення всіх .reg файлів із вибраної папки бекапу
    /// </summary>
    public static async Task<(bool Success, string Message)> RestoreRegistryFromFolderAsync(string folderPath)
    {
        return await Task.Run(() =>
        {
            if (!Directory.Exists(folderPath))
            {
                return (false, "Папку бекапу не знайдено.");
            }

            var regFiles = Directory.GetFiles(folderPath, "*.reg");
            if (regFiles.Length == 0)
            {
                return (false, "У вибраній папці не знайдено файлів .reg.");
            }

            int count = 0;
            foreach (var reg in regFiles)
            {
                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = "reg.exe",
                    Arguments = $"import \"{reg}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                proc?.WaitForExit(3000);
                if (proc?.ExitCode == 0) count++;
            }

            string folderName = Path.GetFileName(folderPath);
            return (true, $"Успішно імпортовано {count} ключів із бекапу [{folderName}].");
        });
    }

    /// <summary>
    /// Швидке відновлення найновішого бекапу
    /// </summary>
    public static async Task<(bool Success, string Message)> RestoreLatestRegistryBackupAsync()
    {
        var backups = await GetAvailableBackupsAsync();
        var latest = backups.OrderByDescending(b => b.CreatedAt).FirstOrDefault();

        if (latest == null)
        {
            return (false, "У системних та додаткових сховищах немає доступних копій реєстру.");
        }

        return await RestoreRegistryFromFolderAsync(latest.FolderPath);
    }

    /// <summary>
    /// Отримання об'єднаного списку бекапів з усіх локацій (ProgramData, локальної папки та додаткових дисків) без дублікатів
    /// </summary>
    public static async Task<List<BackupEntry>> GetAvailableBackupsAsync()
    {
        return await Task.Run(() =>
        {
            var list = new List<BackupEntry>();
            var scannedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var searchRoots = new List<string> { BackupsDirectory, LocalBackupsDirectory };
            searchRoots.AddRange(GetSecondaryDriveBackupDirs());

            foreach (var rootPath in searchRoots)
            {
                if (!Directory.Exists(rootPath)) continue;

                var directories = Directory.GetDirectories(rootPath);
                foreach (var dir in directories)
                {
                    string dirName = Path.GetFileName(dir);
                    if (scannedFolders.Contains(dirName)) continue; // Уникаємо дублювання в списку RestoreWindow

                    var regFiles = Directory.GetFiles(dir, "*.reg");
                    if (regFiles.Length == 0) continue;

                    string metaFile = Path.Combine(dir, "meta.json");
                    BackupEntry? entry = null;

                    if (File.Exists(metaFile))
                    {
                        try
                        {
                            string json = File.ReadAllText(metaFile);
                            entry = JsonSerializer.Deserialize<BackupEntry>(json);
                        }
                        catch { }
                    }

                    if (entry == null)
                    {
                        var dirInfo = new DirectoryInfo(dir);
                        entry = new BackupEntry
                        {
                            FolderPath = dir,
                            Name = dirInfo.Name,
                            CreatedAt = dirInfo.CreationTime,
                            KeyCount = regFiles.Length,
                            MachineName = Environment.MachineName,
                            User = Environment.UserName
                        };
                    }
                    else
                    {
                        entry.FolderPath = dir;
                    }

                    long totalBytes = regFiles.Sum(f => new FileInfo(f).Length);
                    entry.SizeFormatted = FormatBytes(totalBytes);

                    list.Add(entry);
                    scannedFolders.Add(dirName);
                }
            }

            return list.OrderByDescending(x => x.CreatedAt).ToList();
        });
    }

    /// <summary>
    /// Видалення копії бекапу з усіх дзеркальних сховищ
    /// </summary>
    public static async Task<bool> DeleteBackupAsync(string folderPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                string folderName = Path.GetFileName(folderPath);

                // Видаляємо з поточної папки
                if (Directory.Exists(folderPath))
                {
                    Directory.Delete(folderPath, true);
                }

                // Видаляємо дзеркала за назвою
                var mirrorRoots = new List<string> { BackupsDirectory, LocalBackupsDirectory };
                mirrorRoots.AddRange(GetSecondaryDriveBackupDirs());

                foreach (var root in mirrorRoots)
                {
                    string mirrorFolder = Path.Combine(root, folderName);
                    if (Directory.Exists(mirrorFolder))
                    {
                        try { Directory.Delete(mirrorFolder, true); } catch { }
                    }
                }

                return true;
            }
            catch { }
            return false;
        });
    }

    #endregion

    #region Системні вікна

    public static void OpenSystemRestoreUI()
    {
        Process.Start(new ProcessStartInfo { FileName = "rstrui.exe", UseShellExecute = true });
    }

    public static void OpenBackupsFolder()
    {
        Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{BackupsDirectory}\"", UseShellExecute = true });
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024) return $"{bytes / (1024.0 * 1024):N2} МБ";
        if (bytes >= 1024) return $"{bytes / 1024.0:N2} КБ";
        return $"{bytes} Байт";
    }

    #endregion
}