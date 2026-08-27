using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MASLOOPTIMIZER;

public enum MsiSortMode
{
    Default,
    MsiFirst,
    LineBasedFirst,
    PriorityDescending,
    NameAscending,
    Category,
    Vendor
}

public class MsiStats
{
    public int TotalDevices { get; set; }
    public int MsiEnabledCount { get; set; }
    public int LineBasedCount => TotalDevices - MsiEnabledCount;
    public int GpuCount { get; set; }
    public int NetCount { get; set; }
    public int StorageCount { get; set; }
    public int UsbCount { get; set; }
    public int AudioCount { get; set; }
    public double MsiPercentage => TotalDevices > 0 ? Math.Round((MsiEnabledCount / (double)TotalDevices) * 100, 1) : 0;
}

public class PciMsiDevice : INotifyPropertyChanged
{
    public PciMsiDevice()
    {
        LocalizationManager.Instance.PropertyChanged += OnLocalizationChanged;
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ActionButtonText));
        OnPropertyChanged(nameof(ToolTipText));
        OnPropertyChanged(nameof(PriorityLabelText));
    }

    public string DeviceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Відеокарта (GPU)";
    public string Vendor { get; set; } = "PCI Device";
    public string VendorBadge => Vendor switch
    {
        "NVIDIA" => "#76B900",
        "AMD" => "#ED1C24",
        "Intel" => "#0071C5",
        "Realtek" => "#005596",
        "Samsung" => "#1428A0",
        _ => "#475569"
    };

    public string CategoryIcon => Category switch
    {
        "Відеокарта (GPU)" => "🎮",
        "Мережевий адаптер (NIC)" => "🌐",
        "USB Контролер (Input)" => "🖱️",
        "Накопичувач (NVMe/SATA)" => "💾",
        "Звуковий контролер (Audio)" => "🔊",
        _ => "⚡"
    };

    public string PnpClass { get; set; } = string.Empty;
    public string RegistryPath { get; set; } = string.Empty;
    public string DriverDesc { get; set; } = string.Empty;
    public string LocationInfo { get; set; } = string.Empty;
    public bool IsSafeDevice { get; set; } = true;

    private bool _isMsiSupported;
    public bool IsMsiSupported
    {
        get => _isMsiSupported;
        set
        {
            if (_isMsiSupported != value)
            {
                _isMsiSupported = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(ActionButtonText));
                OnPropertyChanged(nameof(ActionButtonBg));
            }
        }
    }

    private string _priority = "Undefined";
    public string Priority
    {
        get => _priority;
        set
        {
            if (_priority != value)
            {
                _priority = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PriorityColor));
            }
        }
    }

    private int _messageLimit = 16;
    public int MessageLimit
    {
        get => _messageLimit;
        set
        {
            if (_messageLimit != value)
            {
                _messageLimit = value;
                OnPropertyChanged();
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
                OnPropertyChanged(nameof(ActionButtonText));
            }
        }
    }

    public string StatusText
    {
        get
        {
            var loc = LocalizationManager.Instance;
            return IsMsiSupported ? loc["Msi.StatusSupported"] : loc["Msi.StatusNotSupported"];
        }
    }
    public string StatusColor => IsMsiSupported ? "#00FF9D" : "#64748B";

    public string PriorityColor => Priority switch
    {
        "High" => "#00FF9D",
        "Normal" => "#38BDF8",
        "Low" => "#F59E0B",
        _ => "#94A3B8"
    };

    /// <summary>Локалізована мітка "Пріоритет:".</summary>
    public string PriorityLabelText => LocalizationManager.Instance["Msi.PriorityLabel"];

    public string ActionButtonText
    {
        get
        {
            var loc = LocalizationManager.Instance;
            if (IsBusy) return "⏳...";
            if (!IsSafeDevice) return loc["Msi.ProtectedDevice"];
            return IsMsiSupported ? loc["Msi.BtnDisable"] : loc["Msi.BtnEnable"];
        }
    }

    public string ActionButtonBg
    {
        get
        {
            if (!IsSafeDevice) return "#334155";
            return IsMsiSupported ? "#1E293B" : "#0078D4";
        }
    }

    /// <summary>
    /// Розгорнутий локалізований Tooltip для рядка MSI Utility:
    /// пояснення MSI Mode, значення пріоритету High та ліміту переривань.
    /// </summary>
    public string ToolTipText
    {
        get
        {
            var loc = LocalizationManager.Instance;
            return $"{loc["Msi.TooltipTitle"]}\n\n" +
                   $"{loc["Msi.TooltipWhat"]}\n\n" +
                   $"{loc["Msi.TooltipPriority"]}\n\n" +
                   $"{loc["Msi.TooltipLimit"]}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class MsiEngine
{
    public static List<PciMsiDevice> Devices { get; } = new();
    private static readonly object _lock = new();

    #region 1. Сканування шини PCI (64-бітний реєстр + WMI Fallback)

    public static async Task ScanPciDevicesAsync()
    {
        await Task.Run(() =>
        {
            var discovered = new List<PciMsiDevice>();

            try
            {
                using var rootKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                                              .OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\PCI");

                if (rootKey != null)
                {
                    foreach (var devName in rootKey.GetSubKeyNames().Where(d => d.StartsWith("VEN_", StringComparison.OrdinalIgnoreCase)))
                    {
                        using var devKey = rootKey.OpenSubKey(devName);
                        if (devKey == null) continue;

                        string vendor = ExtractVendorFromHwid(devName);

                        foreach (var instName in devKey.GetSubKeyNames())
                        {
                            using var instKey = devKey.OpenSubKey(instName);
                            if (instKey == null) continue;

                            string pnpClass = instKey.GetValue("Class")?.ToString() ?? instKey.GetValue("PNPClass")?.ToString() ?? "";
                            string classGuid = instKey.GetValue("ClassGUID")?.ToString() ?? "";

                            string? category = DetermineCategory(pnpClass, classGuid);
                            if (category == null) continue;

                            string rawName = instKey.GetValue("FriendlyName")?.ToString() ??
                                             instKey.GetValue("DeviceDesc")?.ToString() ?? "PCI Device";
                            string cleanName = CleanDeviceDescription(rawName);

                            string fullDevId = $@"PCI\{devName}\{instName}";
                            string regPath = $@"SYSTEM\CurrentControlSet\Enum\PCI\{devName}\{instName}\Device Parameters\Interrupt Management";

                            bool isMsi = false;
                            int msgLimit = category.Contains("GPU") || category.Contains("NVMe") ? 16 : 4;
                            string priority = "Undefined";

                            using (var msiKey = instKey.OpenSubKey(@"Device Parameters\Interrupt Management\MessageSignaledInterruptProperties"))
                            {
                                if (msiKey != null)
                                {
                                    isMsi = Convert.ToInt32(msiKey.GetValue("MSISupported") ?? 0) == 1;
                                    msgLimit = Convert.ToInt32(msiKey.GetValue("MessageNumberLimit") ?? msgLimit);
                                }
                            }

                            using (var affKey = instKey.OpenSubKey(@"Device Parameters\Interrupt Management\Affinity Policy"))
                            {
                                if (affKey != null)
                                {
                                    int pVal = Convert.ToInt32(affKey.GetValue("DevicePriority") ?? 0);
                                    priority = pVal switch
                                    {
                                        3 => "High",
                                        2 => "Normal",
                                        1 => "Low",
                                        _ => "Undefined"
                                    };
                                }
                            }

                            string loc = instKey.GetValue("LocationInformation")?.ToString() ?? "";
                            bool isSafe = !cleanName.Contains("Bridge", StringComparison.OrdinalIgnoreCase) &&
                                          !cleanName.Contains("Root Port", StringComparison.OrdinalIgnoreCase);

                            discovered.Add(new PciMsiDevice
                            {
                                DeviceId = fullDevId,
                                Name = cleanName,
                                Category = category,
                                Vendor = vendor,
                                PnpClass = pnpClass,
                                RegistryPath = regPath,
                                DriverDesc = rawName,
                                LocationInfo = loc,
                                IsSafeDevice = isSafe,
                                IsMsiSupported = isMsi,
                                MessageLimit = msgLimit,
                                Priority = priority
                            });
                        }
                    }
                }
            }
            catch { }

            if (discovered.Count == 0)
            {
                ScanViaWmiFallback(discovered);
            }

            var ordered = discovered
                .OrderBy(d => GetCategorySortWeight(d.Category))
                .ThenBy(d => d.Name)
                .ToList();

            lock (_lock)
            {
                Devices.Clear();
                Devices.AddRange(ordered);
            }
        });
    }

    private static void ScanViaWmiFallback(List<PciMsiDevice> list)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT DeviceID, Name, PNPClass FROM Win32_PnPEntity WHERE DeviceID LIKE 'PCI\\\\%'");
            foreach (var obj in searcher.Get())
            {
                string pnpClass = obj["PNPClass"]?.ToString() ?? "";
                string name = obj["Name"]?.ToString() ?? "";
                string devId = obj["DeviceID"]?.ToString() ?? "";

                string? cat = DetermineCategory(pnpClass, "");
                if (cat == null) continue;

                string regPath = $@"SYSTEM\CurrentControlSet\Enum\{devId}\Device Parameters\Interrupt Management";
                bool msi = false;
                string prio = "Undefined";

                using (var msiKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                                               .OpenSubKey($@"{regPath}\MessageSignaledInterruptProperties"))
                {
                    if (msiKey != null)
                    {
                        msi = Convert.ToInt32(msiKey.GetValue("MSISupported") ?? 0) == 1;
                    }
                }

                list.Add(new PciMsiDevice
                {
                    DeviceId = devId,
                    Name = CleanDeviceDescription(name),
                    Category = cat,
                    Vendor = ExtractVendorFromHwid(devId),
                    PnpClass = pnpClass,
                    RegistryPath = regPath,
                    IsSafeDevice = true,
                    IsMsiSupported = msi,
                    Priority = prio
                });
            }
        }
        catch { }
    }

    #endregion

    #region 2. Конфігурація стану MSI та пріоритетів

    public static bool SetMsiState(PciMsiDevice device, bool enableMsi, string priority = "High", int messageLimit = 16)
    {
        if (!device.IsSafeDevice) return false;
        device.IsBusy = true;

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

            string msiSubKey = $@"{device.RegistryPath}\MessageSignaledInterruptProperties";
            using (var key = baseKey.CreateSubKey(msiSubKey))
            {
                if (key != null)
                {
                    key.SetValue("MSISupported", enableMsi ? 1 : 0, RegistryValueKind.DWord);
                    if (enableMsi)
                    {
                        key.SetValue("MessageNumberLimit", messageLimit > 0 ? messageLimit : 16, RegistryValueKind.DWord);
                    }
                }
            }

            string affSubKey = $@"{device.RegistryPath}\Affinity Policy";
            using (var key = baseKey.CreateSubKey(affSubKey))
            {
                if (key != null)
                {
                    int pVal = priority switch
                    {
                        "High" => 3,
                        "Normal" => 2,
                        "Low" => 1,
                        _ => 0
                    };

                    if (pVal > 0)
                    {
                        key.SetValue("DevicePriority", pVal, RegistryValueKind.DWord);
                    }
                    else
                    {
                        key.DeleteValue("DevicePriority", false);
                    }
                }
            }

            device.IsMsiSupported = enableMsi;
            device.Priority = priority;
            device.MessageLimit = messageLimit;

            AppLogger.Log($"MSI Mode для [{device.Name}]: {(enableMsi ? $"УВІМКНЕНО ({priority} Priority, Limit: {messageLimit})" : "ВИМКНЕНО")}", "SUCCESS");
            device.IsBusy = false;
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Помилка налаштування MSI для {device.Name}: {ex.Message}", "ERROR");
            device.IsBusy = false;
            return false;
        }
    }

    #endregion

    #region 3. Пакетна оптимізація Gaming MSI Preset & Backup

    public static async Task<int> ApplyOptimalGamingMsiAsync()
    {
        return await Task.Run(async () =>
        {
            await ScanPciDevicesAsync();
            await BackupCurrentMsiStateAsync();

            int optimizedCount = 0;

            foreach (var dev in Devices.Where(d => d.IsSafeDevice))
            {
                if (dev.Category.Contains("GPU") || dev.Category.Contains("Мереж") ||
                    dev.Category.Contains("USB") || dev.Category.Contains("NVMe") ||
                    dev.Category.Contains("Audio"))
                {
                    int limit = dev.Category.Contains("GPU") || dev.Category.Contains("NVMe") ? 16 : 4;
                    bool ok = SetMsiState(dev, true, "High", limit);
                    if (ok) optimizedCount++;
                }
            }

            AppLogger.Log($"1-Click Gaming MSI: оптимізовано {optimizedCount} пристроїв шини PCI (потрібне перезавантаження)", "SUCCESS");
            return optimizedCount;
        });
    }

    public static async Task<int> RestoreAllToDefaultAsync()
    {
        return await Task.Run(async () =>
        {
            await ScanPciDevicesAsync();
            int restoredCount = 0;

            foreach (var dev in Devices.Where(d => d.IsMsiSupported && d.IsSafeDevice))
            {
                bool ok = SetMsiState(dev, false, "Undefined", 0);
                if (ok) restoredCount++;
            }

            AppLogger.Log($"MSI Utility: повернено стандартний режим для {restoredCount} пристроїв", "INFO");
            return restoredCount;
        });
    }

    private static async Task BackupCurrentMsiStateAsync()
    {
        try
        {
            var snapshot = Devices.Select(d => new
            {
                d.DeviceId,
                d.RegistryPath,
                d.IsMsiSupported,
                d.Priority,
                d.MessageLimit
            }).ToList();

            string backupFolder = AppPaths.Backups;
            Directory.CreateDirectory(backupFolder);
            string backupPath = Path.Combine(backupFolder, $"Msi_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.json");

            string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(backupPath, json);
        }
        catch { }
    }

    #endregion

    #region 4. Фільтрація, сортування та статистика

    public static IEnumerable<PciMsiDevice> GetFilteredAndSortedDevices(
        string? category = null,
        string? searchQuery = null,
        MsiSortMode sortMode = MsiSortMode.Default)
    {
        var query = Devices.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(category) && !category.Equals("Всі", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(d => string.Equals(d.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            string q = searchQuery.Trim();
            query = query.Where(d =>
                d.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                d.DeviceId.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                d.Vendor.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                d.Category.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        return sortMode switch
        {
            MsiSortMode.MsiFirst => query.OrderByDescending(d => d.IsMsiSupported).ThenBy(d => d.Name),
            MsiSortMode.LineBasedFirst => query.OrderBy(d => d.IsMsiSupported).ThenBy(d => d.Name),
            MsiSortMode.PriorityDescending => query.OrderByDescending(d => GetPriorityWeight(d.Priority)).ThenBy(d => d.Name),
            MsiSortMode.NameAscending => query.OrderBy(d => d.Name),
            MsiSortMode.Vendor => query.OrderBy(d => d.Vendor).ThenBy(d => d.Name),
            MsiSortMode.Category => query.OrderBy(d => GetCategorySortWeight(d.Category)).ThenBy(d => d.Name),
            _ => query.OrderBy(d => GetCategorySortWeight(d.Category)).ThenBy(d => d.Name)
        };
    }

    public static List<string> GetCategories()
    {
        var categories = Devices.Select(d => d.Category).Distinct().OrderBy(c => GetCategorySortWeight(c)).ToList();
        categories.Insert(0, "Всі");
        return categories;
    }

    public static MsiStats GetStatistics()
    {
        return new MsiStats
        {
            TotalDevices = Devices.Count,
            MsiEnabledCount = Devices.Count(d => d.IsMsiSupported),
            GpuCount = Devices.Count(d => d.Category.Contains("GPU")),
            NetCount = Devices.Count(d => d.Category.Contains("Мереж")),
            StorageCount = Devices.Count(d => d.Category.Contains("NVMe") || d.Category.Contains("Накопич")),
            UsbCount = Devices.Count(d => d.Category.Contains("USB")),
            AudioCount = Devices.Count(d => d.Category.Contains("Audio") || d.Category.Contains("Звук"))
        };
    }

    #endregion

    #region Допоміжні методи

    private static string? DetermineCategory(string pnpClass, string classGuid)
    {
        if (pnpClass.Equals("Display", StringComparison.OrdinalIgnoreCase) || classGuid.Equals("{4d36e968-e325-11ce-bfc1-08002be10318}", StringComparison.OrdinalIgnoreCase))
            return "Відеокарта (GPU)";

        if (pnpClass.Equals("Net", StringComparison.OrdinalIgnoreCase) || classGuid.Equals("{4d36e972-e325-11ce-bfc1-08002be10318}", StringComparison.OrdinalIgnoreCase))
            return "Мережевий адаптер (NIC)";

        if (pnpClass.Equals("USB", StringComparison.OrdinalIgnoreCase) || classGuid.Equals("{36fc9e60-c465-11cf-8056-444553540000}", StringComparison.OrdinalIgnoreCase))
            return "USB Контролер (Input)";

        if (pnpClass.Equals("SCSIAdapter", StringComparison.OrdinalIgnoreCase) || pnpClass.Equals("HDC", StringComparison.OrdinalIgnoreCase) || classGuid.Equals("{4d36e97b-e325-11ce-bfc1-08002be10318}", StringComparison.OrdinalIgnoreCase))
            return "Накопичувач (NVMe/SATA)";

        if (pnpClass.Equals("MEDIA", StringComparison.OrdinalIgnoreCase) || pnpClass.Equals("AudioEndpoint", StringComparison.OrdinalIgnoreCase) || classGuid.Equals("{4d36e96c-e325-11ce-bfc1-08002be10318}", StringComparison.OrdinalIgnoreCase))
            return "Звуковий контролер (Audio)";

        return null;
    }

    private static int GetCategorySortWeight(string category)
    {
        if (category.Contains("GPU")) return 1;
        if (category.Contains("Мереж")) return 2;
        if (category.Contains("NVMe") || category.Contains("Накопич")) return 3;
        if (category.Contains("USB")) return 4;
        if (category.Contains("Audio") || category.Contains("Звук")) return 5;
        return 6;
    }

    private static int GetPriorityWeight(string priority)
    {
        return priority switch
        {
            "High" => 3,
            "Normal" => 2,
            "Low" => 1,
            _ => 0
        };
    }

    private static string ExtractVendorFromHwid(string hwid)
    {
        string upper = hwid.ToUpperInvariant();
        if (upper.Contains("VEN_10DE")) return "NVIDIA";
        if (upper.Contains("VEN_1002") || upper.Contains("VEN_1022")) return "AMD";
        if (upper.Contains("VEN_8086")) return "Intel";
        if (upper.Contains("VEN_10EC")) return "Realtek";
        if (upper.Contains("VEN_144D")) return "Samsung";
        if (upper.Contains("VEN_1B21")) return "ASMedia";
        if (upper.Contains("VEN_1987")) return "Phison";
        if (upper.Contains("VEN_14E4")) return "Broadcom";
        return "PCI Device";
    }

    private static string CleanDeviceDescription(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "PCI Device";
        if (raw.Contains(";"))
        {
            var parts = raw.Split(';', StringSplitOptions.RemoveEmptyEntries);
            return parts[^1].Trim();
        }
        if (raw.StartsWith("@") && raw.Contains(","))
        {
            int commaIdx = raw.IndexOf(',');
            if (commaIdx >= 0 && commaIdx < raw.Length - 1)
            {
                return raw.Substring(commaIdx + 1).Trim('%', ' ');
            }
        }
        return raw.Trim();
    }

    #endregion
}