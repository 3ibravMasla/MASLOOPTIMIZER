using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
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
    // Фаза 4.2: debounce історії 30 с (було 2 с) — жодних повних перезаписів JSON під час гри.
    private static readonly TimeSpan HistoryFlushDelay = TimeSpan.FromSeconds(30);

    // Текстовий лог пишеться на диск фоновим Task-циклом, а не напряму з Log(),
    // щоб жоден виклик логування не блокував UI або GameWatcher на File I/O.
    private static readonly TimeSpan LogFlushInterval = TimeSpan.FromSeconds(1);
    private static readonly ConcurrentQueue<string> _logQueue = new();
    private static CancellationTokenSource? _logFlushCts;

    // Файл логу тримається відкритим протягом усього життя програми:
    // жодного повторного open/close/flush кожну секунду.
    private static readonly TimeSpan LogDiskFlushInterval = TimeSpan.FromSeconds(15);
    // Фаза 4.3: у quiet-режимі (гра/GameMode) скидання на диск — не частіше ніж раз на 60 с.
    private static readonly TimeSpan QuietDiskFlushInterval = TimeSpan.FromSeconds(60);
    // Фаза 4.5: ротація app.log за розміром (5 МБ → app.log.1, один ротаційний файл).
    private const long LogRotationBytes = 5 * 1024 * 1024;
    private static readonly object _logIoLock = new();
    private static FileStream? _logFileStream;
    private static StreamWriter? _logWriter;
    private static DateTime _lastDiskFlushUtc = DateTime.UtcNow;
    private static volatile bool _diskDataPending;   // є незаписані на диск рядки
    private static volatile bool _logWindowOpen;     // Фаза 4.4: LogWindow реально відкрите

    public static ObservableCollection<LogEntry> LogEntries { get; } = new();
    private static readonly List<LogEntry> _historySnapshot = new();
    private static readonly object _lock = new();
    private static System.Threading.Timer? _historyFlushTimer;
    private static bool _historyDirty;

    // Фаза 4.3: адаптивний інтервал скидання логу на диск (quiet → 60 с).
    private static TimeSpan CurrentDiskFlushInterval =>
        AppActivityCoordinator.IsQuietMode ? QuietDiskFlushInterval : LogDiskFlushInterval;

    static AppLogger()
    {
        _historyFlushTimer = new System.Threading.Timer(_ => FlushHistory(), null, Timeout.Infinite, Timeout.Infinite);
        StartLogFlusher();
        LoadHistory();

        // Фаза 4.2: вихід із quiet-режиму розблоковує відкладену історію.
        AppActivityCoordinator.ModeChanged += OnActivityModeChanged;
    }

    private static void OnActivityModeChanged()
    {
        if (AppActivityCoordinator.IsQuietMode) return;

        lock (_lock)
        {
            if (_historyDirty)
            {
                _historyFlushTimer?.Change(HistoryFlushDelay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    public static void Log(string message, string level = "INFO")
    {
        string lvl = level.ToUpperInvariant();

        // Фаза 4.1 (таск 2.4): у quiet-режимі (гра/GameMode) INFO/SUCCESS/DEBUG повністю
        // пригнічуються; WARN/ERROR пишуться завжди (критичні події).
        bool quiet = AppActivityCoordinator.IsQuietMode;
        if (quiet && lvl != "WARN" && lvl != "ERROR")
        {
            return;
        }

        var entry = new LogEntry
        {
            Timestamp = DateTime.Now.ToString("HH:mm:ss"),
            Level = lvl,
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

        // Фаза 4.4: UI-пост лише коли LogWindow відкрите; у quiet-режимі UI не чіпаємо взагалі.
        if (_logWindowOpen && !quiet)
        {
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                LogEntries.Insert(0, entry);
                if (LogEntries.Count > 300)
                {
                    LogEntries.RemoveAt(LogEntries.Count - 1);
                }
            });
        }

        EnqueueLogLine(entry);

        // Фаза 4.2: історія в quiet-режимі відкладається (див. ScheduleHistoryFlush).
        ScheduleHistoryFlush();
    }

    /// <summary>
    /// Фаза 4.4: відкриття/закриття LogWindow керує UI-постами; при відкритті одразу
    /// показуємо актуальний in-memory снепшот історії (пости могли бути пропущені).
    /// </summary>
    public static void SetLogWindowOpen(bool open)
    {
        _logWindowOpen = open;
        if (!open) return;

        List<LogEntry> snapshot;
        lock (_lock)
        {
            snapshot = new List<LogEntry>(_historySnapshot);
        }

        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            LogEntries.Clear();
            foreach (var item in snapshot)
            {
                LogEntries.Add(item);
            }
        });
    }

    private static void EnqueueLogLine(LogEntry entry)
    {
        // Лише O(1) вставка в потокобезпечну чергу — без жодного File I/O тут.
        string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{entry.Level}] {entry.Message}{Environment.NewLine}";
        _logQueue.Enqueue(logLine);
    }

    private static void StartLogFlusher()
    {
        _logFlushCts = new CancellationTokenSource();
        CancellationToken token = _logFlushCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(LogFlushInterval, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                FlushLogQueue();
            }
        }, token);
    }

    private static void FlushLogQueue()
    {
        var batch = new StringBuilder();
        while (_logQueue.TryDequeue(out string? line))
        {
            batch.Append(line);
        }

        if (batch.Length > 0)
        {
            string text = batch.ToString();
            WriteToLogStream(text);

            // Фаза 4.3: критичні події (ERROR) скидаються на диск негайно.
            if (text.Contains("[ERROR]", StringComparison.Ordinal))
            {
                FlushLogStreamToDisk();
                return;
            }
        }

        // Фаза 4.5: перевірка ротації — дешева (лише метадані файлу).
        RotateLogIfNeeded();

        // Фаза 4.3: адаптивний flush — типово 15 с, у quiet-режимі 60 с,
        // і лише коли справді є незаписані рядки.
        if (_diskDataPending && DateTime.UtcNow - _lastDiskFlushUtc >= CurrentDiskFlushInterval)
        {
            FlushLogStreamToDisk();
        }
    }

    private static void WriteToLogStream(string text)
    {
        lock (_logIoLock)
        {
            try
            {
                EnsureLogWriter();
                _logWriter?.Write(text);
                _diskDataPending = true;
            }
            catch
            {
                // Пошкоджений/заблокований стрім скидається, наступна спроба перевідкриє файл.
                CloseLogStream();
            }
        }
    }

    private static void EnsureLogWriter()
    {
        if (_logWriter != null)
        {
            return;
        }

        AppPaths.EnsureDirectories();

        // Фаза 4.5: файл попередньої сесії понад ліміт — ротуємо перед відкриттям нового.
        RotateLogIfNeeded();

        // FileShare.Read дозволяє LogWindow та зовнішнім переглядачам читати файл під час запису.
        _logFileStream = new FileStream(
            AppPaths.AppLogFile,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096);

        _logWriter = new StreamWriter(_logFileStream, new UTF8Encoding(false), bufferSize: 4096)
        {
            AutoFlush = false
        };

        _lastDiskFlushUtc = DateTime.UtcNow;
    }

    private static void FlushLogStreamToDisk()
    {
        lock (_logIoLock)
        {
            try
            {
                _logWriter?.Flush();
                _logFileStream?.Flush(flushToDisk: true);
            }
            catch
            {
                CloseLogStream();
            }
            finally
            {
                _lastDiskFlushUtc = DateTime.UtcNow;
                _diskDataPending = false;
            }
        }
    }

    private static void CloseLogStream()
    {
        try { _logWriter?.Dispose(); } catch { }
        try { _logFileStream?.Dispose(); } catch { }
        _logWriter = null;
        _logFileStream = null;
    }

    /// <summary>
    /// Фаза 4.5: ротація app.log за розміром — понад 5 МБ файл переїжджає в app.log.1
    /// (лишається рівно один ротаційний файл). Виклик дешевий: перевірка метаданих.
    /// </summary>
    private static void RotateLogIfNeeded()
    {
        lock (_logIoLock)
        {
            try
            {
                long size = 0;
                if (_logFileStream != null)
                {
                    size = _logFileStream.Length;
                }
                else if (File.Exists(AppPaths.AppLogFile))
                {
                    size = new FileInfo(AppPaths.AppLogFile).Length;
                }

                if (size < LogRotationBytes) return;

                if (_logWriter != null || _logFileStream != null)
                {
                    try { _logWriter?.Flush(); _logFileStream?.Flush(flushToDisk: true); } catch { }
                    CloseLogStream();
                }

                string rotated = AppPaths.AppLogFile + ".1";
                if (File.Exists(rotated)) File.Delete(rotated);
                File.Move(AppPaths.AppLogFile, rotated);
                _lastDiskFlushUtc = DateTime.UtcNow;
                _diskDataPending = false;
            }
            catch { }
        }
    }

    /// <summary>
    /// Фінальне скидання логу при виході з програми: гасимо фоновий цикл,
    /// дописуємо залишок черги, скидаємо буфер на диск і закриваємо стрім.
    /// </summary>
    public static void Shutdown()
    {
        try { _logFlushCts?.Cancel(); } catch { }
        try { AppActivityCoordinator.ModeChanged -= OnActivityModeChanged; } catch { }

        lock (_logIoLock)
        {
            try
            {
                var batch = new StringBuilder();
                while (_logQueue.TryDequeue(out string? line))
                {
                    batch.Append(line);
                }

                if (batch.Length > 0)
                {
                    EnsureLogWriter();
                    _logWriter?.Write(batch.ToString());
                }

                if (_logWriter != null)
                {
                    _logWriter.Flush();
                    _logFileStream?.Flush(flushToDisk: true);
                }
            }
            catch { }
            finally
            {
                CloseLogStream();
            }
        }
    }

    private static void ScheduleHistoryFlush()
    {
        lock (_lock)
        {
            _historyDirty = true;

            // Фаза 4.2: у quiet-режимі повний перезапис history.json відкладається
            // до виходу з режиму — розблокує OnActivityModeChanged.
            if (AppActivityCoordinator.IsQuietMode) return;

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

            // Фаза 4.2: атомарний запис (.tmp + підміна) — файл історії ніколи не буває
            // порожнім/битим при аварійному завершенні.
            string tmp = AppPaths.HistoryLogFile + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            if (File.Exists(AppPaths.HistoryLogFile))
            {
                try { File.Replace(tmp, AppPaths.HistoryLogFile, null); }
                catch
                {
                    File.Copy(tmp, AppPaths.HistoryLogFile, overwrite: true);
                    File.Delete(tmp);
                }
            }
            else
            {
                File.Move(tmp, AppPaths.HistoryLogFile);
            }
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