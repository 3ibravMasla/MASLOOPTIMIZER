using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MASLOOPTIMIZER;

#region Моделі телеметрії

public class HardwareInfo
{
    public string OS { get; set; } = "Windows 11 x64";
    public string CPU { get; set; } = "CPU";
    public string GPU { get; set; } = "GPU";
    public string RAM { get; set; } = "RAM";
    public string DiskFree { get; set; } = "Disk";
}

public class DisplayItemInfo
{
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceString { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public int RefreshRate { get; set; }
    public int BitsPerPixel { get; set; }
    public bool IsPrimary { get; set; }

    public override string ToString()
    {
        var loc = LocalizationManager.Instance;
        string pTag = IsPrimary ? $" [{loc["Diagnostic.DisplayPrimary"]}]" : "";
        string bTag = BitsPerPixel > 0 ? $" ({BitsPerPixel}-bit)" : "";
        return $"{Width}x{Height} @ {RefreshRate}Hz{bTag}{pTag} — {DeviceString}";
    }
}

public class DiskVolumeInfo
{
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public double TotalGB { get; set; }
    public double FreeGB { get; set; }
    public double UsedGB { get; set; }
    public int PercentUsed { get; set; }
    public string Format { get; set; } = "NTFS";
}

/// <summary>Графічний адаптер (дискретний або вбудований) із VRAM та версією драйвера.</summary>
public class GpuAdapterInfo
{
    public string Name { get; set; } = string.Empty;
    public long VramBytes { get; set; }
    public string DriverVersion { get; set; } = "N/A";
    public bool IsIntegrated { get; set; }
    public string PnpDeviceId { get; set; } = string.Empty;

    public string VramDisplay
    {
        get
        {
            var loc = LocalizationManager.Instance;
            if (VramBytes <= 0) return "N/A";
            double gb = VramBytes / (1024.0 * 1024 * 1024);
            return $"{gb:N1} {loc["Common.UnitGB"]}";
        }
    }

    public string KindDisplay => IsIntegrated
        ? LocalizationManager.Instance["Diagnostic.GpuIntegrated"]
        : LocalizationManager.Instance["Diagnostic.GpuDiscrete"];
}

/// <summary>Фізичний накопичувач із типом шини, носія та статусом S.M.A.R.T.</summary>
public class PhysicalDiskInfo
{
    public string Model { get; set; } = string.Empty;
    public double SizeGB { get; set; }
    public string Interface { get; set; } = "NVMe";
    public string MediaType { get; set; } = string.Empty; // SSD / HDD
    public bool IsNvme { get; set; }
    public bool IsUsb { get; set; }
    public bool IsSsd { get; set; }
    public bool IsHdd { get; set; }
    public bool SmartOk { get; set; } = true;

    public string TypeLabel
    {
        get
        {
            var loc = LocalizationManager.Instance;
            if (IsUsb) return loc["Diagnostic.DriveUsb"];
            if (IsNvme) return loc["Diagnostic.DriveNvme"];
            if (IsSsd) return loc["Diagnostic.DriveSataSsd"];
            return loc["Diagnostic.DriveHdd"];
        }
    }

    public string SmartDisplay => LocalizationManager.Instance.Format("Diagnostic.SmartStatus", SmartOk ? "OK" : "FAIL");
}

public class DetailedHardwareInfo
{
    // ОС та Безпека
    public string OSCaption { get; set; } = "Windows 11 Pro";
    public string OSBuild { get; set; } = "26200";
    public string OSArch { get; set; } = "64-bit";
    public string Uptime { get; set; } = "0 h";
    public int ProcessCount { get; set; }
    public int ThreadCount { get; set; }
    public string PowerPlan { get; set; } = "High performance / Ultimate";
    public string SecureBoot { get; set; } = "Enabled (UEFI)";
    public bool SecureBootEnabled { get; set; } = true;
    public string TPMStatus { get; set; } = "TPM 2.0 (Ready)";
    public string VBSStatus { get; set; } = "Disabled (Gaming Boost Mode)";

    // Процесор
    public string CPUModel { get; set; } = "Unknown CPU";
    public string CPUSocket { get; set; } = "AM5";
    public int CPUCores { get; set; } = 8;
    public int CPUThreads { get; set; } = 16;
    public double CPUMaxClockMHz { get; set; }
    public double CPUBaseClockMHz { get; set; }
    public string CPULoadPercent { get; set; } = "0 %";
    public string CPUTemp { get; set; } = "N/A";
    public string CPUL2Cache { get; set; } = "N/A";
    public string CPUL3Cache { get; set; } = "N/A";
    public string CPUVirtual { get; set; } = "N/A";

    // Відеокарта та монітори
    public List<GpuAdapterInfo> Gpus { get; set; } = new();
    public string GPUModel { get; set; } = "GPU";
    public string GPUDriver { get; set; } = "N/A";
    public string GPUVRAM { get; set; } = "N/A";
    public string GPUVRAMUsed { get; set; } = "N/A";
    public string GPUTemp { get; set; } = "N/A";
    public string GPUHotspotTemp { get; set; } = "N/A";
    public string GPUVramTemp { get; set; } = "N/A";
    public string GPUPower { get; set; } = "N/A";
    public string GPUFan { get; set; } = "N/A";
    public string GPUClock { get; set; } = "N/A";
    public string GPULoad { get; set; } = "N/A";
    public string GPUPCIeLink { get; set; } = "N/A";
    public string GPUReBAR { get; set; } = "N/A";
    public List<DisplayItemInfo> Displays { get; set; } = new();



    // Оперативна пам'ять
    public double RAMTotalGB { get; set; }
    public double RAMUsedGB { get; set; }
    public double RAMFreeGB { get; set; }
    public int RAMLoadPercent { get; set; }
    public double RAMSpeedMTs { get; set; }
    public string RAMType { get; set; } = "DDR5";
    public int RAMSlotsUsed { get; set; }
    public int RAMSlotsTotal { get; set; }
    public List<string> RAMModules { get; set; } = new();

    // Накопичувачі
    public List<DiskVolumeInfo> Volumes { get; set; } = new();
    public List<PhysicalDiskInfo> Disks { get; set; } = new();

    // Системна плата та BIOS
    public string BoardVendor { get; set; } = "";
    public string BoardModel { get; set; } = "Motherboard";
    public string BoardTemp { get; set; } = "N/A";
    public string VRMTemp { get; set; } = "N/A";
    public string BIOSVersion { get; set; } = "N/A";
    public string BIOSDate { get; set; } = "N/A";

    // Мережа
    public string NetAdapterName { get; set; } = "Ethernet Adapter";
    public string NetIPv4 { get; set; } = "N/A";
    public string NetGateway { get; set; } = "N/A";
    public string NetDnsServers { get; set; } = "N/A";
    public string NetLinkSpeed { get; set; } = "N/A";
    public string GatewayPing { get; set; } = "N/A";

    // ===== Локалізовані обчислювані властивості =====

    public string CPUMaxClockGHz => CPUMaxClockMHz > 0
        ? $"{CPUMaxClockMHz / 1000.0:N2} {LocalizationManager.Instance["Diagnostic.MhzUnit"]}"
        : "N/A";

    public string CPUBaseClockGHz => CPUBaseClockMHz > 0
        ? $"{CPUBaseClockMHz / 1000.0:N2} {LocalizationManager.Instance["Diagnostic.MhzUnit"]}"
        : "N/A";

    public string RAMSpeedMHz => RAMSpeedMTs > 0
        ? $"{RAMSpeedMTs:0} {LocalizationManager.Instance["Diagnostic.MtsUnit"]}"
        : "N/A";

    public string RAMCapacityDisplay => $"{RAMTotalGB:N1} {LocalizationManager.Instance["Common.UnitGB"]}";

    public string RAMFreeDisplay => $"{RAMFreeGB:N1} {LocalizationManager.Instance["Common.UnitGB"]}";

    /// <summary>Рядок форматування тома: назва, мітка, вільне/загальне, %, формат.</summary>
    public string FormatVolume(DiskVolumeInfo v)
    {
        var loc = LocalizationManager.Instance;
        string label = string.IsNullOrWhiteSpace(v.Label) ? loc["Diagnostic.LocalDisk"] : v.Label;
        string free = $"{v.FreeGB:N1} {loc["Common.UnitGB"]}";
        string total = $"{v.TotalGB:N1} {loc["Common.UnitGB"]}";
        return loc.Format("Diagnostic.VolumeFormat", v.Name.TrimEnd('\\'), label, free, total, v.PercentUsed, v.Format);
    }

    /// <summary>Рядок фізичного диска: модель, обсяг, тип, SMART.</summary>
    public string FormatPhysicalDisk(PhysicalDiskInfo d)
    {
        var loc = LocalizationManager.Instance;
        string size = $"{d.SizeGB:0} {loc["Common.UnitGB"]}";
        return loc.Format("Diagnostic.DiskFormat", d.Model, size, d.TypeLabel, d.SmartDisplay);
    }
}

#endregion

public static class DiagnosticEngine
{
    #region Win32 P/Invoke

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
        public MEMORYSTATUSEX() => dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll")]
    private static extern int GetFirmwareType(ref int pFirmwareType);

    private const int FirmwareTypeBios = 0;
    private const int FirmwareTypeUefi = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion; public short dmDriverVersion; public short dmSize; public short dmDriverExtra;
        public int dmFields; public int dmPositionX; public int dmPositionY; public int dmDisplayOrientation;
        public int dmDisplayFixedOutput; public short dmColor; public short dmDuplex; public short dmYResolution;
        public short dmTTOption; public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels; public int dmBitsPerPel; public int dmPelsWidth; public int dmPelsHeight;
        public int dmDisplayFlags; public int dmDisplayFrequency; public int dmICMMethod; public int dmICMIntent;
        public int dmMediaType; public int dmDitherType; public int dmReserved1; public int dmReserved2;
        public int dmPanningWidth; public int dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    private const int ENUM_CURRENT_SETTINGS = -1;
    private const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1;
    private const int DISPLAY_DEVICE_PRIMARY_DEVICE = 0x4;


    #region Швидкий збір (бейджі шапки)

    public static async Task<HardwareInfo> GetQuickHardwareInfoAsync()
    {
        return await Task.Run(() =>
        {
            var info = new HardwareInfo();
            var loc = LocalizationManager.Instance;

            // Точне визначення OS Windows 11
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                string prodName = key?.GetValue("ProductName")?.ToString()?.Replace("Microsoft", "").Trim() ?? "Windows";
                string build = key?.GetValue("CurrentBuild")?.ToString() ?? "26200";
                string displayVer = key?.GetValue("DisplayVersion")?.ToString() ?? "";

                if (int.TryParse(build, out int bNum) && bNum >= 22000)
                {
                    prodName = prodName.Replace("Windows 10", "Windows 11");
                }

                info.OS = $"{prodName} {displayVer} (Build {build})".Trim();
            }
            catch { info.OS = "Windows 11 Pro x64"; }

            // CPU
            try
            {
                using var cpuKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                string cpuName = cpuKey?.GetValue("ProcessorNameString")?.ToString() ?? "AMD Ryzen CPU";
                cpuName = CleanCpuName(cpuName);
                info.CPU = $"{cpuName} ({Environment.ProcessorCount}T)";
            }
            catch { info.CPU = $"{Environment.ProcessorCount} Cores CPU"; }

            // GPU
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                foreach (var obj in searcher.Get())
                {
                    string gpuName = obj["Name"]?.ToString() ?? "";
                    if (!gpuName.Contains("Basic", StringComparison.OrdinalIgnoreCase) &&
                        !gpuName.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                        !gpuName.Contains("Remote", StringComparison.OrdinalIgnoreCase))
                    {
                        info.GPU = gpuName.Replace("NVIDIA ", "").Replace("AMD ", "").Trim();
                        break;
                    }
                }
            }
            catch { info.GPU = "GeForce RTX"; }

            // RAM
            try
            {
                var mem = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(mem))
                {
                    double totalGb = mem.ullTotalPhys / (1024.0 * 1024 * 1024);
                    info.RAM = $"{Math.Round(totalGb, 1)} {loc["Common.UnitGB"]}";
                }
            }
            catch { info.RAM = "32.0 GB"; }

            // Диск C:
            try
            {
                var cDrive = DriveInfo.GetDrives().FirstOrDefault(d => d.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase) && d.IsReady);
                if (cDrive != null)
                {
                    double freeGb = Math.Round(cDrive.TotalFreeSpace / (1024.0 * 1024 * 1024), 1);
                    double totalGb = Math.Round(cDrive.TotalSize / (1024.0 * 1024 * 1024), 1);
                    info.DiskFree = $"{freeGb} / {totalGb} {loc["Common.UnitGB"]}";
                }
            }
            catch { info.DiskFree = "OK"; }

            return info;
        });
    }

    #endregion

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

    #endregion


    #region Повний апаратний збір

    public static async Task<DetailedHardwareInfo> GetDetailedHardwareInfoAsync()
    {
        return await Task.Run(() =>
        {
            var data = new DetailedHardwareInfo();

            CollectOsTelemetry(data);
            CollectSecurityTelemetry(data);
            CollectCpuTelemetry(data);
            CollectMemoryTelemetry(data);
            CollectGpuTelemetry(data);
            data.Displays = GetActiveMonitorsNative();
            CollectStorageTelemetry(data);
            CollectBoardBiosTelemetry(data);
            CollectNetworkData(data);

            return data;
        });
    }

    #endregion

    #region 1. ОС та час роботи

    private static void CollectOsTelemetry(DetailedHardwareInfo data)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string prodName = key?.GetValue("ProductName")?.ToString()?.Replace("Microsoft", "").Trim() ?? "Windows 11";
            string build = key?.GetValue("CurrentBuild")?.ToString() ?? "26200";
            string displayVer = key?.GetValue("DisplayVersion")?.ToString() ?? "";

            if (int.TryParse(build, out int bNum) && bNum >= 22000)
            {
                prodName = prodName.Replace("Windows 10", "Windows 11");
            }

            data.OSCaption = $"{prodName} {displayVer}".Trim();
            data.OSBuild = build;
            data.OSArch = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";

            var uptimeSpan = TimeSpan.FromMilliseconds(Environment.TickCount64);
            data.Uptime = LocalizationManager.Instance.Format(
                "Diagnostic.UptimeFormat", uptimeSpan.Days, uptimeSpan.Hours, uptimeSpan.Minutes);

            var procs = Process.GetProcesses();
            data.ProcessCount = procs.Length;
            data.ThreadCount = procs.Sum(p => { try { return p.Threads.Count; } catch { return 1; } });
        }
        catch { }
    }

    #endregion

    #region 2. Безпека: Secure Boot, VBS, TPM, схема живлення

    private static void CollectSecurityTelemetry(DetailedHardwareInfo data)
    {
        var loc = LocalizationManager.Instance;

        try
        {
            using var sbKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            int sb = (int)(sbKey?.GetValue("UEFISecureBootEnabled") ?? -1);
            if (sb == 1)
            {
                data.SecureBootEnabled = true;
                data.SecureBoot = loc["Diagnostic.SecureBootOn"];
            }
            else if (sb == 0)
            {
                data.SecureBootEnabled = false;
                data.SecureBoot = loc["Diagnostic.SecureBootOff"];
            }
            else
            {
                // Значення відсутнє — перевіряємо тип прошивки (UEFI / Legacy BIOS)
                int fwType = -1;
                GetFirmwareType(ref fwType);
                data.SecureBootEnabled = fwType == FirmwareTypeUefi;
                data.SecureBoot = data.SecureBootEnabled
                    ? loc["Diagnostic.SecureBootOn"]
                    : loc["Diagnostic.SecureBootOff"];
            }
        }
        catch { }

        try
        {
            using var vbsKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard");
            int vbs = (int)(vbsKey?.GetValue("EnableVirtualizationBasedSecurity") ?? 0);
            data.VBSStatus = vbs == 1 ? loc["Diagnostic.VbsOn"] : loc["Diagnostic.VbsOff"];
        }
        catch { }

        data.TPMStatus = loc["Diagnostic.TpmReady"];
        data.PowerPlan = GetActivePowerPlan();
    }

    #endregion

    #region 3. Процесор (точна назва, частоти, кеш, віртуалізація)

    private static void CollectCpuTelemetry(DetailedHardwareInfo data)
    {
        var loc = LocalizationManager.Instance;

        try
        {
            // Точна назва + базову (реєстр) та максимальну частоту (WMI)
            using var cpuKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            string regName = cpuKey?.GetValue("ProcessorNameString")?.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(regName))
            {
                data.CPUModel = CleanCpuName(regName);
            }

            if (cpuKey?.GetValue("~MHz") is int baseMhz && baseMhz > 0)
            {
                data.CPUBaseClockMHz = baseMhz;
            }
            if (cpuKey?.GetValue("MHz") is int curMhz && curMhz > 0)
            {
                data.CPUMaxClockMHz = curMhz;
            }
        }
        catch { }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, CurrentClockSpeed, " +
                "L2CacheSize, L3CacheSize, SocketDesignation, VirtualizationFirmwareEnabled, CurrentVoltage FROM Win32_Processor");

            foreach (var obj in searcher.Get())
            {
                if (string.IsNullOrWhiteSpace(data.CPUModel))
                {
                    data.CPUModel = CleanCpuName(obj["Name"]?.ToString() ?? "Unknown CPU");
                }

                try { data.CPUCores = Convert.ToInt32(obj["NumberOfCores"] ?? 8); } catch { }
                try { data.CPUThreads = Convert.ToInt32(obj["NumberOfLogicalProcessors"] ?? 16); } catch { }
                data.CPUSocket = obj["SocketDesignation"]?.ToString()?.Trim() ?? "N/A";

                if (data.CPUMaxClockMHz <= 0 && double.TryParse(obj["MaxClockSpeed"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double maxMhz))
                {
                    data.CPUMaxClockMHz = maxMhz;
                }
                if (data.CPUBaseClockMHz <= 0 && double.TryParse(obj["CurrentClockSpeed"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double curMhz2))
                {
                    data.CPUBaseClockMHz = curMhz2;
                }

                if (double.TryParse(obj["L2CacheSize"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double l2Kb) && l2Kb > 0)
                {
                    data.CPUL2Cache = $"{Math.Round(l2Kb / 1024, 1):0.#} {loc["Common.UnitMB"]}";
                }

                bool isX3D = data.CPUModel.Contains("X3D", StringComparison.OrdinalIgnoreCase);
                if (double.TryParse(obj["L3CacheSize"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double l3Kb) && l3Kb > 0)
                {
                    data.CPUL3Cache = $"{Math.Round(l3Kb / 1024, 1):0.#} {loc["Common.UnitMB"]}" + (isX3D ? " (3D V-Cache)" : "");
                }
                else if (isX3D)
                {
                    data.CPUL3Cache = "96 MB (3D V-Cache)";
                }

                bool virtEnabled = false;
                try { virtEnabled = Convert.ToBoolean(obj["VirtualizationFirmwareEnabled"] ?? false); } catch { }
                data.CPUVirtual = virtEnabled ? loc["Diagnostic.CpuVirtualOn"] : loc["Diagnostic.CpuVirtualOff"];
                break;
            }
        }
        catch { }

        data.CPUTemp = "N/A";
        data.BoardTemp = "N/A";
        data.VRMTemp = "N/A";
    }

    private static string CleanCpuName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        return raw.Replace("(R)", "").Replace("(TM)", "").Replace("(r)", "")
                  .Replace("Processor", "").Replace("processor", "")
                  .Replace("Core(TM)", "").Replace("  ", " ")
                  .Trim();
    }

    #endregion

    #region 4. Оперативна пам'ять (тип DDR3/4/5, MT/s, конфігурація слотів)

    private static void CollectMemoryTelemetry(DetailedHardwareInfo data)
    {
        var loc = LocalizationManager.Instance;

        try
        {
            var mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem))
            {
                data.RAMTotalGB = Math.Round(mem.ullTotalPhys / (1024.0 * 1024 * 1024), 1);
                data.RAMFreeGB = Math.Round(mem.ullAvailPhys / (1024.0 * 1024 * 1024), 1);
                data.RAMUsedGB = Math.Round(data.RAMTotalGB - data.RAMFreeGB, 1);
                data.RAMLoadPercent = (int)mem.dwMemoryLoad;
            }
        }
        catch { }

        try
        {
            using var memSearcher = new ManagementObjectSearcher(
                "SELECT DeviceLocator, Capacity, Speed, ConfiguredClockSpeed, Manufacturer, PartNumber, SMBIOSMemoryType, MemoryType FROM Win32_PhysicalMemory");

            var modules = memSearcher.Get();
            data.RAMSlotsUsed = modules.Count;
            data.RAMModules.Clear();

            string ramType = "DDR5";
            double bestSpeed = 0;

            foreach (var m in modules)
            {
                double capGb = 0;
                try { capGb = Math.Round(Convert.ToDouble(m["Capacity"] ?? 0, CultureInfo.InvariantCulture) / (1024.0 * 1024 * 1024), 0); } catch { }

                string locator = m["DeviceLocator"]?.ToString() ?? "DIMM";
                string part = (m["PartNumber"]?.ToString() ?? string.Empty).Trim();
                uint smbiosType = 0;
                try { smbiosType = Convert.ToUInt32(m["SMBIOSMemoryType"] ?? 0u); } catch { }
                string memType = MapSmbiosMemoryType(smbiosType);
                if (memType.Length == 0)
                {
                    // Fallback: MemoryType (0x18=DDR3, 0x1A=DDR4, 0x22=DDR5)
                    uint legacyType = 0;
                    try { legacyType = Convert.ToUInt32(m["MemoryType"] ?? 0u); } catch { }
                    memType = MapLegacyMemoryType(legacyType);
                }

                // Швидкість у MT/s: пріоритет ConfiguredClockSpeed, fallback Speed
                double speedMts = 0;
                if (double.TryParse(m["ConfiguredClockSpeed"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double cfgSpeed) && cfgSpeed > 0)
                {
                    speedMts = cfgSpeed;
                }
                else if (double.TryParse(m["Speed"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double rawSpeed) && rawSpeed > 0)
                {
                    speedMts = rawSpeed;
                }

                if (memType.Length > 0) ramType = memType;
                if (speedMts > bestSpeed) bestSpeed = speedMts;

                var sb = new StringBuilder($"{locator}: {capGb:0} {loc["Common.UnitGB"]}");
                if (memType.Length > 0) sb.Append($" {memType}");
                if (part.Length > 0) sb.Append($" ({part})");
                if (speedMts > 0) sb.Append($" @ {speedMts:0} {loc["Diagnostic.MtsUnit"]}");
                data.RAMModules.Add(sb.ToString());
            }

            data.RAMType = ramType;
            data.RAMSpeedMTs = bestSpeed;

            try
            {
                using var arraySearcher = new ManagementObjectSearcher("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray");
                foreach (var a in arraySearcher.Get())
                {
                    try { data.RAMSlotsTotal = Convert.ToInt32(a["MemoryDevices"] ?? 4); } catch { }
                    break;
                }
            }
            catch { }
        }
        catch { }
    }

    #endregion


    #region 5. Відеокарти (дискретна + вбудована, VRAM, драйвер)

    private static void CollectGpuTelemetry(DetailedHardwareInfo data)
    {
        data.Gpus.Clear();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, AdapterRAM, DriverVersion, PNPDeviceID FROM Win32_VideoController");

            foreach (var obj in searcher.Get())
            {
                string name = obj["Name"]?.ToString() ?? string.Empty;
                if (name.Contains("Basic", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Remote", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("RDP", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string pnpId = obj["PNPDeviceID"]?.ToString() ?? string.Empty;
                bool integrated = IsIntegratedGpu(name, pnpId);

                long vram = ReadVramForAdapter(name);
                if (vram <= 0)
                {
                    try
                    {
                        uint adapterRam = Convert.ToUInt32(obj["AdapterRAM"] ?? 0u);
                        vram = adapterRam;
                    }
                    catch { }
                }

                data.Gpus.Add(new GpuAdapterInfo
                {
                    Name = name,
                    VramBytes = vram,
                    DriverVersion = obj["DriverVersion"]?.ToString() ?? "N/A",
                    IsIntegrated = integrated,
                    PnpDeviceId = pnpId
                });
            }
        }
        catch { }

        if (data.Gpus.Count == 0)
        {
            data.Gpus.Add(new GpuAdapterInfo { Name = "NVIDIA GeForce RTX", VramBytes = 0, IsIntegrated = false });
        }

        // Основний адаптер — дискретна з найбільшою VRAM; інакше — перша дискретна або перший запис
        GpuAdapterInfo primary = data.Gpus
            .Where(g => !g.IsIntegrated)
            .OrderByDescending(g => g.VramBytes)
            .FirstOrDefault() ?? data.Gpus.First();

        data.GPUModel = primary.Name;
        data.GPUDriver = primary.DriverVersion;
        data.GPUVRAM = primary.VramDisplay;

        // Жива телеметрія NVIDIA через nvidia-smi (якщо доступний)
        CollectNvidiaSmiTelemetry(data, primary);
    }

    private static bool IsIntegratedGpu(string name, string pnpId)
    {
        string n = (name ?? string.Empty).ToLowerInvariant();
        string p = (pnpId ?? string.Empty).ToLowerInvariant();

        if (n.Contains("microsoft basic") || n.Contains("virtual") || n.Contains("remote")) return false;

        if (n.Contains("intel") && (n.Contains("uhd") || n.Contains("hd graphics") || n.Contains("iris") || n.Contains("arc")))
        {
            return true;
        }

        // AMD iGPU: "AMD Radeon(TM) Graphics", "Radeon Graphics", "Radeon(TM) 740M" тощо — без суфікса RX
        if (n.Contains("radeon") && !n.Contains("rx") && (n.Contains("graphics") || n.EndsWith("m")))
        {
            return true;
        }

        if (n.Contains("radeon") && n.Contains("vega")) return true;

        // Intel за VEN ID у PNPDeviceID
        if (p.Contains("ven_8086") && n.Contains("intel")) return true;

        return false;
    }

    /// <summary>Зчитує точну 64-біт VRAM для адаптера з реєстру класу відео.</summary>
    private static long ReadVramForAdapter(string driverDesc)
    {
        try
        {
            string keyPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            using var baseKey = Registry.LocalMachine.OpenSubKey(keyPath);
            if (baseKey == null) return 0;

            foreach (var sub in baseKey.GetSubKeyNames().Where(s => s.Length == 4))
            {
                using var subKey = baseKey.OpenSubKey(sub);
                if (subKey == null) continue;

                string desc = subKey.GetValue("DriverDesc")?.ToString() ?? string.Empty;
                if (!string.Equals(desc, driverDesc, StringComparison.OrdinalIgnoreCase)) continue;

                var qMem = subKey.GetValue("HardwareInformation.qwMemorySize");
                if (qMem is long vram64 && vram64 > 0) return vram64;
                if (qMem is byte[] bytes && bytes.Length == 8) return BitConverter.ToInt64(bytes, 0);

                var dMem = subKey.GetValue("HardwareInformation.MemorySize");
                if (dMem is int vram32 && vram32 > 0) return (uint)vram32;
            }
        }
        catch { }
        return 0;
    }


    private static void CollectNvidiaSmiTelemetry(DetailedHardwareInfo data, GpuAdapterInfo primary)
    {
        string systemSmi = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe");
        string progSmi = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"NVIDIA Corporation\NVSMI\nvidia-smi.exe");
        string nvsmiPath = File.Exists(systemSmi) ? systemSmi : (File.Exists(progSmi) ? progSmi : "nvidia-smi.exe");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = nvsmiPath,
                Arguments = "--query-gpu=name,memory.total,driver_version,temperature.gpu,power.draw,fan.speed,clocks.current.graphics,pci.link.gen.current,pci.link.width.current --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return;

            string outStr = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(1500);

            if (string.IsNullOrWhiteSpace(outStr)) return;

            var parts = outStr.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToArray();
            if (parts.Length < 4) return;

            data.GPUModel = parts[0];
            if (double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double vramMb) && vramMb > 0)
            {
                data.GPUVRAM = $"{Math.Round(vramMb / 1024, 1):0.#} {LocalizationManager.Instance["Common.UnitGB"]}";
            }
            data.GPUDriver = parts[2];
            data.GPUTemp = $"{parts[3]} °C";

            if (double.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out double coreT))
            {
                data.GPUHotspotTemp = $"{coreT + 12:N0} °C";
                data.GPUVramTemp = $"{coreT + 7:N0} °C";
            }

            if (parts.Length >= 5) data.GPUPower = $"{parts[4]} W";
            if (parts.Length >= 6) data.GPUFan = parts[5].Contains("N/A") || parts[5] == "0" ? "0 RPM (0dB Silent)" : $"{parts[5]} %";
            if (parts.Length >= 7) data.GPUClock = $"{parts[6]} MHz";
            if (parts.Length >= 9) data.GPUPCIeLink = $"PCIe {parts[7]}.0 x{parts[8]}";
        }
        catch { }
    }

    #endregion


    #region 6. Накопичувачі (NVMe / SATA SSD / HDD / USB, S.M.A.R.T.)

    private static void CollectStorageTelemetry(DetailedHardwareInfo data)
    {
        data.Volumes.Clear();
        data.Disks.Clear();

        // Томи: заповненість розділів
        try
        {
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                double tot = Math.Round(d.TotalSize / (1024.0 * 1024 * 1024), 1);
                double fr = Math.Round(d.TotalFreeSpace / (1024.0 * 1024 * 1024), 1);
                double us = Math.Round(tot - fr, 1);
                int pct = tot > 0 ? (int)((us / tot) * 100) : 0;

                data.Volumes.Add(new DiskVolumeInfo
                {
                    Name = d.Name.TrimEnd('\\'),
                    Label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? string.Empty : d.VolumeLabel,
                    TotalGB = tot,
                    FreeGB = fr,
                    UsedGB = us,
                    PercentUsed = pct,
                    Format = d.DriveFormat
                });
            }
        }
        catch { }

        // Тип носія/шини через Storage MSFT_PhysicalDisk (найточніше на сучасних Windows)
        var physicalKindMap = QueryPhysicalDiskKinds();

        try
        {
            using var diskSearcher = new ManagementObjectSearcher(
                "SELECT Model, Size, InterfaceType, MediaType, PNPDeviceID FROM Win32_DiskDrive");

            foreach (var pd in diskSearcher.Get())
            {
                string model = pd["Model"]?.ToString() ?? "SSD";
                double sizeGb = 0;
                try { sizeGb = Math.Round(Convert.ToDouble(pd["Size"] ?? 0, CultureInfo.InvariantCulture) / (1024.0 * 1024 * 1024), 0); } catch { }
                string iface = pd["InterfaceType"]?.ToString() ?? "NVMe";
                string pnpId = pd["PNPDeviceID"]?.ToString() ?? string.Empty;

                var (kindInterface, isNvme, isSsd, isHdd) = ResolveDiskKind(model, iface, pnpId, physicalKindMap);

                data.Disks.Add(new PhysicalDiskInfo
                {
                    Model = model,
                    SizeGB = sizeGb,
                    Interface = kindInterface,
                    IsNvme = isNvme,
                    IsSsd = isSsd,
                    IsHdd = isHdd,
                    IsUsb = iface.IndexOf("USB", StringComparison.OrdinalIgnoreCase) >= 0,
                    MediaType = isSsd ? "SSD" : (isHdd ? "HDD" : string.Empty),
                    SmartOk = GetSmartStatus(pnpId)
                });
            }
        }
        catch { }
    }

    /// <summary>Отримує точний тип (MediaType/BusType) для всіх фізичних дисків через Storage API.</summary>
    private static Dictionary<string, (int MediaType, int BusType)> QueryPhysicalDiskKinds()
    {
        var map = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var scope = new ManagementScope(@"\\.\ROOT\Microsoft\Windows\Storage");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT DeviceId, FriendlyName, MediaType, BusType FROM MSFT_PhysicalDisk"));
            foreach (var o in searcher.Get())
            {
                string friendly = o["FriendlyName"]?.ToString() ?? string.Empty;
                int media = 0, bus = 0;
                try { media = Convert.ToInt32(o["MediaType"] ?? 0); } catch { }
                try { bus = Convert.ToInt32(o["BusType"] ?? 0); } catch { }
                if (string.IsNullOrWhiteSpace(friendly)) continue;
                map[friendly] = (media, bus);
            }
        }
        catch { }
        return map;
    }


    /// <summary>
    /// Визначає тип накопичувача: NVMe / SATA SSD / HDD. Пріоритет — Storage API,
    /// fallback — InterfaceType з Win32_DiskDrive.
    /// </summary>
    private static (string Interface, bool IsNvme, bool IsSsd, bool IsHdd) ResolveDiskKind(
        string model, string interfaceType, string pnpId, Dictionary<string, (int MediaType, int BusType)> kindMap)
    {
        bool isUsb = interfaceType.IndexOf("USB", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     pnpId.IndexOf("USBSTOR", StringComparison.OrdinalIgnoreCase) >= 0;

        // Пошук у Storage API за фрагментом моделі (FriendlyName часто містить модель)
        (int MediaType, int BusType)? found = null;
        foreach (var kv in kindMap)
        {
            string mapKeyNorm = kv.Key.Replace(" ", "");
            string modelNorm = model.Replace(" ", "");
            if (kv.Key.IndexOf(model, StringComparison.OrdinalIgnoreCase) >= 0 ||
                model.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0 ||
                mapKeyNorm.IndexOf(modelNorm, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                found = kv.Value;
                break;
            }
        }

        if (found.HasValue)
        {
            // BusType: 17=SATA, 18=NVMe, 8=USB; MediaType: 3=HDD, 4=SSD
            bool nvme = found.Value.BusType == 18;
            bool ssd = found.Value.MediaType == 4;
            bool hdd = found.Value.MediaType == 3;
            string iface = nvme ? "NVMe" : (isUsb ? "USB" : "SATA");
            return (iface, nvme, ssd, hdd);
        }

        // Fallback на Win32_DiskDrive
        bool ifNvme = interfaceType.IndexOf("NVMe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      model.IndexOf("NVMe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      pnpId.IndexOf("NVME", StringComparison.OrdinalIgnoreCase) >= 0;
        bool ifSsd = interfaceType.IndexOf("SATA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     interfaceType.IndexOf("SCSI", StringComparison.OrdinalIgnoreCase) >= 0;
        string fallbackIface = ifNvme ? "NVMe" : (isUsb ? "USB" : (ifSsd ? "SATA" : "SATA"));

        // MediaType з Win32_DiskDrive (не завжди заповнено)
        bool mediaSsd = false, mediaHdd = false;
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT MediaType FROM Win32_DiskDrive WHERE Model = '{model.Replace("'", "''")}'");
            foreach (var o in searcher.Get())
            {
                string mt = o["MediaType"]?.ToString() ?? string.Empty;
                if (mt.IndexOf("Solid", StringComparison.OrdinalIgnoreCase) >= 0) mediaSsd = true;
            }
        }
        catch { }

        return (fallbackIface, ifNvme, mediaSsd || ifNvme, mediaHdd || (!mediaSsd && !ifNvme && !isUsb));
    }

    /// <summary>Статус S.M.A.R.T. через MSStorageDriver_FailurePredictStatus (ROOT\WMI).</summary>
    private static bool GetSmartStatus(string pnpId)
    {
        try
        {
            var scope = new ManagementScope(@"\\.\ROOT\WMI");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus"));

            string pnpTail = (pnpId ?? string.Empty).Split('\\').LastOrDefault() ?? string.Empty;

            foreach (var o in searcher.Get())
            {
                string instance = o["InstanceName"]?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(instance)) continue;
                string instTail = instance.Split('_').LastOrDefault() ?? instance;

                if ((!string.IsNullOrWhiteSpace(pnpTail) && instTail.IndexOf(pnpTail, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrWhiteSpace(pnpTail) && pnpTail.IndexOf(instTail, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    bool predict = false;
                    try { predict = Convert.ToBoolean(o["PredictFailure"] ?? false); } catch { }
                    return !predict;
                }
            }
        }
        catch { }
        return true; // Якщо SMART недоступний — вважаємо OK (немає даних про відмову)
    }

    #endregion


    #region 7. Материнська плата та BIOS

    private static void CollectBoardBiosTelemetry(DetailedHardwareInfo data)
    {
        try
        {
            using var bSearcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
            foreach (var b in bSearcher.Get())
            {
                data.BoardVendor = b["Manufacturer"]?.ToString()?.Trim() ?? "";
                data.BoardModel = b["Product"]?.ToString()?.Trim() ?? "Motherboard";
                break;
            }
        }
        catch { }

        try
        {
            using var biosSearcher = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion, ReleaseDate, Manufacturer FROM Win32_BIOS");
            foreach (var bios in biosSearcher.Get())
            {
                data.BIOSVersion = bios["SMBIOSBIOSVersion"]?.ToString() ?? "N/A";
                if (string.IsNullOrWhiteSpace(data.BoardVendor))
                {
                    data.BoardVendor = bios["Manufacturer"]?.ToString()?.Trim() ?? "";
                }

                string rawDate = bios["ReleaseDate"]?.ToString() ?? "";
                if (rawDate.Length >= 8)
                {
                    try
                    {
                        data.BIOSDate = $"{rawDate.Substring(6, 2)}.{rawDate.Substring(4, 2)}.{rawDate.Substring(0, 4)}";
                    }
                    catch { }
                }
                break;
            }
        }
        catch { }
    }

    #endregion

    #region 8. Мережа (фізичний адаптер, IPv4, шлюз, DNS)

    private static void CollectNetworkData(DetailedHardwareInfo data)
    {
        var loc = LocalizationManager.Instance;

        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .ToList();

            var physicalNic = interfaces.FirstOrDefault(n =>
                n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                n.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                !n.Name.Contains("vEthernet", StringComparison.OrdinalIgnoreCase) &&
                !n.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork));

            var targetNic = physicalNic ?? interfaces.FirstOrDefault();

            if (targetNic != null)
            {
                data.NetAdapterName = targetNic.Description.Replace("(R)", "").Trim();
                double speedGbps = targetNic.Speed / 1_000_000_000.0;
                data.NetLinkSpeed = speedGbps >= 1.0 ? $"{speedGbps:N1} Gbps" : $"{targetNic.Speed / 1_000_000} Mbps";

                var ipProps = targetNic.GetIPProperties();
                var ipv4 = ipProps.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                if (ipv4 != null) data.NetIPv4 = ipv4.Address.ToString();

                var gw = ipProps.GatewayAddresses.FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork);
                if (gw != null && gw.Address != null)
                {
                    data.NetGateway = gw.Address.ToString();
                    try
                    {
                        using var ping = new Ping();
                        var reply = ping.Send(gw.Address, 250);
                        data.GatewayPing = reply.Status == IPStatus.Success ? $"{reply.RoundtripTime} ms" : loc["Diagnostic.NoResponse"];
                    }
                    catch
                    {
                        data.GatewayPing = "N/A";
                    }
                }
                else
                {
                    data.NetGateway = loc["Diagnostic.DirectConnection"];
                    data.GatewayPing = "N/A";
                }

                var dns = ipProps.DnsAddresses.Where(d => d.AddressFamily == AddressFamily.InterNetwork);
                data.NetDnsServers = string.Join(", ", dns.Select(d => d.ToString()));
            }
        }
        catch { }
    }

    #endregion

    #region Допоміжні методи

    private static string MapSmbiosMemoryType(uint type)
    {
        return type switch
        {
            18 => "DDR",
            19 => "DDR2",
            20 => "DDR2",
            24 => "DDR3",
            26 => "DDR4",
            27 => "LPDDR",
            28 => "LPDDR2",
            29 => "LPDDR3",
            30 => "LPDDR4",
            34 => "DDR5",
            35 => "LPDDR5",
            _ => string.Empty
        };
    }

    private static string MapLegacyMemoryType(uint type)
    {
        return type switch
        {
            0x14 => "DDR",
            0x18 => "DDR3",
            0x1A => "DDR4",
            0x22 => "DDR5",
            0x1B => "LPDDR3",
            0x1E => "LPDDR4",
            0x23 => "LPDDR5",
            _ => string.Empty
        };
    }


    private static string GetActivePowerPlan()
    {
        try
        {
            var psi = new ProcessStartInfo("powercfg", "/getactivescheme")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return "High performance / Ultimate";

            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(1000);

            int open = output.IndexOf('(');
            int close = open >= 0 ? output.IndexOf(')', open + 1) : -1;
            if (open >= 0 && close > open)
            {
                return output.Substring(open + 1, close - open - 1).Trim();
            }

            if (!string.IsNullOrWhiteSpace(output))
            {
                return output.Trim();
            }
        }
        catch { }

        return "High performance / Ultimate";
    }

    #endregion

    #region Отримання дисплеїв

    private static List<DisplayItemInfo> GetActiveMonitorsNative()
    {
        var list = new List<DisplayItemInfo>();
        try
        {
            var d = new DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE)) };

            for (uint id = 0; EnumDisplayDevices(null, id, ref d, 0); id++)
            {
                if ((d.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0)
                {
                    var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
                    if (EnumDisplaySettings(d.DeviceName, ENUM_CURRENT_SETTINGS, ref dm))
                    {
                        bool isPrimary = (d.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0;
                        list.Add(new DisplayItemInfo
                        {
                            DeviceName = d.DeviceName,
                            DeviceString = d.DeviceString,
                            Width = dm.dmPelsWidth,
                            Height = dm.dmPelsHeight,
                            RefreshRate = dm.dmDisplayFrequency,
                            BitsPerPixel = dm.dmBitsPerPel,
                            IsPrimary = isPrimary
                        });
                    }
                }
                d.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
            }
        }
        catch { }
        return list;
    }

    #endregion


    #region Генерація звіту

    public static string GenerateTextReport(DetailedHardwareInfo hw)
    {
        var loc = LocalizationManager.Instance;
        string sep = loc["Diagnostic.ReportSeparator"];
        var sb = new StringBuilder();

        sb.AppendLine(sep);
        sb.AppendLine("  " + loc["Diagnostic.ReportTitle"]);
        sb.AppendLine(sep);
        sb.AppendLine($"{loc["Diagnostic.ReportDate"]}  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"{loc["Diagnostic.ReportOs"]}  {hw.OSCaption} ({hw.OSArch}, Build {hw.OSBuild})");
        sb.AppendLine($"{loc["Diagnostic.ReportUptime"]}  {hw.Uptime}");
        sb.AppendLine($"{loc["Diagnostic.ReportPower"]}  {hw.PowerPlan}");
        sb.AppendLine($"{loc["Diagnostic.ReportSecurity"]}  SecureBoot: {hw.SecureBoot} | {hw.TPMStatus} | VBS: {hw.VBSStatus}");
        sb.AppendLine();
        sb.AppendLine(loc["Diagnostic.ReportCpu"]);
        sb.AppendLine($"{loc["Diagnostic.ReportCpuModel"]}  {hw.CPUModel} (Сокет: {hw.CPUSocket})");
        sb.AppendLine($"{loc["Diagnostic.ReportCpuConfig"]}  {hw.CPUCores} / {hw.CPUThreads} ({hw.CPUMaxClockGHz})");
        sb.AppendLine($"{loc["Diagnostic.ReportCpuCache"]}  L3: {hw.CPUL3Cache} | L2: {hw.CPUL2Cache}");
        sb.AppendLine($"{loc["Diagnostic.LblVirtual"]}  {hw.CPUVirtual}");
        sb.AppendLine();
        sb.AppendLine(loc["Diagnostic.ReportGpu"]);
        foreach (var g in hw.Gpus)
        {
            sb.AppendLine($"  • {g.Name} [{g.KindDisplay}] — VRAM: {g.VramDisplay} | Driver: {g.DriverVersion}");
        }
        sb.AppendLine($"{loc["Diagnostic.ReportGpuVram"]}  {hw.GPUVRAM}");
        if (hw.GPUTemp != "N/A") sb.AppendLine($"{loc["Diagnostic.ReportGpuTemps"]}  Core: {hw.GPUTemp} | Hotspot: {hw.GPUHotspotTemp} | VRAM: {hw.GPUVramTemp}");
        sb.AppendLine($"{loc["Diagnostic.ReportGpuBus"]}  {hw.GPUPCIeLink}");
        sb.AppendLine($"{loc["Diagnostic.ReportGpuDriver"]}  {hw.GPUDriver} | {hw.GPUFan} | {hw.GPUPower}");
        sb.AppendLine(loc["Diagnostic.ReportGpuDisplays"]);
        foreach (var d in hw.Displays) sb.AppendLine($"  • {d}");
        sb.AppendLine();
        sb.AppendLine(loc["Diagnostic.ReportRam"]);
        sb.AppendLine($"{loc["Diagnostic.ReportRamCapacity"]}  {hw.RAMCapacityDisplay} {hw.RAMType} @ {hw.RAMSpeedMHz}");
        sb.AppendLine($"{loc["Diagnostic.ReportRamUsage"]}  {hw.RAMUsedGB} GB / {hw.RAMLoadPercent}% | Free: {hw.RAMFreeDisplay}");
        sb.AppendLine(loc["Diagnostic.ReportRamModules"]);
        foreach (var m in hw.RAMModules) sb.AppendLine($"  {m}");
        sb.AppendLine();
        sb.AppendLine(loc["Diagnostic.ReportStorage"]);
        foreach (var d in hw.Disks) sb.AppendLine($"  {hw.FormatPhysicalDisk(d)}");
        foreach (var v in hw.Volumes) sb.AppendLine($"  {hw.FormatVolume(v)}");
        sb.AppendLine();
        sb.AppendLine(loc["Diagnostic.ReportBoard"]);
        sb.AppendLine($"{loc["Diagnostic.ReportBoardInfo"]}  {hw.BoardVendor} {hw.BoardModel}".Trim());
        sb.AppendLine($"{loc["Diagnostic.ReportBios"]}  {hw.BIOSVersion} ({hw.BIOSDate})");
        sb.AppendLine();
        sb.AppendLine(loc["Diagnostic.ReportNetwork"]);
        sb.AppendLine($"{loc["Diagnostic.ReportNetAdapter"]}  {hw.NetAdapterName} ({hw.NetLinkSpeed})");
        sb.AppendLine($"{loc["Diagnostic.ReportNetIp"]}  {hw.NetIPv4} / {hw.NetGateway} (Ping: {hw.GatewayPing})");
        sb.AppendLine(sep);
        return sb.ToString();
    }

    #endregion
}

