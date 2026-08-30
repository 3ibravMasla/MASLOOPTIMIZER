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
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
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

public class PciMsiDevice : INotifyPropertyChanged, IWeakEventListener
{
    public PciMsiDevice()
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
        OnPropertyChanged(nameof(ActionButtonText));
        OnPropertyChanged(nameof(ToolTipText));
        OnPropertyChanged(nameof(PriorityLabelText));
        OnPropertyChanged(nameof(BlacklistedText));
        OnPropertyChanged(nameof(IrqLabelText));
        OnPropertyChanged(nameof(AttachedLabelText));
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

    public bool IsBlacklisted { get; set; }
    public bool DriverAdvertisesMsi { get; set; }

    private string _irqNumber = string.Empty;
    public string IrqNumber
    {
        get => _irqNumber;
        set
        {
            if (_irqNumber != value)
            {
                _irqNumber = value;
                OnPropertyChanged();
            }
        }
    }

    private string _attachedDevices = string.Empty;
    public string AttachedDevices
    {
        get => _attachedDevices;
        set
        {
            if (_attachedDevices != value)
            {
                _attachedDevices = value;
                OnPropertyChanged();
            }
        }
    }

    private string _affinityMask = string.Empty;
    public string AffinityMask
    {
        get => _affinityMask;
        set
        {
            if (_affinityMask != value)
            {
                _affinityMask = value;
                OnPropertyChanged();
            }
        }
    }

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
    public Brush StatusColor => IsMsiSupported ? ThemeEngine.Brush("SuccessBrush") : ThemeEngine.Brush("StatusNeutralBrush");

    public Brush PriorityColor => Priority switch
    {
        "High" => ThemeEngine.Brush("SuccessText"),
        "Normal" => ThemeEngine.Brush("InfoText"),
        "Low" => ThemeEngine.Brush("WarningText"),
        _ => ThemeEngine.Brush("TextSecondary")
    };

    /// <summary>Локалізована мітка "Пріоритет:".</summary>
    public string PriorityLabelText => LocalizationManager.Instance["Msi.PriorityLabel"];

    public string BlacklistedText => LocalizationManager.Instance["Msi.Blacklisted"];

    /// <summary>Локалізована мітка "IRQ/Вектор:".</summary>
    public string IrqLabelText => LocalizationManager.Instance["Msi.IrqLabel"];

    /// <summary>Локалізована мітка "Підключено:".</summary>
    public string AttachedLabelText => LocalizationManager.Instance["Msi.AttachedLabel"];

    public string ActionButtonText
    {
        get
        {
            var loc = LocalizationManager.Instance;
            if (IsBusy) return "⏳...";
            if (IsBlacklisted) return loc["Msi.Blacklisted"];
            if (!IsSafeDevice) return loc["Msi.ProtectedDevice"];
            return IsMsiSupported ? loc["Msi.BtnDisable"] : loc["Msi.BtnEnable"];
        }
    }

    public Brush ActionButtonBg
    {
        get
        {
            if (IsBlacklisted || !IsSafeDevice) return ThemeEngine.Brush("StatusNeutralBrush");
            return IsMsiSupported ? ThemeEngine.Brush("StatusNeutralBrush") : ThemeEngine.Brush("ChipActiveBg");
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

    /// <summary>Сповіщає UI про зміну теми, щоб перерахувати кольорові кисті статусу/пріоритету/кнопки.</summary>
    public void RefreshThemeColors()
    {
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(PriorityColor));
        OnPropertyChanged(nameof(ActionButtonBg));
    }
}

/// <summary>Початковий стан MSI-налаштувань одного PCI-пристрою для коректного відновлення.</summary>
public class MsiBackupEntry
{
    public string DeviceId { get; set; } = string.Empty;
    public string RegistryPath { get; set; } = string.Empty;
    public bool HadMsiSubKey { get; set; }
    public bool WasMsiSupported { get; set; }
    public bool HadMessageLimit { get; set; }
    public int MessageLimit { get; set; }
    public bool HadPrioritySubKey { get; set; }
    public bool HadPriority { get; set; }
    public string Priority { get; set; } = "Undefined";
}

public static class MsiEngine
{
    public static List<PciMsiDevice> Devices { get; } = new();
    private static readonly object _lock = new();
    private static readonly string[] _blacklistedVendors = { "VEN_1969", "VEN_1102", "VEN_1B21" };

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
                            bool driverAdvertisesMsi = false;
                            int msgLimit = category.Contains("GPU") || category.Contains("NVMe") ? 16 : 4;
                            string priority = "Undefined";

                            using (var msiKey = instKey.OpenSubKey(@"Device Parameters\Interrupt Management\MessageSignaledInterruptProperties"))
                            {
                                if (msiKey != null)
                                {
                                    driverAdvertisesMsi = true;
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
                            bool isBlacklisted = _blacklistedVendors.Any(v => fullDevId.ToUpperInvariant().Contains(v));

                            bool isSafe = !cleanName.Contains("Bridge", StringComparison.OrdinalIgnoreCase) &&
                                          !cleanName.Contains("Root Port", StringComparison.OrdinalIgnoreCase);

                            if (isBlacklisted || !driverAdvertisesMsi)
                            {
                                isSafe = false;
                            }

                            var device = new PciMsiDevice
                            {
                                DeviceId = fullDevId,
                                Name = cleanName,
                                Category = category,
                                Vendor = vendor,
                                PnpClass = pnpClass,
                                RegistryPath = regPath,
                                DriverDesc = rawName,
                                LocationInfo = loc,
                                IsBlacklisted = isBlacklisted,
                                DriverAdvertisesMsi = driverAdvertisesMsi,
                                IsSafeDevice = isSafe,
                                IsMsiSupported = isMsi,
                                MessageLimit = msgLimit,
                                Priority = priority
                            };

                            EnrichWithIrq(device);
                            discovered.Add(device);
                        }
                    }
                }
            }
            catch { }

            if (discovered.Count == 0)
            {
                ScanViaWmiFallback(discovered);
            }

            MapUsbHidDevices(discovered);

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
                bool driverAdvertisesMsi = false;
                string prio = "Undefined";

                using (var msiKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                                               .OpenSubKey($@"{regPath}\MessageSignaledInterruptProperties"))
                {
                    if (msiKey != null)
                    {
                        driverAdvertisesMsi = true;
                        msi = Convert.ToInt32(msiKey.GetValue("MSISupported") ?? 0) == 1;
                    }
                }

                bool isBlacklisted = _blacklistedVendors.Any(v => devId.ToUpperInvariant().Contains(v));

                var device = new PciMsiDevice
                {
                    DeviceId = devId,
                    Name = CleanDeviceDescription(name),
                    Category = cat,
                    Vendor = ExtractVendorFromHwid(devId),
                    PnpClass = pnpClass,
                    RegistryPath = regPath,
                    IsBlacklisted = isBlacklisted,
                    DriverAdvertisesMsi = driverAdvertisesMsi,
                    IsSafeDevice = !isBlacklisted && driverAdvertisesMsi,
                    IsMsiSupported = msi,
                    Priority = prio
                };

                EnrichWithIrq(device);
                list.Add(device);
            }
        }
        catch { }
    }

    #endregion

    #region 2. Конфігурація стану MSI та пріоритетів

    public static bool SetMsiState(PciMsiDevice device, bool enableMsi, string priority = "High", int messageLimit = 16, uint? affinityMask = null)
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

                    // Affinity (Interrupt Steering) — лише за явним запитом експерта.
                    // DevicePolicy = 4 (IrqPolicySpecifiedProcessors) + 8-байтова KAFFINITY-маска.
                    if (affinityMask.HasValue)
                    {
                        key.SetValue("DevicePolicy", 4, RegistryValueKind.DWord);
                        key.SetValue("AssignmentSetOverride", BuildAffinityMaskBytes(affinityMask.Value), RegistryValueKind.Binary);
                        device.AffinityMask = $"0x{affinityMask.Value:X8}";
                    }
                    // affinityMask == null → DevicePolicy та AssignmentSetOverride НЕ чіпаємо.
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

    /// <summary>Формує 8-байтову little-endian KAFFINITY-маску для AssignmentSetOverride (маска 0x1 → {1,0,0,0,0,0,0,0}).</summary>
    private static byte[] BuildAffinityMaskBytes(uint mask)
    {
        var bytes = new byte[8];
        bytes[0] = (byte)(mask & 0xFF);
        bytes[1] = (byte)((mask >> 8) & 0xFF);
        bytes[2] = (byte)((mask >> 16) & 0xFF);
        bytes[3] = (byte)((mask >> 24) & 0xFF);
        // Старші 4 байти (процесори 32–63) для uint-маски завжди нульові.
        return bytes;
    }

    #endregion

    #region 3. Пакетна оптимізація Gaming MSI Preset & Backup

    public static async Task<int> ApplyOptimalGamingMsiAsync()
    {
        return await Task.Run(async () =>
        {
            await ScanPciDevicesAsync();
            await BackupCurrentMsiStateAsync();

            // Усі WMI-запити виконуються на фоновому потоці (Task.Run) та є best-effort.
            var activeNicHwids = GetActiveNetworkAdapterHwids();
            bool systemOnNvme = IsSystemOnNvme();
            bool externalUsbAudio = HasExternalUsbAudio();

            int optimizedCount = 0;

            foreach (var dev in Devices.Where(d => d.IsSafeDevice))
            {
                string? priority = null;
                bool enableMsi = false;

                if (IsBluetooth(dev))
                {
                    // Bluetooth — завжди Low, незалежно від категорії; MSI примусово НЕ вмикаємо.
                    priority = "Low";
                    enableMsi = dev.IsMsiSupported;
                }
                else if (IsGpu(dev))
                {
                    priority = "High";
                    enableMsi = true;
                }
                else if (IsNetwork(dev))
                {
                    // MSI+High лише для активного адаптера (Win32_NetworkAdapter WHERE NetEnabled=true).
                    if (activeNicHwids.Any(h => HardwareIdMatches(h, dev.DeviceId)))
                    {
                        priority = "High";
                        enableMsi = true;
                    }
                }
                else if (IsStorage(dev))
                {
                    if (IsNvmeController(dev))
                    {
                        priority = "High";
                        enableMsi = true;
                    }
                    else
                    {
                        // SATA/невідомий накопичувач: Low якщо система вже на NVMe (вторинне сховище), інакше High.
                        priority = systemOnNvme ? "Low" : "High";
                        enableMsi = systemOnNvme ? dev.IsMsiSupported : true;
                    }
                }
                else if (IsUsb(dev))
                {
                    if (HasMouseOrKeyboardAttached(dev))
                    {
                        priority = "High";
                        enableMsi = true;
                    }
                    else if (string.IsNullOrWhiteSpace(dev.AttachedDevices))
                    {
                        priority = "Low"; // порожній USB-хаб
                        enableMsi = dev.IsMsiSupported;
                    }
                }
                else if (IsAudio(dev))
                {
                    // HD Audio — Low лише коли використовується зовнішня USB-аудіокарта; MSI не вмикаємо примусово.
                    if (IsHdAudio(dev) && externalUsbAudio)
                    {
                        priority = "Low";
                        enableMsi = dev.IsMsiSupported;
                    }
                }

                if (priority == null) continue; // решта пристроїв лишається без змін

                int limit = (IsGpu(dev) || IsNvmeController(dev)) ? 16 : 4;

                // Affinity у 1-Click пресеті НЕ застосовується (пінінг на одне ядро ризикований — окремий експертний вибір).
                if (SetMsiState(dev, enableMsi, priority, limit))
                    optimizedCount++;
            }

            AppLogger.Log($"1-Click Gaming MSI: оптимізовано {optimizedCount} пристроїв шини PCI (потрібне перезавантаження)", "SUCCESS");
            return optimizedCount;
        });
    }

    #region Допоміжні методи 1-Click Preset (розумний вибір)

    /// <summary>Повертає нормалізовані PNPDeviceID/DeviceID активних мережевих адаптерів (NetEnabled=true).</summary>
    private static HashSet<string> GetActiveNetworkAdapterHwids()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT PNPDeviceID, DeviceID FROM Win32_NetworkAdapter WHERE NetEnabled = TRUE");
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    string? pnp = obj["PNPDeviceID"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(pnp)) ids.Add(pnp.Trim());

                    string? devId = obj["DeviceID"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(devId)) ids.Add(devId.Trim());
                }
            }
        }
        catch { }
        return ids;
    }

    /// <summary>Визначає, чи встановлено систему на NVMe-накопичувач.</summary>
    private static bool IsSystemOnNvme()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT InterfaceType FROM Win32_DiskDrive");
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    if (string.Equals(obj["InterfaceType"]?.ToString(), "NVMe", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }
        catch { }
        return false;
    }

    /// <summary>Визначає наявність зовнішньої USB-аудіокарти за PNPDeviceID звукових пристроїв.</summary>
    private static bool HasExternalUsbAudio()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT PNPDeviceID FROM Win32_SoundDevice");
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    if (obj["PNPDeviceID"]?.ToString()?.Contains("USB", StringComparison.OrdinalIgnoreCase) == true)
                        return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static bool IsGpu(PciMsiDevice d) => d.Category.Contains("GPU", StringComparison.OrdinalIgnoreCase);
    private static bool IsNetwork(PciMsiDevice d) => d.Category.Contains("Мереж", StringComparison.OrdinalIgnoreCase);
    private static bool IsStorage(PciMsiDevice d) =>
        d.Category.Contains("NVMe", StringComparison.OrdinalIgnoreCase) || d.Category.Contains("Накопич", StringComparison.OrdinalIgnoreCase);
    private static bool IsUsb(PciMsiDevice d) => d.Category.Contains("USB", StringComparison.OrdinalIgnoreCase);
    private static bool IsAudio(PciMsiDevice d) =>
        d.Category.Contains("Audio", StringComparison.OrdinalIgnoreCase) || d.Category.Contains("Звук", StringComparison.OrdinalIgnoreCase);

    private static bool IsBluetooth(PciMsiDevice d) =>
        d.Name.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) ||
        d.DriverDesc.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase) ||
        d.DeviceId.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase);

    private static bool IsNvmeController(PciMsiDevice d) =>
        d.Name.Contains("NVMe", StringComparison.OrdinalIgnoreCase) ||
        d.Name.Contains("NVM Express", StringComparison.OrdinalIgnoreCase) ||
        d.DriverDesc.Contains("NVMe", StringComparison.OrdinalIgnoreCase) ||
        d.DriverDesc.Contains("NVM Express", StringComparison.OrdinalIgnoreCase);

    private static bool IsHdAudio(PciMsiDevice d) =>
        d.Name.Contains("High Definition Audio", StringComparison.OrdinalIgnoreCase) ||
        d.Name.Contains("HD Audio", StringComparison.OrdinalIgnoreCase) ||
        d.DriverDesc.Contains("High Definition Audio", StringComparison.OrdinalIgnoreCase) ||
        d.DriverDesc.Contains("HD Audio", StringComparison.OrdinalIgnoreCase);

    private static bool HasMouseOrKeyboardAttached(PciMsiDevice d)
    {
        if (string.IsNullOrWhiteSpace(d.AttachedDevices)) return false;
        return d.AttachedDevices.Contains("Mouse", StringComparison.OrdinalIgnoreCase) ||
               d.AttachedDevices.Contains("Keyboard", StringComparison.OrdinalIgnoreCase) ||
               d.AttachedDevices.Contains("Razer", StringComparison.OrdinalIgnoreCase) ||
               d.AttachedDevices.Contains("Logitech", StringComparison.OrdinalIgnoreCase) ||
               d.AttachedDevices.Contains("миш", StringComparison.OrdinalIgnoreCase) ||
               d.AttachedDevices.Contains("клав", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Витягує VEN_xxxx&amp;DEV_xxxx з Hardware ID для надійного порівняння PNPDeviceID/DeviceID.</summary>
    private static string ExtractVenDev(string? hwid)
    {
        if (string.IsNullOrWhiteSpace(hwid)) return string.Empty;
        string s = hwid.Trim();
        int idx = s.IndexOf("VEN_", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return s;
        int devIdx = s.IndexOf("&DEV_", idx, StringComparison.OrdinalIgnoreCase);
        if (devIdx < 0) return s;
        int end = devIdx + "&DEV_".Length + 4; // DEV_XXXX
        if (end > s.Length) end = s.Length;
        return s.Substring(idx, end - idx);
    }

    /// <summary>Порівнює PNPDeviceID/DeviceID за спільною частиною VEN_xxxx&amp;DEV_xxxx (case-insensitive).</summary>
    private static bool HardwareIdMatches(string? a, string? b)
    {
        string va = ExtractVenDev(a);
        string vb = ExtractVenDev(b);
        return !string.IsNullOrEmpty(va) && string.Equals(va, vb, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    public static async Task<int> RestoreAllToDefaultAsync()
    {
        return await Task.Run(() =>
        {
            string? backupPath = GetLatestMsiBackupPath();
            if (backupPath == null)
            {
                AppLogger.Log("MSI Utility: не знайдено бекапу початкового стану — відновлення неможливе", "WARN");
                return 0;
            }

            List<MsiBackupEntry>? entries = null;
            try
            {
                string json = File.ReadAllText(backupPath);
                entries = JsonSerializer.Deserialize<List<MsiBackupEntry>>(json);
            }
            catch (Exception ex)
            {
                AppLogger.Log($"MSI Utility: помилка читання бекапу {Path.GetFileName(backupPath)}: {ex.Message}", "ERROR");
            }

            if (entries == null || entries.Count == 0)
            {
                AppLogger.Log("MSI Utility: бекап початкового стану порожній або пошкоджений", "ERROR");
                return 0;
            }

            int restoredCount = 0;
            foreach (var entry in entries)
            {
                if (RestoreMsiDeviceFromBackup(entry)) restoredCount++;
            }

            // Прибираємо використаний бекап та застарілі timestamp-копії
            try
            {
                foreach (var f in Directory.GetFiles(AppPaths.Backups, "Msi_Backup*.json"))
                {
                    File.Delete(f);
                }
            }
            catch { }

            AppLogger.Log($"MSI Utility: повернено початковий стан для {restoredCount} пристроїв (потрібне перезавантаження)", "INFO");
            return restoredCount;
        });
    }

    private static async Task BackupCurrentMsiStateAsync()
    {
        try
        {
            string backupFolder = AppPaths.Backups;
            Directory.CreateDirectory(backupFolder);
            string backupPath = Path.Combine(backupFolder, "Msi_Backup.json");

            // Якщо попередній бекап ще не відновлено — не перезаписуємо оригінальний стан
            if (File.Exists(backupPath))
            {
                AppLogger.Log("MSI Utility: попередній бекап ще не відновлено — новий не створюється", "WARN");
                return;
            }

            var snapshot = new List<MsiBackupEntry>();
            foreach (var d in Devices.Where(x => x.IsSafeDevice))
            {
                snapshot.Add(ReadMsiRegistryState(d.RegistryPath, d.DeviceId));
            }

            string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(backupPath, json);
        }
        catch { }
    }

    /// <summary>Зчитує фактичний стан MSI-налаштувань пристрою з реєстру.</summary>
    private static MsiBackupEntry ReadMsiRegistryState(string registryPath, string deviceId)
    {
        var entry = new MsiBackupEntry { DeviceId = deviceId, RegistryPath = registryPath };

        try
        {
            var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            string msiSubKey = $@"{registryPath}\MessageSignaledInterruptProperties";

            using (var msiKey = baseKey.OpenSubKey(msiSubKey))
            {
                entry.HadMsiSubKey = msiKey != null;
                if (msiKey != null)
                {
                    entry.WasMsiSupported = Convert.ToInt32(msiKey.GetValue("MSISupported") ?? 0) == 1;
                    var ml = msiKey.GetValue("MessageNumberLimit");
                    entry.HadMessageLimit = ml != null;
                    entry.MessageLimit = ml != null ? Convert.ToInt32(ml) : 0;
                }
            }

            string affSubKey = $@"{registryPath}\Affinity Policy";
            using (var affKey = baseKey.OpenSubKey(affSubKey))
            {
                entry.HadPrioritySubKey = affKey != null;
                if (affKey != null)
                {
                    var p = affKey.GetValue("DevicePriority");
                    entry.HadPriority = p != null;
                    entry.Priority = p == null
                        ? "Undefined"
                        : Convert.ToInt32(p) switch { 3 => "High", 2 => "Normal", 1 => "Low", _ => "Undefined" };
                }
            }
        }
        catch { }

        return entry;
    }

    /// <summary>Повертає пристрою саме той стан MSI, що був до оптимізації.</summary>
    private static bool RestoreMsiDeviceFromBackup(MsiBackupEntry entry)
    {
        try
        {
            var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            string msiSubKey = $@"{entry.RegistryPath}\MessageSignaledInterruptProperties";

            if (entry.HadMsiSubKey)
            {
                using (var key = baseKey.CreateSubKey(msiSubKey))
                {
                    if (key == null) return false;
                    key.SetValue("MSISupported", entry.WasMsiSupported ? 1 : 0, RegistryValueKind.DWord);

                    if (entry.HadMessageLimit && entry.MessageLimit > 0)
                    {
                        key.SetValue("MessageNumberLimit", entry.MessageLimit, RegistryValueKind.DWord);
                    }
                    else
                    {
                        key.DeleteValue("MessageNumberLimit", throwOnMissingValue: false);
                    }
                }
            }
            else
            {
                baseKey.DeleteSubKeyTree(msiSubKey, throwOnMissingSubKey: false);
            }

            string affSubKey = $@"{entry.RegistryPath}\Affinity Policy";
            if (entry.HadPrioritySubKey)
            {
                using (var key = baseKey.CreateSubKey(affSubKey))
                {
                    if (entry.HadPriority)
                    {
                        int pVal = entry.Priority switch { "High" => 3, "Normal" => 2, "Low" => 1, _ => 0 };
                        if (pVal > 0) key?.SetValue("DevicePriority", pVal, RegistryValueKind.DWord);
                    }
                    else
                    {
                        key?.DeleteValue("DevicePriority", throwOnMissingValue: false);
                    }
                }
            }
            else
            {
                baseKey.DeleteSubKeyTree(affSubKey, throwOnMissingSubKey: false);
            }

            AppLogger.Log($"MSI Utility: відновлено [{entry.DeviceId}]", "SUCCESS");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"MSI Utility: не вдалося відновити [{entry.DeviceId}]: {ex.Message}", "WARN");
            return false;
        }
    }

    private static string? GetLatestMsiBackupPath()
    {
        try
        {
            string backupFolder = AppPaths.Backups;
            if (!Directory.Exists(backupFolder)) return null;

            string main = Path.Combine(backupFolder, "Msi_Backup.json");
            if (File.Exists(main)) return main;

            // Сумісність зі старими timestamp-файлами
            return Directory.GetFiles(backupFolder, "Msi_Backup_*.json")
                            .OrderByDescending(File.GetLastWriteTime)
                            .FirstOrDefault();
        }
        catch { return null; }
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

    #region WMI-збагачення (IRQ + USB HID)

    /// <summary>
    /// Best-effort визначення IRQ/вектора переривань через асоціацію
    /// Win32_PnPAllocatedResource → Win32_IRQResource. Виконується всередині Task.Run,
    /// тому не блокує UI. Не фатальне: при будь-якій помилці WMI IrqNumber лишається порожнім.
    /// </summary>
    private static void EnrichWithIrq(PciMsiDevice device)
    {
        try
        {
            bool pnpFound = false;

            using (var searcher = new ManagementObjectSearcher(
                       $"SELECT DeviceID FROM Win32_PnPEntity WHERE DeviceID = '{EscapeWqlString(device.DeviceId)}'"))
            {
                foreach (ManagementObject pnp in searcher.Get())
                {
                    pnpFound = true;
                    using (pnp)
                    {
                        foreach (ManagementObject irqRes in pnp.GetRelated("Win32_IRQResource"))
                        {
                            using (irqRes)
                            {
                                object? raw = irqRes["IRQNumber"];
                                if (raw == null) continue;

                                uint irq;
                                try { irq = Convert.ToUInt32(raw); }
                                catch { continue; }

                                // 0xFFFFFFF0+ — маркер MSI/MSI-X на message-векторах, а не реальний IRQ.
                                if (irq >= 0xFFFFFFF0u)
                                {
                                    device.IrqNumber = "Вектор (MSI)";
                                }
                                else if (irq <= 255u)
                                {
                                    device.IrqNumber = $"IRQ {irq}";
                                }

                                return;
                            }
                        }
                    }
                }
            }

            // PnP-пристрій знайдено, але IRQ-ресурсу немає — пристрій на MSI/MSI-X векторах.
            if (pnpFound)
            {
                device.IrqNumber = "Вектор (MSI)";
            }
        }
        catch
        {
            // Non-fatal: залишаємо IrqNumber порожнім.
        }
    }

    /// <summary>
    /// Зіставляє PCI USB-контролери з Win32_USBController та через асоціацію
    /// Win32_USBControllerDevice виявляє підключені миші/клавіатури (HID).
    /// Виконується всередині Task.Run, тому не блокує UI. Не фатальне.
    /// </summary>
    private static void MapUsbHidDevices(List<PciMsiDevice> devices)
    {
        var usbDevices = devices.Where(d => d.Category.Contains("USB")).ToList();
        if (usbDevices.Count == 0) return;

        var pointingIds = CollectPnpIds("Win32_PointingDevice");
        var keyboardIds = CollectPnpIds("Win32_Keyboard");

        try
        {
            using (var searcher = new ManagementObjectSearcher("SELECT DeviceID, PNPDeviceID FROM Win32_USBController"))
            {
                foreach (ManagementObject controller in searcher.Get())
                {
                    using (controller)
                    {
                        string ctrlDeviceId = controller["DeviceID"]?.ToString() ?? "";
                        string ctrlPnpId = controller["PNPDeviceID"]?.ToString() ?? "";

                        PciMsiDevice? match = usbDevices.FirstOrDefault(u =>
                            HwidEquals(ctrlDeviceId, u.DeviceId) || HwidEquals(ctrlPnpId, u.DeviceId));

                        if (match == null) continue;

                        var hidNames = new List<string>();

                        foreach (ManagementObject dependent in controller.GetRelated("CIM_LogicalDevice"))
                        {
                            using (dependent)
                            {
                                string pnp = dependent["PNPDeviceID"]?.ToString() ?? "";
                                string name = dependent["Name"]?.ToString() ?? "";
                                string display = CleanDeviceDescription(name);
                                if (string.IsNullOrWhiteSpace(display)) continue;

                                bool isMouse = pointingIds.Contains(pnp);
                                bool isKeyboard = keyboardIds.Contains(pnp);

                                // Підрядки назви — лише додаткова підказка.
                                if (!isMouse) isMouse = display.Contains("Mouse", StringComparison.OrdinalIgnoreCase);
                                if (!isKeyboard) isKeyboard = display.Contains("Keyboard", StringComparison.OrdinalIgnoreCase);

                                bool vendorHint = display.Contains("Razer", StringComparison.OrdinalIgnoreCase) ||
                                                  display.Contains("Logitech", StringComparison.OrdinalIgnoreCase);

                                if ((isMouse || isKeyboard || vendorHint) &&
                                    !hidNames.Contains(display, StringComparer.OrdinalIgnoreCase))
                                {
                                    hidNames.Add(display);
                                }
                            }
                        }

                        match.AttachedDevices = string.Join(", ", hidNames);
                    }
                }
            }
        }
        catch
        {
            // Non-fatal: AttachedDevices лишається порожнім.
        }
    }

    private static HashSet<string> CollectPnpIds(string wmiClass)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT PNPDeviceID FROM {wmiClass}");
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    string? pnp = obj["PNPDeviceID"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(pnp))
                    {
                        ids.Add(pnp);
                    }
                }
            }
        }
        catch { }
        return ids;
    }

    /// <summary>Екранує зворотний слеш та одинарну лапку у WQL-рядковому літералі.</summary>
    private static string EscapeWqlString(string value)
        => value.Replace("\\", "\\\\").Replace("'", "\\'");

    private static bool HwidEquals(string a, string b)
        => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    #endregion

    #endregion
}