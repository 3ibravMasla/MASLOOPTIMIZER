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

public enum BackupSortMode
{
    DateDescending,
    DateAscending,
    SizeDescending,
    KeyCountDescending,
    NameAscending
}

public class BackupEntry
{
    public string FolderPath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int KeyCount { get; set; }
    public long TotalBytes { get; set; }
    public string MachineName { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string FormattedDate => CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
    public string SizeFormatted { get; set; } = "0 КБ";
    public bool IsValid { get; set; } = true;
}

public static class BackupEngine
{
    public static string BackupsDirectory => AppPaths.Backups;

    public static string LocalBackupsDirectory
    {
        get
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }
    }

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

    public static readonly IReadOnlyList<string> AllRegistryKeys = new List<string>
    {
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
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons",
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System",
        @"HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting",
        @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
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

    public static async Task<(bool Success, string Message)> CreateVssRestorePointAsync(string description = "MASLOOPTIMIZER_RestorePoint")
    {
        return await Task.Run(() =>
        {
            try
            {
                try
                {
                    using var srKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore");
                    srKey?.SetValue("SystemRestorePointCreationFrequency", 0, RegistryValueKind.DWord);
                }
                catch { }

                using var searcher = new ManagementClass(@"\\localhost\root\default:SystemRestore");
                var inParams = searcher.GetMethodParameters("CreateRestorePoint");
                inParams["Description"] = description;
                inParams["RestorePointType"] = 0;
                inParams["EventType"] = 100;

                var outParams = searcher.InvokeMethod("CreateRestorePoint", inParams, null);
                uint returnCode = (uint)(outParams["ReturnValue"] ?? 1);

                if (returnCode == 0)
                {
                    AppLogger.Log("Створено системну точку відновлення Windows (VSS)", "SUCCESS");
                    return (true, "Системну точку відновлення Windows (VSS) успішно створено!");
                }
                else
                {
                    AppLogger.Log($"Помилка створення точки VSS: Код {returnCode}", "WARN");
                    return (false, $"Помилка VSS (Код: {returnCode}). Перевірте, чи увімкнено захист системи у властивостях Windows.");
                }
            }
            catch (Exception ex)
            {
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

                    if (proc?.ExitCode == 0)
                    {
                        AppLogger.Log("Точку відновлення Windows (VSS) створено через PowerShell", "SUCCESS");
                        return (true, "Точку відновлення Windows створено через PowerShell!");
                    }

                    AppLogger.Log("Служба VSS вимкнена або заблокована", "ERROR");
                    return (false, "Служба VSS вимкнена або заблокована політиками захисту Windows.");
                }
                catch
                {
                    AppLogger.Log($"Помилка створення точки VSS: {ex.Message}", "ERROR");
                    return (false, $"Помилка створення точки VSS: {ex.Message}");
                }
            }
        });
    }

    #endregion

    #region Експорт, Дзеркалювання та Відновлення Реєстру

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

                string targetFolder = Path.Combine(BackupsDirectory, folderName);
                Directory.CreateDirectory(targetFolder);

                var keysToExport = customKeys ?? AllRegistryKeys;
                int successCount = 0;
                long totalBytes = 0;

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

                    if (File.Exists(outFile))
                    {
                        long len = new FileInfo(outFile).Length;
                        if (len > 0)
                        {
                            successCount++;
                            totalBytes += len;
                        }
                    }
                }

                var meta = new BackupEntry
                {
                    FolderPath = targetFolder,
                    Name = folderName,
                    CreatedAt = DateTime.Now,
                    KeyCount = successCount,
                    TotalBytes = totalBytes,
                    SizeFormatted = FormatBytes(totalBytes),
                    MachineName = Environment.MachineName,
                    User = Environment.UserName,
                    IsValid = successCount > 0
                };

                string metaJson = JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(targetFolder, "meta.json"), metaJson);

                MirrorBackupToStorageLocations(targetFolder, folderName);

                if (successCount > 0)
                {
                    AppLogger.Log($"Збережено бекап реєстру: {folderName} ({successCount} гілок)", "SUCCESS");
                    return (true, $"Бекап реєстру ({successCount} гілок, {FormatBytes(totalBytes)}) збережено!", targetFolder);
                }

                return (false, "Не вдалося експортувати гілки реєстру.", string.Empty);
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Помилка експорту реєстру: {ex.Message}", "ERROR");
                return (false, $"Помилка експорту реєстру: {ex.Message}", string.Empty);
            }
        });
    }

    private static void MirrorBackupToStorageLocations(string sourceFolder, string folderName)
    {
        try
        {
            string localTarget = Path.Combine(LocalBackupsDirectory, folderName);
            CopyDirectory(sourceFolder, localTarget);

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
            AppLogger.Log($"Відновлено стан реєстру з бекапу: {folderName} ({count} ключів)", "SUCCESS");
            return (true, $"Успішно імпортовано {count} ключів із бекапу [{folderName}].");
        });
    }

    public static async Task<List<BackupEntry>> GetAvailableBackupsAsync(BackupSortMode sortMode = BackupSortMode.DateDescending)
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
                    if (scannedFolders.Contains(dirName)) continue;

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

                    long totalBytes = regFiles.Sum(f => new FileInfo(f).Length);

                    if (entry == null)
                    {
                        var dirInfo = new DirectoryInfo(dir);
                        entry = new BackupEntry
                        {
                            FolderPath = dir,
                            Name = dirInfo.Name,
                            CreatedAt = dirInfo.CreationTime,
                            KeyCount = regFiles.Length,
                            TotalBytes = totalBytes,
                            MachineName = Environment.MachineName,
                            User = Environment.UserName
                        };
                    }
                    else
                    {
                        entry.FolderPath = dir;
                        entry.TotalBytes = totalBytes;
                    }

                    entry.SizeFormatted = FormatBytes(totalBytes);
                    entry.IsValid = regFiles.Length > 0;

                    list.Add(entry);
                    scannedFolders.Add(dirName);
                }
            }

            return sortMode switch
            {
                BackupSortMode.DateDescending => list.OrderByDescending(x => x.CreatedAt).ToList(),
                BackupSortMode.DateAscending => list.OrderBy(x => x.CreatedAt).ToList(),
                BackupSortMode.SizeDescending => list.OrderByDescending(x => x.TotalBytes).ToList(),
                BackupSortMode.KeyCountDescending => list.OrderByDescending(x => x.KeyCount).ToList(),
                BackupSortMode.NameAscending => list.OrderBy(x => x.Name).ToList(),
                _ => list.OrderByDescending(x => x.CreatedAt).ToList()
            };
        });
    }

    public static async Task<bool> DeleteBackupAsync(string folderPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                string folderName = Path.GetFileName(folderPath);

                if (Directory.Exists(folderPath))
                {
                    Directory.Delete(folderPath, true);
                }

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

                AppLogger.Log($"Видалено резервну копію реєстру: {folderName}", "INFO");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Помилка видалення бекапу: {ex.Message}", "ERROR");
            }
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
        if (bytes >= 1024) return $"{bytes / (1024.0 * 1024):N2} КБ";
        return $"{bytes} Байт";
    }

    #endregion
}