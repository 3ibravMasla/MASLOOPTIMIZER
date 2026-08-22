using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Application = System.Windows.Application;

namespace MASLOOPTIMIZER;

public class LogEntry
{
    public string Time { get; set; } = DateTime.Now.ToString("HH:mm:ss");
    public string Level { get; set; } = "INFO";
    public string Message { get; set; } = string.Empty;
    public string LevelColor => Level switch
    {
        "SUCCESS" => "#107C41",
        "WARN" => "#D87A00",
        "ERROR" => "#C42B1C",
        _ => "#0078D4"
    };
}

public static class AppLogger
{
    public static ObservableCollection<LogEntry> SessionLogs { get; } = new();
    private static readonly string LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
    private static readonly string LogFilePath = Path.Combine(LogDirectory, $"optimizer_{DateTime.Now:yyyy-MM-dd}.log");

    public static void Log(string message, string level = "INFO")
    {
        var entry = new LogEntry
        {
            Time = DateTime.Now.ToString("HH:mm:ss"),
            Level = level.ToUpper(),
            Message = message
        };

        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            SessionLogs.Add(entry);
            if (SessionLogs.Count > 500) SessionLogs.RemoveAt(0);
        });

        try
        {
            if (!Directory.Exists(LogDirectory)) Directory.CreateDirectory(LogDirectory);
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{entry.Level}] {entry.Message}{Environment.NewLine}";
            File.AppendAllText(LogFilePath, line);
        }
        catch { }
    }
}