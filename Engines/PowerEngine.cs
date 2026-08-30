using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MASLOOPTIMIZER;

public enum SystemPowerMode
{
    EcoPowerSaver,
    OriginalSnapshot,
    UltraPerformance
}

public class DisplayRefreshRateInfo
{
    public int CurrentHz { get; set; }
    public List<int> AvailableRates { get; set; } = new();
}

public class PowerEngine
{
    #region Події

    /// <summary>Спрацьовує після успішного застосування режиму живлення.</summary>
    public static event Action<SystemPowerMode>? OnPowerModeChanged;

    #endregion

    #region Константи Win32 (дисплей)

    private const int ENUM_CURRENT_SETTINGS = -1; // 0xFFFFFFFF
    private const int DISP_CHANGE_SUCCESSFUL = 0;
    private const int DISP_CHANGE_BADMODE = -2;
    private const int DISP_CHANGE_FAILED = -1;
    private const int DISP_CHANGE_RESTART = 1;

    private const uint CDS_UPDATEREGISTRY = 0x00000001;
    private const int DM_PELSWIDTH = 0x00080000;
    private const int DM_PELSHEIGHT = 0x00100000;
    private const int DM_DISPLAYFREQUENCY = 0x00400000;

    #endregion

    #region Структури Win32

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;

        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;

        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;

        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;      // 0 = offline, 1 = online, 255 = unknown
        public byte BatteryFlag;       // 0x80 (128) = "No system battery"
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    #endregion

    #region P/Invoke — user32.dll (дисплей)

    [DllImport("user32.dll", CharSet = CharSet.Ansi, SetLastError = true, EntryPoint = "EnumDisplaySettingsExA")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettingsEx(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Ansi, SetLastError = true, EntryPoint = "ChangeDisplaySettingsExA")]
    private static extern int ChangeDisplaySettingsEx(string? lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

    #endregion

    #region P/Invoke — powrprof.dll (схеми живлення)

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerGetActiveScheme(IntPtr UserRootPowerKey, out IntPtr ActivePolicyGuid);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerSetActiveScheme(IntPtr UserRootPowerKey, ref Guid SchemeGuid);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerWriteACValueIndex(IntPtr RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroupOfPowerSettingsGuid, ref Guid PowerSettingGuid, uint AcValueIndex);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerReadACValueIndex(IntPtr RootPowerKey, ref Guid SchemeGuid, ref Guid SubGroupOfPowerSettingsGuid, ref Guid PowerSettingGuid, out uint AcValueIndex);

    #endregion

    #region P/Invoke — kernel32.dll (живлення системи)

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    #endregion
    #region Публічні хелпери

    /// <summary>Визначає, чи є пристрій ноутбуком (наявність системної батареї).</summary>
    public static bool IsLaptopDevice()
    {
        try
        {
            if (!GetSystemPowerStatus(out var status))
            {
                AppLogger.Log("PowerEngine: GetSystemPowerStatus повернула false", "WARN");
                return false;
            }

            // 255 — статус невідомий; безпечно вважаємо настільним ПК.
            if (status.BatteryFlag == 255)
                return false;

            // Біт 0x80 (128) — "No system battery". Якщо не встановлений — батарея є (ноутбук).
            return (status.BatteryFlag & 0x80) == 0;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"PowerEngine: помилка визначення типу пристрою: {ex.Message}", "ERROR");
            return false;
        }
    }

    /// <summary>Повертає ім'я активної схеми живлення (локалізоване, якщо доступне).</summary>
    public static string GetActivePowerPlanName()
    {
        try
        {
            Guid? activeGuid = null;

            // 1. Отримуємо GUID активної схеми через Win32 API.
            if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr pGuid) == 0 && pGuid != IntPtr.Zero)
            {
                try
                {
                    activeGuid = Marshal.PtrToStructure<Guid>(pGuid);
                }
                finally
                {
                    LocalFree(pGuid);
                }
            }

            // 2. powercfg /getactivescheme дає найточніше (локалізоване) ім'я схеми.
            try
            {
                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = "/getactivescheme",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                });

                string output = proc?.StandardOutput.ReadToEnd() ?? string.Empty;
                proc?.WaitForExit(3000);

                var nameMatch = Regex.Match(output, @"\(([^)]*)\)");
                if (nameMatch.Success)
                    return nameMatch.Groups[1].Value.Trim();

                var guidMatch = Regex.Match(output, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
                if (guidMatch.Success && Guid.TryParse(guidMatch.Value, out Guid parsedGuid))
                {
                    var known = ResolveKnownPowerPlanName(parsedGuid);
                    return known ?? parsedGuid.ToString();
                }
            }
            catch (Exception procEx)
            {
                AppLogger.Log($"PowerEngine: не вдалося викликати powercfg: {procEx.Message}", "WARN");
            }

            // 3. Fallback на відомі GUID або текстовий ідентифікатор.
            if (activeGuid.HasValue)
            {
                var known = ResolveKnownPowerPlanName(activeGuid.Value);
                return known ?? activeGuid.Value.ToString();
            }

            return "Невідома схема";
        }
        catch (Exception ex)
        {
            AppLogger.Log($"PowerEngine: не вдалося отримати активну схему живлення: {ex.Message}", "ERROR");
            return "Невідома схема";
        }
    }

    /// <summary>Повертає поточну та доступні частоти оновлення дисплея (Гц).</summary>
    public static DisplayRefreshRateInfo GetDisplayRefreshRates()
    {
        var info = new DisplayRefreshRateInfo();

        try
        {
            var rates = new SortedSet<int>();

            for (int i = 0; ; i++)
            {
                var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                if (!EnumDisplaySettingsEx(null, i, ref dm, 0))
                    break;

                if (dm.dmDisplayFrequency > 0)
                    rates.Add(dm.dmDisplayFrequency);
            }

            info.AvailableRates = new List<int>(rates);

            var current = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            if (EnumDisplaySettingsEx(null, ENUM_CURRENT_SETTINGS, ref current, 0))
                info.CurrentHz = current.dmDisplayFrequency;

            return info;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"PowerEngine: не вдалося отримати частоти оновлення дисплея: {ex.Message}", "ERROR");
            return info;
        }
    }

    /// <summary>Встановлює частоту оновлення дисплея (Гц) із записом у реєстр.</summary>
    public static bool SetDisplayRefreshRate(int hz)
    {
        try
        {
            if (hz <= 0)
            {
                AppLogger.Log($"PowerEngine: некоректна частота оновлення {hz} Гц", "WARN");
                return false;
            }

            var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            if (!EnumDisplaySettingsEx(null, ENUM_CURRENT_SETTINGS, ref dm, 0))
            {
                AppLogger.Log("PowerEngine: не вдалося отримати поточні налаштування дисплея", "WARN");
                return false;
            }

            dm.dmFields = DM_DISPLAYFREQUENCY;
            dm.dmDisplayFrequency = hz;

            int result = ChangeDisplaySettingsEx(null, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
            if (result == DISP_CHANGE_SUCCESSFUL)
            {
                AppLogger.Log($"PowerEngine: частоту оновлення змінено на {hz} Гц", "SUCCESS");
                return true;
            }

            AppLogger.Log($"PowerEngine: зміна частоти оновлення повернула код {result}", "WARN");
            return false;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"PowerEngine: не вдалося змінити частоту оновлення дисплея: {ex.Message}", "ERROR");
            return false;
        }
    }

    /// <summary>Повертає GUID активної схеми живлення через Win32 API (або Guid.Empty при помилці).</summary>
    public static Guid GetActivePowerPlanGuid()
    {
        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr pGuid) == 0 && pGuid != IntPtr.Zero)
            {
                try
                {
                    return Marshal.PtrToStructure<Guid>(pGuid);
                }
                finally
                {
                    LocalFree(pGuid);
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"PowerEngine: не вдалося отримати GUID активної схеми живлення: {ex.Message}", "ERROR");
        }

        return Guid.Empty;
    }
    #endregion

    #region Константи GUID (схеми живлення, CPU, PCIe)

    private static readonly Guid PowerSaverPlanGuid = new("a1841308-3541-4fab-bc81-f71556f20b4a");
    private static readonly Guid UltimatePerformancePlanGuid = new("e9a42b02-d5df-448d-aa00-03f14749eb61");
    private static readonly Guid HighPerformancePlanGuid = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    private static readonly Guid ProcessorSubGroupGuid = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid ProcMaxStateGuid = new("bc5038f7-23e0-4960-96da-33abaf5935ec");
    private static readonly Guid ProcMinStateGuid = new("893dee8e-2bef-41e0-89c6-b55d0929964c");
    private static readonly Guid ProcBoostModeGuid = new("be337238-0d82-4146-a960-4f3749d470c7");
    private static readonly Guid CoreParkingMinGuid = new("0cc5b647-c1df-4637-891a-dec35c318583");
    private static readonly Guid SystemCoolingPolicyGuid = new("94d3a615-a899-4ac5-ae2b-e4d8f634367f");

    private static readonly Guid PciExpressSubGroupGuid = new("501a4d13-42af-4429-9fd1-a8218c268e20");
    private static readonly Guid PcieAspmGuid = new("ee12f906-d277-404b-b6da-e5fa1a576df5");

    #endregion

    #region Snapshot Engine

    private static readonly string SnapshotFilePath = Path.Combine(AppPaths.Backups, "PowerState_Snapshot.json");

    private const string NvTweakSubKey = @"SOFTWARE\NVIDIA Corporation\Global\NVTweak";
    private const string PowerMizerValueName = "PowerMizerEnable";
    private const string PowerMizerLevelValueName = "PowerMizerLevel";

    private const string AmdAdapterClassSubKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000";
    private const string EnableUlpsValueName = "EnableUlps";

    /// <summary>Створює первинний знімок стану живлення, якщо він ще не існує (або примусово).</summary>
    public static async Task<bool> CaptureInitialSnapshotIfNeededAsync(bool forceRecapture = false)
    {
        return await Task.Run(() =>
        {
            try
            {
                AppPaths.EnsureDirectories();

                if (!forceRecapture && File.Exists(SnapshotFilePath))
                {
                    AppLogger.Log("PowerEngine: знімок системи вже існує — повторне створення пропущено.", "INFO");
                    return true;
                }

                var snapshot = new PowerSnapshotModel
                {
                    CapturedAt = DateTime.Now,
                    OriginalPlanGuid = GetActivePowerPlanGuid(),
                    OriginalPlanName = GetActivePowerPlanName()
                };

                if (TryGetCurrentDisplaySettings(out int width, out int height, out int hz))
                {
                    snapshot.OriginalDisplayWidth = width;
                    snapshot.OriginalDisplayHeight = height;
                    snapshot.OriginalDisplayHz = hz;
                }
                else
                {
                    snapshot.OriginalDisplayHz = GetDisplayRefreshRates().CurrentHz;
                }

                snapshot.GpuPowerMizerLevel = ReadRegistryDword(RegistryHive.LocalMachine, NvTweakSubKey, PowerMizerValueName, 0);
                snapshot.AmdUlpsState = ReadRegistryDword(RegistryHive.LocalMachine, AmdAdapterClassSubKey, EnableUlpsValueName, 1);

                return SaveSnapshot(snapshot);
            }
            catch (Exception ex)
            {
                AppLogger.Log($"PowerEngine: не вдалося створити знімок системи: {ex.Message}", "ERROR");
                return false;
            }
        });
    }

    /// <summary>Застосовує вибраний режим живлення.</summary>
    public static async Task<bool> ApplyProfileAsync(SystemPowerMode mode)
    {
        return await Task.Run(() =>
        {
            bool success;

            switch (mode)
            {
                case SystemPowerMode.OriginalSnapshot:
                    success = ApplyOriginalSnapshot();
                    break;

                case SystemPowerMode.EcoPowerSaver:
                    success = ApplyEcoPowerSaver();
                    break;

                case SystemPowerMode.UltraPerformance:
                    success = ApplyUltraPerformance();
                    break;

                default:
                    AppLogger.Log($"PowerEngine: невідомий режим живлення: {mode}.", "WARN");
                    return false;
            }

            if (success)
            {
                try
                {
                    OnPowerModeChanged?.Invoke(mode);
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"PowerEngine: помилка під час сповіщення про зміну режиму живлення: {ex.Message}", "WARN");
                }

                AppLogger.Log($"PowerEngine: режим {mode} успішно застосовано.", "SUCCESS");
            }
            else
            {
                AppLogger.Log($"PowerEngine: не вдалося застосувати режим {mode} (див. лог вище).", "WARN");
            }

            return success;
        });
    }

    private static bool ApplyEcoPowerSaver()
    {
        bool allOk = true;

        // 1. Дисплей → 60 Гц.
        allOk &= SetDisplayRefreshRate(60);

        // 2. Параметри плану «Економія енергії» (пишемо до активації, щоб вони застосувались одразу).
        allOk &= WritePowerAcSetting(PowerSaverPlanGuid, ProcessorSubGroupGuid, ProcMaxStateGuid, 60);
        allOk &= WritePowerAcSetting(PowerSaverPlanGuid, ProcessorSubGroupGuid, ProcMinStateGuid, 5);
        allOk &= WritePowerAcSetting(PowerSaverPlanGuid, ProcessorSubGroupGuid, ProcBoostModeGuid, 0);
        allOk &= WritePowerAcSetting(PowerSaverPlanGuid, ProcessorSubGroupGuid, CoreParkingMinGuid, 50);
        allOk &= WritePowerAcSetting(PowerSaverPlanGuid, ProcessorSubGroupGuid, SystemCoolingPolicyGuid, 0);

        // 3. PCIe ASPM → максимальна економія енергії.
        allOk &= WritePowerAcSetting(PowerSaverPlanGuid, PciExpressSubGroupGuid, PcieAspmGuid, 2);

        // 4. Активація плану.
        allOk &= SetActivePowerPlan(PowerSaverPlanGuid);

        // 5. GPU: NVIDIA PowerMizer → економія, AMD ULPS → увімкнено.
        allOk &= WriteRegistryDword(RegistryHive.LocalMachine, NvTweakSubKey, PowerMizerLevelValueName, 3);
        allOk &= WriteRegistryDword(RegistryHive.LocalMachine, AmdAdapterClassSubKey, EnableUlpsValueName, 1);

        return allOk;
    }

    private static bool ApplyUltraPerformance()
    {
        bool allOk = true;

        // 1. Дисплей → максимальна доступна частота оновлення.
        var displayRates = GetDisplayRefreshRates();
        int maxHz = displayRates.AvailableRates.Count > 0
            ? displayRates.AvailableRates[displayRates.AvailableRates.Count - 1]
            : 0;

        if (maxHz > 0)
            allOk &= SetDisplayRefreshRate(maxHz);
        else
            AppLogger.Log("PowerEngine: не вдалося визначити максимальну частоту оновлення дисплея.", "WARN");

        // 2. Визначаємо схему: Ultimate Performance, за потреби — High Performance.
        var planGuid = PowerPlanExists(UltimatePerformancePlanGuid)
            ? UltimatePerformancePlanGuid
            : HighPerformancePlanGuid;

        // 3. Параметри CPU/ASPM (пишемо до активації).
        allOk &= WritePowerAcSetting(planGuid, ProcessorSubGroupGuid, ProcMaxStateGuid, 100);
        allOk &= WritePowerAcSetting(planGuid, ProcessorSubGroupGuid, ProcMinStateGuid, 100);
        allOk &= WritePowerAcSetting(planGuid, ProcessorSubGroupGuid, ProcBoostModeGuid, 2);
        allOk &= WritePowerAcSetting(planGuid, ProcessorSubGroupGuid, CoreParkingMinGuid, 100);
        allOk &= WritePowerAcSetting(planGuid, ProcessorSubGroupGuid, SystemCoolingPolicyGuid, 1);
        allOk &= WritePowerAcSetting(planGuid, PciExpressSubGroupGuid, PcieAspmGuid, 0);

        // 4. Активація плану.
        allOk &= SetActivePowerPlan(planGuid);

        // 5. GPU: NVIDIA PowerMizer → продуктивність, AMD ULPS → вимкнено.
        allOk &= WriteRegistryDword(RegistryHive.LocalMachine, NvTweakSubKey, PowerMizerLevelValueName, 1);
        allOk &= WriteRegistryDword(RegistryHive.LocalMachine, AmdAdapterClassSubKey, EnableUlpsValueName, 0);

        return allOk;
    }

    private static bool WritePowerAcSetting(Guid schemeGuid, Guid subGroupGuid, Guid settingGuid, uint acValue)
    {
        try
        {
            var scheme = schemeGuid;
            var subGroup = subGroupGuid;
            var setting = settingGuid;

            uint result = PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref subGroup, ref setting, acValue);
            if (result == 0)
                return true;

            AppLogger.Log($"PowerEngine: PowerWriteACValueIndex ({settingGuid}) повернула код {result}.", "WARN");
            return false;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"PowerEngine: не вдалося записати параметр живлення ({settingGuid}): {ex.Message}", "ERROR");
            return false;
        }
    }

    private static bool PowerPlanExists(Guid planGuid)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "powercfg.exe",
                Arguments = "/list",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            });

            string output = proc?.StandardOutput.ReadToEnd() ?? string.Empty;
            proc?.WaitForExit(3000);

            return output.IndexOf(planGuid.ToString("D"), StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"PowerEngine: не вдалося перевірити наявність схеми живлення: {ex.Message}", "WARN");
            return false;
        }
    }

    private static bool SaveSnapshot(PowerSnapshotModel snapshot)
    {
        string tmpPath = SnapshotFilePath + ".tmp";

        try
        {
            string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });

            // UTF-8 без BOM + атомарна заміна оригіналу після валідації.
            File.WriteAllText(tmpPath, json, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            if (new FileInfo(tmpPath).Length == 0)
                throw new InvalidOperationException("Тимчасовий файл знімка порожній.");

            if (JsonSerializer.Deserialize<PowerSnapshotModel>(File.ReadAllText(tmpPath)) == null)
                throw new InvalidOperationException("Тимчасовий файл знімка не пройшов валідацію JSON.");

            File.Move(tmpPath, SnapshotFilePath, overwrite: true);
            AppLogger.Log($"PowerEngine: знімок системи збережено ({SnapshotFilePath}).", "SUCCESS");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"PowerEngine: не вдалося зберегти знімок системи: {ex.Message}", "ERROR");
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
            return false;
        }
    }

    private static bool ApplyOriginalSnapshot()
    {
        try
        {
            if (!File.Exists(SnapshotFilePath))
            {
                AppLogger.Log("PowerEngine: знімок системи відсутній — немає що відновлювати.", "WARN");
                return false;
            }

            string json = File.ReadAllText(SnapshotFilePath);
            var snapshot = JsonSerializer.Deserialize<PowerSnapshotModel>(json);
            if (snapshot == null)
            {
                AppLogger.Log("PowerEngine: не вдалося прочитати знімок системи (некоректний JSON).", "ERROR");
                return false;
            }

            bool allOk = true;

            if (snapshot.OriginalDisplayHz > 0)
                allOk &= SetDisplayRefreshRate(snapshot.OriginalDisplayHz);

            if (snapshot.OriginalPlanGuid != Guid.Empty)
                allOk &= SetActivePowerPlan(snapshot.OriginalPlanGuid);

            allOk &= WriteRegistryDword(RegistryHive.LocalMachine, NvTweakSubKey, PowerMizerValueName, snapshot.GpuPowerMizerLevel);
            allOk &= WriteRegistryDword(RegistryHive.LocalMachine, AmdAdapterClassSubKey, EnableUlpsValueName, snapshot.AmdUlpsState);

            AppLogger.Log(
                allOk
                    ? "PowerEngine: початковий стан системи успішно відновлено."
                    : "PowerEngine: відновлення завершено з помилками (див. лог).",
                allOk ? "SUCCESS" : "WARN");

            return allOk;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"PowerEngine: не вдалося відновити початковий стан системи: {ex.Message}", "ERROR");
            return false;
        }
    }

    private static bool SetActivePowerPlan(Guid schemeGuid)
    {
        try
        {
            var guid = schemeGuid;
            uint result = PowerSetActiveScheme(IntPtr.Zero, ref guid);
            if (result == 0)
            {
                AppLogger.Log($"PowerEngine: активну схему живлення відновлено ({schemeGuid}).", "SUCCESS");
                return true;
            }

            AppLogger.Log($"PowerEngine: PowerSetActiveScheme повернула код {result}.", "WARN");
            return false;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"PowerEngine: не вдалося відновити схему живлення: {ex.Message}", "ERROR");
            return false;
        }
    }

    private static bool TryGetCurrentDisplaySettings(out int width, out int height, out int hz)
    {
        width = 0;
        height = 0;
        hz = 0;

        try
        {
            var dm = new DEVMODE { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
            if (!EnumDisplaySettingsEx(null, ENUM_CURRENT_SETTINGS, ref dm, 0))
                return false;

            width = dm.dmPelsWidth;
            height = dm.dmPelsHeight;
            hz = dm.dmDisplayFrequency;
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"PowerEngine: не вдалося отримати поточні параметри дисплея: {ex.Message}", "WARN");
            return false;
        }
    }

    private static int ReadRegistryDword(RegistryHive hive, string subKey, string valueName, int defaultValue)
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(subKey, writable: false);
            if (key == null)
                return defaultValue;

            object? value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value == null)
                return defaultValue;

            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"PowerEngine: не вдалося прочитати значення реєстру [{subKey}\\{valueName}]: {ex.Message}", "WARN");
            return defaultValue;
        }
    }

    private static bool WriteRegistryDword(RegistryHive hive, string subKey, string valueName, int value)
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).CreateSubKey(subKey, writable: true);
            if (key == null)
            {
                AppLogger.Log($"PowerEngine: не вдалося відкрити/створити ключ реєстру [{subKey}].", "WARN");
                return false;
            }

            key.SetValue(valueName, value, RegistryValueKind.DWord);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"PowerEngine: не вдалося записати значення реєстру [{subKey}\\{valueName}]: {ex.Message}", "WARN");
            return false;
        }
    }

    #endregion

    #region 

    private static string? ResolveKnownPowerPlanName(Guid guid)
    {
        return guid.ToString() switch
        {
            "381b4222-f694-41f0-9685-ff5bb260df2e" => "Збалансована",
            "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c" => "Висока продуктивність",
            "a1841308-3541-4fab-bc81-f71556f20b4a" => "Економія енергії",
            "e9a42b02-d5df-448d-aa00-03f14749eb61" => "Максимальна продуктивність",
            _ => null
        };
    }

    #endregion
}
