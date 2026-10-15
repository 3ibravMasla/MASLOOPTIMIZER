using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MASLOOPTIMIZER;

/// <summary>
/// Центральний координатор активності застосунку (Фаза 2 плану продуктивності):
/// єдине джерело правди про стан системи для всіх фонових споживачів
/// (віджет, трей, апдейтер, логер, конвеєри твіків).
///
/// Прапорці (а не взаємовиключні стани): WidgetVisible/Hidden, FullscreenGame,
/// GameModeActive, SessionLocked, OnBattery. Усередині — жодного таймера:
/// оновлення надходять виключно з подій (foreground-хук + SHQueryUserNotificationState,
/// GameModeEngine.OnGameModeStateChanged, SystemEvents.SessionSwitch/PowerModeChanged).
/// </summary>
public static class AppActivityCoordinator
{
    /// <summary>Прапорці активності; комбінуються (гра може йти на батареї з прихованим віджетом).</summary>
    [Flags]
    private enum Activity : byte
    {
        None = 0,
        WidgetVisible = 1,
        FullscreenGame = 2,
        GameModeActive = 4,
        SessionLocked = 8,
        OnBattery = 16
    }

    private static readonly object _lock = new();
    private static Activity _flags = Activity.None;
    private static bool _initialized;

    // Foreground-хук: дешева реакція на кожну зміну активного вікна.
    // Делегат тримаємо статично — інакше GC збере нативний бік і callback зникне.
    private static IntPtr _foregroundHook = IntPtr.Zero;
    private static WinEventDelegate? _foregroundHookProc;

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    // Фаза 9.2: PID вікна-джерела для анти-флуду повторних foreground-подій.
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    // QUNS_* значення, що трактуються як «повноекранна активність» (політика розділу 2 плану):
    // QUNS_BUSY — повноекранне відео/презентація (у т.ч. частина D3D-режимів),
    // QUNS_RUNNING_D3D_FULL_SCREEN — ексклюзивна повноекранна гра.
    private const int QUNS_BUSY = 2;
    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;

    /// <summary>
    /// Подія зміни будь-якого прапорця. Публікується з UI-диспетчера (джерела можуть бути
    /// фоновими); підписники самі читають актуальні властивості — параметрів подія не має.
    /// </summary>
    public static event Action? ModeChanged;

    /// <summary>
    /// Подія нового активного вікна (EVENT_SYSTEM_FOREGROUND) — єдиний фокус-хук застосунку
    /// живе тут (Фаза 7.1); GameModeEngine тощо лише підписуються. Викликається З ПОТОКУ
    /// нативного callback-а: підписники мусять бути швидкими і потокобезпечними.
    /// Параметр — PID вікна-джерела (0, якщо визначити не вдалося) для анти-флуду Фази 9.2.
    /// </summary>
    public static event Action<int>? ForegroundChanged;


    #region Публічний стан

    public static bool WidgetVisible => Has(Activity.WidgetVisible);
    public static bool FullscreenGame => Has(Activity.FullscreenGame);
    public static bool GameModeActive => Has(Activity.GameModeActive);
    public static bool SessionLocked => Has(Activity.SessionLocked);
    public static bool OnBattery => Has(Activity.OnBattery);

    /// <summary>«Тихий режим»: повноекранна гра або явний Game Mode — джерело політики затишшя.</summary>
    public static bool IsQuietMode => HasAny(Activity.FullscreenGame | Activity.GameModeActive);

    /// <summary>Чи зареєстровано foreground-хук координатора (діагностика Фази 7.1).</summary>
    public static bool IsForegroundHookRunning => _foregroundHook != IntPtr.Zero;

    // --- Фаза 9.4: сесійні лічильники діагностики («чому застосунок щось робить») ---
    private static long _telemetryTickCount;
    private static long _suppressedTickCount;
    private static long _foregroundEventCount;
    private static long _foregroundEventDropCount;
    private static long _quietGateWaitCount;

    /// <summary>Тіки телеметрії, фактично виконані віджетом за сесію.</summary>
    public static long TelemetryTickCount => Interlocked.Read(ref _telemetryTickCount);

    /// <summary>Тіки, пригнічені guard-ами віджета (quiet/прихований стан/повільний попередній тік).</summary>
    public static long SuppressedTickCount => Interlocked.Read(ref _suppressedTickCount);

    /// <summary>Усього foreground-подій від єдиного хука за сесію.</summary>
    public static long ForegroundEventCount => Interlocked.Read(ref _foregroundEventCount);

    /// <summary>Foreground-події, відкинуті анти-флудом за повторним PID (Фаза 9.2).</summary>
    public static long ForegroundEventDropCount => Interlocked.Read(ref _foregroundEventDropCount);

    /// <summary>Очікування виходу з quiet-режиму скан-конвеєрами та CLI-хелперами (Фаза 7.3).</summary>
    public static long QuietGateWaitCount => Interlocked.Read(ref _quietGateWaitCount);

    /// <summary>Викликає віджет на кожен фактично виконаний тік телеметрії (9.4).</summary>
    public static void RegisterTelemetryTick() => Interlocked.Increment(ref _telemetryTickCount);

    /// <summary>Викликає віджет, коли тік пропущено (quiet/прихований/попередній ще виконується) (9.4).</summary>
    public static void RegisterSuppressedTick() => Interlocked.Increment(ref _suppressedTickCount);

    /// <summary>Викликає GameModeEngine, коли foreground-подію відкинуто як дубль PID (9.2).</summary>
    public static void RegisterForegroundEventDrop() => Interlocked.Increment(ref _foregroundEventDropCount);

    private static void RegisterQuietGateWait() => Interlocked.Increment(ref _quietGateWaitCount);

    /// <summary>
    /// Інтервал телеметрії віджета за політикою розділу 2 плану.
    /// TimeSpan.Zero = пауза: прихований віджет, quiet-режим або заблокована сесія.
    /// </summary>
    public static TimeSpan TelemetryInterval
    {
        get
        {
            Activity flags;
            lock (_lock) { flags = _flags; }

            if ((flags & (Activity.FullscreenGame | Activity.GameModeActive | Activity.SessionLocked)) != 0)
                return TimeSpan.Zero;

            if ((flags & Activity.WidgetVisible) == 0)
                return TimeSpan.Zero;

            // Політика: на батареї — рідший тік; у звичайному видимому стані — 1.5–2 с.
            return (flags & Activity.OnBattery) != 0
                ? TimeSpan.FromSeconds(5)
                : TimeSpan.FromMilliseconds(1500);
        }
    }

    /// <summary>Короткий опис стану для журналу та діагностики (заготовка для Фази 9.4).</summary>
    public static string ModeDescription
    {
        get
        {
            lock (_lock) { return DescribeFlags(_flags); }
        }
    }

    #endregion

    #region Життєвий цикл

    /// <summary>
    /// Реєстрація джерел подій (хук + SystemEvents). Викликається один раз з UI-потоку:
    /// SetWinEventHook і SystemEvents вимагають повідомлювального насоса. Повторні виклики ігноруються.
    /// </summary>
    public static void Initialize()
    {
        lock (_lock)
        {
            if (_initialized) return;
            _initialized = true;
        }

        var app = System.Windows.Application.Current;
        if (app is not null && !app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.Invoke(InitializeCore);
            return;
        }

        InitializeCore();
    }

    /// <summary>Зняття хука і підписок SystemEvents; викликається з App.OnExit.</summary>
    public static void Shutdown()
    {
        lock (_lock)
        {
            if (!_initialized) return;
            _initialized = false;
        }

        try { SystemEvents.SessionSwitch -= OnSessionSwitch; } catch { }
        try { SystemEvents.PowerModeChanged -= OnPowerModeChanged; } catch { }

        if (_foregroundHook != IntPtr.Zero)
        {
            try { UnhookWinEvent(_foregroundHook); } catch { }
            _foregroundHook = IntPtr.Zero;
        }
        _foregroundHookProc = null;

        try { AppLogger.Log("Координатор активності: зупинено", "INFO"); } catch { }
    }

    private static void InitializeCore()
    {
        // 1. Foreground-хук: на кожну зміну активного вікна — дешевий QUNS-запит без polling.
        try
        {
            WinEventDelegate proc = OnForegroundWinEvent;
            _foregroundHookProc = proc; // статичне посилання — захист від GC
            _foregroundHook = SetWinEventHook(
                EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, proc, 0, 0, WINEVENT_OUTOFCONTEXT);

            if (_foregroundHook == IntPtr.Zero)
                AppLogger.Log("Координатор активності: foreground-хук не зареєстровано — детекція повноекранної гри недоступна", "WARN");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Координатор активності: помилка реєстрації хука: {ex.Message}", "WARN");
        }

        // 2. Сесія/живлення — SystemEvents (реєстрація лише з UI-потоку), статичні делегати.
        try
        {
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Координатор активності: помилка підписки на SystemEvents: {ex.Message}", "WARN");
        }

        // 3. Початковий стан — одноразово, без polling.
        RefreshFullscreenState();
        RefreshPowerState();

        try { AppLogger.Log($"Координатор активності: ініціалізовано ({ModeDescription})", "INFO"); } catch { }
    }

    #endregion

    #region Оновлення стану

    /// <summary>Позначає видимість HUD-віджета (викликає WidgetWindow на кожен Show/Hide).</summary>
    public static void SetWidgetVisible(bool visible) => SetFlag(Activity.WidgetVisible, visible);

    private static void SetFlag(Activity flag, bool value)
    {
        Activity before, after;
        lock (_lock)
        {
            before = _flags;
            _flags = value ? (_flags | flag) : (_flags & ~flag);
            after = _flags;
        }

        if (before == after) return;

        try { AppLogger.Log($"Координатор активності: {DescribeFlags(before)} → {DescribeFlags(after)}", "DEBUG"); } catch { }
        RaiseModeChanged();
    }

    private static void RaiseModeChanged()
    {
        var handler = ModeChanged;
        if (handler is null) return;

        var app = System.Windows.Application.Current;
        if (app is not null && !app.Dispatcher.CheckAccess())
        {
            // Джерела на кшталт GameModeEngine спрацьовують у фоновому потоці —
            // публікуємо з UI-диспетчера, щоб підписники не дбали про маршалинг.
            app.Dispatcher.BeginInvoke(new Action(() => InvokeSubscribers(handler)));
            return;
        }

        InvokeSubscribers(handler);
    }

    private static void InvokeSubscribers(Action handler)
    {
        // Винятки підписників не повинні ламати координатор (як і в GameModeEngine).
        try { handler(); }
        catch (Exception ex)
        {
            try { AppLogger.Log($"Координатор активності: помилка підписника ModeChanged: {ex.Message}", "WARN"); } catch { }
        }
    }

    #endregion

    #region Джерела подій

    // Виняток у нативному callback-потоці неприпустимий — тіло повністю захищене.
    private static void OnForegroundWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        Interlocked.Increment(ref _foregroundEventCount);
        try { RefreshFullscreenState(); } catch { }

        // PID вікна-джерела (0 — визначити не вдалося): потрібен анти-флуду 9.2,
        // щоб той самий PID у межах вікна не перезапускав дебаунс підписників.
        int pid = 0;
        try { GetWindowThreadProcessId(hwnd, out uint nativePid); pid = (int)nativePid; } catch { }

        // Фаза 7.1/7.4: підписники (GameModeEngine тощо) не мають ламати нативний callback —
        // кожен виклик ізольований: виняток у callback-потоці неприпустимий.
        var handler = ForegroundChanged;
        if (handler is null) return;

        foreach (Action<int> subscriber in handler.GetInvocationList())
        {
            try { subscriber(pid); }
            catch (Exception ex)
            {
                try { AppLogger.Log($"Координатор активності: помилка підписника ForegroundChanged: {ex.Message}", "WARN"); } catch { }
            }
        }
    }


    private static void RefreshFullscreenState()
    {
        bool fullscreen = false;
        try
        {
            // S_OK (0) — стан отримано; інакше лишаємо false (безпечний дефолт).
            if (SHQueryUserNotificationState(out int quns) == 0)
                fullscreen = quns == QUNS_RUNNING_D3D_FULL_SCREEN || quns == QUNS_BUSY;
        }
        catch { }

        SetFlag(Activity.FullscreenGame, fullscreen);
    }

    private static void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.ConsoleDisconnect:
            case SessionSwitchReason.RemoteDisconnect:
                SetFlag(Activity.SessionLocked, true);
                break;
            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.ConsoleConnect:
            case SessionSwitchReason.RemoteConnect:
                SetFlag(Activity.SessionLocked, false);
                break;
        }
    }

    private static void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        // Resume теж оновлює стан: після сну живлення могло змінитися (кабель/батарея).
        if (e.Mode == PowerModes.StatusChange || e.Mode == PowerModes.Resume)
            RefreshPowerState();
    }

    private static void RefreshPowerState()
    {
        try
        {
            SetFlag(Activity.OnBattery,
                System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline);
        }
        catch { }
    }

    #endregion

    #region Хелпери

    private static bool Has(Activity flag)
    {
        lock (_lock) { return (_flags & flag) != 0; }
    }

    private static bool HasAny(Activity mask)
    {
        lock (_lock) { return (_flags & mask) != 0; }
    }

    private static string DescribeFlags(Activity flags)
    {
        var parts = new List<string>
        {
            (flags & Activity.WidgetVisible) != 0 ? "Visible" : "Hidden"
        };

        if ((flags & Activity.FullscreenGame) != 0) parts.Add("FullscreenGame");
        if ((flags & Activity.GameModeActive) != 0) parts.Add("GameModeActive");
        if ((flags & Activity.SessionLocked) != 0) parts.Add("Locked");
        if ((flags & Activity.OnBattery) != 0) parts.Add("OnBattery");
        if ((flags & (Activity.FullscreenGame | Activity.GameModeActive)) != 0) parts.Add("Quiet");

        return string.Join("|", parts);
    }

    #endregion

    #region Очікування виходу з quiet-режиму (таск 7.3)

    /// <summary>
    /// Асинхронно чекає завершення quiet-режиму (гра/Game Mode). Без polling: прокидається
    /// від ModeChanged; скасування — через ct. Використовується конвеєрами твіків (7.3).
    /// </summary>
    public static async Task WaitWhileQuietAsync(CancellationToken ct = default)
    {
        if (!IsQuietMode) return;

        // Фаза 9.4: лічильник відкладених сканів (діагностика «чому застосунок мовчить»).
        RegisterQuietGateWait();

        while (IsQuietMode)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action onChanged = () => tcs.TrySetResult(true);
            ModeChanged += onChanged;
            try
            {
                // Повторна перевірка після підписки закриває гонку «режим змінився між перевіркою і підпискою».
                if (!IsQuietMode) continue;
                using (ct.Register(() => tcs.TrySetCanceled(ct)))
                    await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                ModeChanged -= onChanged;
            }
        }
    }

    /// <summary>
    /// Синхронний аналог для CLI-хелперів TweakEngine (виконуються у фонових задачах):
    /// чекає виходу з quiet-режиму, не торкаючись UI. false — вичерпано timeout.
    /// </summary>
    public static bool WaitUntilNotQuiet(TimeSpan timeout)
    {
        if (!IsQuietMode) return true;

        // Фаза 9.4: лічильник очікувань CLI-гейта (Фаза 7.3).
        RegisterQuietGateWait();
        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;

        while (IsQuietMode)
        {
            int remaining = (int)(deadline - Environment.TickCount64);
            if (remaining <= 0) return false;

            using var waiter = new ManualResetEventSlim(false);
            Action onChanged = () => waiter.Set();
            ModeChanged += onChanged;
            try
            {
                if (!IsQuietMode) return true;
                remaining = (int)(deadline - Environment.TickCount64);
                if (remaining <= 0) return false;
                waiter.Wait(remaining);
            }
            finally
            {
                ModeChanged -= onChanged;
            }
        }
        return true;
    }

    #endregion
}
