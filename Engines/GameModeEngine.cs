using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MASLOOPTIMIZER;

public readonly struct ProcessDemotionState
{
    public ProcessPriorityClass PriorityClass { get; init; }
    public int IoPriority { get; init; }
    public bool PriorityBoostEnabled { get; init; }
    public int PagePriority { get; init; }
    public IntPtr ProcessorAffinity { get; init; }
}

public readonly struct GameProcessBoostState
{
    public ProcessPriorityClass PriorityClass { get; init; }
    public int IoPriority { get; init; }
    public int PagePriority { get; init; }
    public uint PowerThrottlingStateMask { get; init; }
    public IntPtr ProcessorAffinity { get; init; }
}

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
    // Фільтр зупинки лише критичних фонових служб. Пасивні апдейтери (edgeupdate/gupdate)
    // та MapsBroker виключені навмисно: їх зупинка не підвищує FPS і лише ризикує стабільністю.
    private static readonly List<string> CandidateServices = new()
    {
        "SysMain",           // Superfetch / SysMain
        "DoSvc",             // Delivery Optimization
        "WpnService",        // Служба push-сповіщень Windows
        "wuauserv",          // Windows Update (запобігає фоновим установкам під час гри)
        "Spooler",           // Диспетчер друку
        "DiagTrack"          // Діагностика та фонова телеметрія
    };

    // State Manager: початковий стан служб (джерело правди для відновлення).
    private static readonly ConcurrentDictionary<string, ServiceControllerStatus> ServiceStateCache = new(StringComparer.OrdinalIgnoreCase);

    // State Manager: кеш демоції процесів із захистом від повторного використання PID.
    private static readonly ConcurrentDictionary<(int Pid, long StartTimeTicks), ProcessDemotionState> ProcessDemotionCache = new();

    // State Manager: кеш бустованих ігрових процесів (захист від повторного використання PID).
    private static readonly ConcurrentDictionary<(int Pid, long StartTimeTicks), GameProcessBoostState> GameBoostStateCache = new();

    // Список фактично зупинених служб (для відображення в UI).
    private static readonly List<string> StoppedServicesState = new();
    private static Guid? _previousPowerPlanGuid = null;
    private static Guid? _createdPowerPlanGuid = null; // створена powercfg -duplicatescheme (видаляється при деактивації)
    private static double _lastFreedMemoryMb = 0;
    private static string _activePowerPlanName = string.Empty; // реально застосована схема (для чесного статусу в UI)
    private static bool _gpuPriorityApplied = false;
    private static bool _cpuThrottlingDisabled = false;
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

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct TOKEN_PRIVILEGES
    {
        public int PrivilegeCount;
        public long Luid;
        public int Attributes;
    }

    #region Win32 PowrProf API

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerGetActiveScheme(IntPtr UserRootPowerKey, out IntPtr ActivePolicyGuid);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerSetActiveScheme(IntPtr UserRootPowerKey, ref Guid SchemeGuid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private static readonly Guid UltimatePlanGuid = new("e9a42b02-d5df-448d-aa00-03f14749eb61");
    private static readonly Guid HighPerfPlanGuid = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

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
            // Standby-кеш вимірюємо через штатні лічильники пам'яті, а не через доступну пам'ять:
            // після purge сторінки переходять standby -> free, тому GlobalMemoryStatusEx завжди
            // показував би ~0 "вивільнених" МБ. Різниця розміру Standby List — чесна метрика.
            double standbyBefore = GetStandbyListMb();

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

            // Послідовне очищення модифікованих сторінок та Standby RAM List
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

            double standbyAfter = GetStandbyListMb();
            // Sentinel -1 означає "вимір не вдалося" — у такому разі не рахуємо хибне "вивільнення"
            double freedMb = (standbyBefore >= 0 && standbyAfter >= 0) ? Math.Max(0, standbyBefore - standbyAfter) : 0;
            lock (_lock)
            {
                _lastFreedMemoryMb = freedMb;
            }

            if (!purgeOk || !privOk)
                AppLogger.Log($"Standby RAM Purge: NtSetSystemInformation повернув помилку, фактично вивільнено {freedMb:N0} МБ standby-кешу", "WARN");
            else
                AppLogger.Log($"Standby RAM Purge: вивільнено {freedMb:N0} МБ standby-кешу", "SUCCESS");

            return (purgeOk && privOk, freedMb);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Помилка очищення Standby RAM List: {ex.Message}", "ERROR");
            return (false, 0);
        }
    }

    private static double GetStandbyListMb()
    {
        // Повний Standby List = Core + Normal Priority + Reserve (документовані лічильники \Memory).
        string[] counters = { "Standby Cache Core Bytes", "Standby Cache Normal Priority Bytes", "Standby Cache Reserve Bytes" };
        double totalBytes = 0;
        int readOk = 0;
        foreach (string counter in counters)
        {
            try
            {
                using var pc = new PerformanceCounter("Memory", counter);
                totalBytes += pc.NextValue();
                readOk++;
            }
            catch
            {
                // Лічильник може бути недоступний на екзотичних збірках — ігноруємо.
            }
        }

        return readOk > 0 ? totalBytes / (1024.0 * 1024.0) : -1; // Sentinel: вимір не вдався
    }

    public static async Task<(bool Success, double FreedMB)> PurgeStandbyListAsync()
    {
        return await Task.Run(() => PurgeStandbyList());
    }

    #endregion

    #region Shader Cache Purge (GPU)

    private static readonly string[] ShaderCacheFolders =
    {
        @"D3DSCache",
        @"NVIDIA\DXCache",
        @"NVIDIA\NV_Cache",
        @"AMD\DxCache"
    };

    /// <summary>Видаляє кеш шейдерів GPU перед запуском гри, щоб уникнути мікрофризів повторної компіляції.</summary>
    private static int PurgeGpuShaderCaches()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        int deletedFiles = 0;

        foreach (string relative in ShaderCacheFolders)
        {
            string folder = Path.Combine(localAppData, relative);
            try
            {
                if (!Directory.Exists(folder))
                    continue;

                foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.Delete(file);
                        deletedFiles++;
                    }
                    catch
                    {
                        // Файл може бути зайнятий активним процесом гри/драйвером — пропускаємо.
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Game Mode: не вдалося очистити кеш шейдерів [{folder}]: {ex.Message}", "WARN");
            }
        }

        if (deletedFiles > 0)
            AppLogger.Log($"Game Mode: очищено {deletedFiles} файлів кешу шейдерів GPU", "SUCCESS");

        return deletedFiles;
    }

    #endregion

    #region 1b. Кіберспортивний таймер (0.5 мс) та Sleep Blocker

    [Flags]
    private enum EXECUTION_STATE : uint
    {
        ES_SYSTEM_REQUIRED = 0x00000001,
        ES_DISPLAY_REQUIRED = 0x00000002,
        ES_AWAYMODE_REQUIRED = 0x00000040,
        ES_CONTINUOUS = 0x80000000
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryTimerResolution(out int MinimumResolution, out int MaximumResolution, out int CurrentResolution);

    [DllImport("ntdll.dll")]
    private static extern int NtSetTimerResolution(int DesiredResolution, bool SetResolution, out int CurrentResolution);

    // 0.5 мс у 100-наносекундних інтервалах (5000 * 100 нс = 500 000 нс) — мінімальний безпечний ліміт Windows.
    private const int TimerTargetResolution = 5000;

    private static CancellationTokenSource? _sleepBlockCts;
    private static Task? _sleepBlockTask;
    private static int _previousTimerResolution;
    private static bool _timerResolutionActive;

    private static void ActivateTimerResolution()
    {
        if (_timerResolutionActive)
            return;

        try
        {
            if (NtQueryTimerResolution(out _, out _, out int currentResolution) != 0)
            {
                AppLogger.Log("Game Mode: не вдалося отримати поточну роздільну здатність таймера", "WARN");
                return;
            }

            _previousTimerResolution = currentResolution;

            if (NtSetTimerResolution(TimerTargetResolution, true, out _) == 0)
            {
                _timerResolutionActive = true;
                AppLogger.Log($"Game Mode: таймер встановлено на 0.5 мс (попередній: {currentResolution / 10000.0:F3} мс)", "SUCCESS");
            }
            else
            {
                AppLogger.Log("Game Mode: не вдалося встановити таймер 0.5 мс", "WARN");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Game Mode: помилка активації таймера: {ex.Message}", "WARN");
        }
    }

    private static void RestoreTimerResolution()
    {
        if (!_timerResolutionActive)
            return;

        try
        {
            // Коректне звільнення запиту таймера: SetResolution=false знімає наш запит,
            // а не створює новий (Windows тримає мінімум по всіх заявках процесів).
            NtSetTimerResolution(0, false, out _);
            AppLogger.Log($"Game Mode: запит таймера звільнено (попередній: {_previousTimerResolution / 10000.0:F3} мс)", "INFO");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Game Mode: помилка звільнення таймера: {ex.Message}", "WARN");
        }
        finally
        {
            _timerResolutionActive = false;
            _previousTimerResolution = 0;
        }
    }

    private static void ActivateSleepBlocker()
    {
        if (_sleepBlockTask != null)
            return;

        try
        {
            var cts = new CancellationTokenSource();
            _sleepBlockCts = cts;

            // SetThreadExecutionState прив'язаний до потоку: стан утримується,
            // поки цей виділений Long-Running Task живий.
            _sleepBlockTask = Task.Factory.StartNew(() =>
            {
                SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_DISPLAY_REQUIRED | EXECUTION_STATE.ES_SYSTEM_REQUIRED);

                try
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        // Періодичне перевиставлення гарантує утримання прапорців на цьому потоці.
                        SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_DISPLAY_REQUIRED | EXECUTION_STATE.ES_SYSTEM_REQUIRED);
                        cts.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(30));
                    }
                }
                finally
                {
                    // Скидання виконується на тому самому потоці, що встановлював стан.
                    SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
                }
            }, cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            AppLogger.Log("Game Mode: блокування сну дисплея/системи активовано", "SUCCESS");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Game Mode: не вдалося активувати блокування сну: {ex.Message}", "WARN");
        }
    }

    private static void DeactivateSleepBlocker()
    {
        try
        {
            _sleepBlockCts?.Cancel();
            try
            {
                _sleepBlockTask?.Wait(TimeSpan.FromSeconds(3));
            }
            catch { }
        }
        finally
        {
            _sleepBlockTask?.Dispose();
            _sleepBlockCts?.Dispose();
            _sleepBlockTask = null;
            _sleepBlockCts = null;
        }
    }

    #endregion

    #region Audio Stack Isolation (audiodg.exe Fix)

    private static int _audiodgPid = -1;
    private static ProcessPriorityClass _audiodgOriginalPriority = ProcessPriorityClass.Normal;
    private static IntPtr _audiodgOriginalAffinity = IntPtr.Zero;

    /// <summary>Ізолює audiodg.exe: High-пріоритет + фіксація на останньому логічному ядрі CPU.</summary>
    private static void IsolateAudioStack()
    {
        try
        {
            using var proc = Process.GetProcessesByName("audiodg").FirstOrDefault();
            if (proc == null)
            {
                AppLogger.Log("Game Mode: audiodg.exe не знайдено — ізоляцію звукового стека пропущено", "WARN");
                return;
            }

            _audiodgPid = proc.Id;
            _audiodgOriginalPriority = proc.PriorityClass;
            _audiodgOriginalAffinity = proc.ProcessorAffinity;

            int lastCore = Math.Max(0, Environment.ProcessorCount - 1);
            long affinityMask = 1L << Math.Min(lastCore, 63);

            proc.PriorityClass = ProcessPriorityClass.High;
            proc.ProcessorAffinity = new IntPtr(affinityMask);

            AppLogger.Log($"Game Mode: audiodg.exe (PID {proc.Id}) переведено на High і зафіксовано на ядрі {lastCore}", "SUCCESS");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Game Mode: не вдалося ізолювати audiodg.exe: {ex.Message}", "WARN");
        }
    }

    private static void RestoreAudioStack()
    {
        if (_audiodgPid <= 0)
            return;

        try
        {
            using var proc = Process.GetProcessById(_audiodgPid);
            if (proc.ProcessName.Equals("audiodg", StringComparison.OrdinalIgnoreCase))
            {
                proc.PriorityClass = _audiodgOriginalPriority;
                proc.ProcessorAffinity = _audiodgOriginalAffinity;
                AppLogger.Log("Game Mode: стан audiodg.exe відновлено", "INFO");
            }
        }
        catch
        {
            // Процес міг завершитись — відновлювати нічого.
        }
        finally
        {
            _audiodgPid = -1;
            _audiodgOriginalPriority = ProcessPriorityClass.Normal;
            _audiodgOriginalAffinity = IntPtr.Zero;
        }
    }

    #endregion

    #region 1c. State Manager: демоція процесів (захист від PID Re-use)

    private const int ProcessIoPriority = 33; // PROCESS_INFORMATION_CLASS.ProcessIoPriority

    private const int IoPriorityLow = 1;
    private const int IoPriorityNormal = 2;

    // Smart Background Demotion: базовий список фонових застосунків.
    private static readonly string[] BaseBackgroundDemotionNames =
    {
        "chrome", "msedge", "discord", "steamwebhelper", "epicgameslauncher", "onedrive", "telegram", "spotify"
    };

    // Smart Background Demotion: додатковий список.
    private static readonly string[] AdditionalBackgroundDemotionNames =
    {
        "slack", "teams", "viber", "whatsapp", "battlenet", "upc", "eadesktop", "riotclientux", "qbittorrent"
    };

    private static readonly HashSet<string> BackgroundDemotionNameSet =
        new(BaseBackgroundDemotionNames.Concat(AdditionalBackgroundDemotionNames), StringComparer.OrdinalIgnoreCase);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass, out int processInformation, int processInformationLength, out int returnLength);

    [DllImport("ntdll.dll")]
    private static extern int NtSetInformationProcess(IntPtr processHandle, int processInformationClass, ref int processInformation, int processInformationLength);

    private static int GetProcessIoPriority(IntPtr handle)
    {
        try
        {
            if (NtQueryInformationProcess(handle, ProcessIoPriority, out int ioPriority, sizeof(int), out _) == 0)
                return ioPriority;
        }
        catch { }
        return IoPriorityNormal;
    }

    private static bool SetProcessIoPriority(IntPtr handle, int ioPriority)
    {
        try
        {
            int value = ioPriority;
            return NtSetInformationProcess(handle, ProcessIoPriority, ref value, sizeof(int)) == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool DemoteProcess(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            long startTimeTicks = proc.StartTime.Ticks;
            var key = (Pid: pid, StartTimeTicks: startTimeTicks);

            var state = new ProcessDemotionState
            {
                PriorityClass = proc.PriorityClass,
                IoPriority = GetProcessIoPriority(proc.Handle),
                PriorityBoostEnabled = proc.PriorityBoostEnabled,
                PagePriority = GetProcessPagePriority(proc.Handle),
                ProcessorAffinity = proc.ProcessorAffinity
            };

            if (!ProcessDemotionCache.TryAdd(key, state))
                return false; // Уже демотовано в цій сесії

            // М'якша демоція (BelowNormal + IO Low): знімає навантаження з гри, але не
            // заморожує голосові чати та оверлеї (Discord, Steam), на відміну від Idle.
            proc.PriorityClass = ProcessPriorityClass.BelowNormal;
            proc.PriorityBoostEnabled = false;
            SetProcessIoPriority(proc.Handle, IoPriorityLow);
            SetProcessPagePriority(proc.Handle, 1);           // Very Low — сторінки витісняються з RAM
            EmptyWorkingSet(proc.Handle);                      // скидання робочого набору
            TrySetAffinity(proc, _backgroundAffinityMask);     // зміщення на останні 2–4 ядра

            AppLogger.Log($"Game Mode: процес [{proc.ProcessName} (PID {pid})] демотовано", "INFO");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Game Mode: не вдалося демотувати процес PID={pid}: {ex.Message}", "WARN");
            return false;
        }
    }

    private static bool IsBackgroundDemotionTarget(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return false;

        // Деякі імена процесів містять крапку (напр. "Battle.net"), тому нормалізуємо перед порівнянням.
        string normalized = processName.Replace(".", string.Empty).Replace(" ", string.Empty);
        return BackgroundDemotionNameSet.Contains(normalized);
    }

    /// <summary>Демотує всі запущені фонові застосунки з базового та додаткового списків.</summary>
    public static int DemoteBackgroundProcesses()
    {
        int demoted = 0;
        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (proc.HasExited || proc.Id <= 4)
                        continue;

                    if (IsBackgroundDemotionTarget(proc.ProcessName) && DemoteProcess(proc.Id))
                    {
                        demoted++;
                    }
                }
                catch { }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Game Mode: помилка перерахування процесів для демоції: {ex.Message}", "WARN");
        }

        return demoted;
    }

    public static bool RestoreProcess(int pid, long startTimeTicks)
    {
        var key = (Pid: pid, StartTimeTicks: startTimeTicks);
        if (!ProcessDemotionCache.TryRemove(key, out var state))
            return false;

        try
        {
            using var proc = Process.GetProcessById(pid);

            // Захист від PID Re-use: якщо PID вже займає інший процес — не чіпаємо його.
            if (proc.StartTime.Ticks != startTimeTicks)
            {
                AppLogger.Log($"Game Mode: PID {pid} повторно використано — відновлення скасовано", "WARN");
                return false;
            }

            // Відновлення виключно з кешу (жодних хардкод-значень).
            proc.PriorityClass = state.PriorityClass;
            proc.PriorityBoostEnabled = state.PriorityBoostEnabled;
            SetProcessIoPriority(proc.Handle, state.IoPriority);
            SetProcessPagePriority(proc.Handle, state.PagePriority);
            if (state.ProcessorAffinity != IntPtr.Zero)
                proc.ProcessorAffinity = state.ProcessorAffinity;

            AppLogger.Log($"Game Mode: процес [{proc.ProcessName} (PID {pid})] відновлено з кешу", "INFO");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Game Mode: не вдалося відновити процес PID={pid}: {ex.Message}", "WARN");
            return false;
        }
    }

    private static void RestoreAllDemotedProcesses()
    {
        foreach (var key in ProcessDemotionCache.Keys.ToList())
        {
            RestoreProcess(key.Pid, key.StartTimeTicks);
        }
    }

    #endregion

    #region 1d. Апаратна топологія CPU, пріоритети пам'яті та Continuous Game Watcher (Phase 2)

    private const int RelationProcessorCore = 0;
    private const int RelationCache = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct GROUP_AFFINITY
    {
        public ulong Mask;
        public ushort Group;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformationEx(int relationshipType, IntPtr buffer, ref uint returnedLength);

    private const int ProcessPagePriority = 39;
    private const int ProcessPowerThrottling = 55;
    private const int IoPriorityHigh = 3;

    private const uint ProcessPowerThrottlingExecutionSpeed = 0x1;
    private const uint ProcessPowerThrottlingCurrentVersion = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct PAGE_PRIORITY_INFORMATION
    {
        public UIntPtr PagePriority;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("ntdll.dll", EntryPoint = "NtQueryInformationProcess")]
    private static extern int NtQueryInformationProcessPowerThrottling(IntPtr processHandle, int processInformationClass, ref PROCESS_POWER_THROTTLING_STATE processInformation, int processInformationLength, out int returnLength);

    [DllImport("ntdll.dll", EntryPoint = "NtQueryInformationProcess")]
    private static extern int NtQueryInformationProcessPagePriority(IntPtr processHandle, int processInformationClass, ref PAGE_PRIORITY_INFORMATION processInformation, int processInformationLength, out int returnLength);

    [DllImport("ntdll.dll", EntryPoint = "NtSetInformationProcess")]
    private static extern int NtSetInformationProcessPagePriority(IntPtr processHandle, int processInformationClass, ref PAGE_PRIORITY_INFORMATION processInformation, int processInformationLength);

    [DllImport("ntdll.dll", EntryPoint = "NtSetInformationProcess")]
    private static extern int NtSetInformationProcessPowerThrottling(IntPtr processHandle, int processInformationClass, ref PROCESS_POWER_THROTTLING_STATE processInformation, int processInformationLength);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private enum CpuArchitecture
    {
        Classic,
        AmdX3D,
        IntelHybrid
    }

    private static CpuArchitecture _cpuArchitecture = CpuArchitecture.Classic;
    private static ulong _gameAffinityMask = ulong.MaxValue;
    private static ulong _backgroundAffinityMask = ulong.MaxValue;
    private static bool _topologyAnalyzed = false;
    private static readonly object _topologyLock = new();

    private static readonly string[] WatcherExcludedForegroundNames =
    {
        "explorer", "dwm", "taskmgr", "maslooptimizer"
    };

    private static CancellationTokenSource? _gameWatcherCts;
    private static Task? _gameWatcherTask;

    public static bool IsGameWatcherRunning => _gameWatcherTask != null;

    private static string GetCpuModelName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return key?.GetValue("ProcessorNameString")?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static ulong FullMask(int logicalProcessors)
    {
        if (logicalProcessors >= 64)
            return ulong.MaxValue;
        return (1UL << logicalProcessors) - 1;
    }

    private static (ulong PerformanceMask, bool HasEfficiencyCores) GetPerformanceCoreTopology()
    {
        ulong performanceMask = 0;
        bool hasECores = false;
        IntPtr buffer = IntPtr.Zero;
        try
        {
            uint size = 0;
            GetLogicalProcessorInformationEx(RelationProcessorCore, IntPtr.Zero, ref size);
            if (size == 0)
                return (0, false);

            buffer = Marshal.AllocHGlobal((int)size);
            if (!GetLogicalProcessorInformationEx(RelationProcessorCore, buffer, ref size))
                return (0, false);

            int offset = 0;
            while (offset < (int)size)
            {
                int relationship = Marshal.ReadInt32(buffer, offset);
                int entrySize = Marshal.ReadInt32(buffer, offset + 4);
                if (entrySize <= 0)
                    break;

                if (relationship == RelationProcessorCore)
                {
                    byte efficiencyClass = Marshal.ReadByte(buffer, offset + 8 + 1);
                    int groupCount = Marshal.ReadInt16(buffer, offset + 8 + 22);
                    if (groupCount > 0)
                    {
                        var group = Marshal.PtrToStructure<GROUP_AFFINITY>(IntPtr.Add(buffer, offset + 8 + 24));
                        if (group.Group == 0)
                        {
                            if (efficiencyClass <= 1)
                                performanceMask |= group.Mask;
                            else
                                hasECores = true;
                        }
                    }
                }

                offset += entrySize;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Game Mode: помилка аналізу топології ядер CPU: {ex.Message}", "WARN");
        }
        finally
        {
            if (buffer != IntPtr.Zero)
                Marshal.FreeHGlobal(buffer);
        }

        return (performanceMask, hasECores);
    }

    private static ulong GetFirstL3CacheMask()
    {
        IntPtr buffer = IntPtr.Zero;
        try
        {
            uint size = 0;
            GetLogicalProcessorInformationEx(RelationCache, IntPtr.Zero, ref size);
            if (size == 0)
                return 0;

            buffer = Marshal.AllocHGlobal((int)size);
            if (!GetLogicalProcessorInformationEx(RelationCache, buffer, ref size))
                return 0;

            int offset = 0;
            while (offset < (int)size)
            {
                int relationship = Marshal.ReadInt32(buffer, offset);
                int entrySize = Marshal.ReadInt32(buffer, offset + 4);
                if (entrySize <= 0)
                    break;

                if (relationship == RelationCache)
                {
                    byte level = Marshal.ReadByte(buffer, offset + 8);
                    if (level == 3)
                    {
                        var group = Marshal.PtrToStructure<GROUP_AFFINITY>(IntPtr.Add(buffer, offset + 8 + 32));
                        if (group.Group == 0)
                            return group.Mask;
                    }
                }

                offset += entrySize;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Game Mode: помилка аналізу L3-кешу (CCD): {ex.Message}", "WARN");
        }
        finally
        {
            if (buffer != IntPtr.Zero)
                Marshal.FreeHGlobal(buffer);
        }

        return 0;
    }

    private static ulong BuildBackgroundAffinityMask(int logicalProcessors)
    {
        if (logicalProcessors >= 64)
            return ulong.MaxValue;

        int count = logicalProcessors >= 8 ? 4 : 2;
        if (logicalProcessors <= count)
            return (1UL << logicalProcessors) - 1;

        ulong mask = 0;
        for (int i = logicalProcessors - count; i < logicalProcessors; i++)
            mask |= 1UL << i;
        return mask;
    }

    private static void AnalyzeCpuTopology()
    {
        lock (_topologyLock)
        {
            if (_topologyAnalyzed)
                return;

            _topologyAnalyzed = true;
            try
            {
                int logicalProcessors = Math.Max(1, Environment.ProcessorCount);
                string model = GetCpuModelName();
                bool isX3D = model.Contains("X3D", StringComparison.OrdinalIgnoreCase);

                var (perfMask, hasECores) = GetPerformanceCoreTopology();

                if (isX3D)
                {
                    _cpuArchitecture = CpuArchitecture.AmdX3D;
                    ulong ccd0 = GetFirstL3CacheMask();
                    _gameAffinityMask = ccd0 != 0 ? ccd0 : FullMask(logicalProcessors);
                }
                else if (hasECores)
                {
                    _cpuArchitecture = CpuArchitecture.IntelHybrid;
                    _gameAffinityMask = perfMask != 0 ? perfMask : FullMask(logicalProcessors);
                }
                else
                {
                    _cpuArchitecture = CpuArchitecture.Classic;
                    _gameAffinityMask = FullMask(logicalProcessors);
                    DisableCoreParking();
                }

                _backgroundAffinityMask = BuildBackgroundAffinityMask(logicalProcessors);
                AppLogger.Log($"Game Mode: топологія CPU = {_cpuArchitecture} (game affinity 0x{_gameAffinityMask:X})", "INFO");
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Game Mode: не вдалося проаналізувати топологію CPU: {ex.Message}", "WARN");
                _cpuArchitecture = CpuArchitecture.Classic;
                _gameAffinityMask = FullMask(Math.Max(1, Environment.ProcessorCount));
                _backgroundAffinityMask = ulong.MaxValue;
            }
        }
    }

    private static void DisableCoreParking()
    {
        try
        {
            int result = RunPowerCfg("/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR 0cc5b647-c1df-4637-891a-dec35c318583 100");
            if (result == 0)
                result = RunPowerCfg("/setactive SCHEME_CURRENT");

            if (result == 0)
                AppLogger.Log("Game Mode: Core Parking вимкнено (CPMINCORES = 100)", "SUCCESS");
            else
                AppLogger.Log($"Game Mode: не вдалося вимкнути Core Parking (powercfg код {result})", "WARN");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Game Mode: не вдалося вимкнути Core Parking: {ex.Message}", "WARN");
        }
    }

    private static int RunPowerCfg(string arguments)
    {
        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName = "powercfg.exe",
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });
        if (proc == null)
            return -1;

        proc.WaitForExit(10_000);
        return proc.ExitCode;
    }

    private static int GetProcessPagePriority(IntPtr handle)
    {
        try
        {
            var info = new PAGE_PRIORITY_INFORMATION();
            if (NtQueryInformationProcessPagePriority(handle, ProcessPagePriority, ref info, Marshal.SizeOf(typeof(PAGE_PRIORITY_INFORMATION)), out _) == 0)
                return (int)info.PagePriority.ToUInt32();
        }
        catch { }
        return 5; // Normal — безпечний дефолт
    }

    private static bool SetProcessPagePriority(IntPtr handle, int pagePriority)
    {
        try
        {
            var info = new PAGE_PRIORITY_INFORMATION { PagePriority = new UIntPtr((uint)pagePriority) };
            return NtSetInformationProcessPagePriority(handle, ProcessPagePriority, ref info, Marshal.SizeOf(typeof(PAGE_PRIORITY_INFORMATION))) == 0;
        }
        catch
        {
            return false;
        }
    }

    private static uint GetProcessPowerThrottlingState(IntPtr handle)
    {
        try
        {
            var state = new PROCESS_POWER_THROTTLING_STATE();
            if (NtQueryInformationProcessPowerThrottling(handle, ProcessPowerThrottling, ref state, Marshal.SizeOf(typeof(PROCESS_POWER_THROTTLING_STATE)), out _) == 0)
                return state.StateMask;
        }
        catch { }
        return ProcessPowerThrottlingExecutionSpeed; // дефолт: троттлінг увімкнено
    }

    private static bool DisableProcessPowerThrottling(IntPtr handle)
    {
        try
        {
            var state = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = ProcessPowerThrottlingCurrentVersion,
                ControlMask = ProcessPowerThrottlingExecutionSpeed,
                StateMask = 0
            };
            return NtSetInformationProcessPowerThrottling(handle, ProcessPowerThrottling, ref state, Marshal.SizeOf(typeof(PROCESS_POWER_THROTTLING_STATE))) == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool RestoreProcessPowerThrottling(IntPtr handle, uint stateMask)
    {
        try
        {
            var state = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = ProcessPowerThrottlingCurrentVersion,
                ControlMask = ProcessPowerThrottlingExecutionSpeed,
                StateMask = stateMask
            };
            return NtSetInformationProcessPowerThrottling(handle, ProcessPowerThrottling, ref state, Marshal.SizeOf(typeof(PROCESS_POWER_THROTTLING_STATE))) == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TrySetAffinity(Process proc, ulong mask)
    {
        if (mask == ulong.MaxValue)
            return false; // повна маска — обмеження не потрібне

        try
        {
            proc.ProcessorAffinity = new IntPtr(unchecked((long)mask));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ApplyGameProcessBoost(Process proc)
    {
        SetProcessPagePriority(proc.Handle, 5);       // Normal (найвищий) — утримує сторінки гри в RAM
        DisableProcessPowerThrottling(proc.Handle);  // Power Throttling OFF
        SetProcessIoPriority(proc.Handle, IoPriorityHigh);
        proc.PriorityClass = ProcessPriorityClass.High;
        TrySetAffinity(proc, _gameAffinityMask);     // CCD0 / P-Cores / повна маска
    }

    private static bool IsWatcherExcludedProcess(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return true;

        foreach (string excluded in WatcherExcludedForegroundNames)
        {
            if (processName.Contains(excluded, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void ApplyBoostToForegroundGame()
    {
        IntPtr hWnd = GetForegroundWindow();
        if (hWnd == IntPtr.Zero)
            return;

        GetWindowThreadProcessId(hWnd, out uint pid);
        if (pid <= 4)
            return;

        try
        {
            using var proc = Process.GetProcessById((int)pid);
            string name = proc.ProcessName;

            if (IsWatcherExcludedProcess(name))
                return;

            // Інтелектуальний фільтр: не бустимо фонові застосунки (Chrome, Discord, Spotify тощо).
            if (IsBackgroundDemotionTarget(name))
                return;

            long startTimeTicks = proc.StartTime.Ticks;
            var key = (Pid: (int)pid, StartTimeTicks: startTimeTicks);

            if (GameBoostStateCache.ContainsKey(key))
                return; // уже бустовано в цій сесії

            var state = new GameProcessBoostState
            {
                PriorityClass = proc.PriorityClass,
                IoPriority = GetProcessIoPriority(proc.Handle),
                PagePriority = GetProcessPagePriority(proc.Handle),
                PowerThrottlingStateMask = GetProcessPowerThrottlingState(proc.Handle),
                ProcessorAffinity = proc.ProcessorAffinity
            };

            if (!GameBoostStateCache.TryAdd(key, state))
                return;

            ApplyGameProcessBoost(proc);

            AppLogger.Log($"Game Watcher: процес [{name} (PID {pid})] отримав ігровий буст", "SUCCESS");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Game Watcher: не вдалося бустнути процес PID={pid}: {ex.Message}", "WARN");
        }
    }

    private static void RestoreGameProcessBoost(int pid, long startTimeTicks)
    {
        var key = (Pid: pid, StartTimeTicks: startTimeTicks);
        if (!GameBoostStateCache.TryRemove(key, out var state))
            return;

        try
        {
            using var proc = Process.GetProcessById(pid);

            // Захист від PID Re-use.
            if (proc.StartTime.Ticks != startTimeTicks)
            {
                AppLogger.Log($"Game Watcher: PID {pid} повторно використано — відновлення скасовано", "WARN");
                return;
            }

            proc.PriorityClass = state.PriorityClass;
            SetProcessIoPriority(proc.Handle, state.IoPriority);
            SetProcessPagePriority(proc.Handle, state.PagePriority);
            RestoreProcessPowerThrottling(proc.Handle, state.PowerThrottlingStateMask);
            if (state.ProcessorAffinity != IntPtr.Zero)
                proc.ProcessorAffinity = state.ProcessorAffinity;

            AppLogger.Log($"Game Watcher: процес [{proc.ProcessName} (PID {pid})] відновлено", "INFO");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Game Watcher: не вдалося відновити процес PID={pid}: {ex.Message}", "WARN");
        }
    }

    private static void RestoreAllGameBoostedProcesses()
    {
        foreach (var key in GameBoostStateCache.Keys.ToList())
        {
            RestoreGameProcessBoost(key.Pid, key.StartTimeTicks);
        }
    }

    private static async Task GameWatcherLoopAsync(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                try
                {
                    ApplyBoostToForegroundGame();
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"Game Watcher: помилка циклу: {ex.Message}", "WARN");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Очікуване завершення при StopGameWatcher().
        }
    }

    public static void StartGameWatcher()
    {
        if (_gameWatcherTask != null)
            return;

        AnalyzeCpuTopology();

        var cts = new CancellationTokenSource();
        _gameWatcherCts = cts;
        _gameWatcherTask = GameWatcherLoopAsync(cts.Token);
    }

    public static void StopGameWatcher()
    {
        _gameWatcherCts?.Cancel();
        try
        {
            _gameWatcherTask?.Wait(TimeSpan.FromSeconds(3));
        }
        catch { }

        _gameWatcherCts?.Dispose();
        _gameWatcherTask?.Dispose();
        _gameWatcherCts = null;
        _gameWatcherTask = null;

        RestoreAllGameBoostedProcesses();
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
                // Очищаємо кеш стану служб перед новою активацією.
                ServiceStateCache.Clear();
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
                        // Фіксуємо початковий стан служби — це єдине джерело правди для відновлення.
                        // Якщо служба вже була Stopped до старту Game Mode, її буде проігноровано при відновленні.
                        var originalStatus = sc.Status;
                        ServiceStateCache.TryAdd(svcName, originalStatus);

                        if (originalStatus == ServiceControllerStatus.Running && sc.CanStop)
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
                }

                // Звільняємо дескриптори всіх отриманих служб (запобігає витоку SCM-хендлів).
                foreach (var svc in installedServices.Values)
                    svc.Dispose();

                // Кіберспортивний таймер (0.5 мс) та блокування сну дисплея/системи
                ActivateTimerResolution();
                ActivateSleepBlocker();

                // Збереження та активація схеми максимальної продуктивності
                SwitchToHighPerformancePower();

                // Очищення кешу оперативної пам'яті
                PurgeStandbyList();

                // Очищення кешу шейдерів GPU (Pre-Launch Shader Purge)
                PurgeGpuShaderCaches();

                // Ізоляція звукового стека (audiodg.exe Fix)
                IsolateAudioStack();

                // Активація ігрових реєстрових оптимізацій (MMCSS)
                ApplyGameModeRegistry();

                // Аналіз топології CPU (X3D CCD0 / Intel P-Cores / класика) перед демоцією фону.
                AnalyzeCpuTopology();

                // Smart Background Demotion: зниження пріоритету фонових застосунків (CPU + IO)
                int demotedCount = DemoteBackgroundProcesses();

                lock (_lock)
                {
                    IsGameModeActive = true;
                }

                // Continuous Game Watcher (інтервал 3 с)
                StartGameWatcher();

                // Подію викликаємо ПОЗА lock; винятки підписників не повинні ламати стан
                try
                {
                    OnGameModeStateChanged?.Invoke(true);
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"Game Mode: помилка підписника події: {ex.Message}", "WARN");
                }

                AppLogger.Log($"Game Mode АКТИВОВАНО: зупинено {stopped.Count} служб, демотовано {demotedCount} процесів", "SUCCESS");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Помилка активації Game Mode: {ex.Message}", "ERROR");

                // Відкат частково застосованих змін, щоб не залишити систему в проміжному стані.
                RestoreServicesFromCache();
                StopGameWatcher();
                RestoreTimerResolution();
                DeactivateSleepBlocker();
                RestoreAllDemotedProcesses();
                RestoreAudioStack();

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
            lock (_lock)
            {
                if (!IsGameModeActive || _busy)
                    return false; // Не активний або йде інша операція — нічого відновлювати
                _busy = true;
            }

            bool result = false;
            try
            {
                // Відновлення служб виключно з кешу стану (без хардкоду).
                int restoredCount = RestoreServicesFromCache();

                // Зупинка Continuous Game Watcher та відновлення бустованих ігрових процесів.
                StopGameWatcher();

                // Відновлення таймера та сну
                RestoreTimerResolution();
                DeactivateSleepBlocker();

                // Відновлення демотованих процесів виключно з кешу (захист від PID Re-use).
                RestoreAllDemotedProcesses();

                // Відновлення звукового стека (audiodg.exe)
                RestoreAudioStack();

                // Відновлення попередньої схеми живлення
                RestorePreviousPowerPlan();

                // Відновлення параметрів реєстру
                RestoreGameModeRegistry();

                AppLogger.Log($"Game Mode ДЕАКТИВОВАНО: відновлено {restoredCount} служб та початковий план живлення", "INFO");
                result = true;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Помилка відновлення системного стану: {ex.Message}", "ERROR");
            }
            finally
            {
                // Прапорець скидаємо в finally: навіть якщо частина відновлення впала,
                // стан "вимкнено" лишається консистентним і не блокує подальші операції.
                lock (_lock)
                {
                    IsGameModeActive = false;
                    _busy = false;
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
            }

            return result;
        });
    }

    private static int RestoreServicesFromCache()
    {
        int restoredCount = 0;

        foreach (var kvp in ServiceStateCache)
        {
            string svcName = kvp.Key;
            ServiceControllerStatus originalStatus = kvp.Value;

            // Умова деактивації: якщо служба була Stopped до старту Game Mode — ігноруємо її при відновленні.
            if (originalStatus == ServiceControllerStatus.Stopped)
                continue;

            // Відновлюємо лише служби, які реально працювали до Game Mode.
            if (originalStatus != ServiceControllerStatus.Running)
                continue;

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

        ServiceStateCache.Clear();
        lock (_lock)
        {
            StoppedServicesState.Clear();
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

            var ultimate = UltimatePlanGuid;
            if (PowerSetActiveScheme(IntPtr.Zero, ref ultimate) == 0)
            {
                _activePowerPlanName = "Ultimate Performance";
                AppLogger.Log("Game Mode: активовано схему Ultimate Performance", "SUCCESS");
                return;
            }

            var highPerf = HighPerfPlanGuid;
            if (PowerSetActiveScheme(IntPtr.Zero, ref highPerf) == 0)
            {
                _activePowerPlanName = "High Performance";
                AppLogger.Log("Game Mode: Ultimate Performance недоступна — активовано High Performance", "WARN");
                return;
            }

            // Спробуємо створити Ultimate Performance через powercfg (якщо схема прихована).
            try
            {
                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = "-duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false
                });

                string output = proc?.StandardOutput.ReadToEnd() ?? string.Empty;
                string error = proc?.StandardError.ReadToEnd() ?? string.Empty;

                var match = Regex.Match(output, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
                if (match.Success && Guid.TryParse(match.Value, out Guid newGuid))
                {
                    if (PowerSetActiveScheme(IntPtr.Zero, ref newGuid) == 0)
                    {
                        _createdPowerPlanGuid = newGuid; // при деактивації схему буде видалено
                        _activePowerPlanName = "Ultimate Performance (створена)";
                        AppLogger.Log("Game Mode: створено та активовано схему Ultimate Performance", "SUCCESS");
                        return;
                    }

                    // Не вдалося активувати створену схему — видаляємо її, щоб не смітити.
                    TryDeletePowerPlan(newGuid);
                }
                else if (!string.IsNullOrWhiteSpace(error))
                {
                    AppLogger.Log($"Game Mode: powercfg -duplicatescheme повернув помилку: {error.Trim()}", "WARN");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Game Mode: помилка створення схеми живлення: {ex.Message}", "WARN");
            }

            _activePowerPlanName = "Не вдалося змінити";
            AppLogger.Log("Game Mode: не вдалося активувати жодну високопродуктивну схему", "WARN");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Game Mode: не вдалося активувати схему максимальної продуктивності: {ex.Message}", "WARN");
        }
    }

    private static void TryDeletePowerPlan(Guid planGuid)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "powercfg.exe",
                Arguments = $"-delete {planGuid}",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            if (proc == null)
                return;

            proc.WaitForExit(5000);
            if (proc.ExitCode != 0)
                AppLogger.Log($"Game Mode: powercfg -delete {planGuid} завершився з кодом {proc.ExitCode}", "WARN");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Game Mode: не вдалося видалити схему живлення: {ex.Message}", "WARN");
        }
    }

    private static void RestorePreviousPowerPlan()
    {
        try
        {
            if (_previousPowerPlanGuid.HasValue)
            {
                var prev = _previousPowerPlanGuid.Value;
                if (PowerSetActiveScheme(IntPtr.Zero, ref prev) == 0)
                {
                    _previousPowerPlanGuid = null;
                    AppLogger.Log("Game Mode: попередню схему живлення відновлено", "INFO");
                }
                else
                {
                    // Не обнуляємо GUID — зберігаємо для можливої повторної спроби.
                    AppLogger.Log("Game Mode: не вдалося відновити попередню схему живлення (PowerSetActiveScheme)", "WARN");
                }
            }

            // Видаляємо створену під час активації схему живлення, якщо вона більше не активна.
            if (_createdPowerPlanGuid.HasValue)
            {
                var created = _createdPowerPlanGuid.Value;
                _createdPowerPlanGuid = null;
                TryDeletePowerPlan(created);
            }

            _activePowerPlanName = string.Empty;
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

    private static bool SetRegistryValue(RegistryHive hive, string subKey, string valueName, object value, RegistryValueKind kind)
    {
        try
        {
            SnapshotRegistryValue(hive, subKey, valueName);

            using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).CreateSubKey(subKey, writable: true);
            if (key == null)
            {
                AppLogger.Log($"Game Mode: не вдалося відкрити/створити ключ реєстру [{subKey}]", "WARN");
                return false;
            }

            key.SetValue(valueName, value, kind);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Game Mode: не вдалося записати значення реєстру [{subKey}\\{valueName}]: {ex.Message}", "WARN");
            return false;
        }
    }

    private static void ApplyGameModeRegistry()
    {
        // Снапшот НЕ очищаємо на повторній активації: SnapshotRegistryValue захищає від перезапису
        // оригіналів через ContainsKey. Якщо після часткового збою активацію запустять повторно,
        // відкат все одно поверне саме ті значення, що були до першого втручання.

        // 1. Активація ігрового режиму Windows GameBar
        SetRegistryValue(RegistryHive.CurrentUser, GameBarSubKey, "AutoGameModeEnabled", 1, RegistryValueKind.DWord);
        SetRegistryValue(RegistryHive.CurrentUser, GameBarSubKey, "AllowAutoGameMode", 1, RegistryValueKind.DWord);

        // 2. Пріоритет виділення ресурсів GPU під час ігор (MMCSS Tasks\Games)
        _gpuPriorityApplied = SetRegistryValue(RegistryHive.LocalMachine, MMCssGamesSubKey, "GPU Priority", 8, RegistryValueKind.DWord);
        SetRegistryValue(RegistryHive.LocalMachine, MMCssGamesSubKey, "Priority", 6, RegistryValueKind.DWord);
        SetRegistryValue(RegistryHive.LocalMachine, MMCssGamesSubKey, "Scheduling Category", "High", RegistryValueKind.String);
        SetRegistryValue(RegistryHive.LocalMachine, MMCssGamesSubKey, "SFIO Priority", "High", RegistryValueKind.String);

        // 3. Зняття системного троттлінгу відгуку (100% CPU на ігри)
        _cpuThrottlingDisabled = SetRegistryValue(RegistryHive.LocalMachine, MMCssProfileSubKey, "SystemResponsiveness", 0, RegistryValueKind.DWord);
        SetRegistryValue(RegistryHive.LocalMachine, MMCssProfileSubKey, "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord);
    }

    private static void RestoreGameModeRegistry()
    {
        // Ітеруємо копію; з оригіналу видаляємо лише успішно відновлені записи,
        // щоб у разі збою залишилась можливість повторної спроби відновлення.
        foreach (var kvp in _registrySnapshot.ToList())
        {
            var snapshot = kvp.Value;
            try
            {
                using var key = RegistryKey.OpenBaseKey(snapshot.Hive, RegistryView.Registry64).OpenSubKey(snapshot.SubKey, writable: true);
                if (key == null)
                {
                    _registrySnapshot.Remove(kvp.Key);
                    continue;
                }

                if (snapshot.Existed)
                {
                    // Значення гарантовано присутнє, якщо Existed == true (знімок зроблено до втручання).
                    key.SetValue(snapshot.ValueName, snapshot.Value!, snapshot.Kind);
                }
                else
                {
                    key.DeleteValue(snapshot.ValueName, throwOnMissingValue: false);
                }

                _registrySnapshot.Remove(kvp.Key);
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Game Mode: не вдалося відновити значення реєстру [{snapshot.SubKey}\\{snapshot.ValueName}]: {ex.Message}", "WARN");
            }
        }

        _gpuPriorityApplied = false;
        _cpuThrottlingDisabled = false;
    }

    public static bool BoostForegroundGameProcess()
        => MonitorEngine.BoostForegroundProcess();

    public static GameModeStatusInfo GetStatusInfo()
    {
        lock (_lock)
        {
            string activePlan = IsGameModeActive && !string.IsNullOrEmpty(_activePowerPlanName)
                ? _activePowerPlanName
                : "Стандартна схема";

            return new GameModeStatusInfo
            {
                IsActive = IsGameModeActive,
                StoppedServicesCount = StoppedServicesState.Count,
                StoppedServicesList = new List<string>(StoppedServicesState),
                LastFreedMemoryMb = _lastFreedMemoryMb,
                ActivePowerPlan = activePlan,
                IsGpuPriorityBoosted = IsGameModeActive && _gpuPriorityApplied,
                IsCpuThrottlingDisabled = IsGameModeActive && _cpuThrottlingDisabled
            };
        }
    }

    #endregion
}