using System;
using System.IO;
using System.Windows;

namespace MASLOOPTIMIZER;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. Глобальне перехоплення збоїв і створення краш-дампів
        RegisterCrashHandlers();

        // 2. Тихий запуск тільки HUD-віджета при автозавантаженні Windows
        if (e.Args.Length > 0 && e.Args[0].Equals("--widget", StringComparison.OrdinalIgnoreCase))
        {
            TrayManager.Initialize();
            TrayManager.ToggleWidget();
            return;
        }

        // 3. Звичайний запуск головного вікна програми
        var mainWindow = new MainWindow();
        mainWindow.Show();
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
            string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(logDir);

            string dumpPath = Path.Combine(logDir, $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
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