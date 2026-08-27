using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MASLOOPTIMIZER;

public enum DebloatSortMode
{
    Default,
    InstalledFirst,
    UninstalledFirst,
    NameAscending,
    NameDescending,
    Category
}

public class DebloatStats
{
    public int Total { get; set; }
    public int Installed { get; set; }
    public int Removed => Total - Installed;
    public double CleanPercentage => Total > 0 ? Math.Round((Removed / (double)Total) * 100, 1) : 0;
}

public class DebloatItem : INotifyPropertyChanged
{
    public DebloatItem()
    {
        LocalizationManager.Instance.PropertyChanged += OnLocalizationChanged;
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ActionButtonText));
        OnPropertyChanged(nameof(UninstallButtonText));
        OnPropertyChanged(nameof(RestoreButtonText));
    }

    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "ШІ & Телеметрія";
    public string PackageMatch { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string StoreId { get; set; } = string.Empty;
    public bool IsSpecialService { get; set; } = false;

    private bool _isInstalled;
    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (_isInstalled != value)
            {
                _isInstalled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(ActionButtonText));
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

    public string StatusText => IsInstalled
        ? LocalizationManager.Instance["Debloat.StatusInstalled"]
        : LocalizationManager.Instance["Debloat.StatusNotInstalled"];
    public string StatusColor => IsInstalled ? "#107C41" : "#2A2D3D";

    public string ActionButtonText
    {
        get
        {
            var loc = LocalizationManager.Instance;
            if (IsBusy) return loc["Common.Busy"];
            return IsInstalled ? loc["Debloat.BtnUninstall"] : loc["Debloat.BtnRestore"];
        }
    }

    public string UninstallButtonText => IsBusy
        ? LocalizationManager.Instance["Common.Busy"]
        : LocalizationManager.Instance["Debloat.BtnUninstall"];

    public string RestoreButtonText => IsBusy
        ? LocalizationManager.Instance["Common.Busy"]
        : LocalizationManager.Instance["Debloat.BtnRestore"];

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class DebloatEngine
{
    public static List<DebloatItem> Catalog { get; } = new()
    {
        // =========================================================================
        // 1. ШІ, ПОМІЧНИКИ ТА ТЕЛЕМЕТРІЯ
        // =========================================================================
        new()
        {
            Id = "uwp_copilot",
            Name = "Microsoft Copilot & Bing Chat",
            Category = "ШІ & Телеметрія",
            PackageMatch = "*Microsoft.Copilot*|*Microsoft.BingChat*|*Windows.Copilot*",
            Description = "Штучний інтелект Copilot, бічні панелі Bing та інтегрований веб-пошук.",
            StoreId = "9NBLGGH516XP"
        },
        new()
        {
            Id = "uwp_cortana",
            Name = "Cortana",
            Category = "ШІ & Телеметрія",
            PackageMatch = "*Microsoft.549981C3F5F10*",
            Description = "Застарілий голосовий асистент Microsoft.",
            StoreId = "9NBLGGH4VSM8"
        },
        new()
        {
            Id = "uwp_feedback",
            Name = "Центр відгуків (Feedback Hub)",
            Category = "ШІ & Телеметрія",
            PackageMatch = "*WindowsFeedbackHub*",
            Description = "Служба збору діагностичних даних, звітів про роботу та опитувань користувача.",
            StoreId = "9NBLGGH4R32N"
        },
        new()
        {
            Id = "uwp_powerautomate",
            Name = "Power Automate Desktop",
            Category = "ШІ & Телеметрія",
            PackageMatch = "*PowerAutomateDesktop*",
            Description = "Вбудований засіб автоматизації фонових макросів.",
            StoreId = "9NBLGGH5158M"
        },
        new()
        {
            Id = "uwp_devhome",
            Name = "Microsoft Dev Home",
            Category = "ШІ & Телеметрія",
            PackageMatch = "*Windows.DevHome*",
            Description = "Панель управління для розробників, яка за замовчуванням встановлюється у Windows 11.",
            StoreId = "9N8MHTPHNGVV"
        },

        // =========================================================================
        // 2. МЕДІА, ВІДЕО ТА ІГРИ
        // =========================================================================
        new()
        {
            Id = "uwp_clipchamp",
            Name = "Clipchamp Video Editor",
            Category = "Ігри & Медіа",
            PackageMatch = "*Clipchamp.Clipchamp*",
            Description = "Хмарний відеоредактор із вбудованими платними підписками.",
            StoreId = "9P1J8S7CCWWT"
        },
        new()
        {
            Id = "uwp_solitaire",
            Name = "Microsoft Solitaire Collection",
            Category = "Ігри & Медіа",
            PackageMatch = "*MicrosoftSolitaireCollection*",
            Description = "Колекція пасьянсів із рекламою та фоновими повідомленнями Xbox Live.",
            StoreId = "9WZDNCRFHWD2"
        },
        new()
        {
            Id = "uwp_xbox",
            Name = "Xbox Game Bar & Gaming Suite",
            Category = "Ігри & Медіа",
            PackageMatch = "*XboxGamingOverlay*|*XboxApp*|*XboxSpeech*|*XboxTCUI*|*XboxIdentityProvider*",
            Description = "Оверлей запису екрана Game Bar, соціальні служби Xbox та голосовий зв'язок.",
            StoreId = "9NZKPSTSNW4P"
        },
        new()
        {
            Id = "uwp_zunevideo",
            Name = "Кіно й ТБ (Movies & TV / ZuneVideo)",
            Category = "Ігри & Медіа",
            PackageMatch = "*ZuneVideo*",
            Description = "Стандартний відеоплеєр Windows із магазином прокату фільмів.",
            StoreId = "9WZDNCRFJ3P2"
        },
        new()
        {
            Id = "uwp_zunemusic",
            Name = "Groove Music (Media Player / ZuneMusic)",
            Category = "Ігри & Медіа",
            PackageMatch = "*ZuneMusic*|*WindowsMediaPlayer*",
            Description = "Стандартний мультимедійний плеєр Windows.",
            StoreId = "9WZDNCRFJ4PT"
        },
        new()
        {
            Id = "uwp_soundrec",
            Name = "Звукозапис (Sound Recorder)",
            Category = "Ігри & Медіа",
            PackageMatch = "*WindowsSoundRecorder*|*SoundRecorder*",
            Description = "Базова системна утиліта запису аудіо з мікрофона.",
            StoreId = "9WZDNCRFHWKN"
        },

        // =========================================================================
        // 3. 3D, ДИЗАЙН ТА ІНСТРУМЕНТИ
        // =========================================================================
        new()
        {
            Id = "uwp_3dviewer",
            Name = "3D Viewer & Print 3D",
            Category = "3D & Інструменти",
            PackageMatch = "*Microsoft3DViewer*|*Print3D*",
            Description = "Переглядач 3D-моделей та підготовка об'єктів до 3D-друку.",
            StoreId = "9NBLGGH42THS"
        },
        new()
        {
            Id = "uwp_paint3d",
            Name = "Paint 3D",
            Category = "3D & Інструменти",
            PackageMatch = "*Microsoft.Paint3D*|*Paint3D*",
            Description = "Редактор тривимірної графіки (не зачіпає класичний Paint).",
            StoreId = "9NBLGGH5FV99"
        },
        new()
        {
            Id = "uwp_mixedreality",
            Name = "Портал змішаної реальності (Mixed Reality)",
            Category = "3D & Інструменти",
            PackageMatch = "*MixedReality.Portal*",
            Description = "Середовище та фонова служба для шоломів VR/MR.",
            StoreId = "9NBLGGH63NW5"
        },

        // =========================================================================
        // 4. НОВИНИ, ВІДЖЕТИ ТА РЕКЛАМА
        // =========================================================================
        new()
        {
            Id = "uwp_news",
            Name = "Новини (MSN News)",
            Category = "Новини & Віджети",
            PackageMatch = "*BingNews*",
            Description = "Фонова стрічка новин MSN на панелі завдань та у віджетах.",
            StoreId = "9WZDNCRFHVFW"
        },
        new()
        {
            Id = "uwp_weather",
            Name = "Погода (MSN Weather)",
            Category = "Новини & Віджети",
            PackageMatch = "*BingWeather*",
            Description = "Прогноз погоди MSN із фоновою синхронізацією геолокації.",
            StoreId = "9WZDNCRFJ3Q2"
        },
        new()
        {
            Id = "uwp_finance",
            Name = "Фінанси та Спорт (MSN Finance & Sports)",
            Category = "Новини & Віджети",
            PackageMatch = "*BingFinance*|*BingSports*",
            Description = "Котирування валют, акцій та спортивні новини MSN.",
            StoreId = "9WZDNCRFHV4V"
        },
        new()
        {
            Id = "uwp_webexp",
            Name = "Windows Web Experience Pack (Віджети)",
            Category = "Новини & Віджети",
            PackageMatch = "*WebExperience*",
            Description = "Фоновий веб-рушій віджетів панелі завдань Windows 11.",
            StoreId = "9MSSGKG3SNGE"
        },
        new()
        {
            Id = "uwp_promostubs",
            Name = "Спонсорські промо-заглушки (TikTok, Spotify, Disney+)",
            Category = "Новини & Віджети",
            PackageMatch = "*Spotify*|*TikTok*|*Disney*|*Facebook*|*Instagram*|*Amazon*",
            Description = "Автоматично встановлювані рекламні ярлики сторонніх сервісів."
        },

        // =========================================================================
        // 5. ЗВ'ЯЗОК, ПОШТА ТА ОРГАНАЙЗЕРИ
        // =========================================================================
        new()
        {
            Id = "uwp_phonelink",
            Name = "Зв'язок зі смартфоном (Phone Link)",
            Category = "Зв'язок & Пошта",
            PackageMatch = "*YourPhone*",
            Description = "Фонова синхронізація повідомлень, дзвінків та фото з телефоном.",
            StoreId = "9NBLGGH4NNS1"
        },
        new()
        {
            Id = "uwp_people",
            Name = "Люди та Контакти (Microsoft People)",
            Category = "Зв'язок & Пошта",
            PackageMatch = "*Microsoft.People*",
            Description = "Вбудована адресна книга та синхронізація контактів.",
            StoreId = "9NBLGGH10PG8"
        },
        new()
        {
            Id = "uwp_maps",
            Name = "Карти Windows (Windows Maps)",
            Category = "Зв'язок & Пошта",
            PackageMatch = "*WindowsMaps*",
            Description = "Офлайн-карти та навігаційний сервіс Microsoft.",
            StoreId = "9WZDNCRBXB69"
        },
        new()
        {
            Id = "uwp_mailcalendar",
            Name = "Пошта та Календар (Mail & Calendar)",
            Category = "Зв'язок & Пошта",
            PackageMatch = "*windowscommunicationsapps*",
            Description = "Класичні клієнти Пошти та Календаря Windows.",
            StoreId = "9WZDNCRFHVQM"
        },
        new()
        {
            Id = "uwp_newoutlook",
            Name = "Новий Outlook для Windows",
            Category = "Зв'язок & Пошта",
            PackageMatch = "*OutlookForWindows*",
            Description = "Веб-поштовик Outlook на рушії Edge WebView з рекламою.",
            StoreId = "9NRXF420CLMW"
        },
        new()
        {
            Id = "uwp_skype",
            Name = "Skype",
            Category = "Зв'язок & Пошта",
            PackageMatch = "*SkypeApp*",
            Description = "Вбудований UWP-додаток відеодзвінків та чату Skype.",
            StoreId = "9WZDNCRFJ364"
        },
        new()
        {
            Id = "uwp_onenote",
            Name = "OneNote для Windows 10",
            Category = "Зв'язок & Пошта",
            PackageMatch = "*Office.OneNote*",
            Description = "Базовий UWP-блокнот заміток OneNote.",
            StoreId = "9WZDNCRD29V9"
        },
        new()
        {
            Id = "uwp_gethelp",
            Name = "Отримати допомогу (Get Help)",
            Category = "Зв'язок & Пошта",
            PackageMatch = "*GetHelp*",
            Description = "Вбудований онлайн-довідник та бот підтримки Microsoft.",
            StoreId = "9PK2ZG8443M9"
        },
        new()
        {
            Id = "uwp_tips",
            Name = "Поради Windows (Tips & QuickAssist)",
            Category = "Зв'язок & Пошта",
            PackageMatch = "*Getstarted*|*QuickAssist*",
            Description = "Спливаючі рекламні підказки та засіб швидкої віддаленої допомоги.",
            StoreId = "9WZDNCRFJ0MP"
        },

        // =========================================================================
        // 6. ХМАРА & ONEDRIVE
        // =========================================================================
        new()
        {
            Id = "uwp_onedrive",
            Name = "Microsoft OneDrive",
            Category = "Хмара & OneDrive",
            IsSpecialService = true,
            Description = "Хмарна синхронізація Windows. Видалення знімає процеси, деінсталює програму та очищає іконку з Провідника.",
            StoreId = ""
        }
    };

    #region Контекстна фільтрація, сортування та статистика

    public static IEnumerable<DebloatItem> GetFilteredAndSortedItems(
        string? category = null,
        string? searchQuery = null,
        DebloatSortMode sortMode = DebloatSortMode.Default)
    {
        var query = Catalog.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(category) && !category.Equals("Всі", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(d => string.Equals(d.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            string q = searchQuery.Trim();
            query = query.Where(d =>
                d.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                d.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                d.Id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                d.PackageMatch.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                d.StoreId.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        return sortMode switch
        {
            DebloatSortMode.InstalledFirst => query.OrderByDescending(d => d.IsInstalled).ThenBy(d => d.Name),
            DebloatSortMode.UninstalledFirst => query.OrderBy(d => d.IsInstalled).ThenBy(d => d.Name),
            DebloatSortMode.NameAscending => query.OrderBy(d => d.Name),
            DebloatSortMode.NameDescending => query.OrderByDescending(d => d.Name),
            DebloatSortMode.Category => query.OrderBy(d => d.Category).ThenBy(d => d.Name),
            _ => query.OrderBy(d => d.Name)
        };
    }

    public static List<string> GetCategories()
    {
        var categories = Catalog.Select(d => d.Category).Distinct().OrderBy(c => c).ToList();
        categories.Insert(0, "Всі");
        return categories;
    }

    public static DebloatStats GetStatistics()
    {
        int total = Catalog.Count;
        int installed = Catalog.Count(d => d.IsInstalled);
        return new DebloatStats
        {
            Total = total,
            Installed = installed
        };
    }

    #endregion

    #region Сканування системи

    public static async Task ScanInstalledPackagesAsync()
    {
        var status = new Dictionary<DebloatItem, bool>();

        await Task.Run(() =>
        {
            var installedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var pkgManager = new Windows.Management.Deployment.PackageManager();
                var pkgs = pkgManager.FindPackagesForUser(string.Empty);
                foreach (var p in pkgs)
                {
                    if (p.Id.Name.Contains("ExperienceHost", StringComparison.OrdinalIgnoreCase) || p.IsFramework || p.IsResourcePackage)
                        continue;

                    installedNames.Add(p.Id.Name);
                    installedNames.Add(p.Id.FullName);
                }
            }
            catch { }

            foreach (var item in Catalog)
            {
                if (item.IsSpecialService)
                {
                    status[item] = CheckOneDriveInstalled();
                    continue;
                }

                var patterns = item.PackageMatch.Split('|', StringSplitOptions.RemoveEmptyEntries);
                bool found = false;

                foreach (var pat in patterns)
                {
                    string clean = pat.Trim().Trim('*');
                    if (installedNames.Any(name => name.Contains(clean, StringComparison.OrdinalIgnoreCase)))
                    {
                        found = true;
                        break;
                    }
                }

                status[item] = found;
            }
        });

        // Оновлення UI-залежних властивостей — лише на UI-потоці (D-11)
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            foreach (var kv in status)
                kv.Key.IsInstalled = kv.Value;
        });
    }

    #endregion

    #region Видалення та Очищення

    public static async Task<bool> UninstallPackageAsync(DebloatItem item)
    {
        item.IsBusy = true;
        bool result = await Task.Run(() =>
        {
            try
            {
                if (item.IsSpecialService)
                {
                    return UninstallOneDriveFull();
                }

                var pkgManager = new Windows.Management.Deployment.PackageManager();
                var patterns = item.PackageMatch.Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim().Trim('*'))
                    .ToList();

                var userPkgs = pkgManager.FindPackagesForUser(string.Empty)
                    .Where(p => patterns.Any(pat => p.Id.Name.Contains(pat, StringComparison.OrdinalIgnoreCase) && !p.Id.Name.Contains("ExperienceHost", StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                foreach (var p in userPkgs)
                {
                    try
                    {
                        pkgManager.RemovePackageAsync(p.Id.FullName, Windows.Management.Deployment.RemovalOptions.RemoveForAllUsers).GetResults();
                    }
                    catch { }
                }

                foreach (var pat in patterns)
                {
                    RunPowerShellQuiet($"Get-AppxProvisionedPackage -Online | Where-Object {{ $_.DisplayName -like '*{pat}*' -and $_.DisplayName -notlike '*ExperienceHost*' }} | Remove-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue");
                }

                AppLogger.Log($"Видалено UWP додаток: {item.Name}", "SUCCESS");
                return true;
            }
            catch
            {
                AppLogger.Log($"Помилка видалення UWP {item.Name}", "ERROR");
                return false;
            }
        });

        if (result)
        {
            // Оновлення UI-залежної властивості — лише на UI-потоці (D-11)
            System.Windows.Application.Current?.Dispatcher.Invoke(() => { item.IsInstalled = false; });
        }

        item.IsBusy = false;
        return result;
    }

    #endregion

    #region Відновлення (Локальне + Microsoft Store)

    public static async Task<bool> RestorePackageAsync(DebloatItem item)
    {
        item.IsBusy = true;
        bool result = await Task.Run(() =>
        {
            if (item.IsSpecialService)
            {
                return InstallOneDrive();
            }

            bool restoredLocally = false;
            var patterns = item.PackageMatch.Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim().Trim('*'))
                .ToList();

            string winAppsDir = @"C:\Program Files\WindowsApps";
            if (Directory.Exists(winAppsDir))
            {
                try
                {
                    foreach (var pat in patterns)
                    {
                        string script = $@"
                        Get-ChildItem -Path '{winAppsDir}' -Recurse -Filter 'AppxManifest.xml' -ErrorAction SilentlyContinue | 
                        Where-Object {{ $_.FullName -like '*{pat}*' -and $_.FullName -notlike '*ExperienceHost*' }} | 
                        ForEach-Object {{ Add-AppxPackage -DisableDevelopmentMode -Register $_.FullName -ErrorAction SilentlyContinue }}";

                        RunPowerShellQuiet(script);
                    }
                }
                catch { }
            }

            var pkgManager = new Windows.Management.Deployment.PackageManager();
            var pkgs = pkgManager.FindPackagesForUser(string.Empty);
            restoredLocally = pkgs.Any(p => patterns.Any(pat => p.Id.Name.Contains(pat, StringComparison.OrdinalIgnoreCase)));

            if (!restoredLocally && !string.IsNullOrWhiteSpace(item.StoreId))
            {
                RestoreViaStore(item);
            }

            item.IsInstalled = restoredLocally;
            AppLogger.Log(restoredLocally
                ? $"Відновлено UWP додаток: {item.Name}"
                : $"Відкрито сторінку встановлення у Store: {item.Name}",
                restoredLocally ? "SUCCESS" : "INFO");
            return restoredLocally;
        });

        item.IsBusy = false;
        return result;
    }

    public static void RestoreViaStore(DebloatItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.StoreId))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"ms-windows-store://pdp/?ProductId={item.StoreId}",
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    public static async Task RestoreAllDefaultUwpAsync(IProgress<(int Percent, string Status)>? progress = null)
    {
        await Task.Run(() =>
        {
            progress?.Report((30, "Перереєстрація системних UWP-компонентів Windows..."));
            string script = @"
            Get-AppxPackage -AllUsers -ErrorAction SilentlyContinue | ForEach-Object {
                $m = Join-Path $_.InstallLocation 'AppxManifest.xml'
                if (Test-Path $m) { Add-AppxPackage -DisableDevelopmentMode -Register $m -ErrorAction SilentlyContinue }
            }";
            RunPowerShellQuiet(script);
            progress?.Report((100, "Усі системні UWP-додатки відновлено."));
        });
    }

    #endregion

    #region OneDrive Інтеграція

    private static bool CheckOneDriveInstalled()
    {
        string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\OneDrive\OneDrive.exe");
        string pf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft OneDrive\OneDrive.exe");
        string pfx86 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft OneDrive\OneDrive.exe");

        return File.Exists(local) || File.Exists(pf) || File.Exists(pfx86);
    }

    private static bool UninstallOneDriveFull()
    {
        try
        {
            foreach (var proc in Process.GetProcessesByName("OneDrive"))
            {
                try { proc.Kill(); proc.WaitForExit(1000); } catch { }
            }

            string sys32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OneDriveSetup.exe");
            string sysWow64 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"SysWOW64\OneDriveSetup.exe");
            string userSetup = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\OneDrive\Update\OneDriveSetup.exe");

            string installer = File.Exists(userSetup) ? userSetup : (File.Exists(sysWow64) ? sysWow64 : (File.Exists(sys32) ? sys32 : string.Empty));

            if (!string.IsNullOrEmpty(installer))
            {
                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = installer,
                    Arguments = "/uninstall",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                proc?.WaitForExit(15000);
            }

            using (var key = Registry.ClassesRoot.OpenSubKey(@"CLSID\{018D5C66-4533-4307-9B53-224DE2ED1FE6}", true))
            {
                key?.SetValue("System.IsPinnedToNameSpaceTree", 0, RegistryValueKind.DWord);
            }
            using (var keyWow = Registry.ClassesRoot.OpenSubKey(@"Wow6432Node\CLSID\{018D5C66-4533-4307-9B53-224DE2ED1FE6}", true))
            {
                keyWow?.SetValue("System.IsPinnedToNameSpaceTree", 0, RegistryValueKind.DWord);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool InstallOneDrive()
    {
        try
        {
            string sys32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OneDriveSetup.exe");
            string sysWow64 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"SysWOW64\OneDriveSetup.exe");

            if (File.Exists(sysWow64))
            {
                Process.Start(new ProcessStartInfo { FileName = sysWow64, UseShellExecute = true });
            }
            else if (File.Exists(sys32))
            {
                Process.Start(new ProcessStartInfo { FileName = sys32, UseShellExecute = true });
            }
            else
            {
                Process.Start(new ProcessStartInfo { FileName = "https://go.microsoft.com/fwlink/p/?LinkId=248256", UseShellExecute = true });
            }

            using (var key = Registry.ClassesRoot.OpenSubKey(@"CLSID\{018D5C66-4533-4307-9B53-224DE2ED1FE6}", true))
            {
                key?.SetValue("System.IsPinnedToNameSpaceTree", 1, RegistryValueKind.DWord);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Допоміжний метод CLI

    private static void RunPowerShellQuiet(string command)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            proc?.WaitForExit(10000);
        }
        catch { }
    }

    #endregion
}