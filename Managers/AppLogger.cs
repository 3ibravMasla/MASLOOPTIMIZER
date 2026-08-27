using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace MASLOOPTIMIZER;

public class LogEntry
{
    [JsonPropertyName("Timestamp")]
    public string Timestamp { get; set; } = DateTime.Now.ToString("HH:mm:ss");

    [JsonPropertyName("Level")]
    public string Level { get; set; } = "INFO"; // INFO, WARN, ERROR, SUCCESS

    [JsonPropertyName("Message")]
    public string Message { get; set; } = string.Empty;

    [JsonIgnore]
    public string LevelColor => Level switch
    {
        "SUCCESS" => "#00FF9D",
        "WARN" => "#F59E0B",
        "ERROR" => "#EF4444",
        _ => "#94A3B8"
    };
}

public static class AppLogger
{
    public static ObservableCollection<LogEntry> LogEntries { get; } = new();
    private static readonly List<LogEntry> _historySnapshot = new();
    private static readonly object _lock = new();

    static AppLogger()
    {
        LoadHistory();
    }

    public static void Log(string message, string level = "INFO")
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now.ToString("HH:mm:ss"),
            Level = level.ToUpperInvariant(),
            Message = message
        };

        lock (_lock)
        {
            _historySnapshot.Insert(0, entry);
            if (_historySnapshot.Count > 300)
            {
                _historySnapshot.RemoveAt(_historySnapshot.Count - 1);
            }
        }

        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            LogEntries.Insert(0, entry);
            if (LogEntries.Count > 300)
            {
                LogEntries.RemoveAt(LogEntries.Count - 1);
            }
        });

        AppendToFile(entry);
    }

    private static void AppendToFile(LogEntry entry)
    {
        lock (_lock)
        {
            try
            {
                AppPaths.EnsureDirectories();

                // Текстовий лог
                string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{entry.Level}] {entry.Message}{Environment.NewLine}";
                File.AppendAllText(AppPaths.AppLogFile, logLine);

                // JSON історія з безпечного знімка
                string json = JsonSerializer.Serialize(_historySnapshot, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(AppPaths.HistoryLogFile, json);
            }
            catch { }
        }
    }

    public static void LoadHistory()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(AppPaths.HistoryLogFile))
                {
                    string json = File.ReadAllText(AppPaths.HistoryLogFile);
                    var items = JsonSerializer.Deserialize<List<LogEntry>>(json);
                    if (items != null)
                    {
                        _historySnapshot.Clear();
                        _historySnapshot.AddRange(items);

                        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                        {
                            LogEntries.Clear();
                            foreach (var item in items)
                            {
                                LogEntries.Add(item);
                            }
                        });
                    }
                }
            }
            catch { }
        }
    }

    public static void ClearHistory()
    {
        lock (_lock)
        {
            try
            {
                _historySnapshot.Clear();
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => LogEntries.Clear());

                if (File.Exists(AppPaths.HistoryLogFile))
                {
                    File.Delete(AppPaths.HistoryLogFile);
                }
            }
            catch { }
        }
    }
}