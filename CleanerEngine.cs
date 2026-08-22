using System;
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

public class CleanerItem : INotifyPropertyChanged
{
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

    private long _bytesFound;
    public long BytesFound
    {
        get => _bytesFound;
        set
        {
            _bytesFound = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SizeFormatted));
        }
    }

    public string SizeFormatted
    {
        get
        {
            if (IsDism) return "Системне сховище";
            if (BytesFound >= 1024 * 1024 * 1024) return $"{BytesFound / (1024.0 * 1024 * 1024):N2} ГБ";
            if (BytesFound >= 1024 * 1024) return $"{BytesFound / (1024.0 * 1024):N2} МБ";
            if (BytesFound >= 1024) return $"{BytesFound / 1024.0:N2} КБ";
            return $"{BytesFound} Байт";
        }
    }

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

    public static List<CleanerItem> Cleaners { get; } = new()
    {
        // =========================================================================
        // 1. GPU ТА ІГРОВІ КЕШІ
        // =========================================================================
        new()
        {
            Id = "clean_gpu_shaders",
            Name = "Кеш шейдерів GPU (DirectX / NVIDIA / AMD / Intel / Vulkan)",
            Category = "Ігри & GPU",
            IsSafeBatch = true,
            Description = "Очищає скомпільований кеш шейдерів. Усуває мікрофризи та артефакти після оновлення драйверів.",
            Benefits = "Звільняє від 1 до 10+ ГБ на SSD та запобігає конфліктам старих шейдерів.",
            SideEffects = "При першому запуску гри відбудеться коротка фонова компіляція свіжих шейдерів.",
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
            Description = "Видаляє тимчасові розпаковані пакети встановлення драйверів GPU.",
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
            Name = "Веб-кеш лаунчерів (Steam / Epic Games / Battle.net)",
            Category = "Ігри & GPU",
            IsSafeBatch = true,
            Description = "Очищає тимчасовий веб-кеш вбудованих браузерів у лаунчерах.",
            Benefits = "Прискорює інтерфейс магазинів, усуває зависання та чорні екрани.",
            SideEffects = "Не зачіпає встановлені ігри, збереження та авторизацію.",
            TargetPaths = new()
            {
                Path.Combine(LocalAppData, @"Steam\htmlcache"),
                Path.Combine(LocalAppData, @"EpicGamesLauncher\Saved\webcache"),
                Path.Combine(LocalAppData, @"Battle.net\Browser\Cache")
            }
        },

        // =========================================================================
        // 2. БЕЗПЕЧНИЙ СИСТЕМНИЙ КЕШ
        // =========================================================================
        new()
        {
            Id = "clean_user_temp",
            Name = "Тимчасові файли користувача (%TEMP%)",
            Category = "Безпечний кеш",
            IsSafeBatch = true,
            Description = "Залишкові файли інсталяторів, розпаковані архіви та кеш поточного сеансу.",
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
            Benefits = "Очищення залишків системних операцій.",
            TargetPaths = new() { Path.Combine(WinDir, "Temp") }
        },
        new()
        {
            Id = "clean_wu_cache",
            Name = "Кеш завантажених оновлень Windows (SoftwareDistribution)",
            Category = "Безпечний кеш",
            IsSafeBatch = true,
            Description = "Завантажені пакети Windows Update, які вже були успішно інстальовані.",
            Benefits = "Звільняє до 3–10 ГБ на диску C:, виправляє помилки центру оновлень.",
            TargetPaths = new() { Path.Combine(WinDir, @"SoftwareDistribution\Download") }
        },
        new()
        {
            Id = "clean_cbs_logs",
            Name = "Журнали обслуговування компонентів (CBS Logs)",
            Category = "Безпечний кеш",
            IsSafeBatch = true,
            Description = "Застарілі журнали обслуговування компонентів після роботи SFC/DISM.",
            Benefits = "Звільняє від 500 МБ до 2+ ГБ системного простору.",
            TargetPaths = new() { Path.Combine(WinDir, @"Logs\CBS") }
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
            Name = "Кошик Windows (Recycle Bin)",
            Category = "Безпечний кеш",
            IsSafeBatch = true,
            Description = "Остаточне очищення Кошика на всіх активних дисках.",
            Benefits = "Повертає реальний вільний дисковий простір.",
            SideEffects = "Файли неможливо буде відновити засобами Провідника.",
            CustomSizeCalculator = async () => await Task.Run(GetRecycleBinTotalSize),
            CustomCleaner = async () => await Task.Run(EmptyRecycleBinNative)
        },

        // =========================================================================
        // 3. БРАУЗЕРИ ТА ДОДАТКИ
        // =========================================================================
        new()
        {
            Id = "clean_browser_cache",
            Name = "Кеш браузерів (Chrome / Edge / Brave / Opera / Firefox)",
            Category = "Браузери & Додатки",
            IsSafeBatch = true,
            Description = "Очищає виключно тимчасовий кеш медіа та скриптів. Зберігає всі паролі, історію та сесії.",
            Benefits = "Звільняє від 1 до 5 ГБ пам'яті, відновлює швидкість браузерів.",
            SideEffects = "При повторному відкритті сайти підвантажуватимуть картинки трохи довше.",
            TargetPaths = new()
            {
                Path.Combine(LocalAppData, @"Google\Chrome\User Data\Default\Cache"),
                Path.Combine(LocalAppData, @"Google\Chrome\User Data\Default\Code Cache"),
                Path.Combine(LocalAppData, @"Google\Chrome\User Data\Default\GPUCache"),
                Path.Combine(LocalAppData, @"Microsoft\Edge\User Data\Default\Cache"),
                Path.Combine(LocalAppData, @"Microsoft\Edge\User Data\Default\Code Cache"),
                Path.Combine(LocalAppData, @"Microsoft\Edge\User Data\Default\GPUCache"),
                Path.Combine(LocalAppData, @"BraveSoftware\Brave-Browser\User Data\Default\Cache"),
                Path.Combine(LocalAppData, @"BraveSoftware\Brave-Browser\User Data\Default\Code Cache"),
                Path.Combine(LocalAppData, @"Mozilla\Firefox\Profiles")
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
            Description = "Очищає завантажені пакети та тимчасові каталоги магазину і пакетного менеджера.",
            Benefits = "Усуває помилки під час встановлення та оновлення програм.",
            TargetPaths = new()
            {
                Path.Combine(LocalAppData, @"Packages\Microsoft.WindowsStore_8wekyb3d8bbwe\LocalCache"),
                Path.Combine(LocalAppData, @"Microsoft\WinGet\Packages")
            }
        },

        // =========================================================================
        // 4. ДАМПИ, ЖУРНАЛИ ТА ДІАГНОСТИКА (MANUAL ONLY)
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
                Path.Combine(LocalAppData, @"Microsoft\Windows\WER\ReportArchive")
            }
        },
        new()
        {
            Id = "clean_event_logs",
            Name = "Системні журнали подій Windows (Event Logs)",
            Category = "Дампи & Логи (Manual)",
            IsSafeBatch = false,
            Description = "Повне очищення всіх системних журналів подій через нативний API.",
            Benefits = "Конфіденційність дій та скидання накопичених логів помилок.",
            SideEffects = "Історія в утиліті «Переглядач подій» (Event Viewer) буде очищена.",
            TargetPaths = new() { Path.Combine(WinDir, @"System32\winevt\Logs") },
            CustomCleaner = async () => await Task.Run(ClearAllEventLogsNative)
        },
        new()
        {
            Id = "clean_explorer_thumbs",
            Name = "Кеш ескізів Провідника (Thumbnails Cache)",
            Category = "Дампи & Логи (Manual)",
            IsSafeBatch = false,
            Description = "Бази даних збережених мініатюр зображень та відео (thumbcache_*.db).",
            Benefits = "Звільняє місце на SSD та виправляє відображення пошкоджених значків.",
            SideEffects = "Провідник заново створюватиме ескізи при відкритті папок із фото/відео.",
            TargetPaths = new() { Path.Combine(LocalAppData, @"Microsoft\Windows\Explorer") },
            CustomCleaner = async () => await Task.Run(CleanThumbnailCache)
        },

        // =========================================================================
        // 5. ДИСКОВИЙ МОДУЛЬ DISM WINSXS
        // =========================================================================
        new()
        {
            Id = "clean_dism_winsxs",
            Name = "Глибоке стиснення WinSxS (DISM Component Cleanup)",
            Category = "Безпечний кеш",
            IsSafeBatch = false,
            IsDism = true,
            Description = "Стиснення та видалення застарілих версій системних компонентів у сховищі WinSxS.",
            Benefits = "Повертає від 2 до 8+ ГБ вільного місця на диску C:.",
            SideEffects = "Процес триває 1–3 хвилини з навантаженням на процесор.",
            CustomCleaner = async () => await Task.Run(RunDismCleanup)
        }
    };

    #region Публічні методи виконання

    public static async Task CalculateSizesAsync()
    {
        await Parallel.ForEachAsync(Cleaners, async (item, token) =>
        {
            if (item.IsDism)
            {
                item.BytesFound = 0;
                return;
            }

            if (item.CustomSizeCalculator != null)
            {
                item.BytesFound = await item.CustomSizeCalculator();
                return;
            }

            long total = 0;
            foreach (var path in item.TargetPaths)
            {
                total += FastGetDirectorySize(path);
            }
            item.BytesFound = total;
        });
    }

    public static async Task<long> CleanItemAsync(CleanerItem item)
    {
        long freed = 0;

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

        item.BytesFound = 0;
        return freed;
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
            await Task.Delay(15);
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
            try { return new FileInfo(path).Length; } catch { return 0; }
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
                foreach (var file in Directory.GetFiles(current))
                {
                    try { total += new FileInfo(file).Length; } catch { }
                }

                foreach (var dir in Directory.GetDirectories(current))
                {
                    try
                    {
                        var di = new DirectoryInfo(dir);
                        // Пропускаємо Junctions та Симлінки для безпеки
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
        if (File.Exists(path))
        {
            try
            {
                long len = new FileInfo(path).Length;
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
            foreach (var file in dir.GetFiles("*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    long len = file.Length;
                    file.Attributes = FileAttributes.Normal;
                    file.Delete();
                    freed += len;
                }
                catch { }
            }

            foreach (var sub in dir.GetDirectories("*", SearchOption.TopDirectoryOnly))
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
        try
        {
            SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
        }
        catch { }
        long after = GetRecycleBinTotalSize();
        return Math.Max(0, before - after);
    }

    private static long ClearAllEventLogsNative()
    {
        long initialSize = FastGetDirectorySize(Path.Combine(WinDir, @"System32\winevt\Logs"));
        try
        {
            var session = new EventLogSession();
            foreach (var name in session.GetLogNames())
            {
                try { session.ClearLog(name); } catch { }
            }
        }
        catch { }
        long finalSize = FastGetDirectorySize(Path.Combine(WinDir, @"System32\winevt\Logs"));
        return Math.Max(0, initialSize - finalSize);
    }

    private static long CleanThumbnailCache()
    {
        string dir = Path.Combine(LocalAppData, @"Microsoft\Windows\Explorer");
        if (!Directory.Exists(dir)) return 0;

        long freed = 0;
        try
        {
            var di = new DirectoryInfo(dir);
            foreach (var file in di.GetFiles("thumbcache_*.db", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    long sz = file.Length;
                    file.Attributes = FileAttributes.Normal;
                    file.Delete();
                    freed += sz;
                }
                catch { }
            }
        }
        catch { }
        return freed;
    }

    private static long RunDismCleanup()
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "dism.exe",
                Arguments = "/online /cleanup-image /startcomponentcleanup /norestart",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            proc?.WaitForExit();
        }
        catch { }
        return 0;
    }

    #endregion
}