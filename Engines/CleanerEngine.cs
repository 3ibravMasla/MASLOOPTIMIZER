using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace MASLOOPTIMIZER;

public enum CleanerSortMode
{
    SizeDescending,
    SizeAscending,
    SafeFirst,
    NameAscending,
    Category
}

public class CleanerStats
{
    public long TotalBytesFound { get; set; }
    public long SafeBytesFound { get; set; }
    public int TotalItemsCount { get; set; }
    public int SafeItemsCount { get; set; }
    public string TotalSizeFormatted => FormatBytes(TotalBytesFound);
    public string SafeSizeFormatted => FormatBytes(SafeBytesFound);

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):N2} ГБ";
        if (bytes >= 1024 * 1024) return $"{bytes / (1024.0 * 1024):N2} МБ";
        if (bytes >= 1024) return $"{bytes / 1024.0:N2} КБ";
        return $"{bytes} Байт";
    }
}

public class CleanerItem : INotifyPropertyChanged
{
    public CleanerItem()
    {
        LocalizationManager.Instance.PropertyChanged += OnLocalizationChanged;
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(SizeFormatted));
        OnPropertyChanged(nameof(ActionButtonText));
        OnPropertyChanged(nameof(NameLocalized));
        OnPropertyChanged(nameof(DescriptionLocalized));
        OnPropertyChanged(nameof(CategoryLocalized));
        OnPropertyChanged(nameof(SafetyBadgeLocalized));
    }

    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Безпечний кеш";
    public string Description { get; set; } = string.Empty;
    public string Benefits { get; set; } = string.Empty;
    public string SideEffects { get; set; } = string.Empty;
    public bool IsSafeBatch { get; set; } = true;
    public bool IsDism { get; set; } = false;

    public List<string> TargetPaths { get; set; } = new();
    public Func<Task<long>>? CustomCleaner { get; set; }
    public Func<Task<long>>? CustomSizeCalculator { get; set; }

    /// <summary>Локалізована назва пункту очищення (Cleaner.Item.{Id}.Name).</summary>
    public string NameLocalized
        => LocalizationManager.Instance.TryGet($"Cleaner.Item.{Id}.Name", out var n) && !string.IsNullOrWhiteSpace(n)
            ? n
            : Name;

    /// <summary>Локалізований опис пункту очищення (Cleaner.Item.{Id}.Description).</summary>
    public string DescriptionLocalized
        => LocalizationManager.Instance.TryGet($"Cleaner.Item.{Id}.Description", out var d) && !string.IsNullOrWhiteSpace(d)
            ? d
            : Description;

    /// <summary>Локалізована категорія (Cleaner.Item.{Id}.Category або Categories.*).</summary>
    public string CategoryLocalized
    {
        get
        {
            if (LocalizationManager.Instance.TryGet($"Cleaner.Item.{Id}.Category", out var cat) && !string.IsNullOrWhiteSpace(cat))
            {
                return cat;
            }
            return LocalizationManager.Instance.TryGet($"Categories.{Category}", out var gen) && !string.IsNullOrWhiteSpace(gen)
                ? gen
                : Category;
        }
    }

    /// <summary>Локалізований бейдж безпеки (Безпечно / Ручний режим).</summary>
    public string SafetyBadgeLocalized => IsSafeBatch
        ? LocalizationManager.Instance["Cleaner.SafeBadge"]
        : LocalizationManager.Instance["Cleaner.ManualBadge"];

    private long _bytesFound;
    public long BytesFound
    {
        get => _bytesFound;
        set
        {
            if (_bytesFound != value)
            {
                _bytesFound = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SizeFormatted));
                OnPropertyChanged(nameof(StatusColor));
            }
        }
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy != value)
            {
                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActionButtonText));
            }
        }
    }

    public string SizeFormatted
    {
        get
        {
            var loc = LocalizationManager.Instance;
            if (IsDism) return loc["Common.SystemStorage"];
            if (BytesFound >= 1024 * 1024 * 1024) return $"{BytesFound / (1024.0 * 1024 * 1024):N2} {loc["Common.UnitGB"]}";
            if (BytesFound >= 1024 * 1024) return $"{BytesFound / (1024.0 * 1024):N2} {loc["Common.UnitMB"]}";
            if (BytesFound >= 1024) return $"{BytesFound / 1024.0:N2} {loc["Common.UnitKB"]}";
            return $"{BytesFound} {loc["Common.UnitBytes"]}";
        }
    }

    public string StatusColor => BytesFound > 0 ? "#38BDF8" : "#64748B";
    public string SafetyBadge => IsSafeBatch ? "🟢 БЕЗПЕЧНО" : "🟡 РУЧНИЙ РЕЖИМ";
    public string ActionButtonText => IsBusy
        ? LocalizationManager.Instance["Common.Cleaning"]
        : LocalizationManager.Instance["Cleaner.BtnClean"];

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class CleanerEngine
{
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static readonly string ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    private static readonly string WinDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static readonly string SysDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";

    // Білий список «не чіпати»: корінь диска, Windows, System32, Program Files, профіль, AppData.
    private static readonly HashSet<string> ProtectedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        SysDrive,
        WinDir,
        Environment.SystemDirectory,
        Path.GetDirectoryName(Environment.SystemDirectory) ?? WinDir,
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        ProgramData,
        AppData,
        LocalAppData,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
    };

    public static List<CleanerItem> Cleaners { get; } = new()
    {
        // =========================================================================
        // 1. GPU ТА ІГРОВІ КЕШІ (100% БЕЗПЕЧНО)
        // =========================================================================
        new()
        {
            Id = "clean_gpu_shaders",
            Name = "Кеш шейдерів GPU (DirectX / NVIDIA / AMD / Intel / Vulkan)",
            Category = "Ігри & GPU",
            IsSafeBatch = true,
            Description = "Очищає скомпільований кеш шейдерів графічного драйвера. Усуває мікрофризи та артефакти після оновлень драйверів.",
            Benefits = "Звільняє від 1 до 10+ ГБ на SSD та запобігає збоям старого графічного кешу.",
            SideEffects = "При першому запуску гри відбудеться швидка фонова перекомпіляція шейдерів.",
            TargetPaths = new()
            {
                Path.Combine(LocalAppData, "D3DSCache"),
                Path.Combine(LocalAppData, @"NVIDIA\DXCache"),
                Path.Combine(LocalAppData, @"NVIDIA\GLCache"),
                Path.Combine(LocalAppData, @"NVIDIA Corporation\NV_Cache"),
                Path.Combine(LocalAppData, @"AMD\DxCache"),
                Path.Combine(LocalAppData, @"AMD\GLCache"),
                Path.Combine(LocalAppData, @"Intel\ShaderCache"),
                Path.Combine(LocalAppData, "Vulkan")
            }
        },
        new()
        {
            Id = "clean_gpu_driver_installers",
            Name = "Залишки інсталяторів драйверів NVIDIA / AMD",
            Category = "Ігри & GPU",
            IsSafeBatch = true,
            Description = "Видаляє тимчасові розпаковані пакети встановлення відеодрайверів GPU.",
            Benefits = "Миттєве звільнення від 1 до 5+ ГБ дискового простору.",
            SideEffects = "Не впливає на поточний встановлений відеодрайвер.",
            TargetPaths = new()
            {
                Path.Combine(ProgramData, @"NVIDIA Corporation\Downloader"),
                Path.Combine(SysDrive, @"NVIDIA\DisplayDriver"),
                Path.Combine(SysDrive, "AMD")
            }
        },
        new()
        {
            Id = "clean_game_launchers_cache",
            Name = "Веб-кеш лаунчерів (Steam / Epic Games / Battle.net / EA / Ubisoft)",
            Category = "Ігри & GPU",
            IsSafeBatch = true,
            Description = "Очищає тимчасовий веб-кеш вбудованих браузерів у лаунчерах.",
            Benefits = "Прискорює інтерфейс магазинів, усуває зависання та чорні екрани.",
            SideEffects = "Не зачіпає встановлені ігри, збереження та авторизацію.",
            TargetPaths = new()
            {
                Path.Combine(LocalAppData, @"Steam\htmlcache"),
                Path.Combine(LocalAppData, @"EpicGamesLauncher\Saved\webcache"),
                Path.Combine(LocalAppData, @"Battle.net\Browser\Cache"),
                Path.Combine(LocalAppData, @"Electronic Arts\EA Desktop\Cache"),
                Path.Combine(LocalAppData, @"Ubisoft Game Launcher\cache")
            }
        },

        // =========================================================================
        // 2. БЕЗПЕЧНИЙ СИСТЕМНИЙ КЕШ (100% БЕЗПЕЧНО)
        // =========================================================================
        new()
        {
            Id = "clean_user_temp",
            Name = "Тимчасові файли користувача (%TEMP%)",
            Category = "Безпечний кеш",
            IsSafeBatch = true,
            Description = "Залишкові файли інсталяторів, тимчасові розпаковані архіви та кеш сеансу користувача.",
            Benefits = "Повністю безпечне звільнення системного розділу.",
            TargetPaths = new() { Path.GetTempPath() }
        },
        new()
        {
            Id = "clean_win_temp",
            Name = "Системний кеш Windows (C:\\Windows\\Temp)",
            Category = "Безпечний кеш",
            IsSafeBatch = true,
            Description = "Тимчасові системні файли фонових служб Windows.",
            Benefits = "Очищення залишків системних інсталяцій.",
            TargetPaths = new() { Path.Combine(WinDir, "Temp") }
        },
        new()
        {
            Id = "clean_wu_cache",
            Name = "Кеш завантажених оновлень (SoftwareDistribution)",
            Category = "Безпечний кеш",
            IsSafeBatch = true,
            Description = "Завантажені пакети Windows Update, які вже були успішно інстальовані в систему.",
            Benefits = "Звільняє до 3–10 ГБ на диску C:, виправляє помилки центру оновлень.",
            TargetPaths = new() { Path.Combine(WinDir, @"SoftwareDistribution\Download") }
        },
        new()
        {
            Id = "clean_cbs_logs",
            Name = "Журнали обслуговування компонентів (CBS & Setup Logs)",
            Category = "Безпечний кеш",
            IsSafeBatch = true,
            Description = "Застарілі журнали обслуговування компонентів після роботи SFC/DISM та установки апдейтів.",
            Benefits = "Звільняє від 500 МБ до 3+ ГБ системного простору.",
            TargetPaths = new()
            {
                Path.Combine(WinDir, @"Logs\CBS"),
                Path.Combine(WinDir, @"Logs\MoSetup"),
                Path.Combine(WinDir, "Panther")
            }
        },
        new()
        {
            Id = "clean_delivery_opt",
            Name = "Кеш оптимізації доставки (Delivery Optimization)",
            Category = "Безпечний кеш",
            IsSafeBatch = true,
            Description = "Кеш служби фонової роздачі файлів оновлень Windows по локальній мережі.",
            Benefits = "Звільняє прихований дисковий простір мережевої служби.",
            TargetPaths = new() { Path.Combine(WinDir, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache") }
        },
        new()
        {
            Id = "clean_recycle_bin",
            Name = "Кошик Windows (Recycle Bin на всіх дисках)",
            Category = "Безпечний кеш",
            IsSafeBatch = true,
            Description = "Остаточне очищення Кошика на всіх підключених дисках.",
            Benefits = "Повертає реальний вільний дисковий простір.",
            SideEffects = "Файли неможливо буде відновити засобами Провідника.",
            CustomSizeCalculator = async () => await Task.Run(GetRecycleBinTotalSize),
            CustomCleaner = async () => await Task.Run(EmptyRecycleBinNative)
        },

        // =========================================================================
        // 3. БРАУЗЕРИ ТА ДОДАТКИ (100% БЕЗПЕЧНО: ПАРОЛІ ТА СЕСІЇ НЕ ЧІПАЮТЬСЯ)
        // =========================================================================
        new()
        {
            Id = "clean_browser_cache",
            Name = "Кеш Chromium-браузерів (Chrome / Edge / Brave / Opera / Opera GX)",
            Category = "Браузери & Додатки",
            IsSafeBatch = true,
            Description = "Очищає виключно тимчасовий кеш медіа, скриптів та GPU. Зберігає всі паролі, історію та активні сесії.",
            Benefits = "Звільняє від 1 до 5+ ГБ пам'яті, відновлює швидкість рендерингу сторінок.",
            SideEffects = "При повторному відкритті сайти підвантажуватимуть картинки трохи довше.",
            TargetPaths = new()
            {
                Path.Combine(LocalAppData, @"Google\Chrome\User Data\Default\Cache"),
                Path.Combine(LocalAppData, @"Google\Chrome\User Data\Default\Code Cache"),
                Path.Combine(LocalAppData, @"Google\Chrome\User Data\Default\GPUCache"),
                Path.Combine(LocalAppData, @"Google\Chrome\User Data\Default\DawnCache"),
                Path.Combine(LocalAppData, @"Microsoft\Edge\User Data\Default\Cache"),
                Path.Combine(LocalAppData, @"Microsoft\Edge\User Data\Default\Code Cache"),
                Path.Combine(LocalAppData, @"Microsoft\Edge\User Data\Default\GPUCache"),
                Path.Combine(LocalAppData, @"BraveSoftware\Brave-Browser\User Data\Default\Cache"),
                Path.Combine(LocalAppData, @"BraveSoftware\Brave-Browser\User Data\Default\Code Cache"),
                Path.Combine(LocalAppData, @"Opera Software\Opera Stable\Cache"),
                Path.Combine(LocalAppData, @"Opera Software\Opera GX Stable\Cache")
            }
        },
        new()
        {
            Id = "clean_messengers_media",
            Name = "Кеш месенджерів (Discord / Telegram / Spotify)",
            Category = "Браузери & Додатки",
            IsSafeBatch = true,
            Description = "Очищає тимчасові кеші аудіо, зображень та потокового відео.",
            Benefits = "Звільняє до 3–8 ГБ прихованого кешу Electron-додатків.",
            SideEffects = "Не скидає активну авторизацію у ваших акаунтах.",
            TargetPaths = new()
            {
                Path.Combine(AppData, @"discord\Cache"),
                Path.Combine(AppData, @"discord\Code Cache"),
                Path.Combine(AppData, @"discord\GPUCache"),
                Path.Combine(AppData, @"Telegram Desktop\tdata\user_data\media_cache"),
                Path.Combine(AppData, @"Telegram Desktop\tdata\user_data\file_dumps"),
                Path.Combine(LocalAppData, @"Spotify\Data"),
                Path.Combine(LocalAppData, @"Spotify\Storage")
            }
        },
        new()
        {
            Id = "clean_store_winget",
            Name = "Кеш Microsoft Store та WinGet",
            Category = "Браузери & Додатки",
            IsSafeBatch = true,
            Description = "Очищає завантажені інсталяційні пакети та тимчасові каталоги магазину і пакетного менеджера.",
            Benefits = "Усуває помилки під час встановлення та оновлення софту.",
            TargetPaths = new()
            {
                Path.Combine(LocalAppData, @"Packages\Microsoft.WindowsStore_8wekyb3d8bbwe\LocalCache"),
                Path.Combine(LocalAppData, @"Microsoft\WinGet\Packages")
            }
        },

        // =========================================================================
        // 4. ДАМПИ, ЖУРНАЛИ ТА ДІАГНОСТИКА (РУЧНИЙ РЕЖИМ)
        // =========================================================================
        new()
        {
            Id = "clean_crash_dumps",
            Name = "Дампи збоїв пам'яті (BSoD Minidump & Memory.dmp)",
            Category = "Дампи & Логи (Manual)",
            IsSafeBatch = false,
            Description = "Аварійні знімки пам'яті після Синіх екранів (BSoD) та звіти про падіння програм.",
            Benefits = "Звільняє великі файли дампів від 1 до 16+ ГБ.",
            SideEffects = "Аналіз причин минулих збоїв утилітою WinDbg стане неможливим.",
            TargetPaths = new()
            {
                Path.Combine(LocalAppData, "CrashDumps"),
                Path.Combine(WinDir, "Minidump"),
                Path.Combine(WinDir, "MEMORY.DMP"),
                Path.Combine(ProgramData, @"Microsoft\Windows\WER\ReportArchive"),
                Path.Combine(ProgramData, @"Microsoft\Windows\WER\Temp"),
                Path.Combine(LocalAppData, @"Microsoft\Windows\WER\ReportArchive")
            }
        },
        new()
        {
            Id = "clean_event_logs",
            Name = "Системні журнали подій Windows (Event Logs)",
            Category = "Дампи & Логи (Manual)",
            IsSafeBatch = false,
            Description = "Очищення системних журналів подій через нативний API (крім журналу безпеки Security).",
            Benefits = "Конфіденційність дій та скидання накопичених логів помилок.",
            SideEffects = "Історія в «Переглядачі подій» (Event Viewer) буде очищена; журнал безпеки (Security) зберігається для аудиту.",
            TargetPaths = new() { Path.Combine(WinDir, @"System32\winevt\Logs") },
            CustomCleaner = async () => await Task.Run(ClearAllEventLogsNative)
        },
        new()
        {
            Id = "clean_explorer_thumbs",
            Name = "Кеш ескізів Провідника (Thumbnails Cache)",
            Category = "Дампи & Логи (Manual)",
            IsSafeBatch = false,
            Description = "Бази даних збережених мініатюр та значків (thumbcache_*.db, iconcache_*.db).",
            Benefits = "Звільняє місце на SSD та виправляє відображення пошкоджених значків.",
            SideEffects = "Провідник не перезапускається; зайняті файли буде пропущено. Ескізи створюватимуться заново.",
            TargetPaths = new() { Path.Combine(LocalAppData, @"Microsoft\Windows\Explorer") },
            CustomSizeCalculator = async () => await Task.Run(GetThumbnailCacheSize),
            CustomCleaner = async () => await Task.Run(CleanThumbnailCache)
        },

        // =========================================================================
        // 5. ДИСКОВИЙ МОДУЛЬ DISM WINSXS
        // =========================================================================
        new()
        {
            Id = "clean_dism_winsxs",
            Name = "Глибоке стиснення WinSxS (DISM Component Cleanup)",
            Category = "Системне стиснення",
            IsSafeBatch = false,
            IsDism = true,
            Description = "Стиснення та видалення застарілих версій системних компонентів у сховищі WinSxS.",
            Benefits = "Повертає від 2 до 8+ ГБ вільного місця на диску C:.",
            SideEffects = "Процес триває 1–3 хвилини з навантаженням на процесор.",
            CustomCleaner = async () => await Task.Run(RunDismCleanup)
        }
    };

    #region Контекстне сортування, фільтрація та статистика

    public static IEnumerable<CleanerItem> GetFilteredAndSortedItems(
        string? category = null,
        string? searchQuery = null,
        CleanerSortMode sortMode = CleanerSortMode.SizeDescending,
        bool safeOnly = false)
    {
        var query = Cleaners.AsEnumerable();

        if (safeOnly)
        {
            query = query.Where(c => c.IsSafeBatch);
        }

        if (!string.IsNullOrWhiteSpace(category) && !category.Equals("Всі", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(c => string.Equals(c.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            string q = searchQuery.Trim();
            query = query.Where(c =>
                c.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                c.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                c.Benefits.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                c.Category.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        return sortMode switch
        {
            CleanerSortMode.SizeDescending => query.OrderByDescending(c => c.BytesFound),
            CleanerSortMode.SizeAscending => query.OrderBy(c => c.BytesFound),
            CleanerSortMode.SafeFirst => query.OrderByDescending(c => c.IsSafeBatch).ThenByDescending(c => c.BytesFound),
            CleanerSortMode.NameAscending => query.OrderBy(c => c.Name),
            CleanerSortMode.Category => query.OrderBy(c => c.Category).ThenByDescending(c => c.BytesFound),
            _ => query.OrderByDescending(c => c.BytesFound)
        };
    }

    public static List<string> GetCategories()
    {
        var categories = Cleaners.Select(c => c.Category).Distinct().OrderBy(c => c).ToList();
        categories.Insert(0, "Всі");
        return categories;
    }

    public static CleanerStats GetStatistics()
    {
        long totalFound = Cleaners.Sum(c => c.BytesFound);
        long safeFound = Cleaners.Where(c => c.IsSafeBatch).Sum(c => c.BytesFound);

        return new CleanerStats
        {
            TotalBytesFound = totalFound,
            SafeBytesFound = safeFound,
            TotalItemsCount = Cleaners.Count,
            SafeItemsCount = Cleaners.Count(c => c.IsSafeBatch)
        };
    }

    #endregion

    #region Публічні методи виконання

    public static async Task CalculateSizesAsync()
    {
        var results = new ConcurrentDictionary<CleanerItem, long>();

        await Parallel.ForEachAsync(Cleaners, async (item, token) =>
        {
            if (item.IsDism)
            {
                results[item] = 0;
                return;
            }

            if (item.CustomSizeCalculator != null)
            {
                results[item] = await item.CustomSizeCalculator();
                return;
            }

            long total = 0;
            foreach (var path in item.TargetPaths)
            {
                total += FastGetDirectorySize(path);
            }
            results[item] = total;
        });

        // Оновлення UI-залежних властивостей — лише на UI-потоці (D-10)
        RunOnUi(() =>
        {
            foreach (var kv in results)
                kv.Key.BytesFound = kv.Value;
        });
    }

    public static async Task<long> CleanItemAsync(CleanerItem item)
    {
        RunOnUi(() => item.IsBusy = true);
        long freed = 0;

        try
        {
            if (item.CustomCleaner != null)
            {
                freed = await item.CustomCleaner();
            }
            else
            {
                freed = await Task.Run(() =>
                {
                    long localFreed = 0;
                    foreach (var path in item.TargetPaths)
                    {
                        localFreed += FastDeleteDirectoryContents(path);
                    }
                    return localFreed;
                });
            }

            AppLogger.Log($"Очищено: {item.Name} (+{FormatBytes(freed)})", "SUCCESS");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Помилка очищення {item.Name}: {ex.Message}", "ERROR");
        }

        RunOnUi(() =>
        {
            item.BytesFound = 0;
            item.IsBusy = false;
        });
        return freed;
    }

    // Виконання на UI-потоці лише за потреби; без Dispatcher — інлайн (тести/CLI).
    private static void RunOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return;
        }
        dispatcher.Invoke(action);
    }

    // Захист від випадкового видалення кореня диска або системних каталогів.
    private static bool IsPathSafeForCleanup(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        string full;
        try
        {
            full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return false;
        }

        if (full.Length == 0) return false;

        string root = (Path.GetPathRoot(full) ?? string.Empty)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (full.Length <= root.Length) return false; // корінь диска ("C:\")

        foreach (var protectedPath in ProtectedPaths)
        {
            if (string.IsNullOrWhiteSpace(protectedPath)) continue;

            string pFull;
            try
            {
                pFull = Path.GetFullPath(protectedPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                continue;
            }

            if (string.Equals(full, pFull, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    public static async Task<long> CleanAllSafeAsync(IProgress<(int Percent, string Name, long Freed)>? progress = null)
    {
        var safeItems = Cleaners.Where(c => c.IsSafeBatch).ToList();
        long totalFreed = 0;
        int idx = 0;

        foreach (var item in safeItems)
        {
            idx++;
            int pct = (int)((idx / (double)safeItems.Count) * 100);
            long freed = await CleanItemAsync(item);
            totalFreed += freed;
            progress?.Report((pct, item.Name, freed));
            await Task.Delay(20);
        }

        return totalFreed;
    }

    #endregion

    #region Швидкісний алгоритм дискових операцій (Zero Crash Walker)

    private static long FastGetDirectorySize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return 0;
        if (File.Exists(path))
        {
            try
            {
                var fi = new FileInfo(path);
                return (fi.Attributes & FileAttributes.ReparsePoint) == 0 ? fi.Length : 0;
            }
            catch { return 0; }
        }
        if (!Directory.Exists(path)) return 0;

        long total = 0;
        var stack = new Stack<string>();
        stack.Push(path);

        while (stack.Count > 0)
        {
            string current = stack.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(current))
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        if ((fi.Attributes & FileAttributes.ReparsePoint) == 0)
                            total += fi.Length;
                    }
                    catch { }
                }

                foreach (var dir in Directory.EnumerateDirectories(current))
                {
                    try
                    {
                        var di = new DirectoryInfo(dir);
                        if ((di.Attributes & FileAttributes.ReparsePoint) == 0)
                        {
                            stack.Push(dir);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        return total;
    }

    private static long FastDeleteDirectoryContents(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return 0;
        if (!IsPathSafeForCleanup(path)) return 0;

        if (File.Exists(path))
        {
            try
            {
                var fi = new FileInfo(path);
                if ((fi.Attributes & FileAttributes.ReparsePoint) != 0) return 0;

                long len = fi.Length;
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
                return len;
            }
            catch { return 0; }
        }
        if (!Directory.Exists(path)) return 0;

        long freed = 0;
        try
        {
            var dir = new DirectoryInfo(path);
            foreach (var file in dir.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    if ((file.Attributes & FileAttributes.ReparsePoint) != 0) continue;

                    long len = file.Length;
                    file.Attributes = FileAttributes.Normal;
                    file.Delete();
                    freed += len;
                }
                catch { }
            }

            foreach (var sub in dir.EnumerateDirectories("*", SearchOption.TopDirectoryOnly).ToList())
            {
                try
                {
                    if ((sub.Attributes & FileAttributes.ReparsePoint) == 0)
                    {
                        freed += FastDeleteDirectoryContents(sub.FullName);
                        sub.Delete(true);
                    }
                }
                catch
                {
                    try { sub.Delete(false); } catch { }
                }
            }
        }
        catch { }

        return freed;
    }

    #endregion

    #region Нативні обробники (Recycle Bin, EventLog, DISM, Thumbs)

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    private const uint SHERB_NOCONFIRMATION = 0x00000001;
    private const uint SHERB_NOPROGRESSUI = 0x00000002;
    private const uint SHERB_NOSOUND = 0x00000004;

    private static long GetRecycleBinTotalSize()
    {
        long total = 0;
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO)) };
                if (SHQueryRecycleBin(drive.Name, ref info) == 0)
                {
                    total += info.i64Size;
                }
            }
        }
        catch { }
        return total;
    }

    private static long EmptyRecycleBinNative()
    {
        long before = GetRecycleBinTotalSize();
        uint hresult = 0xFFFFFFFF;
        try
        {
            hresult = SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Помилка очищення Кошика: {ex.Message}", "ERROR");
        }

        long after = GetRecycleBinTotalSize();
        long freed = Math.Max(0, before - after);

        if (hresult != 0)
        {
            AppLogger.Log($"SHEmptyRecycleBin повернула HRESULT 0x{hresult:X8}", "WARN");
        }

        return freed;
    }

    private static long ClearAllEventLogsNative()
    {
        string logsDir = Path.Combine(WinDir, @"System32\winevt\Logs");
        long initialSize = FastGetDirectorySize(logsDir);
        int cleared = 0, skipped = 0;

        try
        {
            using var session = new EventLogSession();
            foreach (var name in session.GetLogNames())
            {
                if (name.Equals("Security", StringComparison.OrdinalIgnoreCase))
                {
                    skipped++; // D-40: не знищуємо аудит-трейл безпеки
                    continue;
                }

                try { session.ClearLog(name); cleared++; } catch { }
            }
        }
        catch { }

        long finalSize = FastGetDirectorySize(logsDir);
        long freed = Math.Max(0, initialSize - finalSize);
        AppLogger.Log($"Журнали подій: очищено {cleared}, пропущено {skipped} (Security)", "INFO");
        return freed;
    }

    private static long GetThumbnailCacheSize()
    {
        string dir = Path.Combine(LocalAppData, @"Microsoft\Windows\Explorer");
        if (!Directory.Exists(dir)) return 0;

        long total = 0;
        try
        {
            var di = new DirectoryInfo(dir);
            foreach (var pattern in new[] { "thumbcache_*.db", "iconcache_*.db" })
            {
                foreach (var file in di.EnumerateFiles(pattern, SearchOption.TopDirectoryOnly))
                {
                    try { total += file.Length; } catch { }
                }
            }
        }
        catch { }
        return total;
    }

    private static long CleanThumbnailCache()
    {
        string dir = Path.Combine(LocalAppData, @"Microsoft\Windows\Explorer");
        if (!Directory.Exists(dir)) return 0;

        long freed = 0;
        try
        {
            var di = new DirectoryInfo(dir);
            var targets = di.EnumerateFiles("thumbcache_*.db", SearchOption.TopDirectoryOnly)
                            .Concat(di.EnumerateFiles("iconcache_*.db", SearchOption.TopDirectoryOnly));

            foreach (var file in targets)
            {
                try
                {
                    long sz = file.Length;
                    file.Attributes = FileAttributes.Normal;
                    file.Delete();
                    freed += sz;
                }
                catch
                {
                    // Файл зайнятий Провідником — пропускаємо без вбивства explorer.exe (D-41).
                }
            }
        }
        catch { }

        return freed;
    }

    private static long RunDismCleanup()
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dism.exe",
                    Arguments = "/online /cleanup-image /startcomponentcleanup /norestart",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            };

            if (!proc.Start())
            {
                AppLogger.Log("DISM: не вдалося запустити dism.exe", "ERROR");
                return 0;
            }

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            const int timeoutMs = 10 * 60 * 1000; // 10 хвилин
            if (!proc.WaitForExit(timeoutMs))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                AppLogger.Log("DISM: таймаут (10 хв) — процес примусово зупинено", "ERROR");
                return 0;
            }

            string stdout = stdoutTask.GetAwaiter().GetResult();
            string stderr = stderrTask.GetAwaiter().GetResult();

            if (proc.ExitCode == 0)
            {
                AppLogger.Log("DISM Component Cleanup завершено успішно", "SUCCESS");
            }
            else if (proc.ExitCode == 740)
            {
                AppLogger.Log("DISM: потрібні права адміністратора (код 740)", "ERROR");
            }
            else
            {
                string detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                AppLogger.Log($"DISM: помилка (код {proc.ExitCode}): {detail.Trim()}", "ERROR");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"DISM: виняток — {ex.Message}", "ERROR");
        }

        return 0;
    }

    #endregion

    // Форматування розміту у людському форматі для логування
    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):N2} ГБ";
        if (bytes >= 1024 * 1024) return $"{bytes / (1024.0 * 1024):N2} МБ";
        if (bytes >= 1024) return $"{bytes / 1024.0:N2} КБ";
        return $"{bytes} Байт";
    }
}