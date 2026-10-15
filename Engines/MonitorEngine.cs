using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MASLOOPTIMIZER;

/// <summary>
/// Гігієна сесії (ручний виклик): асинхронний ipconfig /flushdns + очищення буфера обміну через P/Invoke.
///
/// Safe defaults: автоматичний High-буст процесу у фокусі та наглядач фокусу (SetWinEventHook)
/// прибрані повністю. Єдине місце, яке має право змінювати пріоритети процесів, — явно
/// увімкнений Game Mode (див. GameModeEngine), причому зі снапшотом і гарантованим відкатом.
/// </summary>
public static class MonitorEngine
{
    #region Win32 P/Invoke (буфер обміну)

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    #endregion

    #region Session Hygiene

    /// <summary>
    /// Асинхронно виконує гігієну сесії (DNS-кеш + буфер обміну).
    /// Викликається ЛИШЕ вручну: під час автозапуску на логіні жодні процеси не спавняться
    /// і буфер обміну користувача не очищується (він міг містити важливі дані).
    /// </summary>
    public static async Task RunSessionHygieneAsync()
    {
        await Task.Run(() =>
        {
            FlushDnsCache();
            ClearClipboard();
        }).ConfigureAwait(false);
    }

    private static void FlushDnsCache()
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "ipconfig.exe",
                Arguments = "/flushdns",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            if (proc == null)
                return;

            if (!proc.WaitForExit(10_000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                AppLogger.Log("Гігієна: ipconfig /flushdns перевищив ліміт часу", "WARN");
                return;
            }

            if (proc.ExitCode == 0)
                AppLogger.Log("Гігієна: DNS-кеш очищено (ipconfig /flushdns)", "SUCCESS");
            else
                AppLogger.Log($"Гігієна: ipconfig /flushdns повернув код {proc.ExitCode}", "WARN");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Гігієна: помилка очищення DNS-кешу: {ex.Message}", "WARN");
        }
    }

    /// <summary>
    /// Очищає буфер обміну через P/Invoke (без залежності від STA-потоку WPF).
    /// </summary>
    private static void ClearClipboard()
    {
        try
        {
            // Кілька спроб: інший процес може тимчасово утримувати буфер обміну.
            for (int attempt = 0; attempt < 5; attempt++)
            {
                if (OpenClipboard(IntPtr.Zero))
                {
                    try
                    {
                        EmptyClipboard();
                    }
                    finally
                    {
                        CloseClipboard();
                    }
                    AppLogger.Log("Гігієна: буфер обміну очищено", "INFO");
                    return;
                }
                Thread.Sleep(50);
            }

            AppLogger.Log("Гігієна: не вдалося відкрити буфер обміну для очищення", "WARN");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Гігієна: помилка очищення буфера обміну: {ex.Message}", "WARN");
        }
    }

    #endregion

    #region Standalone Standby RAM Purge (Фаза 6)

    // Standalone-очищення ОЗП (незалежне від GameModeEngine, видаленого у Фазі 1):
    //  - EmptyWorkingSet по всіх доступних процесах;
    //  - NtSetSystemInformation(SystemMemoryListInformation): FlushModified + PurgeStandby + PurgeLowPriorityStandby;
    //  - звіт про фактично звільнені МБ за дельтою доступної фізичної пам'яті.

    [DllImport("psapi.dll")]
    private static extern int EmptyWorkingSet(IntPtr hwProc);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int systemInformationClass, IntPtr systemInformation, int systemInformationLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? systemName, string name, out long luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAllPrivileges, ref TOKEN_PRIVILEGES newState, int bufferLength, IntPtr previousState, IntPtr returnLength);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID_AND_ATTRIBUTES
    {
        public long Luid;
        public int Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public int PrivilegeCount;
        public LUID_AND_ATTRIBUTES Privileges;
    }

    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const int SePrivilegeEnabled = 0x2;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint ProcessSetQuota = 0x0100;

    // SystemMemoryListInformation (0x50) — команди списків пам'яті ядра.
    private const int SystemMemoryListInformation = 0x50;
    private const int MemoryFlushModifiedList = 3;
    private const int MemoryPurgeStandbyList = 4;
    private const int MemoryPurgeLowPriorityStandbyList = 5;

    /// <summary>
    /// Standalone-очищення ОЗП (трифазна очистка Standby RAM + скидання робочих наборів).
    /// Повертає приблизну кількість звільнених байт (дельта доступної фізичної пам'яті).
    /// </summary>
    public static Task<long> PurgeStandbyMemoryAsync() => Task.Run(() =>
    {
        long before = GetAvailablePhysicalMemory();
        try { EnablePurgePrivileges(); } catch { }
        try { EmptyAllWorkingSets(); } catch { }
        try { PurgeStandbyLists(); } catch { }

        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        catch { }

        long after = GetAvailablePhysicalMemory();
        long freed = after - before;

        AppLogger.Log($"Standalone-очищення ОЗП: звільнено ≈{freed / (1024.0 * 1024.0):N0} МБ", "SUCCESS");
        return freed > 0 ? freed : 0;
    });

    private static void EnablePurgePrivileges()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out IntPtr token)) return;
        try
        {
            EnablePrivilege(token, "SeProfileSingleProcessPrivilege");
            EnablePrivilege(token, "SeIncreaseQuotaPrivilege");
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private static void EnablePrivilege(IntPtr token, string name)
    {
        if (!LookupPrivilegeValue(null, name, out long luid)) return;
        var tp = new TOKEN_PRIVILEGES
        {
            PrivilegeCount = 1,
            Privileges = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = SePrivilegeEnabled }
        };
        AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
    }

    private static void EmptyAllWorkingSets()
    {
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                IntPtr handle = OpenProcess(ProcessQueryLimitedInformation | ProcessSetQuota, false, proc.Id);
                if (handle == IntPtr.Zero) continue;
                try { EmptyWorkingSet(handle); }
                finally { CloseHandle(handle); }
            }
            catch { }
            finally
            {
                proc.Dispose();
            }
        }
    }

    private static void PurgeStandbyLists()
    {
        foreach (int command in new[] { MemoryFlushModifiedList, MemoryPurgeStandbyList, MemoryPurgeLowPriorityStandbyList })
        {
            IntPtr buffer = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                Marshal.WriteInt32(buffer, command);
                NtSetSystemInformation(SystemMemoryListInformation, buffer, sizeof(int));
            }
            catch { }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    private static long GetAvailablePhysicalMemory()
    {
        var mem = new MEMORYSTATUSEX();
        return GlobalMemoryStatusEx(mem) ? (long)mem.ullAvailPhys : 0;
    }

    #endregion
}
