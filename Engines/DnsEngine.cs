using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

namespace MASLOOPTIMIZER;

public enum DnsSortMode
{
    FastestFirst,
    SlowestFirst,
    ActiveFirst,
    NameAscending,
    NameDescending,
    Category
}

/// <summary>Групи-чіпси фільтрації DNS: Всі / Швидкі / Безпечні / Геймінг.</summary>
public enum DnsGroup
{
    All,
    Speed,
    Gaming,
    Security
}

public class DnsPreset : INotifyPropertyChanged, IWeakEventListener
{
    public DnsPreset()
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
        OnPropertyChanged(nameof(NameLocalized));
        OnPropertyChanged(nameof(DescriptionLocalized));
        OnPropertyChanged(nameof(CategoryLocalized));
        OnPropertyChanged(nameof(PingText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ButtonText));
    }

    /// <summary>Стабільний ключ для локалізації (Dns.Preset.{NameKey}.Name/Description).</summary>
    public string NameKey { get; set; } = string.Empty;

    /// <summary>Чіп-група (DnsGroup): Speed / Gaming / Security.</summary>
    public string Group { get; set; } = "Security";

    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Швидкість & Геймінг";
    public string Primary { get; set; } = string.Empty;
    public string Secondary { get; set; } = string.Empty;
    public string PingHost { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public string NameLocalized
        => LocalizationManager.Instance.TryGet($"Dns.Preset.{NameKey}.Name", out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : Name;

    public string DescriptionLocalized
        => LocalizationManager.Instance.TryGet($"Dns.Preset.{NameKey}.Description", out var desc) && !string.IsNullOrWhiteSpace(desc)
            ? desc
            : Description;

    public string CategoryLocalized
        => LocalizationManager.Instance.TryGet($"Categories.{Category}", out var cat) && !string.IsNullOrWhiteSpace(cat)
            ? cat
            : Category;

    private int _ping = 999;
    public int Ping
    {
        get => _ping;
        set
        {
            _ping = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PingText));
            OnPropertyChanged(nameof(PingColor));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
        }
    }

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set
        {
            _isActive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
            OnPropertyChanged(nameof(ButtonText));
        }
    }

    public string PingText => Ping < 900
        ? $"{Ping} ms"
        : LocalizationManager.Instance["Dns.PingTimeout"];
    public Brush PingColor => Ping < 30 ? ThemeEngine.Brush("SuccessBrush") : (Ping < 70 ? ThemeEngine.Brush("WarningBrush") : ThemeEngine.Brush("DangerBrush"));
    public string StatusText
    {
        get
        {
            var loc = LocalizationManager.Instance;
            if (IsActive) return loc["Dns.StatusActive"];
            return Ping < 900 ? loc["Dns.StatusAvailable"] : loc["Dns.StatusTimeout"];
        }
    }
    public Brush StatusColor => IsActive ? ThemeEngine.Brush("SuccessBrush") : (Ping < 900 ? ThemeEngine.Brush("StatusNeutralBrush") : ThemeEngine.Brush("DangerBrush"));
    public string ButtonText => IsActive
        ? LocalizationManager.Instance["Dns.BtnActive"]
        : LocalizationManager.Instance["Dns.BtnApply"];

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class OriginalAdapterDns
{
    public string AdapterName { get; set; } = string.Empty;
    public bool IsDhcp { get; set; } = true;
    public List<string> DnsServers { get; set; } = new();
}

public class OriginalDnsBackup
{
    public List<OriginalAdapterDns> Adapters { get; set; } = new();
    public DateTime BackupTime { get; set; } = DateTime.Now;
}

public static class DnsEngine
{
    public static OriginalDnsBackup? OriginalSettings { get; private set; }

    public static List<DnsPreset> Catalog { get; } = new()
    {
        // =========================================================================
        // 1. ШВИДКІСТЬ & ГЕЙМІНГ (ANYCAST & TIER-1)
        // =========================================================================
        new()
        {
            Name = "Cloudflare DNS (1.1.1.1)",
            Category = "Швидкість & Геймінг",
            Primary = "1.1.1.1",
            Secondary = "1.0.0.1",
            PingHost = "1.1.1.1",
            Description = "Мінімальний пінг для онлайн-ігор та швидкий відгук (Anycast у 300+ містах)."
        },
        new()
        {
            Name = "Google Public DNS",
            Category = "Швидкість & Геймінг",
            Primary = "8.8.8.8",
            Secondary = "8.8.4.4",
            PingHost = "8.8.8.8",
            Description = "Глобальна Anycast-інфраструктура Google із максимальною стабільністю."
        },
        new()
        {
            Name = "Gcore Global Anycast",
            Category = "Швидкість & Геймінг",
            Primary = "95.85.95.85",
            Secondary = "2.56.220.2",
            PingHost = "95.85.95.85",
            Description = "Швидка європейська мережа Anycast із прямими магістральними каналами."
        },
        new()
        {
            Name = "OpenDNS Home (Cisco)",
            Category = "Швидкість & Геймінг",
            Primary = "208.67.222.222",
            Secondary = "208.67.220.220",
            PingHost = "208.67.222.222",
            Description = "Хмарна платформа резолвінгу DNS від корпорації Cisco."
        },
        new()
        {
            Name = "Control D (Speed Anycast)",
            Category = "Швидкість & Геймінг",
            Primary = "76.76.2.0",
            Secondary = "76.76.10.0",
            PingHost = "76.76.2.0",
            Description = "Сучасний Anycast DNS із оптимізацією BGP-пакетів без фільтрації."
        },
        new()
        {
            Name = "Level 3 / Lumen Backbone",
            Category = "Швидкість & Геймінг",
            Primary = "4.2.2.1",
            Secondary = "4.2.2.2",
            PingHost = "4.2.2.1",
            Description = "Магістральний провайдер першого рівня (Tier-1) із прямими транзитними стиками."
        },
        new()
        {
            Name = "Level 3 Alternate",
            Category = "Швидкість & Геймінг",
            Primary = "209.244.0.3",
            Secondary = "209.244.0.4",
            PingHost = "209.244.0.3",
            Description = "Резервний магістральний пул Level 3."
        },
        new()
        {
            Name = "Verisign Public DNS",
            Category = "Швидкість & Геймінг",
            Primary = "64.6.64.6",
            Secondary = "64.6.65.6",
            PingHost = "64.6.64.6",
            Description = "Стабільний DNS від офіційного оператора доменних зон .com та .net."
        },
        new()
        {
            Name = "DNS.SB (Anycast Speed)",
            Category = "Швидкість & Геймінг",
            Primary = "185.222.222.222",
            Secondary = "45.11.45.11",
            PingHost = "45.11.45.11",
            Description = "Європейський Anycast DNS з оптимізацією маршрутів та DNSSEC."
        },
        new()
        {
            Name = "UltraDNS (Neustar 1)",
            Category = "Швидкість & Геймінг",
            Primary = "156.154.70.1",
            Secondary = "156.154.71.1",
            PingHost = "156.154.70.1",
            Description = "Високошвидкісні корпоративні резолвери UltraDNS."
        },
        new()
        {
            Name = "AliDNS Global",
            Category = "Швидкість & Геймінг",
            Primary = "223.5.5.5",
            Secondary = "223.6.6.6",
            PingHost = "223.5.5.5",
            Description = "Оптимізовані маршрути до азійських та європейських ігрових кластерів."
        },

        // =========================================================================
        // 2. БЛОКУВАННЯ РЕКЛАМИ ТА ТРЕКЕРІВ (ADBLOCK)
        // =========================================================================
        new()
        {
            Name = "AdGuard DNS (Блокування реклами)",
            Category = "Блокування реклами",
            Primary = "94.140.14.14",
            Secondary = "94.140.15.15",
            PingHost = "94.140.14.14",
            Description = "Блокує банери, аналітику, спливаючі вікна та телеметрію на рівні всієї ОС."
        },
        new()
        {
            Name = "Mullvad DNS (AdBlock + Malware)",
            Category = "Блокування реклами",
            Primary = "194.242.2.3",
            Secondary = "193.19.108.3",
            PingHost = "194.242.2.3",
            Description = "Відсікання реклами, трекінгу та фішингу на шведських серверах Mullvad."
        },
        new()
        {
            Name = "Control D (Ad & Tracker Block)",
            Category = "Блокування реклами",
            Primary = "76.76.2.2",
            Secondary = "76.76.10.2",
            PingHost = "76.76.2.2",
            Description = "Блокує рекламу, шпигунські скрипти та фонову телеметрію."
        },
        new()
        {
            Name = "Mullvad Extended (Ad + Social)",
            Category = "Блокування реклами",
            Primary = "194.242.2.9",
            Secondary = "193.19.108.9",
            PingHost = "194.242.2.9",
            Description = "Повне блокування реклами, трекерів та віджетів соціальних мереж."
        },
        new()
        {
            Name = "Alternate DNS (AdBlock)",
            Category = "Блокування реклами",
            Primary = "76.76.19.19",
            Secondary = "76.223.122.150",
            PingHost = "76.76.19.19",
            Description = "Хмарне блокування реклами, спам-ботів та небажаних редиректів."
        },

        // =========================================================================
        // 3. БЕЗПЕКА & КІБЕРЗАХИСТ (ANTI-MALWARE)
        // =========================================================================
        new()
        {
            Name = "Quad9 (Безпека & Захист)",
            Category = "Безпека & Захист",
            Primary = "9.9.9.9",
            Secondary = "149.112.112.112",
            PingHost = "9.9.9.9",
            Description = "Блокує фішинг, експлойти та ботнети за базами 20+ провідних лабораторій."
        },
        new()
        {
            Name = "Cloudflare Security (Anti-Malware)",
            Category = "Безпека & Захист",
            Primary = "1.1.1.2",
            Secondary = "1.0.0.2",
            PingHost = "1.1.1.2",
            Description = "Швидкість Cloudflare із автоматичною ізоляцією вірусів та шкідливих сайтів."
        },
        new()
        {
            Name = "Control D (Malware Shield)",
            Category = "Безпека & Захист",
            Primary = "76.76.2.1",
            Secondary = "76.76.10.1",
            PingHost = "76.76.2.1",
            Description = "Блокує небезпечні домени, фішингові форми та джерела криптомайнінгу."
        },
        new()
        {
            Name = "CleanBrowsing Security",
            Category = "Безпека & Захист",
            Primary = "185.228.168.9",
            Secondary = "185.228.169.9",
            PingHost = "185.228.168.9",
            Description = "Фільтрація фішингу, сайтів-клонів та спам-серверів розповсюдження вірусів."
        },
        new()
        {
            Name = "Comodo Secure DNS",
            Category = "Безпека & Захист",
            Primary = "8.26.56.26",
            Secondary = "8.20.247.20",
            PingHost = "8.26.56.26",
            Description = "Хмарний евристичний фільтр небезпечних веб-ресурсів від Comodo."
        },
        new()
        {
            Name = "Neustar Threat Protection",
            Category = "Безпека & Захист",
            Primary = "156.154.70.2",
            Secondary = "156.154.71.2",
            PingHost = "156.154.70.2",
            Description = "Корпоративний захист від кібератак, підміни DNS та шкідливих вузлів."
        },
        new()
        {
            Name = "CIRA Canadian Shield",
            Category = "Безпека & Захист",
            Primary = "149.112.121.20",
            Secondary = "149.112.122.20",
            PingHost = "149.112.121.20",
            Description = "Високий рівень захисту від крадіжки облікових даних та фішингу."
        },

        // =========================================================================
        // 4. ПРИВАТНІСТЬ & ZERO-LOGS
        // =========================================================================
        new()
        {
            Name = "Mullvad DNS (Zero-Logs)",
            Category = "Приватність",
            Primary = "194.242.2.2",
            Secondary = "193.19.108.2",
            PingHost = "194.242.2.2",
            Description = "Конфіденційність: сервери у Швеції, що працюють у RAM без збереження логів."
        },
        new()
        {
            Name = "DNS.WATCH (Німеччина)",
            Category = "Приватність",
            Primary = "84.200.69.80",
            Secondary = "84.200.70.40",
            PingHost = "84.200.69.80",
            Description = "Швидкісні сервери в Німеччині: повна відсутність цензури та логування."
        },
        new()
        {
            Name = "Digitale Gesellschaft (Швейцарія)",
            Category = "Приватність",
            Primary = "185.95.218.42",
            Secondary = "185.95.218.43",
            PingHost = "185.95.218.42",
            Description = "Некомерційний швейцарський DNS із захистом приватності та DNSSEC."
        },
        new()
        {
            Name = "Applied Privacy (Австрія / ЄС)",
            Category = "Приватність",
            Primary = "146.255.56.98",
            Secondary = "194.36.144.87",
            PingHost = "146.255.56.98",
            Description = "Незалежні австрійські сервери для захисту від провайдерського стеження."
        },
        new()
        {
            Name = "Quad9 Uncensored (Без фільтрації)",
            Category = "Приватність",
            Primary = "9.9.9.10",
            Secondary = "149.112.112.10",
            PingHost = "9.9.9.10",
            Description = "Оригінальний Quad9 без списків блокувань із підтримкою DNSSEC."
        },
        new()
        {
            Name = "Censurfridns (Данія)",
            Category = "Приватність",
            Primary = "91.239.100.100",
            Secondary = "89.233.43.71",
            PingHost = "91.239.100.100",
            Description = "Некомерційний проєкт із прямим підключенням до вузлів обміну трафіком ЄС."
        },
        new()
        {
            Name = "FreeDNS (Австрія)",
            Category = "Приватність",
            Primary = "37.235.1.174",
            Secondary = "37.235.1.177",
            PingHost = "37.235.1.174",
            Description = "Австрійський некомерційний DNS без збереження запитів."
        },
        new()
        {
            Name = "FDN (Франція)",
            Category = "Приватність",
            Primary = "80.67.169.12",
            Secondary = "80.67.169.40",
            PingHost = "80.67.169.12",
            Description = "Громадські сервери асоціації FDN для вільного доступу до мережі."
        },

        // =========================================================================
        // 5. СІМЕЙНИЙ ЗАХИСТ & SAFESEARCH (18+)
        // =========================================================================
        new()
        {
            Name = "Cloudflare Family (Захист сім'ї)",
            Category = "Сімейний захист",
            Primary = "1.1.1.3",
            Secondary = "1.0.0.3",
            PingHost = "1.1.1.3",
            Description = "Блокування шкідливих сайтів та контенту 18+ (SafeSearch) на базі Cloudflare."
        },
        new()
        {
            Name = "AdGuard Family (Безпечний пошук)",
            Category = "Сімейний захист",
            Primary = "94.140.14.15",
            Secondary = "94.140.15.16",
            PingHost = "94.140.14.15",
            Description = "Блокування реклами + безпечний пошук у Google, Bing та YouTube."
        },
        new()
        {
            Name = "CleanBrowsing Family Filter",
            Category = "Сімейний захист",
            Primary = "185.228.168.168",
            Secondary = "185.228.169.168",
            PingHost = "185.228.168.168",
            Description = "Строга сімейна фільтрація дорослого контенту, проксі, торрентів та SafeSearch."
        },
        new()
        {
            Name = "OpenDNS FamilyShield (Cisco)",
            Category = "Сімейний захист",
            Primary = "208.67.222.123",
            Secondary = "208.67.220.123",
            PingHost = "208.67.222.123",
            Description = "Примусове блокування сайтів 18+, фішингу та небезпечних доменів від Cisco."
        },
        new()
        {
            Name = "Neustar Family Secure",
            Category = "Сімейний захист",
            Primary = "156.154.70.3",
            Secondary = "156.154.71.3",
            PingHost = "156.154.70.3",
            Description = "Фільтрація контенту для дорослих та шкідливих посилань."
        }
    };

    #region Статична ініціалізація: NameKey та чіп-групи (Group)

    static DnsEngine()
    {
        foreach (var preset in Catalog)
        {
            if (string.IsNullOrWhiteSpace(preset.NameKey) && NameKeyMap.TryGetValue(preset.Name, out var key))
            {
                preset.NameKey = key;
            }

            if (GroupMap.TryGetValue(preset.Name, out var group))
            {
                preset.Group = group;
            }
        }
    }

    private static readonly Dictionary<string, string> NameKeyMap = new(StringComparer.Ordinal)
    {
        ["Cloudflare DNS (1.1.1.1)"] = "cloudflare",
        ["Google Public DNS"] = "google",
        ["Gcore Global Anycast"] = "gcore",
        ["OpenDNS Home (Cisco)"] = "opendns_home",
        ["Control D (Speed Anycast)"] = "controld_speed",
        ["Level 3 / Lumen Backbone"] = "level3",
        ["Level 3 Alternate"] = "level3_alt",
        ["Verisign Public DNS"] = "verisign",
        ["DNS.SB (Anycast Speed)"] = "dnssb",
        ["UltraDNS (Neustar 1)"] = "ultradns",
        ["AliDNS Global"] = "alidns",
        ["AdGuard DNS (Блокування реклами)"] = "adguard",
        ["Mullvad DNS (AdBlock + Malware)"] = "mullvad_adblock",
        ["Control D (Ad & Tracker Block)"] = "controld_ad",
        ["Mullvad Extended (Ad + Social)"] = "mullvad_extended",
        ["Alternate DNS (AdBlock)"] = "alternate_dns",
        ["Quad9 (Безпека & Захист)"] = "quad9",
        ["Cloudflare Security (Anti-Malware)"] = "cloudflare_security",
        ["Control D (Malware Shield)"] = "controld_malware",
        ["CleanBrowsing Security"] = "cleanbrowsing_security",
        ["Comodo Secure DNS"] = "comodo",
        ["Neustar Threat Protection"] = "neustar_threat",
        ["CIRA Canadian Shield"] = "cira",
        ["Mullvad DNS (Zero-Logs)"] = "mullvad_zero",
        ["DNS.WATCH (Німеччина)"] = "dnswatch",
        ["Digitale Gesellschaft (Швейцарія)"] = "digitale_gesellschaft",
        ["Applied Privacy (Австрія / ЄС)"] = "applied_privacy",
        ["Quad9 Uncensored (Без фільтрації)"] = "quad9_uncensored",
        ["Censurfridns (Данія)"] = "censurfridns",
        ["FreeDNS (Австрія)"] = "freedns",
        ["FDN (Франція)"] = "fdn",
        ["Cloudflare Family (Захист сім'ї)"] = "cloudflare_family",
        ["AdGuard Family (Безпечний пошук)"] = "adguard_family",
        ["CleanBrowsing Family Filter"] = "cleanbrowsing_family",
        ["OpenDNS FamilyShield (Cisco)"] = "opendns_familyshield",
        ["Neustar Family Secure"] = "neustar_family"
    };

    private static readonly Dictionary<string, string> GroupMap = new(StringComparer.Ordinal)
    {
        ["Google Public DNS"] = "Speed",
        ["Gcore Global Anycast"] = "Speed",
        ["OpenDNS Home (Cisco)"] = "Speed",
        ["Level 3 / Lumen Backbone"] = "Speed",
        ["Level 3 Alternate"] = "Speed",
        ["Verisign Public DNS"] = "Speed",
        ["UltraDNS (Neustar 1)"] = "Speed",
        ["Cloudflare DNS (1.1.1.1)"] = "Gaming",
        ["Control D (Speed Anycast)"] = "Gaming",
        ["DNS.SB (Anycast Speed)"] = "Gaming",
        ["AliDNS Global"] = "Gaming"
        // Всі інші пресети (захист, реклама, приватність, сім'я) — Security за замовчуванням
    };

    #endregion

    #region Контекстна фільтрація, сортування та категорії

    /// <summary>Список чіпсів-груп для фільтрації DNS (стабільний порядок).</summary>
    public static IReadOnlyList<DnsGroup> GetGroups() => new[] { DnsGroup.All, DnsGroup.Speed, DnsGroup.Security, DnsGroup.Gaming };

    public static bool PresetMatchesGroup(DnsPreset preset, DnsGroup group)
    {
        return group switch
        {
            DnsGroup.All => true,
            DnsGroup.Speed => string.Equals(preset.Group, "Speed", StringComparison.OrdinalIgnoreCase),
            DnsGroup.Gaming => string.Equals(preset.Group, "Gaming", StringComparison.OrdinalIgnoreCase),
            DnsGroup.Security => string.Equals(preset.Group, "Security", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    /// <summary>
    /// Отримує відфільтровані та відсортовані DNS-пресети за контекстом
    /// </summary>
    public static IEnumerable<DnsPreset> GetFilteredAndSortedPresets(
        string? category = null,
        string? searchQuery = null,
        DnsSortMode sortMode = DnsSortMode.FastestFirst,
        DnsGroup? group = null)
    {
        var query = Catalog.AsEnumerable();

        // 1. Фільтрація за чіп-групою (Всі / Швидкі / Безпечні / Геймінг)
        if (group.HasValue && group.Value != DnsGroup.All)
        {
            query = query.Where(d => PresetMatchesGroup(d, group.Value));
        }

        // 1a. Фільтрація за категорією
        if (!string.IsNullOrWhiteSpace(category) && !category.Equals("Всі", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(d => string.Equals(d.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        // 2. Пошуковий запит (назва, IP, опис)
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            string q = searchQuery.Trim();
            query = query.Where(d =>
                d.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                d.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                d.Primary.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                d.Secondary.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        // 3. Контекстне сортування
        return sortMode switch
        {
            DnsSortMode.FastestFirst => query.OrderBy(d => d.Ping),
            DnsSortMode.SlowestFirst => query.OrderByDescending(d => d.Ping),
            DnsSortMode.ActiveFirst => query.OrderByDescending(d => d.IsActive).ThenBy(d => d.Ping),
            DnsSortMode.NameAscending => query.OrderBy(d => d.Name),
            DnsSortMode.NameDescending => query.OrderByDescending(d => d.Name),
            DnsSortMode.Category => query.OrderBy(d => d.Category).ThenBy(d => d.Ping),
            _ => query.OrderBy(d => d.Ping)
        };
    }

    /// <summary>
    /// Отримує унікальний список категорій DNS
    /// </summary>
    public static List<string> GetCategories()
    {
        var categories = Catalog.Select(d => d.Category).Distinct().OrderBy(c => c).ToList();
        categories.Insert(0, "Всі");
        return categories;
    }

    /// <summary>
    /// Детектує поточний активний DNS у системі та оновлює статус пресетів
    /// </summary>
    public static void DetectActiveDns()
    {
        try
        {
            var activeAdapter = GetPhysicalActiveAdapters().FirstOrDefault();
            if (activeAdapter == null) return;

            var ipProps = activeAdapter.GetIPProperties();
            var dnsAddrs = ipProps.DnsAddresses
                .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(a => a.ToString())
                .ToList();

            string primaryDns = dnsAddrs.Count > 0 ? dnsAddrs[0] : string.Empty;

            foreach (var preset in Catalog)
            {
                preset.IsActive = !string.IsNullOrEmpty(primaryDns) &&
                    (preset.Primary.Equals(primaryDns, StringComparison.OrdinalIgnoreCase) ||
                     preset.Secondary.Equals(primaryDns, StringComparison.OrdinalIgnoreCase));
            }
        }
        catch { }
    }

    #endregion

    #region Замір Ping та Сортування

    public static async Task MeasureAllPingsAsync()
    {
        var hosts = Catalog.Select(c => c.PingHost).Distinct().ToList();
        var results = new ConcurrentDictionary<string, int>();

        var tasks = hosts.Select(async host =>
        {
            try
            {
                using var pinger = new Ping();
                var reply = await pinger.SendPingAsync(host, 500);
                results[host] = (reply.Status == IPStatus.Success) ? (int)reply.RoundtripTime : 999;
            }
            catch
            {
                results[host] = 999;
            }
        });

        await Task.WhenAll(tasks);

        foreach (var preset in Catalog)
        {
            if (results.TryGetValue(preset.PingHost, out int ms))
            {
                preset.Ping = ms;
            }
        }

        // Оновлюємо статус активності в системі
        DetectActiveDns();

        // Каталог НЕ сортуємо глобально — фільтр/сортування виконує GetFilteredAndSortedPresets
        // (інакше інші вкладки отримали б пересортований список).
    }

    public static DnsPreset? GetFastestPreset()
    {
        return Catalog.Where(p => p.Ping > 0 && p.Ping < 900).OrderBy(p => p.Ping).FirstOrDefault();
    }

    #endregion

    #region Збереження та Відкат початкового DNS

    public static void BackupOriginalDns()
    {
        if (OriginalSettings != null) return;

        try
        {
            var adapters = GetPhysicalActiveAdapters();
            var backup = new OriginalDnsBackup();

            // Бекапимо DNS ВСІХ фізичних адаптерів, щоб відкат повертав кожен інтерфейс
            foreach (var adapter in adapters)
            {
                try
                {
                    var ipProps = adapter.GetIPProperties();
                    var dnsAddrs = ipProps.DnsAddresses
                        .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        .Select(a => a.ToString())
                        .ToList();

                    var ipv4Props = ipProps.GetIPv4Properties();
                    bool isDhcp = ipv4Props?.IsDhcpEnabled ?? true;

                    backup.Adapters.Add(new OriginalAdapterDns
                    {
                        AdapterName = adapter.Name,
                        IsDhcp = isDhcp || dnsAddrs.Count == 0,
                        DnsServers = dnsAddrs
                    });
                }
                catch { }
            }

            if (backup.Adapters.Count > 0)
            {
                OriginalSettings = backup;
            }
        }
        catch { }
    }

    public static bool RestoreOriginalDns()
    {
        if (OriginalSettings == null || OriginalSettings.Adapters.Count == 0)
        {
            return ApplyDns("DHCP", "");
        }

        bool allOk = true;

        foreach (var adapter in OriginalSettings.Adapters)
        {
            try
            {
                bool ok;
                if (adapter.IsDhcp || adapter.DnsServers.Count == 0)
                {
                    ok = RunCmd($"powershell.exe -NoProfile -Command \"Set-DnsClientServerAddress -InterfaceAlias '{adapter.AdapterName}' -ResetServerAddresses\"");
                }
                else
                {
                    string prim = adapter.DnsServers[0];
                    string sec = adapter.DnsServers.Count > 1 ? adapter.DnsServers[1] : "";
                    string dnsList = string.IsNullOrWhiteSpace(sec) ? $"'{prim}'" : $"'{prim}','{sec}'";
                    ok = RunCmd($"powershell.exe -NoProfile -Command \"Set-DnsClientServerAddress -InterfaceAlias '{adapter.AdapterName}' -ServerAddresses {dnsList}\"");
                }

                if (!ok) allOk = false;
            }
            catch
            {
                allOk = false;
            }
        }

        FlushDnsCache();
        DetectActiveDns();

        // Бекап використано — наступне застосування створить свіжий
        OriginalSettings = null;

        AppLogger.Log(allOk ? "Початкові налаштування DNS успішно відновлено" : "Частину DNS-адаптерів не вдалося відновити", allOk ? "SUCCESS" : "WARN");
        return allOk;
    }

    #endregion

    #region Застосування та Очищення кешу

    public static bool ApplyDns(string primary, string secondary)
    {
        BackupOriginalDns();

        try
        {
            var adapters = GetPhysicalActiveAdapters();
            if (adapters.Count == 0) return false;

            bool allOk = true;

            foreach (var adapter in adapters)
            {
                bool ok;
                if (string.IsNullOrWhiteSpace(primary) || primary.Equals("DHCP", StringComparison.OrdinalIgnoreCase))
                {
                    ok = RunCmd($"powershell.exe -NoProfile -Command \"Set-DnsClientServerAddress -InterfaceAlias '{adapter.Name}' -ResetServerAddresses\"");
                }
                else
                {
                    string dnsList = string.IsNullOrWhiteSpace(secondary) ? $"'{primary.Trim()}'" : $"'{primary.Trim()}','{secondary.Trim()}'";
                    ok = RunCmd($"powershell.exe -NoProfile -Command \"Set-DnsClientServerAddress -InterfaceAlias '{adapter.Name}' -ServerAddresses {dnsList}\"");
                }

                if (!ok) allOk = false;
            }

            FlushDnsCache();
            DetectActiveDns();
            AppLogger.Log(string.IsNullOrWhiteSpace(primary) || primary.Equals("DHCP", StringComparison.OrdinalIgnoreCase)
                ? "DNS налаштування скинуто до автоматичних (DHCP)"
                : $"Активовано DNS-сервер: {primary} / {secondary}", "SUCCESS");
            return allOk;
        }
        catch
        {
            AppLogger.Log($"Помилка встановлення DNS ({primary})", "ERROR");
            return false;
        }
    }

    public static void FlushDnsCache()
    {
        try
        {
            RunCmd("ipconfig /flushdns");
            RunCmd("nbtstat -R");
        }
        catch { }
    }

    private static List<NetworkInterface> GetPhysicalActiveAdapters()
    {
        var list = new List<NetworkInterface>();
        try
        {
            var all = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var nic in all)
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                string desc = nic.Description.ToLower();
                if (desc.Contains("virtual") || desc.Contains("vmware") || desc.Contains("hyper-v") ||
                    desc.Contains("wsl") || desc.Contains("tailscale") || desc.Contains("zerotier") ||
                    desc.Contains("tap") || desc.Contains("vpn") || desc.Contains("npcap"))
                    continue;

                list.Add(nic);
            }

            if (list.Count == 0)
            {
                list = all.Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback).ToList();
            }
        }
        catch { }
        return list;
    }

    private static bool RunCmd(string command)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            if (proc == null) return false;

            // Set-DnsClientServerAddress через PowerShell може виконуватися довше 2 с
            if (!proc.WaitForExit(15_000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return false;
            }

            return proc.ExitCode == 0;
        }
        catch { return false; }
    }

    #endregion
}