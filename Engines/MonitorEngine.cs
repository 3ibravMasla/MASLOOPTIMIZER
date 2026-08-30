using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MASLOOPTIMIZER;

/// <summary>
/// Динамічний моніторинг (Фаза 2):
///   - Zero-Allocation Watcher (наглядач фокусу на базі PeriodicTimer, 10 секунд);
///   - Гігієна сесії (асинхронний ipconfig /flushdns + очищення буфера обміну через P/Invoke).
/// </summary>
public static class MonitorEngine
{
    #region Win32 P/Invoke (Zero-Allocation)

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, [Out] char[] lpExeName, ref uint lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetPriorityClass(IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetPriorityClass(IntPtr hProcess, uint dwPriorityClass);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    private const uint ProcessQueryInfo = 0x0400; // PROCESS_QUERY_INFORMATION
    private const uint ProcessSetInfo = 0x0200;   // PROCESS_SET_INFORMATION

    private const uint HighPriorityClass = 0x80;
    private const uint RealTimePriorityClass = 0x100;

    private static readonly string[] ExcludedForegroundNames =
    {
        "explorer", "maslooptimizer", "taskmgr"
    };

    #endregion

    #region Focus Watcher (Zero-Allocation)

    private static CancellationTokenSource? _watcherCts;
    private static Task? _watcherTask;
    private static uint _activePid; // Кеш ActivePid

    public static bool IsWatcherRunning => _watcherTask != null;

    /// <summary>Запускає наглядач фокусу (інтервал 10 секунд).</summary>
    public static void StartFocusWatcher()
    {
        if (_watcherTask != null)
            return;

        var cts = new CancellationTokenSource();
        _watcherCts = cts;
        _watcherTask = WatchLoopAsync(cts.Token);
    }

    /// <summary>Зупиняє наглядач фокусу та звільняє ресурси.</summary>
    public static void StopFocusWatcher()
    {
        _watcherCts?.Cancel();
        try
        {
            _watcherTask?.Wait(TimeSpan.FromSeconds(3));
        }
        catch { }

        _watcherCts?.Dispose();
        _watcherTask?.Dispose();
        _watcherCts = null;
        _watcherTask = null;
        _activePid = 0;
    }

    private static async Task WatchLoopAsync(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                try
                {
                    BoostForegroundProcess();
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"Monitor: помилка наглядача фокусу: {ex.Message}", "WARN");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Очікуване завершення при StopFocusWatcher().
        }
    }

    /// <summary>
    /// Підвищує пріоритет процесу у фокусі до High (уникаючи RealTime).
    /// Zero-Allocation: жодного Process.GetProcessById().ProcessName — лише P/Invoke + ArrayPool.
    /// </summary>
    public static bool BoostForegroundProcess()
    {
        IntPtr hWnd = GetForegroundWindow();
        if (hWnd == IntPtr.Zero)
        {
            _activePid = 0;
            return false;
        }

        GetWindowThreadProcessId(hWnd, out uint pid);
        if (pid <= 4)
        {
            _activePid = 0;
            return false;
        }

        // Кеш ActivePid: аналіз виконуємо лише при зміні вікна фокусу.
        if (pid == _activePid)
            return false;

        _activePid = pid;

        IntPtr hProcess = OpenProcess(ProcessQueryInfo | ProcessSetInfo, false, pid);
        if (hProcess == IntPtr.Zero)
            return false;

        string? fileName = null;
        try
        {
            char[] buffer = ArrayPool<char>.Shared.Rent(1024);
            try
            {
                uint size = (uint)buffer.Length;
                if (!QueryFullProcessImageName(hProcess, 0, buffer, ref size) || size == 0)
                    return false;

                ReadOnlySpan<char> fullPath = buffer.AsSpan(0, (int)size);
                ReadOnlySpan<char> name = GetFileNamePart(fullPath);

                if (IsExcludedForeground(name))
                    return false;

                fileName = name.ToString();
            }
            finally
            {
                ArrayPool<char>.Shared.Return(buffer);
            }

            uint current = GetPriorityClass(hProcess);
            if (current == 0 || current == HighPriorityClass || current == RealTimePriorityClass)
                return false; // Вже High/RealTime — не чіпаємо

            if (!SetPriorityClass(hProcess, HighPriorityClass))
                return false;
        }
        finally
        {
            CloseHandle(hProcess);
        }

        AppLogger.Log($"Foreground Boost: процесу [{fileName} (PID {pid})] встановлено пріоритет High", "SUCCESS");
        return true;
    }

    private static ReadOnlySpan<char> GetFileNamePart(ReadOnlySpan<char> path)
    {
        int backSlash = path.LastIndexOf('\\');
        int forwardSlash = path.LastIndexOf('/');
        int idx = Math.Max(backSlash, forwardSlash);
        return idx >= 0 ? path.Slice(idx + 1) : path;
    }

    private static bool IsExcludedForeground(ReadOnlySpan<char> fileName)
    {
        foreach (string excluded in ExcludedForegroundNames)
        {
            if (fileName.IndexOf(excluded.AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    #endregion

    #region Session Hygiene

    /// <summary>Асинхронно виконує гігієну сесії (DNS-кеш + буфер обміну).</summary>
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
}
