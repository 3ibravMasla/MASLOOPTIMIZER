using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
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
    // Пауза накопичення змін перед збереженням history.json (debounce),
    // щоб уникнути повного перезапису файлу на кожен виклик Log().
    private static readonly TimeSpan HistoryFlushDelay = TimeSpan.FromSeconds(2);

    public static ObservableCollection<LogEntry> LogEntries { get; } = new();
    private static readonly List<LogEntry> _historySnapshot = new();
    private static readonly object _lock = new();
    private static System.Threading.Timer? _historyFlushTimer;
    private static bool _historyDirty;

    static AppLogger()
    {
        _historyFlushTimer = new System.Threading.Timer(_ => FlushHistory(), null, Timeout.Infinite, Timeout.Infinite);
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
        ScheduleHistoryFlush();
    }

    private static void AppendToFile(LogEntry entry)
    {
        lock (_lock)
        {
            try
            {
                AppPaths.EnsureDirectories();

                // Текстовий лог — дешеве дописування в кінець файлу.
                string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{entry.Level}] {entry.Message}{Environment.NewLine}";
                File.AppendAllText(AppPaths.AppLogFile, logLine);
            }
            catch { }
        }
    }

    private static void ScheduleHistoryFlush()
    {
        lock (_lock)
        {
            _historyDirty = true;
            _historyFlushTimer?.Change(HistoryFlushDelay, Timeout.InfiniteTimeSpan);
        }
    }

    public static void FlushHistory()
    {
        List<LogEntry> snapshot;
        lock (_lock)
        {
            if (!_historyDirty) return;
            snapshot = new List<LogEntry>(_historySnapshot);
            _historyDirty = false;
        }

        try
        {
            AppPaths.EnsureDirectories();
            string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(AppPaths.HistoryLogFile, json);
        }
        catch { }
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
                _historyDirty = false;
                _historyFlushTimer?.Change(Timeout.Infinite, Timeout.Infinite);
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