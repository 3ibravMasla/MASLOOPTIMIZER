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
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using LibreHardwareMonitor.Hardware;

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
    public string DriverDate { get; set; } = "N/A";
    public string BusInterface { get; set; } = "PCI Express";
    public string ReBar { get; set; } = "N/A";
    public bool IsIntegrated { get; set; }
    public string PnpDeviceId { get; set; } = string.Empty;

    public string VramDisplay
    {
        get
        {
            var loc = LocalizationManager.Instance;
            if (VramBytes <= 0) return "—";
            double gb = VramBytes / (1024.0 * 1024 * 1024);
            return $"{gb:N0} {loc["Common.UnitGB"]}";
        }
    }

    public string KindDisplay => IsIntegrated
        ? LocalizationManager.Instance["Diagnostic.GpuIntegrated"]
        : LocalizationManager.Instance["Diagnostic.GpuDiscrete"];
}

/// <summary>Фізичний накопичувач із типом шини, носія, S.M.A.R.T. та LHM-сенсорами.</summary>
public class PhysicalDiskInfo
{
    public string Model { get; set; } = string.Empty;
    public double SizeGB { get; set; }
    public string Interface { get; set; } = "NVMe";
    public string MediaType { get; set; } = string.Empty;
    public bool IsNvme { get; set; }
    public bool IsUsb { get; set; }
    public bool IsSsd { get; set; }
    public bool IsHdd { get; set; }
    public bool SmartOk { get; set; } = true;

    // LHM Storage sensors
    public double? TemperatureC { get; set; }
    public double? RemainingLifePercent { get; set; }
    public double? TotalBytesWrittenGb { get; set; }
    public double? PowerOnHours { get; set; }

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

    public string SmartLabel => SmartOk
        ? LocalizationManager.Instance["Diagnostic.SmartOk"]
        : LocalizationManager.Instance["Diagnostic.SmartWarn"];

    public string TempDisplay => TemperatureC.HasValue ? $"{TemperatureC.Value:0} °C" : "—";

    public string LifeDisplay => RemainingLifePercent.HasValue ? $"{RemainingLifePercent.Value:0} %" : "—";
}

/// <summary>Один встановлений модуль DIMM.</summary>
public class DimmSlotInfo
{
    public string Slot { get; set; } = "DIMM";
    public double CapacityGB { get; set; }
    public string MemoryType { get; set; } = string.Empty;
    public double SpeedMTs { get; set; }
    public string FormFactor { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string PartNumber { get; set; } = string.Empty;
}

/// <summary>Статичний апаратний аудит — знімок обладнання (без живих сенсорів).</summary>
public sealed class StaticHardwareSpecs
{
    // ОС та безпека
    public string OSCaption { get; set; } = "Windows 11";
    public string OSDisplayVersion { get; set; } = "—";
    public string OSBuild { get; set; } = "—";
    public string OSArch { get; set; } = "64-bit";
    public string DirectXVersion { get; set; } = "—";
    public string Uptime { get; set; } = "—";
    public string VBS { get; set; } = "—";
    public string SecureBoot { get; set; } = "—";
    public string TPM { get; set; } = "—";
    public string PowerPlan { get; set; } = "—";

    // Процесор (статика)
    public string CPUModel { get; set; } = "—";
    public string CPUArchitecture { get; set; } = "—";
    public string CPUSocket { get; set; } = "—";
    public int CPUCores { get; set; }
    public int CPUThreads { get; set; }
    public double CPUBaseClockMHz { get; set; }
    public double CPUMaxClockMHz { get; set; }
    public string CPUL1Cache { get; set; } = "—";
    public string CPUL2Cache { get; set; } = "—";
    public string CPUL3Cache { get; set; } = "—";
    public string CPUVirtual { get; set; } = "—";

    // Відеокарти та монітори (статика)
    public List<GpuAdapterInfo> Gpus { get; set; } = new();
    public List<DisplayItemInfo> Displays { get; set; } = new();

    // Оперативна пам'ять (статика)
    public double RAMTotalGB { get; set; }
    public string RAMType { get; set; } = "—";
    public double RAMSpeedMTs { get; set; }
    public string RAMFormFactor { get; set; } = "—";
    public int RAMSlotsUsed { get; set; }
    public int RAMSlotsTotal { get; set; }
    public List<DimmSlotInfo> Dimms { get; set; } = new();

    // Накопичувачі (статика)
    public List<PhysicalDiskInfo> Disks { get; set; } = new();
    public List<DiskVolumeInfo> Volumes { get; set; } = new();

    // Плата та BIOS
    public string BoardVendor { get; set; } = string.Empty;
    public string BoardModel { get; set; } = "—";
    public string BIOSVendor { get; set; } = "—";
    public string BIOSVersion { get; set; } = "—";
    public string BIOSDate { get; set; } = "—";

    // Мережа
    public string NetAdapterName { get; set; } = "—";
    public string NetMac { get; set; } = "—";
    public string NetLinkSpeed { get; set; } = "—";
    public string NetIPv4 { get; set; } = "—";

    public string OsTitleDisplay => $"{OSCaption} {OSDisplayVersion} (Build {OSBuild})".Replace("  ", " ").Trim();

    public string CpuCoresDisplay => CPUCores > 0
        ? $"{CPUCores} / {CPUThreads}"
        : "—";

    public string CpuCacheDisplay => $"L1 {CPUL1Cache} | L2 {CPUL2Cache} | L3 {CPUL3Cache}";

    public GpuAdapterInfo? PrimaryGpu =>
        Gpus.Where(g => !g.IsIntegrated).OrderByDescending(g => g.VramBytes).FirstOrDefault()
        ?? Gpus.FirstOrDefault();

    public string RamSpeedDisplay => RAMSpeedMTs > 0
        ? $"{RAMType} @ {RAMSpeedMTs:0} {LocalizationManager.Instance["Diagnostic.MtsUnit"]}"
        : RAMType;
}

/// <summary>Живий знімок сенсорів — оновлюється кожні 1.5 с через UpdateVisitor.</summary>
public sealed class LiveTelemetrySnapshot
{
    // CPU
    public double? CpuLoadPercent { get; set; }
    public double? CpuPackageTempC { get; set; }
    public double? CpuMaxCoreTempC { get; set; }
    public double? CpuPackagePowerW { get; set; }
    public double? CpuVcoreV { get; set; }
    public double? CpuAvgClockMHz { get; set; }
    public double? CpuPeakClockMHz { get; set; }

    // GPU
    public double? GpuLoadPercent { get; set; }
    public double? GpuCoreTempC { get; set; }
    public double? GpuHotspotTempC { get; set; }
    public double? GpuVramTempC { get; set; }
    public double? GpuPowerW { get; set; }
    public double? GpuCoreClockMHz { get; set; }
    public double? GpuMemClockMHz { get; set; }
    public double? GpuFanRpm { get; set; }
    public double? GpuFanPercent { get; set; }
    public double? GpuVramUsedMb { get; set; }
    public double? GpuVramTotalMb { get; set; }

    // Плата та кулери
    public double? VrmMosTempC { get; set; }
    public double? SystemTempC { get; set; }
    public double? CpuFanRpm { get; set; }
    public double? SystemFanRpm { get; set; }

    // RAM (живе)
    public double? RamUsedGB { get; set; }
    public double? RamAvailableGB { get; set; }
    public double? RamUsagePercent { get; set; }
    public double? CommitUsedGB { get; set; }

    public string CpuLoadDisplay => FmtPercent(CpuLoadPercent);
    public string CpuPackageTempDisplay => FmtTemp(CpuPackageTempC);
    public string CpuCoreMaxTempDisplay => FmtTemp(CpuMaxCoreTempC);
    public string CpuPowerDisplay => CpuPackagePowerW is double w ? $"{w:0.#} W" : "—";
    public string CpuVcoreDisplay => CpuVcoreV is double v ? $"{v:0.000} V" : "—";
    public string CpuClockDisplay => FmtMhz(CpuPeakClockMHz);
    public string CpuAvgClockDisplay => FmtMhz(CpuAvgClockMHz);

    public string GpuLoadDisplay => FmtPercent(GpuLoadPercent);
    public string GpuCoreTempDisplay => FmtTemp(GpuCoreTempC);
    public string GpuHotspotTempDisplay => FmtTemp(GpuHotspotTempC);
    public string GpuVramTempDisplay => FmtTemp(GpuVramTempC);
    public string GpuPowerDisplay => GpuPowerW is double gp ? $"{gp:0.#} W" : "—";
    public string GpuCoreClockDisplay => FmtMhz(GpuCoreClockMHz);
    public string GpuMemClockDisplay => FmtMhz(GpuMemClockMHz);

    public string GpuFanDisplay
    {
        get
        {
            string? rpm = GpuFanRpm is double r ? $"{r:0} RPM" : null;
            string? pct = GpuFanPercent is double p ? $"{p:0} %" : null;
            if (rpm != null && pct != null) return $"{rpm} / {pct}";
            return rpm ?? pct ?? "—";
        }
    }

    public string GpuVramUsedDisplay
    {
        get
        {
            var loc = LocalizationManager.Instance;
            if (GpuVramUsedMb is not double used) return "—";
            const double mbPerGb = 1024.0;
            double usedGb = used / mbPerGb;
            if (GpuVramTotalMb is double total && total > 0)
                return $"{usedGb:0.#} / {total / mbPerGb:0.#} {loc["Common.UnitGB"]}";
            return $"{usedGb:0.#} {loc["Common.UnitGB"]}";
        }
    }

    public string VrmTempDisplay => FmtTemp(VrmMosTempC);
    public string SystemTempDisplay => FmtTemp(SystemTempC);
    public string CpuFanDisplay => FmtRpm(CpuFanRpm);
    public string SystemFanDisplay => FmtRpm(SystemFanRpm);

    public string RamUsedDisplay => FmtGb(RamUsedGB);
    public string RamAvailableDisplay => FmtGb(RamAvailableGB);
    public string RamUsageDisplay => FmtPercent(RamUsagePercent);
    public string CommitDisplay => FmtGb(CommitUsedGB);

    private static string FmtPercent(double? v) => v.HasValue ? $"{v.Value:0.#} %" : "—";
    private static string FmtTemp(double? v) => v.HasValue ? $"{v.Value:0.#} °C" : "—";
    private static string FmtMhz(double? v) => v.HasValue ? $"{v.Value:0} MHz" : "—";
    private static string FmtRpm(double? v) => v.HasValue && v.Value > 0 ? $"{v.Value:0} RPM" : "—";
    private static string FmtGb(double? v) => v.HasValue ? $"{v.Value:0.#} {LocalizationManager.Instance["Common.UnitGB"]}" : "—";
}

#endregion

public static class DiagnosticEngine
{
    // ---- Стан для обчислення дельти завантаження CPU через GetSystemTimes ----
    private static ulong? _cpuIdlePrev;
    private static ulong? _cpuKernelPrev;
    private static ulong? _cpuUserPrev;
    private static readonly object _cpuLoadLock = new();

    // Кешування статусу безпеки: TPM і Secure Boot не змінюються під час роботи ПК.
    private static SecureBootState? _cachedSecureBoot;
    private static TpmState? _cachedTpm;
    private static string _cachedTpmSpec = string.Empty;
    private static readonly object _securityCacheLock = new();

    private enum SecureBootState { Unknown, Disabled, Enabled }
    private enum TpmState { Ready, Disabled, Unavailable }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    private static ulong ToUInt64(FILETIME ft) => ((ulong)ft.dwHighDateTime << 32) | ft.dwLowDateTime;

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

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

    #endregion

    #region LibreHardwareMonitor — постійна сесія + UpdateVisitor

    /// <summary>Обхід дерева LHM: оновлює апаратуру та рекурсивно заходить у SubHardware.</summary>
    public sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (IHardware sub in hardware.SubHardware) sub.Accept(this);
        }

        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }

    private static readonly object _lhmLock = new();
    private static Computer? _computer;
    private static readonly UpdateVisitor _visitor = new();

    /// <summary>Відкриває Ring0-драйвер один раз на весь час роботи вікна діагностики.</summary>
    public static void StartLiveSession()
    {
        lock (_lhmLock)
        {
            if (_computer != null) return;

            var computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsControllerEnabled = true,
                IsStorageEnabled = true,
                IsNetworkEnabled = true,
            };

            try
            {
                computer.Open();
            }
            catch
            {
                try { computer.Close(); } catch { }
                return;
            }

            _computer = computer;
        }
    }

    /// <summary>Закриває драйвер і звільняє Ring0-хендли при закритті вікна.</summary>
    public static void StopLiveSession()
    {
        Computer? computer;
        lock (_lhmLock)
        {
            computer = _computer;
            _computer = null;
        }
        if (computer != null)
        {
            try { computer.Close(); } catch { }
        }
    }

    private static List<IHardware> EnumerateAll(IComputer computer)
    {
        var result = new List<IHardware>();
        try
        {
            foreach (IHardware hw in computer.Hardware) AddRecursive(hw, result);
        }
        catch { }
        return result;
    }

    private static void AddRecursive(IHardware hardware, List<IHardware> target)
    {
        target.Add(hardware);
        try
        {
            foreach (IHardware sub in hardware.SubHardware) AddRecursive(sub, target);
        }
        catch { }
    }

    /// <summary>Живе опитування сенсорів на відкритому Computer (кожні ~1.5 с).</summary>
    public static async Task<LiveTelemetrySnapshot> PollLiveSensorsAsync()
    {
        return await Task.Run(() =>
        {
            var snap = new LiveTelemetrySnapshot();
            FillRamLive(snap);

            lock (_lhmLock)
            {
                var computer = _computer;
                if (computer != null)
                {
                    try { computer.Accept(_visitor); } catch { }
                    try { CollectLiveSensors(computer, snap); } catch { }
                }
            }

            if (snap.CpuLoadPercent == null)
                snap.CpuLoadPercent = ReadCpuLoadPercent();
            if (snap.RamUsagePercent == null)
                FillRamLive(snap);

            return snap;
        });
    }

    private static void CollectLiveSensors(IComputer computer, LiveTelemetrySnapshot snap)
    {
        var all = EnumerateAll(computer);

        var cpu = all.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        if (cpu != null) ReadCpuLive(cpu, snap);

        var gpus = all.Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel).ToList();
        var gpu = gpus.FirstOrDefault(IsDiscreteGpu) ?? gpus.FirstOrDefault();
        if (gpu != null) ReadGpuLive(gpu, snap);

        foreach (var board in all.Where(h =>
                     h.HardwareType is HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.EmbeddedController))
        {
            ReadBoardLive(board, snap);
        }
    }

    private static bool IsDiscreteGpu(IHardware hw)
    {
        if (hw.HardwareType == HardwareType.GpuNvidia || hw.HardwareType == HardwareType.GpuAmd) return true;
        string n = (hw.Name ?? string.Empty).ToLowerInvariant();
        if (n.Contains("radeon") && (n.Contains("rx") || n.Contains("vega"))) return true;
        return false;
    }

    private static void ReadCpuLive(IHardware cpu, LiveTelemetrySnapshot snap)
    {
        double clockSum = 0;
        int clockCount = 0;

        foreach (ISensor s in cpu.Sensors)
        {
            if (!s.Value.HasValue) continue;
            float v = s.Value.Value;
            string name = s.Name ?? string.Empty;

            switch (s.SensorType)
            {
                case SensorType.Load:
                    if (name.Equals("CPU Total", StringComparison.OrdinalIgnoreCase))
                        snap.CpuLoadPercent = v;
                    break;

                case SensorType.Temperature:
                    if (name.Contains("Distance", StringComparison.OrdinalIgnoreCase)) break;
                    if (name.Contains("Package", StringComparison.OrdinalIgnoreCase))
                        snap.CpuPackageTempC = v;
                    if (v > 0 && v < 150)
                        snap.CpuMaxCoreTempC = snap.CpuMaxCoreTempC is double m ? Math.Max(m, v) : v;
                    break;

                case SensorType.Power:
                    if (name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("CPU", StringComparison.OrdinalIgnoreCase))
                        snap.CpuPackagePowerW = v;
                    break;

                case SensorType.Voltage:
                    if (snap.CpuVcoreV == null &&
                        (name.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("Vcore", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("SVI", StringComparison.OrdinalIgnoreCase)))
                        snap.CpuVcoreV = v;
                    break;

                case SensorType.Clock:
                    if (v <= 0) break;
                    if (name.Contains("Bus", StringComparison.OrdinalIgnoreCase)) break;
                    clockSum += v;
                    clockCount++;
                    snap.CpuPeakClockMHz = snap.CpuPeakClockMHz is double p ? Math.Max(p, v) : v;
                    break;
            }
        }

        if (clockCount > 0) snap.CpuAvgClockMHz = Math.Round(clockSum / clockCount, 0);
    }

    private static void ReadGpuLive(IHardware gpu, LiveTelemetrySnapshot snap)
    {
        foreach (ISensor s in gpu.Sensors)
        {
            if (!s.Value.HasValue) continue;
            float v = s.Value.Value;
            string name = s.Name ?? string.Empty;

            switch (s.SensorType)
            {
                case SensorType.Load:
                    if (name.Contains("Core", StringComparison.OrdinalIgnoreCase) || name.Equals("GPU", StringComparison.OrdinalIgnoreCase))
                        snap.GpuLoadPercent = v;
                    break;

                case SensorType.Temperature:
                    if (name.Contains("Hot", StringComparison.OrdinalIgnoreCase) || name.Contains("Spot", StringComparison.OrdinalIgnoreCase))
                        snap.GpuHotspotTempC = v;
                    else if (name.Contains("Memory", StringComparison.OrdinalIgnoreCase) || name.Contains("VRAM", StringComparison.OrdinalIgnoreCase) ||
                             name.Contains("GDDR", StringComparison.OrdinalIgnoreCase))
                        snap.GpuVramTempC = v;
                    else if (name.Contains("Core", StringComparison.OrdinalIgnoreCase) || name.Contains("GPU", StringComparison.OrdinalIgnoreCase))
                        snap.GpuCoreTempC ??= v;
                    break;

                case SensorType.Power:
                    if (v > 0) snap.GpuPowerW = v;
                    break;

                case SensorType.Clock:
                    if (name.Contains("Memory", StringComparison.OrdinalIgnoreCase))
                        snap.GpuMemClockMHz = v;
                    else if (name.Contains("Core", StringComparison.OrdinalIgnoreCase) || name.Contains("GPU", StringComparison.OrdinalIgnoreCase))
                        snap.GpuCoreClockMHz = v;
                    break;

                case SensorType.Fan:
                    if (v > 0) snap.GpuFanRpm = v;
                    break;

                case SensorType.Control:
                    if (name.Contains("Fan", StringComparison.OrdinalIgnoreCase))
                        snap.GpuFanPercent = v;
                    break;

                case SensorType.SmallData:
                    if (name.Contains("Used", StringComparison.OrdinalIgnoreCase))
                        snap.GpuVramUsedMb = v;
                    else if (name.Contains("Total", StringComparison.OrdinalIgnoreCase))
                        snap.GpuVramTotalMb = v;
                    break;
            }
        }
    }

    private static void ReadBoardLive(IHardware board, LiveTelemetrySnapshot snap)
    {
        foreach (ISensor s in board.Sensors)
        {
            if (!s.Value.HasValue) continue;
            float v = s.Value.Value;
            string name = s.Name ?? string.Empty;

            if (s.SensorType == SensorType.Temperature)
            {
                if (name.Contains("VRM", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("MOS", StringComparison.OrdinalIgnoreCase))
                    snap.VrmMosTempC = v;
                else if (name.Contains("System", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("Chipset", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("PCH", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("Motherboard", StringComparison.OrdinalIgnoreCase))
                    snap.SystemTempC = v;
            }
            else if (s.SensorType == SensorType.Fan && v > 0)
            {
                if (name.Contains("CPU", StringComparison.OrdinalIgnoreCase))
                    snap.CpuFanRpm = v;
                else if (name.Contains("System", StringComparison.OrdinalIgnoreCase) || name.Contains("Chassis", StringComparison.OrdinalIgnoreCase))
                    snap.SystemFanRpm = v;
            }
        }
    }

    private static void FillRamLive(LiveTelemetrySnapshot snap)
    {
        try
        {
            var mem = new MEMORYSTATUSEX();
            if (!GlobalMemoryStatusEx(mem)) return;
            const double gb = 1024.0 * 1024 * 1024;
            snap.RamUsedGB = Math.Round((mem.ullTotalPhys - mem.ullAvailPhys) / gb, 1);
            snap.RamAvailableGB = Math.Round(mem.ullAvailPhys / gb, 1);
            snap.RamUsagePercent = mem.dwMemoryLoad;
            if (mem.ullTotalPageFile > mem.ullAvailPageFile)
                snap.CommitUsedGB = Math.Round((mem.ullTotalPageFile - mem.ullAvailPageFile) / gb, 1);
        }
        catch { }
    }

    /// <summary>Завантаження CPU через GetSystemTimes (fallback, якщо LHM не надав "CPU Total").</summary>
    private static double? ReadCpuLoadPercent()
    {
        lock (_cpuLoadLock)
        {
            try
            {
                if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;

                ulong idleNow = ToUInt64(idle);
                ulong kernNow = ToUInt64(kernel);
                ulong userNow = ToUInt64(user);

                if (_cpuIdlePrev is ulong iPrev && _cpuKernelPrev is ulong kPrev && _cpuUserPrev is ulong uPrev)
                {
                    ulong idleDelta = idleNow - iPrev;
                    ulong totalDelta = (kernNow - kPrev) + (userNow - uPrev);

                    _cpuIdlePrev = idleNow;
                    _cpuKernelPrev = kernNow;
                    _cpuUserPrev = userNow;

                    if (totalDelta > 0)
                        return Math.Clamp(Math.Round((1.0 - (double)idleDelta / totalDelta) * 100.0, 1), 0.0, 100.0);
                }
                else
                {
                    _cpuIdlePrev = idleNow;
                    _cpuKernelPrev = kernNow;
                    _cpuUserPrev = userNow;
                }
            }
            catch { }
            return null;
        }
    }

    #endregion

    #region Швидкий збір (бейджі шапки)

    private static readonly TimeSpan QuickHwCacheTtl = TimeSpan.FromDays(7);
    private const string QuickHwCacheFileName = "quick_hw_cache.json";

    private sealed class QuickHwCache
    {
        public string OS { get; set; } = string.Empty;
        public string CPU { get; set; } = string.Empty;
        public string GPU { get; set; } = string.Empty;
        public string GpuDriverFingerprint { get; set; } = string.Empty;
        public DateTime CachedAtUtc { get; set; }
    }

    private static string QuickHwCachePath => Path.Combine(AppPaths.Config, QuickHwCacheFileName);

    public static async Task<HardwareInfo> GetQuickHardwareInfoAsync()
    {
        return await Task.Run(() =>
        {
            var info = new HardwareInfo();
            var loc = LocalizationManager.Instance;

            var hwCache = LoadQuickHwCache();
            string driverFp = GetGpuDriverFingerprint();
            bool cacheFresh = hwCache != null
                && (DateTime.UtcNow - hwCache.CachedAtUtc) < QuickHwCacheTtl
                && !string.IsNullOrEmpty(hwCache.GPU)
                && string.Equals(hwCache.GpuDriverFingerprint, driverFp, StringComparison.OrdinalIgnoreCase);

            info.OS = cacheFresh && !string.IsNullOrEmpty(hwCache!.OS) ? hwCache.OS : DetectOsName();
            info.CPU = cacheFresh && !string.IsNullOrEmpty(hwCache!.CPU) ? hwCache.CPU : DetectCpuName();

            string? detectedGpu = cacheFresh ? hwCache!.GPU : QueryFirstGpuName();
            info.GPU = string.IsNullOrEmpty(detectedGpu) ? "GPU (—)" : detectedGpu;

            try
            {
                var mem = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(mem))
                {
                    double totalGb = mem.ullTotalPhys / (1024.0 * 1024 * 1024);
                    info.RAM = $"{Math.Round(totalGb, 1)} {loc["Common.UnitGB"]}";
                }
            }
            catch { info.RAM = "—"; }

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
            catch { info.DiskFree = "—"; }

            if (!cacheFresh && !string.IsNullOrEmpty(detectedGpu))
            {
                SaveQuickHwCache(new QuickHwCache
                {
                    OS = info.OS,
                    CPU = info.CPU,
                    GPU = info.GPU,
                    GpuDriverFingerprint = driverFp
                });
            }

            return info;
        });
    }

    private static string GetGpuDriverFingerprint()
    {
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (classKey == null) return string.Empty;

            var parts = new List<string>();
            foreach (string sub in classKey.GetSubKeyNames())
            {
                using var subKey = classKey.OpenSubKey(sub);
                string? drv = subKey?.GetValue("DriverVersion")?.ToString();
                if (!string.IsNullOrEmpty(drv)) parts.Add($"{sub}:{drv}");
            }
            parts.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join("|", parts);
        }
        catch { return string.Empty; }
    }

    private static QuickHwCache? LoadQuickHwCache()
    {
        try
        {
            string path = QuickHwCachePath;
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<QuickHwCache>(File.ReadAllText(path));
        }
        catch { return null; }
    }

    private static void SaveQuickHwCache(QuickHwCache cache)
    {
        try
        {
            cache.CachedAtUtc = DateTime.UtcNow;
            string tmp = QuickHwCachePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(cache), new UTF8Encoding(false));
            File.Move(tmp, QuickHwCachePath, overwrite: true);
        }
        catch { }
    }

    private static string DetectOsName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string prodName = key?.GetValue("ProductName")?.ToString()?.Replace("Microsoft", "").Trim() ?? "Windows";
            string build = key?.GetValue("CurrentBuild")?.ToString() ?? "26200";
            string displayVer = key?.GetValue("DisplayVersion")?.ToString() ?? "";

            if (int.TryParse(build, out int bNum) && bNum >= 22000)
                prodName = prodName.Replace("Windows 10", "Windows 11");

            return $"{prodName} {displayVer} (Build {build})".Trim();
        }
        catch { return "Windows"; }
    }

    private static string DetectCpuName()
    {
        try
        {
            using var cpuKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            string cpuName = cpuKey?.GetValue("ProcessorNameString")?.ToString() ?? "CPU";
            cpuName = CleanCpuName(cpuName);
            return $"{cpuName} ({Environment.ProcessorCount}T)";
        }
        catch { return "CPU (—)"; }
    }

    private static string? QueryFirstGpuName()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            using var results = searcher.Get();
            foreach (ManagementObject obj in results)
                using (obj)
                {
                    string gpuName = obj["Name"]?.ToString() ?? "";
                    if (!gpuName.Contains("Basic", StringComparison.OrdinalIgnoreCase) &&
                        !gpuName.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                        !gpuName.Contains("Remote", StringComparison.OrdinalIgnoreCase))
                    {
                        return gpuName.Replace("NVIDIA ", "").Replace("AMD ", "").Trim();
                    }
                }
        }
        catch { }
        return null;
    }

    #endregion

    #region Статичний апаратний аудит

    /// <summary>Повний статичний аудит заліза. Кожна підсистема — власний try/catch.</summary>
    public static async Task<StaticHardwareSpecs> GetStaticHardwareSpecsAsync()
    {
        return await Task.Run(() =>
        {
            var s = new StaticHardwareSpecs();
            CollectOsSecurity(s);
            CollectCpuStatic(s);
            CollectGpuStatic(s);
            CollectMemoryStatic(s);
            CollectStorageStatic(s);
            CollectBoardBios(s);
            CollectNetwork(s);
            return s;
        });
    }

    // ---------------- 1. ОС та безпека ----------------

    private static void CollectOsSecurity(StaticHardwareSpecs s)
    {
        var loc = LocalizationManager.Instance;

        // ОС
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string prod = key?.GetValue("ProductName")?.ToString()?.Replace("Microsoft", "").Trim() ?? "Windows 11";
            string display = key?.GetValue("DisplayVersion")?.ToString() ?? "";
            string build = key?.GetValue("CurrentBuild")?.ToString() ?? "";
            string ubr = key?.GetValue("UBR")?.ToString() ?? "";

            if (int.TryParse(build, out int b) && b >= 22000)
                prod = prod.Replace("Windows 10", "Windows 11");

            s.OSCaption = prod;
            s.OSDisplayVersion = string.IsNullOrWhiteSpace(display) ? "—" : display;
            s.OSBuild = string.IsNullOrWhiteSpace(build) ? "—" : (string.IsNullOrWhiteSpace(ubr) ? build : $"{build}.{ubr}");
            s.OSArch = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";
        }
        catch { }

        // Uptime
        try
        {
            var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
            s.Uptime = loc.Format("Diagnostic.UptimeFormat", up.Days, up.Hours, up.Minutes);
        }
        catch { }

        // DirectX
        try { s.DirectXVersion = DetectDirectXVersion(); } catch { }

        // Secure Boot
        try
        {
            var sb = ReadSecureBootState();
            if (sb != SecureBootState.Unknown)
                s.SecureBoot = sb == SecureBootState.Enabled ? loc["Diagnostic.On"] : loc["Diagnostic.Off"];
        }
        catch { }

        // VBS / HVCI
        try
        {
            using var dg = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard");
            bool vbs = Convert.ToInt32(dg?.GetValue("EnableVirtualizationBasedSecurity") ?? 0) == 1;

            bool hvci = false;
            using var scen = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity");
            if (scen?.GetValue("Enabled") is int hv) hvci = hv == 1;

            string vbsTxt = vbs ? loc["Diagnostic.On"] : loc["Diagnostic.Off"];
            if (hvci) vbsTxt += " / HVCI";
            s.VBS = vbsTxt;
        }
        catch { }

        // TPM
        try { s.TPM = GetTpmDisplay(); } catch { }

        // Схема живлення
        try { s.PowerPlan = GetActivePowerPlan(); } catch { }
    }

    private static string DetectDirectXVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string build = key?.GetValue("CurrentBuild")?.ToString() ?? "";
            if (int.TryParse(build, out int b) && b >= 22000) return "DirectX 12 Ultimate";

            string sys = Environment.SystemDirectory;
            if (File.Exists(Path.Combine(sys, "d3d12.dll"))) return "DirectX 12";
            if (File.Exists(Path.Combine(sys, "d3d11.dll"))) return "DirectX 11";
        }
        catch { }
        return "—";
    }

    private static SecureBootState ReadSecureBootState()
    {
        if (_cachedSecureBoot.HasValue) return _cachedSecureBoot.Value;

        lock (_securityCacheLock)
        {
            if (_cachedSecureBoot.HasValue) return _cachedSecureBoot.Value;

            try
            {
                using var sbKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
                int sb = Convert.ToInt32(sbKey?.GetValue("UEFISecureBootEnabled") ?? -1);
                if (sb == 1) _cachedSecureBoot = SecureBootState.Enabled;
                else if (sb == 0) _cachedSecureBoot = SecureBootState.Disabled;
                else
                {
                    int fwType = -1;
                    GetFirmwareType(ref fwType);
                    _cachedSecureBoot = fwType == FirmwareTypeUefi ? SecureBootState.Enabled : SecureBootState.Disabled;
                }
                return _cachedSecureBoot.Value;
            }
            catch { return SecureBootState.Unknown; }
        }
    }

    private static TpmState ReadTpmState()
    {
        if (_cachedTpm.HasValue) return _cachedTpm.Value;

        lock (_securityCacheLock)
        {
            if (_cachedTpm.HasValue) return _cachedTpm.Value;

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT IsEnabled_InitialValue, IsActivated_InitialValue, SpecVersion FROM Win32_Tpm");
                using var results = searcher.Get();
                foreach (ManagementObject o in results)
                    using (o)
                    {
                        bool enabled = false, activated = false;
                        try { enabled = Convert.ToBoolean(o["IsEnabled_InitialValue"] ?? false); } catch { }
                        try { activated = Convert.ToBoolean(o["IsActivated_InitialValue"] ?? false); } catch { }
                        try { _cachedTpmSpec = o["SpecVersion"]?.ToString() ?? string.Empty; } catch { }

                        _cachedTpm = enabled && activated ? TpmState.Ready : TpmState.Disabled;
                        return _cachedTpm.Value;
                    }
            }
            catch { }

            _cachedTpm = TpmState.Unavailable;
            return _cachedTpm.Value;
        }
    }

    private static string GetTpmDisplay()
    {
        var loc = LocalizationManager.Instance;
        string version = "2.0";
        if (!string.IsNullOrWhiteSpace(_cachedTpmSpec))
        {
            string spec = _cachedTpmSpec.Split(',')[0].Trim();
            if (spec.StartsWith("1", StringComparison.Ordinal)) version = "1.2";
        }

        return ReadTpmState() switch
        {
            TpmState.Ready => $"TPM {version} ({loc["Diagnostic.TpmReady"]})",
            TpmState.Disabled => $"TPM {version} ({loc["Diagnostic.TpmDisabled"]})",
            _ => loc["Diagnostic.TpmUnavailable"],
        };
    }

    // ---------------- 2. Процесор (статика) ----------------

    private static void CollectCpuStatic(StaticHardwareSpecs s)
    {
        var loc = LocalizationManager.Instance;

        // Назва + базова частота з реєстру
        try
        {
            using var cpuKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            string regName = cpuKey?.GetValue("ProcessorNameString")?.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(regName)) s.CPUModel = CleanCpuName(regName);
            if (cpuKey?.GetValue("~MHz") is int baseMhz && baseMhz > 0) s.CPUBaseClockMHz = baseMhz;
        }
        catch { }

        // WMI: ядра, потоки, сокет, архітектура, частоти, віртуалізація
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, SocketDesignation, " +
                "Architecture, VirtualizationFirmwareEnabled, L2CacheSize, L3CacheSize FROM Win32_Processor");

            using var results = searcher.Get();
            foreach (ManagementObject obj in results)
                using (obj)
                {
                    if (s.CPUModel is "—" or "")
                        s.CPUModel = CleanCpuName(obj["Name"]?.ToString() ?? "CPU");

                    try { s.CPUCores = Convert.ToInt32(obj["NumberOfCores"] ?? 0); } catch { }
                    try { s.CPUThreads = Convert.ToInt32(obj["NumberOfLogicalProcessors"] ?? 0); } catch { }

                    s.CPUSocket = obj["SocketDesignation"]?.ToString()?.Trim() ?? "—";

                    if (s.CPUMaxClockMHz <= 0 &&
                        double.TryParse(obj["MaxClockSpeed"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double maxMhz))
                        s.CPUMaxClockMHz = maxMhz;

                    if (s.CPUBaseClockMHz <= 0 &&
                        double.TryParse(obj["CurrentClockSpeed"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double curMhz))
                        s.CPUBaseClockMHz = curMhz;

                    try
                    {
                        int arch = Convert.ToInt32(obj["Architecture"] ?? 0);
                        s.CPUArchitecture = MapCpuArchitecture(arch);
                    }
                    catch { }

                    bool virt = false;
                    try { virt = Convert.ToBoolean(obj["VirtualizationFirmwareEnabled"] ?? false); } catch { }
                    s.CPUVirtual = virt ? loc["Diagnostic.CpuVirtualOn"] : loc["Diagnostic.CpuVirtualOff"];

                    // Кеш L2/L3 з Win32_Processor (KB) — fallback, якщо CacheMemory не дасть
                    if (double.TryParse(obj["L2CacheSize"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double l2Kb) && l2Kb > 0)
                        s.CPUL2Cache = FormatCacheKb((long)l2Kb);
                    if (double.TryParse(obj["L3CacheSize"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double l3Kb) && l3Kb > 0)
                        s.CPUL3Cache = FormatCacheKb((long)l3Kb);

                    break;
                }
        }
        catch { }

        // Точні кеші L1/L2/L3 із Win32_CacheMemory (сумування за рівнем)
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Level, MaxCacheSize FROM Win32_CacheMemory");
            using var results = searcher.Get();

            long l1 = 0, l2 = 0, l3 = 0;
            foreach (ManagementObject o in results)
                using (o)
                {
                    int level;
                    long kb;
                    try { level = Convert.ToInt32(o["Level"] ?? 0); } catch { continue; }
                    try { kb = Convert.ToInt64(o["MaxCacheSize"] ?? 0); } catch { continue; }
                    if (kb <= 0) continue;
                    switch (level)
                    {
                        case 3: l1 += kb; break;
                        case 4: l2 += kb; break;
                        case 5: l3 += kb; break;
                    }
                }

            if (l1 > 0) s.CPUL1Cache = FormatCacheKb(l1);
            if (l2 > 0) s.CPUL2Cache = FormatCacheKb(l2);
            if (l3 > 0) s.CPUL3Cache = FormatCacheKb(l3);
        }
        catch { }

        // Fallback назви/ядер із LHM
        if (s.CPUModel is "—" or "" || s.CPUCores <= 0)
        {
            lock (_lhmLock)
            {
                var computer = _computer;
                if (computer != null)
                {
                    try { computer.Accept(_visitor); } catch { }
                    var cpu = EnumerateAll(computer).FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
                    if (cpu != null)
                    {
                        if (s.CPUModel is "—" or "" && !string.IsNullOrWhiteSpace(cpu.Name))
                            s.CPUModel = CleanCpuName(cpu.Name);
                        if (s.CPUThreads <= 0) s.CPUThreads = Environment.ProcessorCount;
                        if (s.CPUCores <= 0) s.CPUCores = Math.Max(1, s.CPUThreads / 2);
                    }
                }
            }
        }
    }

    private static string MapCpuArchitecture(int arch) => arch switch
    {
        0 => "x86 (32-bit)",
        5 => "ARM",
        6 => "Itanium",
        9 => "x64 (AMD64)",
        12 => "ARM64",
        _ => "—",
    };

    private static string FormatCacheKb(long kb)
    {
        if (kb <= 0) return "—";
        if (kb >= 1024 * 1024) return $"{kb / (1024.0 * 1024):0.##} GB";
        if (kb >= 1024) return $"{kb / 1024.0:0.##} MB";
        return $"{kb} KB";
    }

    private static string CleanCpuName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        return raw.Replace("(R)", "").Replace("(TM)", "").Replace("(r)", "")
                  .Replace("Processor", "").Replace("processor", "")
                  .Replace("Core(TM)", "").Replace("  ", " ")
                  .Trim();
    }

    // ---------------- 3. Відеокарти та монітори (статика) ----------------

    private static void CollectGpuStatic(StaticHardwareSpecs s)
    {
        s.Gpus.Clear();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, AdapterRAM, DriverVersion, DriverDate, PNPDeviceID FROM Win32_VideoController");

            using var results = searcher.Get();
            foreach (ManagementObject obj in results)
                using (obj)
                {
                    string name = obj["Name"]?.ToString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (name.Contains("Basic", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Remote", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("RDP", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string pnpId = obj["PNPDeviceID"]?.ToString() ?? string.Empty;

                    long vram = ReadVramForAdapter(name);
                    if (vram <= 0)
                    {
                        try { vram = Convert.ToUInt32(obj["AdapterRAM"] ?? 0u); } catch { }
                    }

                    string driverDate = ReadGpuDriverDateFromRegistry(name);
                    if (driverDate is "—" or "")
                        driverDate = FormatWmiDate(obj["DriverDate"]?.ToString());

                    s.Gpus.Add(new GpuAdapterInfo
                    {
                        Name = name,
                        VramBytes = vram,
                        DriverVersion = obj["DriverVersion"]?.ToString() ?? "—",
                        DriverDate = driverDate,
                        IsIntegrated = IsIntegratedGpu(name, pnpId),
                        PnpDeviceId = pnpId,
                        BusInterface = pnpId.Contains("PCI", StringComparison.OrdinalIgnoreCase) ? "PCI Express" : "—",
                        ReBar = DetectReBar(name),
                    });
                }
        }
        catch { }

        // Fallback назв GPU із LHM
        if (s.Gpus.Count == 0)
        {
            lock (_lhmLock)
            {
                var computer = _computer;
                if (computer != null)
                {
                    try { computer.Accept(_visitor); } catch { }
                    foreach (var hw in EnumerateAll(computer).Where(h =>
                                 h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel))
                    {
                        if (string.IsNullOrWhiteSpace(hw.Name)) continue;
                        s.Gpus.Add(new GpuAdapterInfo
                        {
                            Name = hw.Name,
                            IsIntegrated = hw.HardwareType == HardwareType.GpuIntel,
                            VramBytes = ReadVramForAdapter(hw.Name),
                        });
                    }
                }
            }
        }

        // Монітори
        try { s.Displays = GetActiveMonitorsNative(); } catch { }
    }

    /// <summary>Зчитує точну 64-біт VRAM для адаптера з реєстру класу відео.</summary>
    private static long ReadVramForAdapter(string driverDesc)
    {
        try
        {
            const string keyPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            using var baseKey = Registry.LocalMachine.OpenSubKey(keyPath);
            if (baseKey == null) return 0;

            foreach (string sub in baseKey.GetSubKeyNames().Where(x => x.Length == 4))
            {
                using var subKey = baseKey.OpenSubKey(sub);
                if (subKey == null) continue;

                string desc = subKey.GetValue("DriverDesc")?.ToString() ?? string.Empty;
                if (!string.Equals(desc, driverDesc, StringComparison.OrdinalIgnoreCase)) continue;

                var qMem = subKey.GetValue("HardwareInformation.qwMemorySize");
                if (qMem is long vram64 && vram64 > 0) return vram64;
                if (qMem is byte[] bytes && bytes.Length == 8)
                {
                    long v = BitConverter.ToInt64(bytes, 0);
                    if (v > 0) return v;
                }

                var dMem = subKey.GetValue("HardwareInformation.MemorySize");
                if (dMem is int vram32 && vram32 > 0) return (uint)vram32;
            }
        }
        catch { }
        return 0;
    }

    private static string ReadGpuDriverDateFromRegistry(string driverDesc)
    {
        try
        {
            const string keyPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            using var baseKey = Registry.LocalMachine.OpenSubKey(keyPath);
            if (baseKey == null) return "—";

            foreach (string sub in baseKey.GetSubKeyNames().Where(x => x.Length == 4))
            {
                using var subKey = baseKey.OpenSubKey(sub);
                if (subKey == null) continue;

                string desc = subKey.GetValue("DriverDesc")?.ToString() ?? string.Empty;
                if (!string.Equals(desc, driverDesc, StringComparison.OrdinalIgnoreCase)) continue;

                if (subKey.GetValue("DriverDateData") is byte[] data && data.Length >= 8)
                {
                    long fileTime = BitConverter.ToInt64(data, 0);
                    if (fileTime > 0)
                        return DateTime.FromFileTimeUtc(fileTime).ToLocalTime().ToString("dd.MM.yyyy");
                }

                string raw = subKey.GetValue("DriverDate")?.ToString() ?? string.Empty;
                if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    return dt.ToString("dd.MM.yyyy");
            }
        }
        catch { }
        return "—";
    }

    private static string FormatWmiDate(string? wmiDate)
    {
        if (string.IsNullOrWhiteSpace(wmiDate) || wmiDate.Length < 8) return "—";
        try
        {
            return $"{wmiDate.Substring(6, 2)}.{wmiDate.Substring(4, 2)}.{wmiDate.Substring(0, 4)}";
        }
        catch { return "—"; }
    }

    private static bool IsIntegratedGpu(string name, string pnpId)
    {
        string n = (name ?? string.Empty).ToLowerInvariant();
        string p = (pnpId ?? string.Empty).ToLowerInvariant();

        if (n.Contains("microsoft basic") || n.Contains("virtual") || n.Contains("remote")) return false;

        if (n.Contains("intel") && (n.Contains("uhd") || n.Contains("hd graphics") || n.Contains("iris") || n.Contains("arc")))
            return true;

        if (n.Contains("radeon") && !n.Contains("rx") && (n.Contains("graphics") || n.EndsWith("m")))
            return true;

        if (n.Contains("radeon") && n.Contains("vega")) return true;

        if (p.Contains("ven_8086") && n.Contains("intel")) return true;

        return false;
    }

    private static string DetectReBar(string gpuName)
    {
        var loc = LocalizationManager.Instance;
        string n = (gpuName ?? string.Empty).ToLowerInvariant();

        bool capable =
            n.Contains("rtx 30") || n.Contains("rtx 40") || n.Contains("rtx 50") ||
            n.Contains("rx 6") || n.Contains("rx 7") || n.Contains("rx 9") ||
            n.Contains("arc ");

        return capable ? loc["Diagnostic.ReBarCapable"] : "—";
    }

    // ---------------- 4. Оперативна пам'ять (статика) ----------------

    private static void CollectMemoryStatic(StaticHardwareSpecs s)
    {
        var loc = LocalizationManager.Instance;

        try
        {
            var mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem))
                s.RAMTotalGB = Math.Round(mem.ullTotalPhys / (1024.0 * 1024 * 1024), 1);
        }
        catch { }

        try
        {
            using var memSearcher = new ManagementObjectSearcher(
                "SELECT DeviceLocator, BankLabel, Capacity, Speed, ConfiguredClockSpeed, Manufacturer, PartNumber, " +
                "SMBIOSMemoryType, MemoryType, FormFactor FROM Win32_PhysicalMemory");

            using var modules = memSearcher.Get();
            s.Dimms.Clear();
            s.RAMSlotsUsed = modules.Count;

            string ramType = "—";
            string formFactor = "—";
            double bestSpeed = 0;

            foreach (ManagementObject m in modules)
                using (m)
                {
                    double capGb = 0;
                    try { capGb = Math.Round(Convert.ToDouble(m["Capacity"] ?? 0, CultureInfo.InvariantCulture) / (1024.0 * 1024 * 1024), 0); } catch { }

                    string slot = m["DeviceLocator"]?.ToString()?.Trim() ?? m["BankLabel"]?.ToString()?.Trim() ?? "DIMM";
                    string part = (m["PartNumber"]?.ToString() ?? string.Empty).Trim();
                    string manufacturer = (m["Manufacturer"]?.ToString() ?? string.Empty).Trim();

                    uint smbiosType = 0;
                    try { smbiosType = Convert.ToUInt32(m["SMBIOSMemoryType"] ?? 0u); } catch { }
                    string memType = MapSmbiosMemoryType(smbiosType);
                    if (memType.Length == 0)
                    {
                        uint legacyType = 0;
                        try { legacyType = Convert.ToUInt32(m["MemoryType"] ?? 0u); } catch { }
                        memType = MapLegacyMemoryType(legacyType);
                    }

                    double speedMts = 0;
                    if (double.TryParse(m["ConfiguredClockSpeed"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double cfg) && cfg > 0)
                        speedMts = cfg;
                    else if (double.TryParse(m["Speed"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double spd) && spd > 0)
                        speedMts = spd;

                    string ff = MapFormFactor(Convert.ToInt32(m["FormFactor"] ?? 0));

                    if (memType.Length > 0) ramType = memType;
                    if (ff != "—") formFactor = ff;
                    if (speedMts > bestSpeed) bestSpeed = speedMts;

                    s.Dimms.Add(new DimmSlotInfo
                    {
                        Slot = slot,
                        CapacityGB = capGb,
                        MemoryType = memType,
                        SpeedMTs = speedMts,
                        FormFactor = ff,
                        Manufacturer = manufacturer,
                        PartNumber = part,
                    });
                }

            s.RAMType = ramType;
            s.RAMFormFactor = formFactor;
            s.RAMSpeedMTs = bestSpeed;

            try
            {
                using var arraySearcher = new ManagementObjectSearcher("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray");
                using var arrayResults = arraySearcher.Get();
                foreach (ManagementObject a in arrayResults)
                    using (a)
                    {
                        s.RAMSlotsTotal = Convert.ToInt32(a["MemoryDevices"] ?? 0);
                        break;
                    }
            }
            catch { }
        }
        catch { }
    }

    private static string MapFormFactor(int ff) => ff switch
    {
        8 => "DIMM",
        12 => "SODIMM",
        13 => "SRIMM",
        11 => "RIMM",
        _ => "—",
    };

    private static string MapSmbiosMemoryType(uint type) => type switch
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
        _ => string.Empty,
    };

    private static string MapLegacyMemoryType(uint type) => type switch
    {
        0x14 => "DDR",
        0x18 => "DDR3",
        0x1A => "DDR4",
        0x22 => "DDR5",
        0x1B => "LPDDR3",
        0x1E => "LPDDR4",
        0x23 => "LPDDR5",
        _ => string.Empty,
    };

    // ---------------- 5. Накопичувачі (статика + S.M.A.R.T.) ----------------

    private static void CollectStorageStatic(StaticHardwareSpecs s)
    {
        s.Volumes.Clear();
        s.Disks.Clear();

        try
        {
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                double tot = Math.Round(d.TotalSize / (1024.0 * 1024 * 1024), 1);
                double fr = Math.Round(d.TotalFreeSpace / (1024.0 * 1024 * 1024), 1);
                double us = Math.Round(tot - fr, 1);
                int pct = tot > 0 ? (int)((us / tot) * 100) : 0;

                s.Volumes.Add(new DiskVolumeInfo
                {
                    Name = d.Name.TrimEnd('\\'),
                    Label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? string.Empty : d.VolumeLabel,
                    TotalGB = tot,
                    FreeGB = fr,
                    UsedGB = us,
                    PercentUsed = pct,
                    Format = d.DriveFormat,
                });
            }
        }
        catch { }

        var kindMap = QueryPhysicalDiskKinds();

        try
        {
            using var diskSearcher = new ManagementObjectSearcher(
                "SELECT Model, Size, InterfaceType, PNPDeviceID FROM Win32_DiskDrive");

            using var diskResults = diskSearcher.Get();
            foreach (ManagementObject pd in diskResults)
                using (pd)
                {
                    string iface = pd["InterfaceType"]?.ToString() ?? string.Empty;
                    if (iface.Contains("USB", StringComparison.OrdinalIgnoreCase)) continue; // зовнішні — геть з аудиту

                    string model = pd["Model"]?.ToString()?.Trim() ?? "SSD";
                    double sizeGb = 0;
                    try { sizeGb = Math.Round(Convert.ToDouble(pd["Size"] ?? 0, CultureInfo.InvariantCulture) / (1024.0 * 1024 * 1024), 0); } catch { }
                    string pnpId = pd["PNPDeviceID"]?.ToString() ?? string.Empty;

                    var (kindInterface, isNvme, isSsd, isHdd) = ResolveDiskKind(model, iface, pnpId, kindMap);

                    s.Disks.Add(new PhysicalDiskInfo
                    {
                        Model = model,
                        SizeGB = sizeGb,
                        Interface = kindInterface,
                        IsNvme = isNvme,
                        IsSsd = isSsd,
                        IsHdd = isHdd,
                        IsUsb = false,
                        MediaType = isSsd ? "SSD" : (isHdd ? "HDD" : string.Empty),
                        SmartOk = GetSmartStatus(pnpId),
                    });
                }
        }
        catch { }

        EnrichStorageFromLhm(s.Disks);
    }

    private static void EnrichStorageFromLhm(List<PhysicalDiskInfo> disks)
    {
        if (disks.Count == 0) return;

        lock (_lhmLock)
        {
            var computer = _computer;
            if (computer == null) return;

            try { computer.Accept(_visitor); } catch { }

            try
            {
                foreach (var hw in EnumerateAll(computer).Where(h => h.HardwareType == HardwareType.Storage))
                {
                    var disk = MatchDisk(disks, hw.Name);
                    if (disk == null) continue;

                    foreach (ISensor sensor in hw.Sensors)
                    {
                        if (!sensor.Value.HasValue) continue;
                        string name = sensor.Name ?? string.Empty;
                        float v = sensor.Value.Value;

                        if (sensor.SensorType == SensorType.Temperature && name.Contains("Temperature", StringComparison.OrdinalIgnoreCase))
                            disk.TemperatureC ??= v;
                        else if (name.Contains("Remaining Life", StringComparison.OrdinalIgnoreCase) ||
                                 name.Equals("Life", StringComparison.OrdinalIgnoreCase))
                            disk.RemainingLifePercent = v;
                        else if (name.Contains("Total Bytes Written", StringComparison.OrdinalIgnoreCase) ||
                                 name.Contains("Data Written", StringComparison.OrdinalIgnoreCase))
                            disk.TotalBytesWrittenGb = v;
                        else if (name.Contains("Power On Hours", StringComparison.OrdinalIgnoreCase))
                            disk.PowerOnHours = v;
                    }
                }
            }
            catch { }
        }
    }

    private static PhysicalDiskInfo? MatchDisk(List<PhysicalDiskInfo> disks, string? hwName)
    {
        if (string.IsNullOrWhiteSpace(hwName)) return null;
        string target = Normalize(hwName);

        return disks.FirstOrDefault(d =>
            Normalize(d.Model).Contains(target, StringComparison.OrdinalIgnoreCase) ||
            target.Contains(Normalize(d.Model), StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Replace(" ", "").Trim();

    private static Dictionary<string, (int MediaType, int BusType)> QueryPhysicalDiskKinds()
    {
        var map = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var scope = new ManagementScope(@"\\.\ROOT\Microsoft\Windows\Storage");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT DeviceId, FriendlyName, MediaType, BusType FROM MSFT_PhysicalDisk"));
            using var results = searcher.Get();
            foreach (ManagementObject o in results)
                using (o)
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

    private static (string Interface, bool IsNvme, bool IsSsd, bool IsHdd) ResolveDiskKind(
        string model, string interfaceType, string pnpId, Dictionary<string, (int MediaType, int BusType)> kindMap)
    {
        bool isUsb = interfaceType.IndexOf("USB", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     pnpId.IndexOf("USBSTOR", StringComparison.OrdinalIgnoreCase) >= 0;

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
            bool nvme = found.Value.BusType == 18;
            bool ssd = found.Value.MediaType == 4;
            bool hdd = found.Value.MediaType == 3;
            string iface = nvme ? "NVMe" : (isUsb ? "USB" : "SATA");
            return (iface, nvme, ssd, hdd);
        }

        bool ifNvme = interfaceType.IndexOf("NVMe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      model.IndexOf("NVMe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      pnpId.IndexOf("NVME", StringComparison.OrdinalIgnoreCase) >= 0;
        bool ifSata = interfaceType.IndexOf("SATA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      interfaceType.IndexOf("SCSI", StringComparison.OrdinalIgnoreCase) >= 0;
        string fallbackIface = ifNvme ? "NVMe" : (isUsb ? "USB" : "SATA");

        bool isSsdFallback = ifNvme;
        bool isHddFallback = !ifNvme && !ifSata && !isUsb;
        return (fallbackIface, ifNvme, isSsdFallback, isHddFallback);
    }

    private static bool GetSmartStatus(string pnpId)
    {
        try
        {
            var scope = new ManagementScope(@"\\.\ROOT\WMI");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus"));

            string pnpTail = (pnpId ?? string.Empty).Split('\\').LastOrDefault() ?? string.Empty;

            using var results = searcher.Get();
            foreach (ManagementObject o in results)
                using (o)
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
        return true;
    }

    // ---------------- 6. Плата та BIOS ----------------

    private static void CollectBoardBios(StaticHardwareSpecs s)
    {
        try
        {
            using var bSearcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
            using var boardResults = bSearcher.Get();
            foreach (ManagementObject b in boardResults)
                using (b)
                {
                    s.BoardVendor = b["Manufacturer"]?.ToString()?.Trim() ?? "";
                    s.BoardModel = b["Product"]?.ToString()?.Trim() ?? "—";
                    break;
                }
        }
        catch { }

        try
        {
            using var biosSearcher = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion, ReleaseDate, Manufacturer FROM Win32_BIOS");
            using var biosResults = biosSearcher.Get();
            foreach (ManagementObject bios in biosResults)
                using (bios)
                {
                    s.BIOSVersion = bios["SMBIOSBIOSVersion"]?.ToString() ?? "—";
                    s.BIOSVendor = bios["Manufacturer"]?.ToString()?.Trim() ?? "—";
                    if (string.IsNullOrWhiteSpace(s.BoardVendor))
                        s.BoardVendor = s.BIOSVendor;

                    s.BIOSDate = FormatWmiDate(bios["ReleaseDate"]?.ToString());
                    break;
                }
        }
        catch { }
    }

    // ---------------- 7. Мережа ----------------

    private static void CollectNetwork(StaticHardwareSpecs s)
    {
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
            if (targetNic == null) return;

            s.NetAdapterName = targetNic.Description.Replace("(R)", "").Trim();

            try
            {
                var mac = targetNic.GetPhysicalAddress();
                string macStr = mac?.ToString() ?? string.Empty;
                if (macStr.Length == 12)
                    macStr = string.Join(":", Enumerable.Range(0, 6).Select(i => macStr.Substring(i * 2, 2)));
                s.NetMac = string.IsNullOrWhiteSpace(macStr) ? "—" : macStr;
            }
            catch { }

            double speedGbps = targetNic.Speed / 1_000_000_000.0;
            s.NetLinkSpeed = speedGbps >= 1.0 ? $"{speedGbps:N1} Gbps" : $"{targetNic.Speed / 1_000_000} Mbps";

            var ipProps = targetNic.GetIPProperties();
            var ipv4 = ipProps.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
            s.NetIPv4 = ipv4?.Address.ToString() ?? "—";
        }
        catch { }
    }

    #endregion

    #region Допоміжні

    private static string GetActivePowerPlan()
    {
        try
        {
            var psi = new ProcessStartInfo("powercfg", "/getactivescheme")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc == null) return "—";

            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(1000);

            int open = output.IndexOf('(');
            int close = open >= 0 ? output.IndexOf(')', open + 1) : -1;
            if (open >= 0 && close > open) return output.Substring(open + 1, close - open - 1).Trim();
            if (!string.IsNullOrWhiteSpace(output)) return output.Trim();
        }
        catch { }
        return "—";
    }

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
                            IsPrimary = isPrimary,
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

    public static string GenerateTextReport(StaticHardwareSpecs hw, LiveTelemetrySnapshot live)
    {
        var loc = LocalizationManager.Instance;
        string sep = loc["Diagnostic.ReportSeparator"];
        var sb = new StringBuilder();

        sb.AppendLine(sep);
        sb.AppendLine("  " + loc["Diagnostic.ReportTitle"]);
        sb.AppendLine(sep);
        sb.AppendLine($"{loc["Diagnostic.ReportDate"]}  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"{loc["Diagnostic.ReportOs"]}  {hw.OsTitleDisplay} ({hw.OSArch})");
        sb.AppendLine($"{loc["Diagnostic.ReportUptime"]}  {hw.Uptime}");
        sb.AppendLine($"{loc["Diagnostic.LblDirectX"]}  {hw.DirectXVersion}");
        sb.AppendLine($"{loc["Diagnostic.LblPowerPlan"]}  {hw.PowerPlan}");
        sb.AppendLine($"{loc["Diagnostic.LblSecureBoot"]}  {hw.SecureBoot} | {hw.VBS} | {hw.TPM}");
        sb.AppendLine();

        sb.AppendLine(loc["Diagnostic.ReportCpu"]);
        sb.AppendLine($"{loc["Diagnostic.ReportCpuModel"]}  {hw.CPUModel} ({hw.CPUArchitecture}, {hw.CPUSocket})");
        sb.AppendLine($"{loc["Diagnostic.ReportCpuConfig"]}  {hw.CpuCoresDisplay} | {hw.CpuCacheDisplay}");
        sb.AppendLine($"{loc["Diagnostic.LblBaseFreq"]}  {FmtMhz(hw.CPUBaseClockMHz)} | {loc["Diagnostic.LblBoostFreq"]} {FmtMhz(hw.CPUMaxClockMHz)}");
        sb.AppendLine($"  {hw.CPUVirtual}");
        sb.AppendLine($"  {loc["Diagnostic.BadgeLoad"]} {live.CpuLoadDisplay} | {loc["Diagnostic.LblTemp"]} {live.CpuPackageTempDisplay} | " +
                      $"{loc["Diagnostic.LblPower"]} {live.CpuPowerDisplay} | {loc["Diagnostic.LblVcore"]} {live.CpuVcoreDisplay} | " +
                      $"{loc["Diagnostic.LblClock"]} {live.CpuClockDisplay}");
        sb.AppendLine();

        sb.AppendLine(loc["Diagnostic.ReportGpu"]);
        foreach (var g in hw.Gpus)
            sb.AppendLine($"  • {g.Name} [{g.KindDisplay}] — VRAM: {g.VramDisplay} | Driver: {g.DriverVersion} ({g.DriverDate}) | {g.BusInterface}");
        sb.AppendLine($"  {loc["Diagnostic.BadgeLoad"]} {live.GpuLoadDisplay} | {loc["Diagnostic.LblGpuTemp"]} {live.GpuCoreTempDisplay} | " +
                      $"{loc["Diagnostic.LblHotspot"]} {live.GpuHotspotTempDisplay} | {loc["Diagnostic.LblVramTemp"]} {live.GpuVramTempDisplay}");
        sb.AppendLine($"  {loc["Diagnostic.LblPower"]} {live.GpuPowerDisplay} | {loc["Diagnostic.LblCoreClock"]} {live.GpuCoreClockDisplay} | " +
                      $"{loc["Diagnostic.LblMemClock"]} {live.GpuMemClockDisplay} | {loc["Diagnostic.LblFan"]} {live.GpuFanDisplay} | VRAM: {live.GpuVramUsedDisplay}");
        sb.AppendLine(loc["Diagnostic.ReportGpuDisplays"]);
        foreach (var d in hw.Displays) sb.AppendLine($"  • {d}");
        sb.AppendLine();

        sb.AppendLine(loc["Diagnostic.ReportRam"]);
        sb.AppendLine($"{loc["Diagnostic.ReportRamCapacity"]}  {hw.RAMTotalGB:N1} {loc["Common.UnitGB"]} {hw.RamSpeedDisplay} ({hw.RAMFormFactor})");
        sb.AppendLine($"{loc["Diagnostic.ReportRamUsage"]}  {live.RamUsedDisplay} / {live.RamAvailableDisplay} ({live.RamUsageDisplay}) | Commit: {live.CommitDisplay}");
        sb.AppendLine($"{loc["Diagnostic.LblRamSlots"]}  {hw.RAMSlotsUsed} / {hw.RAMSlotsTotal}");
        sb.AppendLine(loc["Diagnostic.ReportRamModules"]);
        foreach (var dimm in hw.Dimms) sb.AppendLine($"  • {FormatDimm(dimm)}");
        sb.AppendLine();

        sb.AppendLine(loc["Diagnostic.ReportStorage"]);
        foreach (var d in hw.Disks)
            sb.AppendLine($"  • {d.Model} — {d.SizeGB:0} {loc["Common.UnitGB"]} ({d.TypeLabel}) | {d.TempDisplay} | S.M.A.R.T.: {d.SmartLabel} | Life: {d.LifeDisplay}");
        foreach (var v in hw.Volumes)
            sb.AppendLine($"  {loc.Format("Diagnostic.VolumeFormat", v.Name, v.Label, v.FreeGB.ToString("N1"), v.TotalGB.ToString("N1"), v.PercentUsed, v.Format)}");
        sb.AppendLine();

        sb.AppendLine(loc["Diagnostic.ReportBoard"]);
        sb.AppendLine($"{loc["Diagnostic.ReportBoardInfo"]}  {hw.BoardVendor} {hw.BoardModel}".Trim());
        sb.AppendLine($"{loc["Diagnostic.ReportBios"]}  {hw.BIOSVendor} {hw.BIOSVersion} ({hw.BIOSDate})");
        sb.AppendLine($"  {loc["Diagnostic.LblBoardSensors"]}  VRM {live.VrmTempDisplay} | {loc["Diagnostic.LblTemp"]} {live.SystemTempDisplay}");
        sb.AppendLine($"  {loc["Diagnostic.LblBoardFans"]}  CPU {live.CpuFanDisplay} | System {live.SystemFanDisplay}");
        sb.AppendLine();

        sb.AppendLine(loc["Diagnostic.ReportNetwork"]);
        sb.AppendLine($"{loc["Diagnostic.ReportNetAdapter"]}  {hw.NetAdapterName} ({hw.NetLinkSpeed}, {hw.NetMac})");
        sb.AppendLine($"{loc["Diagnostic.ReportNetIp"]}  {hw.NetIPv4}");
        sb.AppendLine(sep);

        return sb.ToString();
    }

    private static string FmtMhz(double mhz) => mhz > 0 ? $"{mhz:0} MHz" : "—";

    private static string FormatDimm(DimmSlotInfo d)
    {
        var loc = LocalizationManager.Instance;
        var sb = new StringBuilder();
        sb.Append($"{d.Slot}: {d.CapacityGB:0} {loc["Common.UnitGB"]}");
        if (d.MemoryType.Length > 0) sb.Append($" {d.MemoryType}");
        if (d.SpeedMTs > 0) sb.Append($" @ {d.SpeedMTs:0} {loc["Diagnostic.MtsUnit"]}");
        if (d.Manufacturer.Length > 0) sb.Append($" — {d.Manufacturer}");
        if (d.PartNumber.Length > 0) sb.Append($" ({d.PartNumber})");
        return sb.ToString();
    }

    #endregion
}
