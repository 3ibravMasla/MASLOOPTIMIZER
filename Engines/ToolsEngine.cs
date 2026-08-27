using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MASLOOPTIMIZER;

public enum ToolSortMode
{
    Default,
    InstalledFirst,
    NotInstalledFirst,
    NameAscending,
    NameDescending,
    Category
}

public class ToolStats
{
    public int Total { get; set; }
    public int Installed { get; set; }
    public int Available => Total - Installed;
    public int CategoriesCount { get; set; }
}

public class ToolItem : INotifyPropertyChanged
{
    public ToolItem()
    {
        LocalizationManager.Instance.PropertyChanged += OnLocalizationChanged;
    }

    private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(SiteButtonText));
    }

    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Залізо & Сенсори";
    public string Description { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string WingetId { get; set; } = string.Empty;
    public string SpecialAction { get; set; } = string.Empty;
    public string ExeCheckName { get; set; } = string.Empty;

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
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    private string _statusText = string.Empty;
    public string StatusText
    {
        get
        {
            var loc = LocalizationManager.Instance;
            if (IsBusy) return loc["Common.Installing"];
            if (!string.IsNullOrWhiteSpace(SpecialAction))
            {
                return SpecialAction switch
                {
                    "MAS" => loc["Tools.BtnActivate"],
                    "VCREDIST" => loc["Tools.BtnInstallAll"],
                    "DIRECTX" => loc["Tools.BtnUpdateDx"],
                    _ => _statusText
                };
            }
            return IsInstalled ? loc["Tools.BtnInstalled"] : loc["Tools.BtnInstall"];
        }
        set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public string SiteButtonText => LocalizationManager.Instance["Tools.BtnSite"];

    public string StatusColor => IsInstalled ? "#107C41" : "#0078D4";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class ToolsEngine
{
    public static List<ToolItem> Catalog { get; } = new()
    {
        new()
        {
            Id = "hwinfo",
            Name = "HWiNFO64",
            Category = "Залізо & Сенсори",
            Description = "Глибокий моніторинг датчиків: напруги, температури VRM, споживання (PPT/TDC/EDC), троттлінг та таймінги.",
            Url = "https://www.hwinfo.com/download/",
            WingetId = "REALiX.HWiNFO",
            ExeCheckName = "HWiNFO64.exe"
        },
        new()
        {
            Id = "cpuz",
            Name = "CPU-Z",
            Category = "Залізо & Сенсори",
            Description = "Детальні специфікації CPU, степінг ядра, ревізія материнської плати, SPD та реальна частота пам'яті.",
            Url = "https://www.cpuid.com/softwares/cpu-z.html",
            WingetId = "CPUID.CPU-Z",
            ExeCheckName = "cpuz.exe"
        },
        new()
        {
            Id = "gpuz",
            Name = "GPU-Z",
            Category = "Залізо & Сенсори",
            Description = "Повна діагностика відеокарт: версія vBIOS, лінії PCIe, напруги, Hotspot, Memory Temp та тип чіпів VRAM.",
            Url = "https://www.techpowerup.com/download/techpowerup-gpu-z/",
            WingetId = "TechPowerUp.GPU-Z",
            ExeCheckName = "GPU-Z.exe"
        },
        new()
        {
            Id = "zentimings",
            Name = "ZenTimings (AMD Ryzen AM4/AM5)",
            Category = "Залізо & Сенсори",
            Description = "Зчитування первинних, вторинних та третинних таймінгів RAM, шини FCLK/UCLK/MCLK та напруг SoC для AMD.",
            Url = "https://zentimings.proton.me/",
            WingetId = "",
            ExeCheckName = "ZenTimings.exe"
        },
        new()
        {
            Id = "fancontrol",
            Name = "Fan Control",
            Category = "Залізо & Сенсори",
            Description = "Найкраще відкрите керування вентиляторами: створення змішаних температурних кривих (CPU+GPU) та безшумний режим.",
            Url = "https://getfancontrol.com/",
            WingetId = "Rem0o.FanControl",
            ExeCheckName = "FanControl.exe"
        },
        new()
        {
            Id = "afterburner",
            Name = "MSI Afterburner & RTSS",
            Category = "Залізо & Сенсори",
            Description = "Розгін та андервольтинг відеокарт NVIDIA/AMD через криву вольт-частоти (Curve Editor) та екранний оверлей RTSS.",
            Url = "https://www.msi.com/Landing/afterburner/graphics-cards",
            WingetId = "Guru3D.Afterburner",
            ExeCheckName = "MSIAfterburner.exe"
        },
        new()
        {
            Id = "ryzenmaster",
            Name = "AMD Ryzen Master",
            Category = "Залізо & Сенсори",
            Description = "Офіційна утиліта AMD для тонкого налаштування Curve Optimizer per-core, PBO та ручного тюнінгу Ryzen.",
            Url = "https://www.amd.com/en/products/software/ryzen-master.html",
            WingetId = "AdvancedMicroDevicesInc.RyzenMaster",
            ExeCheckName = "AMD Ryzen Master.exe"
        },
        new()
        {
            Id = "presentmon",
            Name = "Intel PresentMon",
            Category = "Залізо & Сенсори",
            Description = "Офіційна телеметрія Intel: метрика GPU Busy, затримки графічного конвеєра та взаємодія GPU з CPU.",
            Url = "https://game.intel.com/us/intel-presentmon/",
            WingetId = "Intel.PresentMon",
            ExeCheckName = "PresentMon.exe"
        },
        new()
        {
            Id = "quickcpu",
            Name = "QuickCPU",
            Category = "Залізо & Сенсори",
            Description = "Тонке керування процесором: вимкнення Core Parking, ліміти Turbo Boost, масштабування частот та C-States.",
            Url = "https://coderbag.com/product/quickcpu",
            WingetId = "CoderBag.QuickCPU",
            ExeCheckName = "QuickCpu.exe"
        },
        new()
        {
            Id = "aida64",
            Name = "AIDA64 Extreme",
            Category = "Залізо & Сенсори",
            Description = "Комплексний діагностичний комбайн: детальний аудит заліза, Cache & Memory Benchmark та стрес-тести.",
            Url = "https://www.aida64.com/downloads",
            WingetId = "FinalWire.AIDA64.Extreme",
            ExeCheckName = "aida64.exe"
        },
        new()
        {
            Id = "occt",
            Name = "OCCT (OverClock Checking Tool)",
            Category = "Стрес-тести & Бенчмарки",
            Description = "Індустріальний стрес-тест стабільності CPU, оперативної пам'яті (AVX2/SSE), GPU та блоку живлення.",
            Url = "https://www.ocbase.com/download",
            WingetId = "OCCT.OCCT",
            ExeCheckName = "OCCT.exe"
        },
        new()
        {
            Id = "furmark",
            Name = "FurMark 2",
            Category = "Стрес-тести & Бенчмарки",
            Description = "Стрес-тест екстремального навантаження GPU з підтримкою Vulkan/OpenGL для перевірки VRM та охолодження.",
            Url = "https://geeks3d.com/furmark/",
            WingetId = "Geeks3D.FurMark.2",
            ExeCheckName = "FurMark.exe"
        },
        new()
        {
            Id = "tm5",
            Name = "TestMem5 (TM5)",
            Category = "Стрес-тести & Бенчмарки",
            Description = "Еталонний тест RAM: виявлення помилок розгону, перегріву чіпів та нестабільних субтаймінгів DDR4/DDR5.",
            Url = "https://testmem.tz.ru/testmem5.htm",
            WingetId = "",
            ExeCheckName = "TM5.exe"
        },
        new()
        {
            Id = "cinebench",
            Name = "Cinebench 2024",
            Category = "Стрес-тести & Бенчмарки",
            Description = "Сучасний рендер-бенчмарк на рушії Redshift для оцінки чистої обчислювальної потужності CPU та GPU.",
            Url = "https://www.maxon.net/en/cinebench",
            WingetId = "Maxon.Cinebench",
            ExeCheckName = "Cinebench.exe"
        },
        new()
        {
            Id = "geekbench",
            Name = "Geekbench 6",
            Category = "Стрес-тести & Бенчмарки",
            Description = "Кросплатформний бенчмарк для оцінки Single-Core / Multi-Core продуктивності та обчислень OpenCL/Vulkan.",
            Url = "https://www.geekbench.com/",
            WingetId = "PrimateLabs.Geekbench.6",
            ExeCheckName = "Geekbench6.exe"
        },
        new()
        {
            Id = "latencymon",
            Name = "LatencyMon",
            Category = "FPS, Геймінг & Затримки",
            Description = "Головний аналізатор DPC/ISR затримок системних драйверів. Виявляє причини мікрофризів та розривів аудіо в іграх.",
            Url = "https://www.resplendence.com/latencymon",
            WingetId = "Resplendence.LatencyMon",
            ExeCheckName = "LatMon.exe"
        },
        new()
        {
            Id = "capframex",
            Name = "CapFrameX",
            Category = "FPS, Геймінг & Затримки",
            Description = "Професійний аналіз плавності геймплею: побудова графіків Frametime, метрики 1% та 0.1% Low FPS.",
            Url = "https://www.capframex.com/",
            WingetId = "CapFrameX.CapFrameX",
            ExeCheckName = "CapFrameX.exe"
        },
        new()
        {
            Id = "cru",
            Name = "Custom Resolution Utility (CRU)",
            Category = "FPS, Геймінг & Затримки",
            Description = "Розгін герцовки монітора (Hz), налаштування розтягнутих роздільних здатностей (4:3) та очищення блоків EDID.",
            Url = "https://www.monitortests.com/forum/Thread-Custom-Resolution-Utility-CRU",
            WingetId = "",
            ExeCheckName = "CRU.exe"
        },
        new()
        {
            Id = "nvidiaapp",
            Name = "NVIDIA App",
            Category = "FPS, Геймінг & Затримки",
            Description = "Сучасний центр керування графікою NVIDIA: заміна GeForce Experience без обов'язкового логіну та запис 4K AV1.",
            Url = "https://www.nvidia.com/en-us/software/nvidia-app/",
            WingetId = "Nvidia.NvidiaApp",
            ExeCheckName = "NVIDIA App.exe"
        },
        new()
        {
            Id = "obs",
            Name = "OBS Studio",
            Category = "FPS, Геймінг & Затримки",
            Description = "Відкритий інструмент для запису екрана та стрімінгу з підтримкою апаратних енкодерів NVENC (AV1), AMF та QuickSync.",
            Url = "https://obsproject.com/",
            WingetId = "OBSProject.OBSStudio",
            ExeCheckName = "obs64.exe"
        },
        new()
        {
            Id = "discord",
            Name = "Discord",
            Category = "FPS, Геймінг & Затримки",
            Description = "Платформа голосового зв'язку, чатів та трансляцій для геймерів із мінімальною затримкою передачі звуку.",
            Url = "https://discord.com/",
            WingetId = "Discord.Discord",
            ExeCheckName = "Discord.exe"
        },
        new()
        {
            Id = "adwcleaner",
            Name = "Malwarebytes AdwCleaner",
            Category = "Безпека & Сканери",
            Description = "Портативна утиліта проти рекламного ПЗ, браузерних шпигунів, стіллерів та небажаних служб. Працює без інсталяції.",
            Url = "https://www.malwarebytes.com/adwcleaner",
            WingetId = "Malwarebytes.AdwCleaner",
            ExeCheckName = "adwcleaner.exe"
        },
        new()
        {
            Id = "eek",
            Name = "Emsisoft Emergency Kit (EEK)",
            Category = "Безпека & Сканери",
            Description = "Потужний портативний сканер із подвійним рушієм. Знаходить приховані трояни, майнери та бекдори без встановлення служб.",
            Url = "https://www.emsisoft.com/en/home/emergencykit/",
            WingetId = "Emsisoft.EmergencyKit",
            ExeCheckName = "a2emergencykit.exe"
        },
        new()
        {
            Id = "hitmanpro",
            Name = "HitmanPro (Cloud Scanner)",
            Category = "Безпека & Сканери",
            Description = "Хмарний мультирушійний сканер екстреного реагування для перевірки на складні руткіти та інфіковані файли.",
            Url = "https://www.hitmanpro.com/en-us/downloads",
            WingetId = "Sophos.HitmanPro",
            ExeCheckName = "hitmanpro.exe"
        },
        new()
        {
            Id = "crystaldiskinfo",
            Name = "CrystalDiskInfo",
            Category = "Накопичувачі & Образи",
            Description = "Моніторинг здоров'я SSD/HDD, параметрів S.M.A.R.T., поточної температури контролера та зносу (TBW).",
            Url = "https://crystalmark.info/en/software/crystaldiskinfo/",
            WingetId = "CrystalDewWorld.CrystalDiskInfo",
            ExeCheckName = "DiskInfo64.exe"
        },
        new()
        {
            Id = "crystaldiskmark",
            Name = "CrystalDiskMark",
            Category = "Накопичувачі & Образи",
            Description = "Тестування швидкості лінійного та випадкового читання/запису (RND4K) для швидкісних NVMe SSD.",
            Url = "https://crystalmark.info/en/software/crystaldiskmark/",
            WingetId = "CrystalDewWorld.CrystalDiskMark",
            ExeCheckName = "DiskMark64.exe"
        },
        new()
        {
            Id = "wiztree",
            Name = "WizTree",
            Category = "Накопичувачі & Образи",
            Description = "Надшвидкий аналізатор зайнятого дискового простору через пряме читання таблиці MFT NTFS за 1 секунду.",
            Url = "https://diskanalyzer.com/download",
            WingetId = "AntibodySoftware.WizTree",
            ExeCheckName = "WizTree64.exe"
        },
        new()
        {
            Id = "everything",
            Name = "Everything Search",
            Category = "Накопичувачі & Образи",
            Description = "Миттєвий індексатор та пошуковик файлів на всіх накопичувачах за мілісекунди без навантаження на CPU.",
            Url = "https://www.voidtools.com/",
            WingetId = "voidtools.Everything",
            ExeCheckName = "Everything.exe"
        },
        new()
        {
            Id = "rufus",
            Name = "Rufus",
            Category = "Накопичувачі & Образи",
            Description = "Створення завантажувальних USB з автообходом вимог TPM 2.0, Secure Boot та облікового запису MS у Windows 11.",
            Url = "https://rufus.ie/",
            WingetId = "Rufus.Rufus",
            ExeCheckName = "rufus.exe"
        },
        new()
        {
            Id = "ventoy",
            Name = "Ventoy",
            Category = "Накопичувачі & Образи",
            Description = "Мультизавантажувальний USB-комбайн: дозволяє копіювати будь-які ISO/WIM образи на флешку без повторного форматування.",
            Url = "https://www.ventoy.net/",
            WingetId = "Ventoy.Ventoy",
            ExeCheckName = "Ventoy2Disk.exe"
        },
        new()
        {
            Id = "7zip",
            Name = "7-Zip",
            Category = "Система & Утиліти",
            Description = "Швидкий відкритий архіватор із високим рівнем стиснення 7z, шифруванням AES-256 та розпакуванням усіх форматів.",
            Url = "https://www.7-zip.org/",
            WingetId = "7zip.7zip",
            ExeCheckName = "7zFM.exe"
        },
        new()
        {
            Id = "nanazip",
            Name = "NanaZip (Форк 7-Zip під Windows 11)",
            Category = "Система & Утиліти",
            Description = "Сучасний форк 7-Zip, повністю інтегрований у нове контекстне меню Windows 11 із підтримкою темної теми.",
            Url = "https://github.com/M2Team/NanaZip",
            WingetId = "M2Team.NanaZip",
            ExeCheckName = "NanaZip.exe"
        },
        new()
        {
            Id = "systeminformer",
            Name = "System Informer (Process Hacker 3)",
            Category = "Система & Утиліти",
            Description = "Потужний диспетчер процесів, дескрипторів, мережевих з'єднань, служб та розблокування зайнятих файлів.",
            Url = "https://systeminformer.sourceforge.io/",
            WingetId = "SystemInformer.SystemInformer",
            ExeCheckName = "SystemInformer.exe"
        },
        new()
        {
            Id = "powertoys",
            Name = "Microsoft PowerToys",
            Category = "Система & Утиліти",
            Description = "Набір інструментів продуктивності: зручне зонування вікон FancyZones, Text Extractor (OCR) та швидкий ресайз картинок.",
            Url = "https://learn.microsoft.com/en-us/windows/powertoys/",
            WingetId = "Microsoft.PowerToys",
            ExeCheckName = "PowerToys.exe"
        },
        new()
        {
            Id = "notepadplusplus",
            Name = "Notepad++",
            Category = "Система & Утиліти",
            Description = "Швидкий текстовий редактор із підсвіткою коду, підтримкою UTF-8, регулярних виразів та порівнянням файлів.",
            Url = "https://notepad-plus-plus.org/",
            WingetId = "Notepad++.Notepad++",
            ExeCheckName = "notepad++.exe"
        },
        new()
        {
            Id = "ddu",
            Name = "Display Driver Uninstaller (DDU)",
            Category = "Драйвери & Деінсталятори",
            Description = "Повне очищення системи від залишків відеодрайверів NVIDIA, AMD та Intel у безпечному режимі.",
            Url = "https://www.wagnardsoft.com/display-driver-uninstaller-ddu-",
            WingetId = "Wagnardsoft.DisplayDriverUninstaller",
            ExeCheckName = "Display Driver Uninstaller.exe"
        },
        new()
        {
            Id = "nvcleanstall",
            Name = "NVCleanstall",
            Category = "Драйвери & Деінсталятори",
            Description = "Кастомізація інсталятора NVIDIA: видалення телеметрії, Shield, звукових служб та фонових трекерів.",
            Url = "https://www.techpowerup.com/download/techpowerup-nvcleanstall/",
            WingetId = "TechPowerUp.NVCleanstall",
            ExeCheckName = "NVCleanstall.exe"
        },
        new()
        {
            Id = "bcuninstaller",
            Name = "Bulk Crap Uninstaller (BCUninstaller)",
            Category = "Драйвери & Деінсталятори",
            Description = "Потужний деінсталятор програм та UWP-пакетів: автоматичний пошук залишків у реєстрі та пакетне тихе видалення.",
            Url = "https://www.bcuninstaller.com/",
            WingetId = "Klocman.BulkCrapUninstaller",
            ExeCheckName = "BCUninstaller.exe"
        },
        new()
        {
            Id = "geekuninstaller",
            Name = "Geek Uninstaller",
            Category = "Драйвери & Деінсталятори",
            Description = "Швидкий та портативний деінсталятор із функцією примусового видалення пошкоджених записів.",
            Url = "https://geekuninstaller.com/",
            WingetId = "GeekUninstaller.GeekUninstaller",
            ExeCheckName = "geek.exe"
        },
        new()
        {
            Id = "wireshark",
            Name = "Wireshark",
            Category = "Мережа & VPN",
            Description = "Професійний аналізатор мережевих пакетів: глибокий перехоплювач трафіку в реальному часі та діагностика мережі.",
            Url = "https://www.wireshark.org/",
            WingetId = "WiresharkFoundation.Wireshark",
            ExeCheckName = "Wireshark.exe"
        },
        new()
        {
            Id = "warp",
            Name = "Cloudflare WARP (1.1.1.1)",
            Category = "Мережа & VPN",
            Description = "Швидкісний клієнт Cloudflare із шифруванням DNS-запитів (DoH) та захищеним тунелюванням трафіку.",
            Url = "https://1.1.1.1/",
            WingetId = "Cloudflare.Warp",
            ExeCheckName = "Cloudflare WARP.exe"
        },
        new()
        {
            Id = "protonvpn",
            Name = "Proton VPN",
            Category = "Мережа & VPN",
            Description = "Швейцарський VPN-сервіс із відкритим кодом, суворою політикою без логів та безкоштовним тарифом.",
            Url = "https://protonvpn.com/",
            WingetId = "Proton.ProtonVPN",
            ExeCheckName = "ProtonVPN.exe"
        },
        new()
        {
            Id = "qbittorrent",
            Name = "qBittorrent",
            Category = "Мережа & VPN",
            Description = "Чистий та швидкий BitTorrent-клієнт без реклами, спаму та прихованих служб стеження.",
            Url = "https://www.qbittorrent.org/",
            WingetId = "qBittorrent.qBittorrent",
            ExeCheckName = "qbittorrent.exe"
        },
        new()
        {
            Id = "brave",
            Name = "Brave Browser",
            Category = "Браузери & Медіа",
            Description = "Швидкісний приватний браузер на базі Chromium із вбудованим апаратним блокуванням реклами та трекерів.",
            Url = "https://brave.com/",
            WingetId = "Brave.Brave",
            ExeCheckName = "brave.exe"
        },
        new()
        {
            Id = "chrome",
            Name = "Google Chrome",
            Category = "Браузери & Медіа",
            Description = "Популярний веб-браузер на Chromium із синхронізацією облікового запису Google та швидким рушієм V8.",
            Url = "https://www.google.com/chrome/",
            WingetId = "Google.Chrome",
            ExeCheckName = "chrome.exe"
        },
        new()
        {
            Id = "firefox",
            Name = "Mozilla Firefox",
            Category = "Браузери & Медіа",
            Description = "Незалежний браузер на власному рушії Gecko з акцентом на приватність та захист від відстеження.",
            Url = "https://www.mozilla.org/firefox/",
            WingetId = "Mozilla.Firefox",
            ExeCheckName = "firefox.exe"
        },
        new()
        {
            Id = "vlc",
            Name = "VLC Media Player",
            Category = "Браузери & Медіа",
            Description = "Універсальний відеоплеєр із вбудованими кодеками, підтримкою апаратного декодування GPU та всіх форматів.",
            Url = "https://www.videolan.org/vlc/",
            WingetId = "VideoLAN.VLC",
            ExeCheckName = "vlc.exe"
        },
        new()
        {
            Id = "sharex",
            Name = "ShareX",
            Category = "Браузери & Медіа",
            Description = "Відкритий інструмент захоплення екрана: швидкі скріншоти з OCR-розпізнаванням тексту, запис GIF та лінійка.",
            Url = "https://getsharex.com/",
            WingetId = "ShareX.ShareX",
            ExeCheckName = "ShareX.exe"
        },
        new()
        {
            Id = "rustdesk",
            Name = "RustDesk (Remote Desktop)",
            Category = "Браузери & Медіа",
            Description = "Відкритий клієнт віддаленого керування ПК із прямим підключенням без лімітів часу (заміна AnyDesk).",
            Url = "https://rustdesk.com/",
            WingetId = "RustDesk.RustDesk",
            ExeCheckName = "rustdesk.exe"
        },
        new()
        {
            Id = "steam",
            Name = "Steam",
            Category = "Ігрові лаунчери",
            Description = "Офіційний клієнт найбільшої ігрової платформи Steam для завантаження, оновлення та запуску ігор.",
            Url = "https://store.steampowered.com/about/",
            WingetId = "Valve.Steam",
            ExeCheckName = "steam.exe"
        },
        new()
        {
            Id = "epicgames",
            Name = "Epic Games Launcher",
            Category = "Ігрові лаунчери",
            Description = "Офіційний лаунчер Epic Games для Fortnite, Unreal Engine та регулярних роздач ліцензійних ігор.",
            Url = "https://store.epicgames.com/",
            WingetId = "EpicGames.EpicGamesLauncher",
            ExeCheckName = "EpicGamesLauncher.exe"
        },
        new()
        {
            Id = "battlenet",
            Name = "Battle.net",
            Category = "Ігрові лаунчери",
            Description = "Ігрова платформа Blizzard: Call of Duty (Warzone), Diablo IV, Overwatch 2, World of Warcraft.",
            Url = "https://www.blizzard.com/apps/battle.net/desktop",
            WingetId = "Blizzard.BattleNet",
            ExeCheckName = "Battle.net.exe"
        },
        new()
        {
            Id = "eaapp",
            Name = "EA App (Electronic Arts)",
            Category = "Ігрові лаунчери",
            Description = "Швидкісний лаунчер нового покоління для ігор Battlefield, EA SPORTS FC (FIFA), Apex Legends.",
            Url = "https://www.ea.com/ea-app",
            WingetId = "ElectronicArts.EADesktop",
            ExeCheckName = "EADesktop.exe"
        },
        new()
        {
            Id = "goggalaxy",
            Name = "GOG GALAXY",
            Category = "Ігрові лаунчери",
            Description = "Універсальний лаунчер від CD Projekt для ігор без DRM та об'єднання бібліотек з усіх інших платформ.",
            Url = "https://www.gog.com/galaxy",
            WingetId = "GOG.Galaxy",
            ExeCheckName = "GalaxyClient.exe"
        },
        new()
        {
            Id = "vcredist",
            Name = "Visual C++ Redistributable (All-in-One 2005–2026)",
            Category = "Runtime & Активація",
            Description = "Пакетне встановлення всіх версій Microsoft Visual C++ (2005–2026 x86/x64). Усуває помилки відсутніх .dll в іграх.",
            Url = "https://github.com/abbodi1406/vcredist",
            SpecialAction = "VCREDIST",
            WingetId = "abbodi1406.vcredist"
        },
        new()
        {
            Id = "directx",
            Name = "DirectX End-User Runtime Web Installer",
            Category = "Runtime & Активація",
            Description = "Офіційне онлайн-оновлення бібліотек DirectX 9/10/11 (d3dx9_*.dll, XAudio), необхідних для стабільного запуску ігор.",
            Url = "https://www.microsoft.com/en-us/download/details.aspx?id=35",
            SpecialAction = "DIRECTX"
        },
        new()
        {
            Id = "dotnetruntime",
            Name = ".NET Desktop Runtime 8",
            Category = "Runtime & Активація",
            Description = "Офіційне середовище виконання Microsoft .NET 8 (LTS) для роботи сучасних Windows-додатків.",
            Url = "https://dotnet.microsoft.com/download/dotnet/8.0",
            WingetId = "Microsoft.DotNet.DesktopRuntime.8"
        },
        new()
        {
            Id = "javaruntime",
            Name = "Java Runtime (Eclipse Temurin JRE 17)",
            Category = "Runtime & Активація",
            Description = "Швидке та стабільне середовище виконання Adoptium Java 17 для запуску серверів та Minecraft.",
            Url = "https://adoptium.net/",
            WingetId = "EclipseAdoptium.Temurin.17.JRE"
        },
        new()
        {
            Id = "mas",
            Name = "Microsoft Activation Scripts (MAS)",
            Category = "Runtime & Активація",
            Description = "Офіційний відкритий інструмент для цифрової HWID-активації Windows 10/11 та Office без вірусів.",
            Url = "https://massgrave.dev/",
            SpecialAction = "MAS"
        }
    };

    #region Контекстна фільтрація, сортування та статистика

    public static IEnumerable<ToolItem> GetFilteredAndSortedTools(
        string? category = null,
        string? searchQuery = null,
        ToolSortMode sortMode = ToolSortMode.Default)
    {
        var query = Catalog.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(category) && !category.Equals("Всі", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(t => string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            string q = searchQuery.Trim();
            query = query.Where(t =>
                t.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                t.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                t.Category.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                t.WingetId.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        return sortMode switch
        {
            ToolSortMode.InstalledFirst => query.OrderByDescending(t => t.IsInstalled).ThenBy(t => t.Name),
            ToolSortMode.NotInstalledFirst => query.OrderBy(t => t.IsInstalled).ThenBy(t => t.Name),
            ToolSortMode.NameAscending => query.OrderBy(t => t.Name),
            ToolSortMode.NameDescending => query.OrderByDescending(t => t.Name),
            ToolSortMode.Category => query.OrderBy(t => t.Category).ThenBy(t => t.Name),
            _ => query.OrderBy(t => t.Name)
        };
    }

    public static List<string> GetCategories()
    {
        var categories = Catalog.Select(t => t.Category).Distinct().OrderBy(c => c).ToList();
        categories.Insert(0, "Всі");
        return categories;
    }

    public static ToolStats GetStatistics()
    {
        return new ToolStats
        {
            Total = Catalog.Count,
            Installed = Catalog.Count(t => t.IsInstalled),
            CategoriesCount = Catalog.Select(t => t.Category).Distinct().Count()
        };
    }

    #endregion

    #region Детектування встановленого софту

    public static async Task DetectInstalledToolsAsync()
    {
        await Task.Run(() =>
        {
            var installedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            ScanUninstallRegistry(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", installedNames);
            ScanUninstallRegistry(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", installedNames);
            ScanUninstallRegistry(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", installedNames);

            foreach (var tool in Catalog)
            {
                if (!string.IsNullOrWhiteSpace(tool.SpecialAction))
                {
                    tool.IsInstalled = false;
                    continue;
                }

                bool found = installedNames.Any(name => name.Contains(tool.Name, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(tool.Id) && name.Contains(tool.Id, StringComparison.OrdinalIgnoreCase)));

                if (!found && !string.IsNullOrWhiteSpace(tool.ExeCheckName))
                {
                    string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    string pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                    string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

                    found = File.Exists(Path.Combine(pf, tool.Name, tool.ExeCheckName)) ||
                            File.Exists(Path.Combine(pfx86, tool.Name, tool.ExeCheckName)) ||
                            File.Exists(Path.Combine(localApp, "Programs", tool.Name, tool.ExeCheckName));
                }

                tool.IsInstalled = found;
            }
        });
    }

    private static void ScanUninstallRegistry(RegistryKey root, string subKey, HashSet<string> names)
    {
        try
        {
            using var key = root.OpenSubKey(subKey);
            if (key == null) return;

            foreach (var kn in key.GetSubKeyNames())
            {
                try
                {
                    using var appKey = key.OpenSubKey(kn);
                    string? disp = appKey?.GetValue("DisplayName")?.ToString();
                    if (!string.IsNullOrWhiteSpace(disp)) names.Add(disp.Trim());
                }
                catch { }
            }
        }
        catch { }
    }

    #endregion

    #region Виконання дій інсталяції

    public static async Task<bool> InstallWingetPackageAsync(ToolItem tool)
    {
        tool.IsBusy = true;
        tool.StatusText = "⏳ Встановлення...";

        bool success = await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "winget.exe",
                    Arguments = $"install --id \"{tool.WingetId}\" -e --silent --accept-source-agreements --accept-package-agreements --disable-interactivity",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var proc = Process.Start(psi);
                proc?.WaitForExit();
                return proc?.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        });

        tool.IsInstalled = success;
        tool.StatusText = success ? "✓ Встановлено" : "⚠️ Помилка";
        tool.IsBusy = false;
        AppLogger.Log(success
            ? $"Успішно встановлено через Winget: {tool.Name}"
            : $"Помилка встановлення утиліти: {tool.Name}",
            success ? "SUCCESS" : "ERROR");
        return success;
    }

    public static void RunMasActivation()
    {
        try
        {
            AppLogger.Log("Запущено скрипт активації Microsoft Activation Scripts (MAS)", "INFO");
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoExit -Command \"irm https://get.activated.win | iex\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Помилка запуску MAS: {ex.Message}", "ERROR");
        }
    }

    public static async Task<bool> InstallVcRedistAllAsync(ToolItem tool)
    {
        tool.IsBusy = true;
        tool.StatusText = "⏳ Встановлення VC++...";

        bool success = await Task.Run(async () =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "winget.exe",
                    Arguments = "install --id \"abbodi1406.vcredist\" -e --silent --accept-source-agreements --accept-package-agreements",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit();
                if (proc?.ExitCode == 0) return true;
            }
            catch { }

            try
            {
                string tempZip = Path.Combine(Path.GetTempPath(), "vcredist_aio.zip");
                string extractDir = Path.Combine(Path.GetTempPath(), "vcredist_aio_extracted");

                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromMinutes(3);
                    http.DefaultRequestHeaders.Add("User-Agent", "MASLOOPTIMIZER-Installer");

                    var data = await http.GetByteArrayAsync("https://github.com/abbodi1406/vcredist/releases/latest/download/VisualCppRedist_AIO_x86_x64.zip");
                    await File.WriteAllBytesAsync(tempZip, data);
                }

                if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
                ZipFile.ExtractToDirectory(tempZip, extractDir);

                var setupExe = Directory.GetFiles(extractDir, "*.exe").FirstOrDefault();
                if (setupExe != null)
                {
                    using var proc = Process.Start(new ProcessStartInfo
                    {
                        FileName = setupExe,
                        Arguments = "/ai",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    });
                    proc?.WaitForExit();
                    return proc?.ExitCode == 0;
                }
                return false;
            }
            catch
            {
                return false;
            }
        });

        tool.StatusText = success ? "✓ Встановлено" : "⚠️ Помилка";
        tool.IsBusy = false;
        AppLogger.Log(success
            ? "Visual C++ All-in-One успішно встановлено!"
            : "Помилка встановлення пакетів Visual C++",
            success ? "SUCCESS" : "ERROR");
        return success;
    }

    public static async Task<bool> InstallDirectXWebAsync(ToolItem tool)
    {
        tool.IsBusy = true;
        tool.StatusText = "⏳ Оновлення DirectX...";

        bool success = await Task.Run(async () =>
        {
            try
            {
                string setupExe = Path.Combine(Path.GetTempPath(), "dxwebsetup.exe");

                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromMinutes(2);
                    http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
                    var data = await http.GetByteArrayAsync("https://download.microsoft.com/download/1/7/1/1718CCC4-6315-4D8E-9543-8E28A4E18C4C/dxwebsetup.exe");
                    await File.WriteAllBytesAsync(setupExe, data);
                }

                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = setupExe,
                    Arguments = "/Q",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                proc?.WaitForExit();
                return proc?.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        });

        tool.StatusText = success ? "✓ Оновлено" : "⚠️ Помилка";
        tool.IsBusy = false;
        AppLogger.Log(success
            ? "Бібліотеки DirectX успішно оновлено!"
            : "Помилка оновлення бібліотек DirectX",
            success ? "SUCCESS" : "ERROR");
        return success;
    }

    public static void OpenUrl(string url)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{url}\"",
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }
    }

    #endregion
}