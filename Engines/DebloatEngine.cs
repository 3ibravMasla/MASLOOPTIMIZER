using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
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

public class DebloatItem : INotifyPropertyChanged, IWeakEventListener
{
    public DebloatItem()
    {
        // Слабка підписка на зміну мови: статичний синглтон не утримує об'єкт.
        PropertyChangedEventManager.AddListener(LocalizationManager.Instance, this, string.Empty);
    }

    bool IWeakEventListener.ReceiveWeakEvent(Type managerType, object sender, EventArgs e)
    {
        if (managerType == typeof(PropertyChangedEventManager))
        {
            OnLocalizationChanged(sender, (PropertyChangedEventArgs)e);
            return true;
        }

        return false;
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ActionButtonText));
        OnPropertyChanged(nameof(UninstallButtonText));
        OnPropertyChanged(nameof(RestoreButtonText));
        OnPropertyChanged(nameof(LocalizedDescription));
        OnPropertyChanged(nameof(CategoryLocalized));
        OnPropertyChanged(nameof(RiskBadge));
    }

    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "System & Telemetry";
    public string PackageMatch { get; set; } = string.Empty;
    public string LocalizedDescription => LocalizationManager.Instance.T($"Debloat.Desc.{Id}");
    public string StoreId { get; set; } = string.Empty;

    /// <summary>Рівень ризику: Safe / Medium / High.</summary>
    public string Risk { get; set; } = "Safe";

    public bool IsSpecialService { get; set; } = false;

    /// <summary>Локалізована назва категорії (ключ Categories.{Category}).</summary>
    public string CategoryLocalized
        => LocalizationManager.Instance.TryGet($"Categories.{Category}", out var cat) && !string.IsNullOrWhiteSpace(cat)
            ? cat
            : Category;

    public string RiskBadge
    {
        get
        {
            string key = Risk switch
            {
                "High" => "Risk.High",
                "Medium" => "Risk.Medium",
                _ => "Risk.Safe"
            };
            return LocalizationManager.Instance.TryGet(key, out var txt) && !string.IsNullOrWhiteSpace(txt) ? txt : Risk;
        }
    }

    /// <summary>Показувати бейдж ризику лише для Medium/High (безпечні не захаращують картку).</summary>
    public bool ShowRisk => !string.Equals(Risk, "Safe", StringComparison.OrdinalIgnoreCase);

    public Brush RiskColor => Risk switch
    {
        "High" => ThemeEngine.Brush("DangerBrush"),
        _ => ThemeEngine.Brush("StatusNeutralBrush")
    };

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

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public string StatusText => IsInstalled
        ? LocalizationManager.Instance["Debloat.StatusInstalled"]
        : LocalizationManager.Instance["Debloat.NotInSystem"];
    public Brush StatusColor => IsInstalled ? ThemeEngine.Brush("SuccessBrush") : ThemeEngine.Brush("StatusNeutralBrush");

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
    private const string CatAi = "AI & Copilot";
    private const string CatMs = "Microsoft Bloatware";
    private const string CatXbox = "Xbox & Gaming";
    private const string CatMedia = "Media & Lifestyle";
    private const string CatSys = "System & Telemetry";
    private const string CatOneDrive = "OneDrive";

    /// <summary>Каталог цільових пакетів Windows 11 24H2/25H2 (~40 позицій + OneDrive).</summary>
    public static List<DebloatItem> Catalog { get; } = new()
    {
        // =========================================================================
        // 1. AI & COPILOT
        // =========================================================================
        new()
        {
            Id = "uwp_copilot",
            Name = "Microsoft Copilot",
            Category = CatAi,
            PackageMatch = "*Microsoft.Copilot*|*Microsoft.BingChat*",
            StoreId = "9NBLGGH516XP"
        },
        new()
        {
            Id = "uwp_copilot_provider",
            Name = "Copilot AI Provider (Windows.Ai.Copilot.Provider)",
            Category = CatAi,
            PackageMatch = "*Windows.Ai.Copilot.Provider*",
        },
        new()
        {
            Id = "uwp_webexp",
            Name = "Windows Web Experience Pack (Widgets)",
            Category = CatAi,
            PackageMatch = "*WebExperience*",
            StoreId = "9MSSGKG3SNGE",
            Risk = "Medium"
        },

        // =========================================================================
        // 2. MICROSOFT BLOATWARE
        // =========================================================================
        new()
        {
            Id = "uwp_clipchamp",
            Name = "Clipchamp Video Editor",
            Category = CatMs,
            PackageMatch = "*Clipchamp.Clipchamp*",
            StoreId = "9P1J8S7CCWWT"
        },
        new()
        {
            Id = "uwp_newoutlook",
            Name = "New Outlook for Windows",
            Category = CatMs,
            PackageMatch = "*OutlookForWindows*",
            StoreId = "9NRXF420CLMW"
        },
        new()
        {
            Id = "uwp_devhome",
            Name = "Microsoft Dev Home",
            Category = CatMs,
            PackageMatch = "*Windows.DevHome*",
            StoreId = "9N8MHTPHNGVV"
        },
        new()
        {
            Id = "uwp_news",
            Name = "MSN News (Bing News)",
            Category = CatMs,
            PackageMatch = "*BingNews*",
            StoreId = "9WZDNCRFHVFW"
        },
        new()
        {
            Id = "uwp_weather",
            Name = "MSN Weather (Bing Weather)",
            Category = CatMs,
            PackageMatch = "*BingWeather*",
            StoreId = "9WZDNCRFJ3Q2"
        },
        new()
        {
            Id = "uwp_finance",
            Name = "MSN Finance (Bing Finance)",
            Category = CatMs,
            PackageMatch = "*BingFinance*",
            StoreId = "9WZDNCRFHV4V"
        },
        new()
        {
            Id = "uwp_sports",
            Name = "MSN Sports (Bing Sports)",
            Category = CatMs,
            PackageMatch = "*BingSports*",
            StoreId = "9WZDNCRFHWCF"
        },
        new()
        {
            Id = "uwp_todos",
            Name = "Microsoft To Do",
            Category = CatMs,
            PackageMatch = "*Todos*",
            StoreId = "9NBLGGH5R558"
        },
        new()
        {
            Id = "uwp_stickynotes",
            Name = "Microsoft Sticky Notes",
            Category = CatMs,
            PackageMatch = "*MicrosoftStickyNotes*",
            StoreId = "9NBLGGH4QGHW"
        },
        new()
        {
            Id = "uwp_officehub",
            Name = "Microsoft Office Hub",
            Category = CatMs,
            PackageMatch = "*MicrosoftOfficeHub*",
            StoreId = "9WZDNCRFJ3PZ"
        },
        new()
        {
            Id = "uwp_gethelp",
            Name = "Get Help",
            Category = CatMs,
            PackageMatch = "*GetHelp*",
            StoreId = "9PKDZBMV1H3T"
        },
        new()
        {
            Id = "uwp_tips",
            Name = "Tips (Get Started)",
            Category = CatMs,
            PackageMatch = "*Getstarted*",
            StoreId = "9WZDNCRFJ0MP"
        },
        new()
        {
            Id = "uwp_feedback",
            Name = "Windows Feedback Hub",
            Category = CatMs,
            PackageMatch = "*WindowsFeedbackHub*",
            StoreId = "9NBLGGH4R32N"
        },
        new()
        {
            Id = "uwp_powerautomate",
            Name = "Power Automate Desktop",
            Category = CatMs,
            PackageMatch = "*PowerAutomateDesktop*",
            StoreId = "9NBLGGH5158M"
        },
        new()
        {
            Id = "uwp_quickassist",
            Name = "Quick Assist",
            Category = CatMs,
            PackageMatch = "*QuickAssist*",
            StoreId = "9P7BP5VNWKX5"
        },
        new()
        {
            Id = "uwp_phonelink",
            Name = "Phone Link (Your Phone)",
            Category = CatMs,
            PackageMatch = "*YourPhone*",
            StoreId = "9NBLGGH4NNS1"
        },
        new()
        {
            Id = "uwp_people",
            Name = "Microsoft People",
            Category = CatMs,
            PackageMatch = "*Microsoft.People*",
            StoreId = "9NBLGGH10PG8"
        },
        new()
        {
            Id = "uwp_maps",
            Name = "Windows Maps",
            Category = CatMs,
            PackageMatch = "*WindowsMaps*",
            StoreId = "9WZDNCRBXB69"
        },
        new()
        {
            Id = "uwp_bingsearch",
            Name = "Microsoft Bing Search",
            Category = CatMs,
            PackageMatch = "*Microsoft.BingSearch*|*BingSearch*",
            StoreId = "9NBLGGH6B4XK"
        },

        // =========================================================================
        // 3. MEDIA & LIFESTYLE
        // =========================================================================
        new()
        {
            Id = "uwp_solitaire",
            Name = "Microsoft Solitaire Collection",
            Category = CatMedia,
            PackageMatch = "*MicrosoftSolitaireCollection*",
            StoreId = "9WZDNCRFHWD2"
        },
        new()
        {
            Id = "uwp_zunemusic",
            Name = "Media Player (Zune Music)",
            Category = CatMedia,
            PackageMatch = "*ZuneMusic*",
            StoreId = "9WZDNCRFJ4PT"
        },
        new()
        {
            Id = "uwp_zunevideo",
            Name = "Movies & TV (Zune Video)",
            Category = CatMedia,
            PackageMatch = "*ZuneVideo*",
            StoreId = "9WZDNCRFJ3P2"
        },
        new()
        {
            Id = "uwp_soundrec",
            Name = "Sound Recorder",
            Category = CatMedia,
            PackageMatch = "*WindowsSoundRecorder*|*SoundRecorder*",
            StoreId = "9WZDNCRFHWKN"
        },
        new()
        {
            Id = "uwp_camera",
            Name = "Windows Camera",
            Category = CatMedia,
            PackageMatch = "*WindowsCamera*",
            StoreId = "9WZDNCRFJBBG"
        },
        new()
        {
            Id = "uwp_alarms",
            Name = "Windows Clock & Alarms",
            Category = CatMedia,
            PackageMatch = "*WindowsAlarms*",
            StoreId = "9WZDNCRFJ3PR"
        },
        new()
        {
            Id = "uwp_mixedreality",
            Name = "Mixed Reality Portal",
            Category = CatMedia,
            PackageMatch = "*MixedReality.Portal*",
            StoreId = "9NBLGGH63NW5"
        },
        new()
        {
            Id = "uwp_photos",
            Name = "Microsoft Photos (optional)",
            Category = CatMedia,
            PackageMatch = "*Microsoft.Windows.Photos*",
            StoreId = "9WZDNCRFJBH4",
            Risk = "Medium"
        },
        new()
        {
            Id = "uwp_paint",
            Name = "Paint (optional)",
            Category = CatMedia,
            PackageMatch = "*Microsoft.Paint*",
            StoreId = "9PCFS5B6T72H",
            Risk = "Medium"
        },

        // =========================================================================
        // 4. XBOX & GAMING
        // =========================================================================
        new()
        {
            Id = "uwp_xboxapp",
            Name = "Xbox App (Gaming App)",
            Category = CatXbox,
            PackageMatch = "*Microsoft.GamingApp*",
            StoreId = "9MV0B5HZVK9Z"
        },
        new()
        {
            Id = "uwp_xboxgamebar",
            Name = "Xbox Game Bar",
            Category = CatXbox,
            PackageMatch = "*XboxGamingOverlay*",
            StoreId = "9NZKPSTSNW4P"
        },
        new()
        {
            Id = "uwp_xboxoverlay",
            Name = "Xbox Game Overlay",
            Category = CatXbox,
            PackageMatch = "*XboxGameOverlay*",
        },
        new()
        {
            Id = "uwp_xboxid",
            Name = "Xbox Identity Provider",
            Category = CatXbox,
            PackageMatch = "*XboxIdentityProvider*",
            StoreId = "9NBLGGH4RX5Z"
        },
        new()
        {
            Id = "uwp_xboxspeech",
            Name = "Xbox Speech To Text Overlay",
            Category = CatXbox,
            PackageMatch = "*XboxSpeechToTextOverlay*",
        },
        new()
        {
            Id = "uwp_xboxtcui",
            Name = "Xbox TCUI",
            Category = CatXbox,
            PackageMatch = "*Xbox.TCUI*",
        },

        // =========================================================================
        // 5. SYSTEM & TELEMETRY
        // =========================================================================
        new()
        {
            Id = "uwp_cortana",
            Name = "Cortana",
            Category = CatSys,
            PackageMatch = "*Microsoft.549981C3F5F10*",
            StoreId = "9NBLGGH4VSM8"
        },
        new()
        {
            Id = "uwp_skype",
            Name = "Skype",
            Category = CatSys,
            PackageMatch = "*SkypeApp*",
            StoreId = "9WZDNCRFJ364"
        },
        new()
        {
            Id = "uwp_onenote",
            Name = "OneNote for Windows",
            Category = CatSys,
            PackageMatch = "*Office.OneNote*",
            StoreId = "9WZDNCRD29V9"
        },
        new()
        {
            Id = "uwp_mailcalendar",
            Name = "Mail & Calendar",
            Category = CatSys,
            PackageMatch = "*windowscommunicationsapps*",
            StoreId = "9WZDNCRFHVQM"
        },
        new()
        {
            Id = "uwp_promostubs",
            Name = "Promo stubs (TikTok / Spotify / Disney+ / ...)",
            Category = CatSys,
            PackageMatch = "*Spotify*|*TikTok*|*Disney*|*Facebook*|*Instagram*|*Amazon*|*Netflix*|*CandyCrush*|*Asphalt*",
            Risk = "Medium"
        },
        new()
        {
            Id = "uwp_family",
            Name = "Microsoft Family",
            Category = CatSys,
            PackageMatch = "*MicrosoftFamily*|*Microsoft.Family*",
            StoreId = "9PD17T8LBBGZ"
        },
        new()
        {
            Id = "uwp_crossdevice",
            Name = "Cross Device Experience",
            Category = CatSys,
            PackageMatch = "*CrossDevice*",
            Risk = "Medium"
        },

        // =========================================================================
        // 6. ONEDRIVE (Win32/System — спеціальна обробка)
        // =========================================================================
        new()
        {
            Id = "uwp_onedrive",
            Name = "Microsoft OneDrive",
            Category = CatOneDrive,
            IsSpecialService = true,
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
                d.LocalizedDescription.Contains(q, StringComparison.OrdinalIgnoreCase) ||
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
        RunOnUi(() =>
        {
            foreach (var kv in status)
                kv.Key.IsInstalled = kv.Value;
        });
    }

    #endregion

    #region Видалення та Очищення

    /// <summary>
    /// Видаляє один UWP-пакет: і для користувача (AppxPackage), і провізійований (AppxProvisionedPackage)
    /// в ОДНОМУ виклику PowerShell, щоб Windows Update не повернув додаток після перезавантаження.
    /// </summary>
    public static async Task<bool> UninstallPackageAsync(DebloatItem item)
    {
        RunOnUi(() => item.IsBusy = true);
        bool result = await Task.Run(() =>
        {
            try
            {
                if (item.IsSpecialService)
                {
                    return UninstallOneDriveFull();
                }

                var patterns = item.PackageMatch.Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => p.Trim().Trim('*'))
                    .Where(p => p.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                string script = BuildRemovalScript(patterns);
                bool ok = RunPowerShellEncoded(script, timeoutMs: 180_000);

                AppLogger.Log($"Видалено UWP додаток: {item.Name}", ok ? "SUCCESS" : "WARN");
                return ok;
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
            RunOnUi(() => { item.IsInstalled = false; });
        }

        RunOnUi(() => item.IsBusy = false);
        return result;
    }

    /// <summary>
    /// Пакетне видалення: формує ОДИН спільний сценарій PowerShell для всіх обраних пакетів
    /// і виконує його в єдиному процесі powershell.exe (замість N послідовних запусків).
    /// Оновлює IsInstalled = false на місці.
    /// </summary>
    public static async Task<int> RemoveBatchPackagesAsync(IEnumerable<DebloatItem> items, IProgress<string>? progress = null)
    {
        var targets = items?.Where(i => i != null).Distinct().ToList() ?? new List<DebloatItem>();
        if (targets.Count == 0) return 0;

        var loc = LocalizationManager.Instance;
        var uwp = targets.Where(t => !t.IsSpecialService).ToList();
        var special = targets.Where(t => t.IsSpecialService).ToList();

        return await Task.Run(() =>
        {
            int removed = 0;

            if (uwp.Count > 0)
            {
                progress?.Report(loc.Format("Debloat.BatchRemoving", uwp.Count));

                var patterns = uwp
                    .SelectMany(i => i.PackageMatch.Split('|', StringSplitOptions.RemoveEmptyEntries))
                    .Select(p => p.Trim().Trim('*'))
                    .Where(p => p.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                string script = BuildRemovalScript(patterns);
                bool ok = RunPowerShellEncoded(script, timeoutMs: 600_000);

                foreach (var i in uwp)
                {
                    RunOnUi(() => i.IsInstalled = false);
                    RunOnUi(() => i.IsSelected = false);
                    removed++;
                }

                AppLogger.Log($"Пакетне видалення: оброблено {uwp.Count} пакетів ({(ok ? "OK" : "з помилками")})", ok ? "SUCCESS" : "WARN");
            }

            foreach (var s in special)
            {
                progress?.Report(loc.Format("Debloat.Uninstalling", s.Name));
                bool ok = UninstallOneDriveFull();
                if (ok)
                {
                    RunOnUi(() => s.IsInstalled = false);
                    RunOnUi(() => s.IsSelected = false);
                    removed++;
                }
            }

            progress?.Report(loc.Format("Debloat.BatchRemoved", removed));
            return removed;
        });
    }

    /// <summary>Будує єдиний PowerShell-сценарій повного видалення (Appx + Provisioned) для набору масок.</summary>
    private static string BuildRemovalScript(IEnumerable<string> patterns)
    {
        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference='SilentlyContinue'");
        sb.AppendLine("$ProgressPreference='SilentlyContinue'");

        foreach (var raw in patterns)
        {
            string p = EscapeForSingleQuotedPs(raw);
            if (p.Length == 0) continue;

            sb.AppendLine($"Get-AppxPackage -AllUsers -Name '*{p}*' | Where-Object {{ $_.Name -notlike '*ExperienceHost*' }} | Remove-AppxPackage -AllUsers -ErrorAction SilentlyContinue");
            sb.AppendLine($"Get-AppxProvisionedPackage -Online | Where-Object {{ $_.DisplayName -like '*{p}*' -and $_.DisplayName -notlike '*ExperienceHost*' }} | Remove-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue");
        }

        return sb.ToString();
    }

    private static string EscapeForSingleQuotedPs(string value)
        => value.Replace("'", "''");

    #endregion

    #region Відновлення (Локальне + Microsoft Store)

    public static async Task<bool> RestorePackageAsync(DebloatItem item)
    {
        RunOnUi(() => item.IsBusy = true);
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

            AppLogger.Log(restoredLocally
                ? $"Відновлено UWP додаток: {item.Name}"
                : $"Відкрито сторінку встановлення у Store: {item.Name}",
                restoredLocally ? "SUCCESS" : "INFO");
            return restoredLocally;
        });

        RunOnUi(() => item.IsInstalled = result);
        RunOnUi(() => item.IsBusy = false);
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

            // Реєстрація всіх пакетів може тривати довго — даємо до 2 хвилин і чесно звітуємо про результат
            bool ok = RunPowerShellEncoded(script, timeoutMs: 120_000);
            progress?.Report((100, ok
                ? "Усі системні UWP-додатки відновлено."
                : "Перереєстрацію не вдалося завершити (таймаут або відсутні права адміністратора)."));
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
                proc?.WaitForExit(20000);
            }

            using (var key = Registry.ClassesRoot.OpenSubKey(@"CLSID\{018D5C66-4533-4307-9B53-224DE2ED1FE6}", true))
            {
                key?.SetValue("System.IsPinnedToNameSpaceTree", 0, RegistryValueKind.DWord);
            }
            using (var keyWow = Registry.ClassesRoot.OpenSubKey(@"Wow6432Node\CLSID\{018D5C66-4533-4307-9B53-224DE2ED1FE6}", true))
            {
                keyWow?.SetValue("System.IsPinnedToNameSpaceTree", 0, RegistryValueKind.DWord);
            }

            using (var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
            {
                run?.DeleteValue("OneDrive", false);
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

    #region Допоміжні методи CLI

    /// <summary>Виконання PowerShell-команди з перевіркою ExitCode та таймаутом.</summary>
    private static bool RunPowerShellQuiet(string command, int timeoutMs = 60_000)
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

            if (proc == null) return false;

            if (!proc.WaitForExit(timeoutMs))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return false;
            }

            return proc.ExitCode == 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// Виконання складного багаторядкового сценарію PowerShell без проблем із екрануванням лапок:
    /// сценарій кодується Base64 (UTF-16LE) і передається через -EncodedCommand.
    /// </summary>
    private static bool RunPowerShellEncoded(string script, int timeoutMs = 60_000)
    {
        try
        {
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            if (proc == null) return false;

            if (!proc.WaitForExit(timeoutMs))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return false;
            }

            return proc.ExitCode == 0;
        }
        catch { return false; }
    }

    /// <summary>Виконання на UI-потоці; в тестах/CLI (без Application) — інлайн.</summary>
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

    #endregion
}
