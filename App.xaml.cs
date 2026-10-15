using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Application = System.Windows.Application;

namespace MASLOOPTIMIZER;

public partial class App : System.Windows.Application
{
    // Single-instance guard: другий запуск (напр., випадковий повторний клік при автозапуску
    // віджета) тихо виходить, щоб не плодити треї, хуки, лічильники та фонові перевірки.
    private static Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1.0. Захист від подвійного запуску (per-session).
        _singleInstanceMutex = new Mutex(true, @"Local\MASLOOPTIMIZER_SINGLE_INSTANCE", out bool createdNew);
        if (!createdNew)
        {
            Shutdown(0);
            return;
        }

        // 1. Глобальне перехоплення збоїв і створення краш-дампів
        RegisterCrashHandlers();

        // 1.1 Локалізація ініціалізується ДО створення будь-яких вікон:
        //     політика мови запуску (RU→UA+lock, збережена мова, системна/EN).
        _ = LocalizationManager.Instance;

        // 1.2 Safe defaults: жодного наглядача фокусу та автоматичної гігієни сесії на старті.
        //     Гігієна (DNS + буфер обміну) доступна лише як ручна дія: MonitorEngine.RunSessionHygieneAsync().

        // 1.3 Центральний координатор активності (Фаза 2): єдине джерело правди про стан
        //     (видимий/прихований віджет, повноекранна гра, Game Mode, сесія, живлення).
        //     Реєстрація джерел подій — з UI-потоку; всередині жодних таймерів.
        AppActivityCoordinator.Initialize();

        // Режими запуску з командного рядка
        bool widgetOnly = HasArg(e.Args, "--widget", "--widget-only");
        bool silent = HasArg(e.Args, "-silent", "--silent");

        // 2. Тихий запуск тільки HUD-віджета при автозавантаженні Windows.
        //    Головне вікно НЕ створюється і не висить у фоні.
        if (widgetOnly)
        {
            TrayManager.Initialize();
            TrayManager.ToggleWidget();
            LogStartupReady("widget");
            return;
        }

        // 3. Тему застосовуємо до створення головного вікна
        ThemeEngine.ApplySavedAppTheme();

        var mainWindow = new MainWindow();

        if (silent)
        {
            // Повна програма, згорнута в трей: вікно створене, але не показане.
            TrayManager.Initialize();
            Application.Current.MainWindow = mainWindow;
            LogStartupReady("silent");
        }
        else
        {
            mainWindow.Show();
            LogStartupReady("ui");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Коректне звільнення ресурсів: зняття підписок зі статичних подій, зупинка таймерів,
        // диспозиція PerformanceCounter та NotifyIcon. Виконується навіть якщо вікна лише приховані.
        try { (Application.Current.MainWindow as MainWindow)?.DisposeResources(); } catch { }
        try { TrayManager.Shutdown(); } catch { }
        try { AppActivityCoordinator.Shutdown(); } catch { }

        AppLogger.Shutdown();
        AppLogger.FlushHistory();

        try { _singleInstanceMutex?.Dispose(); } catch { }
        base.OnExit(e);
    }

    private static bool HasArg(string[] args, params string[] names)
    {
        return args.Any(a => names.Any(n => a.Equals(n, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Фаза 6.2: маркер готовності старту для замірів холодного старту
    /// (Tools\MeasureColdStart.ps1) — час від старту процесу до готового трею/вікна.
    /// </summary>
    private static void LogStartupReady(string mode)
    {
        try
        {
            double ms = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
            AppLogger.Log($"Старт ({mode}): готовність за {ms:F0} мс [startup-ready]", "DEBUG");
        }
        catch { }
    }

    private void RegisterCrashHandlers()
    {
        // Помилки потоку UI (WPF)
        DispatcherUnhandledException += (s, args) =>
        {
            GenerateCrashDump("UI_THREAD_EXCEPTION", args.Exception);

            // Не ковтаємо помилку мовчки: повідомляємо користувача та коректно
            // завершуємо роботу, щоб додаток не лишився у зламаному стані.
            try
            {
                System.Windows.MessageBox.Show(
                    LocalizationManager.Instance.Format("Dialogs.CriticalError", args.Exception.Message),
                    LocalizationManager.Instance["Dialogs.CriticalErrorTitle"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch { }

            args.Handled = true;
            Shutdown(1);
        };

        // Загальні критичні помилки AppDomain
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                GenerateCrashDump("DOMAIN_UNHANDLED_EXCEPTION", ex);
            }
        };

        // Помилки у фонових асинхронних задачах (Task)
        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            GenerateCrashDump("ASYNC_TASK_EXCEPTION", args.Exception);
            args.SetObserved();
        };
    }

    private static void GenerateCrashDump(string source, Exception ex)
    {
        try
        {
            AppPaths.EnsureDirectories();
            string dumpPath = Path.Combine(AppPaths.Logs, $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            string dumpContent = $@"=====================================================
MASLOOPTIMIZER CRASH DUMP
Date/Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
Source: {source}
OS Version: {Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})
.NET Runtime: {Environment.Version}
=====================================================
Exception: {ex.GetType().FullName}
Message: {ex.Message}
Target Site: {ex.TargetSite}

StackTrace:
{ex.StackTrace}

InnerException:
{ex.InnerException?.ToString() ?? "None"}
=====================================================";

            File.WriteAllText(dumpPath, dumpContent);
        }
        catch { }
    }
}
