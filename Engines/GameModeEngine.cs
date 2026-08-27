using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MASLOOPTIMIZER;

public class GameModeStatusInfo
{
    public bool IsActive { get; set; }
    public int StoppedServicesCount { get; set; }
    public List<string> StoppedServicesList { get; set; } = new();
    public double LastFreedMemoryMb { get; set; }
    public string ActivePowerPlan { get; set; } = "Стандартна схема";
    public bool IsGpuPriorityBoosted { get; set; }
    public bool IsCpuThrottlingDisabled { get; set; }
}

public static class GameModeEngine
{
    private static readonly List<string> CandidateServices = new()
    {
        "WSearch",           // Windows Search Indexer
        "SysMain",           // Superfetch / SysMain
        "Spooler",           // Диспетчер друку
        "DiagTrack",         // Діагностика та фонова телеметрія
        "MapsBroker",        // Диспетчер завантажених карт
        "BITS",              // Фонова служба інтелектуальної передачі
        "wuauserv",          // Windows Update (запобігає фоновим установкам під час гри)
        "edgeupdate",        // Фоновий апдейтер Edge
        "edgeupdatem",
        "gupdate",           // Фоновий апдейтер Google
        "gupdatem"
    };

    private static readonly List<string> StoppedServicesState = new();
    private static Guid? _previousPowerPlanGuid = null;
    private static double _lastFreedMemoryMb = 0;
    private static bool _busy = false; // Захист від повторного входу (подвійний клік)
    private static readonly object _lock = new();

    // Снапшот оригінальних значень реєстру для коректного відновлення при вимкненні Game Mode
    private static readonly Dictionary<string, RegistryValueSnapshot> _registrySnapshot = new();

    public static bool IsGameModeActive { get; private set; } = false;
    public static event Action<bool>? OnGameModeStateChanged;

    private const string GameBarSubKey = @"Software\Microsoft\GameBar";
    private const string MMCssGamesSubKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";
    private const string MMCssProfileSubKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";

    private sealed class RegistryValueSnapshot
    {
        public RegistryHive Hive;
        public string SubKey = string.Empty;
        public string ValueName = string.Empty;
        public bool Existed;
        public RegistryValueKind Kind = RegistryValueKind.Unknown;
        public object? Value;
    }

    #region 1. Win32 & NT API (Standby List Purge, Privileges & Memory)

    [DllImport("ntdll.dll")]
    private static extern uint NtSetSystemInformation(int infoClass, IntPtr info, int length);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out long lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr tokenHandle,
        bool disableAllPrivileges,
        ref TOKEN_PRIVILEGES newState,
        int bufferLength,
        IntPtr previousState,
        IntPtr returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr hwProc);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct TOKEN_PRIVILEGES
    {
        public int PrivilegeCount;
        public long Luid;
        public int Attributes;
    }

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

    #region Win32 PowrProf API

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerGetActiveScheme(IntPtr UserRootPowerKey, out IntPtr ActivePolicyGuid);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerSetActiveScheme(IntPtr UserRootPowerKey, ref Guid SchemeGuid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private static readonly Guid UltimatePlanGuid = new("e9a42b02-d5df-448d-aa00-03f14749eb61");
    private static readonly Guid HighPerfPlanGuid = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    private static readonly Guid BalancedPlanGuid = new("a1841308-3541-4fab-bc81-f71556f20b4a");

    #endregion

    private const int SE_PRIVILEGE_ENABLED = 0x00000002;
    private const int TOKEN_ADJUST_PRIVILEGES = 0x00000020;
    private const int TOKEN_QUERY = 0x00000008;
    private const int ERROR_NOT_ALL_ASSIGNED = 1300;

    private const int SystemMemoryListInformation = 80;
    private const int MemoryFlushModifiedList = 3;
    private const int MemoryPurgeStandbyList = 4;
    private const int MemoryPurgeLowPriorityStandbyList = 5;

    private static bool EnablePrivilege(string privilegeName)
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr hToken))
            return false;

        try
        {
            if (!LookupPrivilegeValue(null, privilegeName, out long luid))
                return false;

            var tp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = SE_PRIVILEGE_ENABLED
            };

            if (!AdjustTokenPrivileges(hToken, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
                return false;

            // AdjustTokenPrivileges може повернути true навіть якщо привілей не призначено повністю.
            return Marshal.GetLastWin32Error() != ERROR_NOT_ALL_ASSIGNED;
        }
        finally
        {
            CloseHandle(hToken);
        }
    }

    public static (bool Success, double FreedMB) PurgeStandbyList()
    {
        try
        {
            double memBefore = GetAvailableMemoryMb();

            // Перевіряємо результати активування привілеїв — без них NtSetSystemInformation не спрацює
            bool privOk = true;
            if (!EnablePrivilege("SeProfileSingleProcessPrivilege"))
            {
                privOk = false;
                AppLogger.Log("Game Mode: не вдалося активувати SeProfileSingleProcessPrivilege (потрібен адміністратор)", "WARN");
            }
            if (!EnablePrivilege("SeIncreaseQuotaPrivilege"))
            {
                privOk = false;
                AppLogger.Log("Game Mode: не вдалося активувати SeIncreaseQuotaPrivilege (потрібен адміністратор)", "WARN");
            }

            // 1. Послідовне очищення модифікованих сторінок та Standby RAM List
            bool purgeOk = true;
            IntPtr pCmd = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                Marshal.WriteInt32(pCmd, MemoryFlushModifiedList);
                if (NtSetSystemInformation(SystemMemoryListInformation, pCmd, sizeof(int)) != 0)
                    purgeOk = false;

                Marshal.WriteInt32(pCmd, MemoryPurgeStandbyList);
                if (NtSetSystemInformation(SystemMemoryListInformation, pCmd, sizeof(int)) != 0)
                    purgeOk = false;

                Marshal.WriteInt32(pCmd, MemoryPurgeLowPriorityStandbyList);
                if (NtSetSystemInformation(SystemMemoryListInformation, pCmd, sizeof(int)) != 0)
                    purgeOk = false;
            }
            finally
            {
                Marshal.FreeHGlobal(pCmd);
            }

            // 2. Очищення Working Set поточного процесу та скидання GC
            GC.Collect();
            GC.WaitForPendingFinalizers();
            EmptyWorkingSet(GetCurrentProcess());

            double memAfter = GetAvailableMemoryMb();
            // Sentinel -1 означає "вимір не вдалося" — у такому разі не рахуємо хибне "вивільнення"
            double freedMb = (memBefore >= 0 && memAfter >= 0) ? Math.Max(0, memAfter - memBefore) : 0;
            lock (_lock)
            {
                _lastFreedMemoryMb = freedMb;
            }

            if (!purgeOk || !privOk)
                AppLogger.Log($"Standby RAM Purge: NtSetSystemInformation повернув помилку, фактично вивільнено {freedMb:N0} МБ", "WARN");
            else
                AppLogger.Log($"Standby RAM Purge: вивільнено {freedMb:N0} МБ системного кешу пам'яті", "SUCCESS");

            return (purgeOk && privOk, freedMb);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Помилка очищення Standby RAM List: {ex.Message}", "ERROR");
            return (false, 0);
        }
    }

    public static async Task<(bool Success, double FreedMB)> PurgeStandbyListAsync()
    {
        return await Task.Run(() => PurgeStandbyList());
    }

    private static double GetAvailableMemoryMb()
    {
        try
        {
            var mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem))
            {
                return mem.ullAvailPhys / (1024.0 * 1024.0);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Помилка GlobalMemoryStatusEx: {ex.Message}", "WARN");
        }
        return -1; // Sentinel: вимір доступної пам'яті не вдався
    }

    #endregion

    #region 2. Активація та Деактивація Game Mode

    public static async Task<bool> ToggleGameModeAsync()
    {
        bool active;
        lock (_lock)
        {
            active = IsGameModeActive;
        }
        return active ? await DeactivateGameModeAsync() : await ActivateGameModeAsync();
    }

    public static async Task<bool> ActivateGameModeAsync()
    {
        return await Task.Run(() =>
        {
            lock (_lock)
            {
                if (IsGameModeActive || _busy)
                    return false; // Уже активний або йде інша операція — ігноруємо повторний запуск
                _busy = true;
            }

            var stopped = new List<string>();
            try
            {
                lock (_lock)
                {
                    StoppedServicesState.Clear();
                }

                // Отримуємо актуальний словник встановлених служб системи
                Dictionary<string, ServiceController> installedServices;
                try
                {
                    installedServices = ServiceController.GetServices()
                        .ToDictionary(s => s.ServiceName, StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"Game Mode: не вдалося отримати список служб: {ex.Message}", "WARN");
                    installedServices = new Dictionary<string, ServiceController>(StringComparer.OrdinalIgnoreCase);
                }

                foreach (var svcName in CandidateServices)
                {
                    if (!installedServices.TryGetValue(svcName, out var sc))
                        continue;

                    try
                    {
                        if (sc.Status == ServiceControllerStatus.Running && sc.CanStop)
                        {
                            sc.Stop();
                            try
                            {
                                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromMilliseconds(2000));
                            }
                            catch (System.ServiceProcess.TimeoutException)
                            {
                                // Служба ще зупиняється — все одно обліковуємо її для відновлення
                                AppLogger.Log($"Game Mode: служба [{svcName}] не зупинилася за 2 с — додаємо в облік", "WARN");
                            }

                            stopped.Add(svcName);
                            lock (_lock)
                            {
                                StoppedServicesState.Add(svcName);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Log($"Game Mode: не вдалося зупинити службу [{svcName}]: {ex.Message}", "WARN");
                    }
                    finally
                    {
                        sc.Dispose();
                    }
                }

                // Збереження та активація схеми максимальної продуктивності
                SwitchToHighPerformancePower();

                // Очищення кешу оперативної пам'яті
                PurgeStandbyList();

                // Активація ігрових реєстрових оптимізацій (MMCSS + DirectFlip)
                ApplyGameModeRegistry();

                lock (_lock)
                {
                    IsGameModeActive = true;
                }

                // Подію викликаємо ПОЗА lock; винятки підписників не повинні ламати стан
                try
                {
                    OnGameModeStateChanged?.Invoke(true);
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"Game Mode: помилка підписника події: {ex.Message}", "WARN");
                }

                AppLogger.Log($"Game Mode АКТИВОВАНО: зупинено {stopped.Count} служб", "SUCCESS");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Помилка активації Game Mode: {ex.Message}", "ERROR");

                // Відкат частково застосованих змін, щоб не залишити систему в проміжному стані.
                if (stopped.Count > 0)
                    TryStartServices(stopped);

                RestorePreviousPowerPlan();
                RestoreGameModeRegistry();

                lock (_lock)
                {
                    StoppedServicesState.Clear();
                }
                return false;
            }
            finally
            {
                lock (_lock)
                {
                    _busy = false;
                }
            }
        });
    }

    public static async Task<bool> DeactivateGameModeAsync()
    {
        return await Task.Run(() =>
        {
            List<string> toRestore;
            lock (_lock)
            {
                if (!IsGameModeActive || _busy)
                    return false; // Не активний або йде інша операція — нічого відновлювати
                _busy = true;
                toRestore = new List<string>(StoppedServicesState);
                StoppedServicesState.Clear();
            }

            try
            {
                int restoredCount = TryStartServices(toRestore);

                // Відновлення попередньої схеми живлення
                RestorePreviousPowerPlan();

                // Відновлення параметрів реєстру
                RestoreGameModeRegistry();

                lock (_lock)
                {
                    IsGameModeActive = false;
                }

                // Подію викликаємо ПОЗА lock; винятки підписників не повинні ламати стан
                try
                {
                    OnGameModeStateChanged?.Invoke(false);
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"Game Mode: помилка підписника події: {ex.Message}", "WARN");
                }

                AppLogger.Log($"Game Mode ДЕАКТИВОВАНО: відновлено {restoredCount} служб та початковий план живлення", "INFO");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Помилка відновлення системного стану: {ex.Message}", "ERROR");
                return false;
            }
            finally
            {
                lock (_lock)
                {
                    _busy = false;
                }
            }
        });
    }

    private static int TryStartServices(IEnumerable<string> serviceNames)
    {
        int restoredCount = 0;
        foreach (var svcName in serviceNames)
        {
            try
            {
                using var sc = new ServiceController(svcName);

                // Спроба запуску Disabled-служби викличе виняток — його обробляє catch нижче.
                if (sc.Status != ServiceControllerStatus.Running)
                {
                    sc.Start();
                    try
                    {
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromMilliseconds(3000));
                    }
                    catch (System.ServiceProcess.TimeoutException)
                    {
                        AppLogger.Log($"Служба [{svcName}] ще запускається", "WARN");
                    }
                    restoredCount++;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Не вдалося запустити службу [{svcName}]: {ex.Message}", "WARN");
            }
        }
        return restoredCount;
    }

    #endregion

    #region 3. Керування живленням, пріоритетами та реєстром

    private static void SwitchToHighPerformancePower()
    {
        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr pGuid) == 0 && pGuid != IntPtr.Zero)
            {
                try
                {
                    _previousPowerPlanGuid = Marshal.PtrToStructure<Guid>(pGuid);
                }
                finally
                {
                    LocalFree(pGuid);
                }
            }

            var target = UltimatePlanGuid;
            if (PowerSetActiveScheme(IntPtr.Zero, ref target) != 0)
            {
                var fallback = HighPerfPlanGuid;
                if (PowerSetActiveScheme(IntPtr.Zero, ref fallback) != 0)
                {
                    using var proc = Process.Start(new ProcessStartInfo
                    {
                        FileName = "powercfg.exe",
                        Arguments = "-duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61",
                        RedirectStandardOutput = true,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                    string output = proc?.StandardOutput.ReadToEnd() ?? string.Empty;
                    proc?.WaitForExit(1500);

                    var match = Regex.Match(output, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
                    if (match.Success && Guid.TryParse(match.Value, out Guid newGuid))
                    {
                        PowerSetActiveScheme(IntPtr.Zero, ref newGuid);
                    }
                    else
                    {
                        PowerSetActiveScheme(IntPtr.Zero, ref fallback);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Game Mode: не вдалося активувати схему максимальної продуктивності: {ex.Message}", "WARN");
        }
    }

    private static void RestorePreviousPowerPlan()
    {
        try
        {
            if (_previousPowerPlanGuid.HasValue)
            {
                var prev = _previousPowerPlanGuid.Value;
                PowerSetActiveScheme(IntPtr.Zero, ref prev);
                _previousPowerPlanGuid = null;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Game Mode: не вдалося відновити попередню схему живлення: {ex.Message}", "WARN");
        }
    }

    private static string RegistrySnapshotKey(RegistryHive hive, string subKey, string valueName)
        => $"{hive}\\{subKey}\\{valueName}";

    private static void SnapshotRegistryValue(RegistryHive hive, string subKey, string valueName)
    {
        string snapshotKey = RegistrySnapshotKey(hive, subKey, valueName);
        if (_registrySnapshot.ContainsKey(snapshotKey))
            return; // Оригінал уже збережено — не перезаписуємо його

        try
        {
            using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(subKey, writable: false);
            if (key == null)
            {
                _registrySnapshot[snapshotKey] = new RegistryValueSnapshot
                {
                    Hive = hive,
                    SubKey = subKey,
                    ValueName = valueName,
                    Existed = false
                };
                return;
            }

            string[] valueNames = key.GetValueNames();
            bool existed = Array.Exists(valueNames, n => string.Equals(n, valueName, StringComparison.OrdinalIgnoreCase));

            _registrySnapshot[snapshotKey] = new RegistryValueSnapshot
            {
                Hive = hive,
                SubKey = subKey,
                ValueName = valueName,
                Existed = existed,
                Kind = existed ? key.GetValueKind(valueName) : RegistryValueKind.Unknown,
                Value = existed ? key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) : null
            };
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Game Mode: не вдалося зберегти знімок реєстру [{subKey}\\{valueName}]: {ex.Message}", "WARN");
        }
    }

    private static void SetRegistryValue(RegistryHive hive, string subKey, string valueName, object value, RegistryValueKind kind)
    {
        try
        {
            SnapshotRegistryValue(hive, subKey, valueName);

            using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).CreateSubKey(subKey, writable: true);
            if (key == null)
            {
                AppLogger.Log($"Game Mode: не вдалося відкрити/створити ключ реєстру [{subKey}]", "WARN");
                return;
            }

            key.SetValue(valueName, value, kind);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Game Mode: не вдалося записати значення реєстру [{subKey}\\{valueName}]: {ex.Message}", "WARN");
        }
    }

    private static void ApplyGameModeRegistry()
    {
        // Снапшот робимо один раз на початку активації, щоб відновлення повертало саме ті значення,
        // які були до втручання, а не жорстко закодовані значення "за замовчуванням".
        _registrySnapshot.Clear();

        // 1. Активація ігрового режиму Windows GameBar
        SetRegistryValue(RegistryHive.CurrentUser, GameBarSubKey, "AutoGameModeEnabled", 1, RegistryValueKind.DWord);
        SetRegistryValue(RegistryHive.CurrentUser, GameBarSubKey, "AllowAutoGameMode", 1, RegistryValueKind.DWord);

        // 2. Пріоритет виділення ресурсів GPU під час ігор (MMCSS Tasks\Games)
        SetRegistryValue(RegistryHive.LocalMachine, MMCssGamesSubKey, "GPU Priority", 8, RegistryValueKind.DWord);
        SetRegistryValue(RegistryHive.LocalMachine, MMCssGamesSubKey, "Priority", 6, RegistryValueKind.DWord);
        SetRegistryValue(RegistryHive.LocalMachine, MMCssGamesSubKey, "Scheduling Category", "High", RegistryValueKind.String);
        SetRegistryValue(RegistryHive.LocalMachine, MMCssGamesSubKey, "SFIO Priority", "High", RegistryValueKind.String);

        // 3. Зняття системного троттлінгу відгуку (100% CPU на ігри)
        SetRegistryValue(RegistryHive.LocalMachine, MMCssProfileSubKey, "SystemResponsiveness", 0, RegistryValueKind.DWord);
        SetRegistryValue(RegistryHive.LocalMachine, MMCssProfileSubKey, "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord);
    }

    private static void RestoreGameModeRegistry()
    {
        Dictionary<string, RegistryValueSnapshot> snapshots = new(_registrySnapshot);
        _registrySnapshot.Clear();

        foreach (var snapshot in snapshots.Values)
        {
            try
            {
                using var key = RegistryKey.OpenBaseKey(snapshot.Hive, RegistryView.Registry64).OpenSubKey(snapshot.SubKey, writable: true);
                if (key == null)
                    continue;

                if (snapshot.Existed)
                {
                    // Значення гарантовано присутнє, якщо Existed == true (знімок зроблено до втручання).
                    key.SetValue(snapshot.ValueName, snapshot.Value!, snapshot.Kind);
                }
                else
                {
                    key.DeleteValue(snapshot.ValueName, throwOnMissingValue: false);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Game Mode: не вдалося відновити значення реєстру [{snapshot.SubKey}\\{snapshot.ValueName}]: {ex.Message}", "WARN");
            }
        }
    }

    public static bool BoostForegroundGameProcess()
    {
        try
        {
            IntPtr hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero) return false;

            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid <= 4) return false;

            using var proc = Process.GetProcessById((int)pid);
            string pName = proc.ProcessName.ToLowerInvariant();

            // Ігноруємо оболонку Windows та саму програму
            if (pName.Contains("explorer") || pName.Contains("masloptimizer") || pName.Contains("taskmgr"))
                return false;

            if (proc.PriorityClass != ProcessPriorityClass.High)
            {
                proc.PriorityClass = ProcessPriorityClass.High;
                AppLogger.Log($"Foreground Boost: процесу гри [{proc.ProcessName}] встановлено пріоритет High", "SUCCESS");
                return true;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Foreground Boost: не вдалося підвищити пріоритет процесу: {ex.Message}", "WARN");
        }
        return false;
    }

    public static GameModeStatusInfo GetStatusInfo()
    {
        lock (_lock)
        {
            return new GameModeStatusInfo
            {
                IsActive = IsGameModeActive,
                StoppedServicesCount = StoppedServicesState.Count,
                StoppedServicesList = new List<string>(StoppedServicesState),
                LastFreedMemoryMb = _lastFreedMemoryMb,
                ActivePowerPlan = IsGameModeActive ? "Ultimate Performance" : "Стандартна схема",
                IsGpuPriorityBoosted = IsGameModeActive,
                IsCpuThrottlingDisabled = IsGameModeActive
            };
        }
    }

    #endregion
}