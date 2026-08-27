using System;
using System.IO;
using System.Linq;
using System.Windows;
using Application = System.Windows.Application;

namespace MASLOOPTIMIZER;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. Глобальне перехоплення збоїв і створення краш-дампів
        RegisterCrashHandlers();

        // Режими запуску з командного рядка
        bool widgetOnly = HasArg(e.Args, "--widget", "--widget-only");
        bool silent = HasArg(e.Args, "-silent", "--silent");

        // 2. Тихий запуск тільки HUD-віджета при автозавантаженні Windows.
        //    Головне вікно НЕ створюється і не висить у фоні.
        if (widgetOnly)
        {
            TrayManager.Initialize();
            TrayManager.ToggleWidget();
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
        }
        else
        {
            mainWindow.Show();
        }
    }

    private static bool HasArg(string[] args, params string[] names)
    {
        return args.Any(a => names.Any(n => a.Equals(n, StringComparison.OrdinalIgnoreCase)));
    }

    private void RegisterCrashHandlers()
    {
        // Помилки потоку UI (WPF)
        DispatcherUnhandledException += (s, args) =>
        {
            GenerateCrashDump("UI_THREAD_EXCEPTION", args.Exception);
            args.Handled = true;
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