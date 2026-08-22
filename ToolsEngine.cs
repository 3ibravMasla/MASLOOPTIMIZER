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

namespace MASLOOPTIMIZER;

public class ToolItem : INotifyPropertyChanged
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Залізо & Сенсори";
    public string Description { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string WingetId { get; set; } = string.Empty;
    public string SpecialAction { get; set; } = string.Empty;

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            _isBusy = value;
            OnPropertyChanged();
        }
    }

    private string _statusText = "⬇️ Встановити";
    public string StatusText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(SpecialAction))
            {
                return SpecialAction switch
                {
                    "MAS" => "⚡ Активація",
                    "VCREDIST" => "⚡ Встановити все",
                    "DIRECTX" => "⚡ Оновити DirectX",
                    _ => _statusText
                };
            }
            return _statusText;
        }
        set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class ToolsEngine
{
    public static List<ToolItem> Catalog { get; } = new()
    {
        // =========================================================================
        // 1. ЗАЛІЗО, ДІАГНОСТИКА & СЕНСОРИ (AMD / INTEL / NVIDIA)
        // =========================================================================
        new()
        {
            Name = "HWiNFO64",
            Category = "Залізо & Сенсори",
            Description = "Глибокий моніторинг датчиків: напруги, температури VRM, споживання (PPT/TDC/EDC), троттлінг та таймінги.",
            Url = "https://www.hwinfo.com/download/",
            WingetId = "REALiX.HWiNFO"
        },
        new()
        {
            Name = "CPU-Z",
            Category = "Залізо & Сенсори",
            Description = "Детальні специфікації CPU, степінг ядра, ревізія материнської плати, SPD та реальна частота пам'яті.",
            Url = "https://www.cpuid.com/softwares/cpu-z.html",
            WingetId = "CPUID.CPU-Z"
        },
        new()
        {
            Name = "GPU-Z",
            Category = "Залізо & Сенсори",
            Description = "Повна діагностика відеокарт: версія vBIOS, лінії PCIe, напруги, Hotspot, Memory Temp та тип чіпів VRAM.",
            Url = "https://www.techpowerup.com/download/techpowerup-gpu-z/",
            WingetId = "TechPowerUp.GPU-Z"
        },
        new()
        {
            Name = "ZenTimings (AMD Ryzen)",
            Category = "Залізо & Сенсори",
            Description = "Зчитування первинних, вторинних та третинних таймінгів RAM, шини FCLK/UCLK/MCLK та напруг SoC для AMD AM4/AM5.",
            Url = "https://zentimings.proton.me/",
            WingetId = ""
        },
        new()
        {
            Name = "Fan Control",
            Category = "Залізо & Сенсори",
            Description = "Найкраще відкрите керування вентиляторами: створення змішаних температурних кривих (CPU+GPU) та безшумний режим.",
            Url = "https://getfancontrol.com/",
            WingetId = "Rem0o.FanControl"
        },
        new()
        {
            Name = "MSI Afterburner",
            Category = "Залізо & Сенсори",
            Description = "Розгін та андервольтинг відеокарт NVIDIA/AMD через криву вольт-частоти (Curve Editor) та екранний оверлей RTSS.",
            Url = "https://www.msi.com/Landing/afterburner/graphics-cards",
            WingetId = "Guru3D.Afterburner"
        },
        new()
        {
            Name = "AMD Ryzen Master",
            Category = "Залізо & Сенсори",
            Description = "Офіційна утиліта AMD для тонкого налаштування Curve Optimizer per-core, PBO та ручного тюнінгу Ryzen.",
            Url = "https://www.amd.com/en/products/software/ryzen-master.html",
            WingetId = "AdvancedMicroDevicesInc.RyzenMaster"
        },
        new()
        {
            Name = "Intel PresentMon",
            Category = "Залізо & Сенсори",
            Description = "Офіційна телеметрія Intel: метрика GPU Busy, затримки графічного конвеєра та взаємодія GPU з CPU.",
            Url = "https://game.intel.com/us/intel-presentmon/",
            WingetId = "Intel.PresentMon"
        },
        new()
        {
            Name = "QuickCPU",
            Category = "Залізо & Сенсори",
            Description = "Тонке керування процесором: вимкнення Core Parking, ліміти Turbo Boost, масштабування частот та C-States.",
            Url = "https://coderbag.com/product/quickcpu",
            WingetId = "CoderBag.QuickCPU"
        },
        new()
        {
            Name = "Core Temp",
            Category = "Залізо & Сенсори",
            Description = "Легкий моніторинг температури та споживання кожного фізичного ядра процесора у треї Windows.",
            Url = "https://www.alcpu.com/CoreTemp/",
            WingetId = "ALCPU.CoreTemp"
        },
        new()
        {
            Name = "AIDA64 Extreme",
            Category = "Залізо & Сенсори",
            Description = "Комплексний діагностичний комбайн: детальний аудит заліза, Cache & Memory Benchmark та стрес-тести.",
            Url = "https://www.aida64.com/downloads",
            WingetId = "FinalWire.AIDA64.Extreme"
        },

        // =========================================================================
        // 2. СТРЕС-ТЕСТИ, БЕНЧМАРКИ ТА СТАБІЛЬНІСТЬ
        // =========================================================================
        new()
        {
            Name = "OCCT (OverClock Checking Tool)",
            Category = "Стрес-тести",
            Description = "Індустріальний стрес-тест стабільності CPU, оперативної пам'яті (AVX2/SSE), GPU та блоку живлення.",
            Url = "https://www.ocbase.com/download",
            WingetId = "OCCT.OCCT"
        },
        new()
        {
            Name = "FurMark 2",
            Category = "Стрес-тести",
            Description = "Стрес-тест екстремального навантаження GPU з підтримкою Vulkan/OpenGL для перевірки VRM та системи охолодження.",
            Url = "https://geeks3d.com/furmark/",
            WingetId = "Geeks3D.FurMark.2"
        },
        new()
        {
            Name = "TestMem5 (TM5)",
            Category = "Стрес-тести",
            Description = "Еталонний тест RAM: виявлення помилок розгону, перегріву чіпів та нестабільних субтаймінгів DDR4/DDR5.",
            Url = "https://testmem.tz.ru/testmem5.htm",
            WingetId = ""
        },
        new()
        {
            Name = "Prime95",
            Category = "Стрес-тести",
            Description = "Екстремальний стрес-тест обчислень Small FFTs для перевірки максимального нагріву CPU та стабільності Curve Optimizer.",
            Url = "https://www.mersenne.org/download/",
            WingetId = ""
        },
        new()
        {
            Name = "Cinebench 2024",
            Category = "Стрес-тести",
            Description = "Сучасний рендер-бенчмарк на рушії Redshift для оцінки чистої обчислювальної потужності CPU та GPU.",
            Url = "https://www.maxon.net/en/cinebench",
            WingetId = "Maxon.Cinebench"
        },
        new()
        {
            Name = "3DMark",
            Category = "Стрес-тести",
            Description = "Ігровий бенчмарк (Steel Nomad, Time Spy, Speed Way, Port Royal) для оцінки графічної підсистеми.",
            Url = "https://benchmarks.ul.com/3dmark",
            WingetId = "UL.3DMark"
        },
        new()
        {
            Name = "Geekbench 6",
            Category = "Стрес-тести",
            Description = "Кросплатформний бенчмарк для оцінки Single-Core / Multi-Core продуктивності та обчислень OpenCL/Vulkan.",
            Url = "https://www.geekbench.com/",
            WingetId = "PrimateLabs.Geekbench.6"
        },

        // =========================================================================
        // 3. FPS, ГЕЙМІНГ & DPC ЗАТРИМКИ
        // =========================================================================
        new()
        {
            Name = "LatencyMon",
            Category = "FPS & Геймінг",
            Description = "Головний аналізатор DPC/ISR затримок системних драйверів. Виявляє причини мікрофризів та розривів аудіо в іграх.",
            Url = "https://www.resplendence.com/latencymon",
            WingetId = "Resplendence.LatencyMon"
        },
        new()
        {
            Name = "CapFrameX",
            Category = "FPS & Геймінг",
            Description = "Професійний аналіз плавності геймплею: побудова графіків Frametime, метрики 1% та 0.1% Low FPS.",
            Url = "https://www.capframex.com/",
            WingetId = "CapFrameX.CapFrameX"
        },
        new()
        {
            Name = "Custom Resolution Utility (CRU)",
            Category = "FPS & Геймінг",
            Description = "Розгін герцовки монітора (Hz), налаштування розтягнутих роздільних здатностей (4:3) та очищення блоків EDID.",
            Url = "https://www.monitortests.com/forum/Thread-Custom-Resolution-Utility-CRU",
            WingetId = ""
        },
        new()
        {
            Name = "RivaTuner Statistics Server (RTSS)",
            Category = "FPS & Геймінг",
            Description = "Еталонний обмежувач кадрів (Framerate Limiter) з рівним фреймтаймом та оверлеєм моніторингу датчиків.",
            Url = "https://www.guru3d.com/download/rtss-rivatuner-statistics-server-download/",
            WingetId = "Guru3D.RTSS"
        },
        new()
        {
            Name = "NVIDIA App",
            Category = "FPS & Геймінг",
            Description = "Сучасний центр керування графікою NVIDIA: заміна GeForce Experience без обов'язкового логіну та запис 4K AV1.",
            Url = "https://www.nvidia.com/en-us/software/nvidia-app/",
            WingetId = "Nvidia.NvidiaApp"
        },
        new()
        {
            Name = "OBS Studio",
            Category = "FPS & Геймінг",
            Description = "Відкритий інструмент для запису екрана та стрімінгу з підтримкою апаратних енкодерів NVENC (AV1), AMF та QuickSync.",
            Url = "https://obsproject.com/",
            WingetId = "OBSProject.OBSStudio"
        },
        new()
        {
            Name = "Discord",
            Category = "FPS & Геймінг",
            Description = "Платформа голосового зв'язку, чатів та трансляцій для геймерів із мінімальною затримкою передачі звуку.",
            Url = "https://discord.com/",
            WingetId = "Discord.Discord"
        },

        // =========================================================================
        // 4. БЕЗПЕКА & АВТОНОМНІ СКАНЕРИ (БЕЗ ФОНОВОГО НАВАНТАЖЕННЯ)
        // =========================================================================
        new()
        {
            Name = "Malwarebytes AdwCleaner",
            Category = "Безпека & Сканери",
            Description = "Портативна утиліта №1 проти рекламного ПЗ, браузерних шпигунів, стіллерів та небажаних служб. Працює без інсталяції.",
            Url = "https://www.malwarebytes.com/adwcleaner",
            WingetId = "Malwarebytes.AdwCleaner"
        },
        new()
        {
            Name = "Emsisoft Emergency Kit (EEK)",
            Category = "Безпека & Сканери",
            Description = "Потужний портативний сканер із подвійним рушієм. Знаходить приховані трояни, майнери та бекдори без встановлення служб.",
            Url = "https://www.emsisoft.com/en/home/emergencykit/",
            WingetId = "Emsisoft.EmergencyKit"
        },
        new()
        {
            Name = "HitmanPro (Cloud Scanner)",
            Category = "Безпека & Сканери",
            Description = "Хмарний мультирушійний сканер екстреного реагування для перевірки на складні руткіти та інфіковані системні файли.",
            Url = "https://www.hitmanpro.com/en-us/downloads",
            WingetId = "Sophos.HitmanPro"
        },
        new()
        {
            Name = "Kaspersky Virus Removal Tool (KVRT)",
            Category = "Безпека & Сканери",
            Description = "Портативна лікувальна утиліта для глибокого сканування пам'яті, автозапуску та секторів накопичувачів.",
            Url = "https://www.kaspersky.com/downloads/free-virus-removal-tool",
            WingetId = ""
        },

        // =========================================================================
        // 5. НАКОПИЧУВАЧІ ТА ОБРАЗИ
        // =========================================================================
        new()
        {
            Name = "CrystalDiskInfo",
            Category = "Накопичувачі",
            Description = "Моніторинг здоров'я SSD/HDD, параметрів S.M.A.R.T., поточної температури контролера та зносу (TBW).",
            Url = "https://crystalmark.info/en/software/crystaldiskinfo/",
            WingetId = "CrystalDewWorld.CrystalDiskInfo"
        },
        new()
        {
            Name = "CrystalDiskMark",
            Category = "Накопичувачі",
            Description = "Тестування швидкості лінійного та випадкового читання/запису (RND4K) для швидкісних NVMe SSD.",
            Url = "https://crystalmark.info/en/software/crystaldiskmark/",
            WingetId = "CrystalDewWorld.CrystalDiskMark"
        },
        new()
        {
            Name = "WizTree",
            Category = "Накопичувачі",
            Description = "Надшвидкий аналізатор зайнятого дискового простору через пряме читання таблиці MFT NTFS за 1 секунду.",
            Url = "https://diskanalyzer.com/download",
            WingetId = "AntibodySoftware.WizTree"
        },
        new()
        {
            Name = "Everything Search",
            Category = "Накопичувачі",
            Description = "Миттєвий індексатор та пошуковик файлів на всіх накопичувачах за мілісекунди без навантаження на CPU.",
            Url = "https://www.voidtools.com/",
            WingetId = "voidtools.Everything"
        },
        new()
        {
            Name = "Rufus",
            Category = "Накопичувачі",
            Description = "Створення завантажувальних USB з автообходом вимог TPM 2.0, Secure Boot та облікового запису MS у Windows 11.",
            Url = "https://rufus.ie/",
            WingetId = "Rufus.Rufus"
        },
        new()
        {
            Name = "Ventoy",
            Category = "Накопичувачі",
            Description = "Мультизавантажувальний USB-комбайн: дозволяє копіювати будь-які ISO/WIM образи на флешку без повторного форматування.",
            Url = "https://www.ventoy.net/",
            WingetId = "Ventoy.Ventoy"
        },
        new()
        {
            Name = "Samsung Magician",
            Category = "Накопичувачі",
            Description = "Фірмова панель Samsung: оновлення прошивок NVMe, перевірка оригінальності SSD та режим Full Performance.",
            Url = "https://semiconductor.samsung.com/consumer-storage/support/tools/",
            WingetId = "Samsung.SamsungMagician"
        },

        // =========================================================================
        // 6. СИСТЕМНІ ІНСТРУМЕНТИ, АРХІВАТОРИ & POWERTOYS
        // =========================================================================
        new()
        {
            Name = "7-Zip",
            Category = "Система",
            Description = "Швидкий відкритий архіватор із високим рівнем стиснення 7z, шифруванням AES-256 та розпакуванням усіх форматів.",
            Url = "https://www.7-zip.org/",
            WingetId = "7zip.7zip"
        },
        new()
        {
            Name = "WinRAR",
            Category = "Система",
            Description = "Класичний архіватор із підтримкою створення та розпакування архівів RAR/RAR5, ZIP, CAB та відновлення пошкоджених томів.",
            Url = "https://www.rarlab.com/",
            WingetId = "RARLab.WinRAR"
        },
        new()
        {
            Name = "NanaZip",
            Category = "Система",
            Description = "Сучасний форк 7-Zip, повністю інтегрований у нове контекстне меню Windows 11 із підтримкою темної теми.",
            Url = "https://github.com/M2Team/NanaZip",
            WingetId = "M2Team.NanaZip"
        },
        new()
        {
            Name = "System Informer (Process Hacker 3)",
            Category = "Система",
            Description = "Потужний диспетчер процесів, дескрипторів, мережевих з'єднань, служб та розблокування зайнятих файлів.",
            Url = "https://systeminformer.sourceforge.io/",
            WingetId = "SystemInformer.SystemInformer"
        },
        new()
        {
            Name = "Microsoft Sysinternals Suite",
            Category = "Система",
            Description = "Офіційний набір системних утиліт Microsoft: Autoruns (найповніший аудит автозапуску), Process Explorer, TCPView.",
            Url = "https://learn.microsoft.com/en-us/sysinternals/",
            WingetId = "Microsoft.SysinternalsSuite"
        },
        new()
        {
            Name = "Microsoft PowerToys",
            Category = "Система",
            Description = "Набір інструментів продуктивності: зручне зонування вікон FancyZones, Text Extractor (OCR) та швидкий ресайз картинок.",
            Url = "https://learn.microsoft.com/en-us/windows/powertoys/",
            WingetId = "Microsoft.PowerToys"
        },
        new()
        {
            Name = "Notepad++",
            Category = "Система",
            Description = "Швидкий текстовий редактор із підсвіткою коду, підтримкою UTF-8, регулярних виразів та порівнянням файлів.",
            Url = "https://notepad-plus-plus.org/",
            WingetId = "Notepad++.Notepad++"
        },

        // =========================================================================
        // 7. ДРАЙВЕРИ ТА ДЕІНСТАЛЯТОРИ
        // =========================================================================
        new()
        {
            Name = "Display Driver Uninstaller (DDU)",
            Category = "Драйвери & Деінсталятори",
            Description = "Повне очищення системи від залишків відеодрайверів NVIDIA, AMD та Intel у безпечному режимі.",
            Url = "https://www.wagnardsoft.com/display-driver-uninstaller-ddu-",
            WingetId = "Wagnardsoft.DisplayDriverUninstaller"
        },
        new()
        {
            Name = "NVCleanstall",
            Category = "Драйвери & Деінсталятори",
            Description = "Кастомізація інсталятора NVIDIA: видалення телеметрії, Shield, звукових служб та фонових трекерів.",
            Url = "https://www.techpowerup.com/download/techpowerup-nvcleanstall/",
            WingetId = "TechPowerUp.NVCleanstall"
        },
        new()
        {
            Name = "Bulk Crap Uninstaller (BCUninstaller)",
            Category = "Драйвери & Деінсталятори",
            Description = "Потужний деінсталятор програм та UWP-пакетів: автоматичний пошук залишків у реєстрі та пакетне тихе видалення.",
            Url = "https://www.bcuninstaller.com/",
            WingetId = "Klocman.BulkCrapUninstaller"
        },
        new()
        {
            Name = "Geek Uninstaller",
            Category = "Драйвери & Деінсталятори",
            Description = "Швидкий та портативний деінсталятор із функцією примусового видалення пошкоджених записів.",
            Url = "https://geekuninstaller.com/",
            WingetId = "GeekUninstaller.GeekUninstaller"
        },

        // =========================================================================
        // 8. МЕРЕЖА, АНАЛІЗ ТРАФІКУ ТА VPN
        // =========================================================================
        new()
        {
            Name = "DNS Jumper",
            Category = "Мережа & Аналіз",
            Description = "Швидка зміна та пошук найшвидших DNS-серверів для мінімізації мережевих затримок.",
            Url = "https://www.sordum.org/7952/dns-jumper-v2-3/",
            WingetId = ""
        },
        new()
        {
            Name = "Wireshark",
            Category = "Мережа & Аналіз",
            Description = "Професійний аналізатор мережевих пакетів: глибокий перехоплювач трафіку в реальному часі та діагностика мережі.",
            Url = "https://www.wireshark.org/",
            WingetId = "WiresharkFoundation.Wireshark"
        },
        new()
        {
            Name = "Cloudflare WARP (1.1.1.1)",
            Category = "Мережа & Аналіз",
            Description = "Швидкісний клієнт Cloudflare із шифруванням DNS-запитів (DoH) та захищеним тунелюванням трафіку.",
            Url = "https://1.1.1.1/",
            WingetId = "Cloudflare.Warp"
        },
        new()
        {
            Name = "Proton VPN",
            Category = "Мережа & Аналіз",
            Description = "Швейцарський VPN-сервіс із відкритим кодом, суворою політикою без логів та безкоштовним тарифом.",
            Url = "https://protonvpn.com/",
            WingetId = "Proton.ProtonVPN"
        },
        new()
        {
            Name = "qBittorrent",
            Category = "Мережа & Аналіз",
            Description = "Чистий та швидкий BitTorrent-клієнт без реклами, спаму та прихованих служб стеження.",
            Url = "https://www.qbittorrent.org/",
            WingetId = "qBittorrent.qBittorrent"
        },

        // =========================================================================
        // 9. БРАУЗЕРИ & МЕДІА
        // =========================================================================
        new()
        {
            Name = "Brave Browser",
            Category = "Браузери",
            Description = "Швидкісний приватний браузер на базі Chromium із вбудованим апаратним блокуванням реклами та трекерів.",
            Url = "https://brave.com/",
            WingetId = "Brave.Brave"
        },
        new()
        {
            Name = "Google Chrome",
            Category = "Браузери",
            Description = "Популярний веб-браузер на Chromium із синхронізацією облікового запису Google та швидким рушієм V8.",
            Url = "https://www.google.com/chrome/",
            WingetId = "Google.Chrome"
        },
        new()
        {
            Name = "Mozilla Firefox",
            Category = "Браузери",
            Description = "Незалежний браузер на власному рушії Gecko з акцентом на приватність та захист від відстеження.",
            Url = "https://www.mozilla.org/firefox/",
            WingetId = "Mozilla.Firefox"
        },
        new()
        {
            Name = "VLC Media Player",
            Category = "Медіа & Софт",
            Description = "Універсальний відеоплеєр із вбудованими кодеками, підтримкою апаратного декодування GPU та всіх форматів.",
            Url = "https://www.videolan.org/vlc/",
            WingetId = "VideoLAN.VLC"
        },
        new()
        {
            Name = "ShareX",
            Category = "Медіа & Софт",
            Description = "Відкритий інструмент захоплення екрана: швидкі скріншоти з OCR-розпізнаванням тексту, запис GIF та лінійка.",
            Url = "https://getsharex.com/",
            WingetId = "ShareX.ShareX"
        },
        new()
        {
            Name = "RustDesk (Remote Desktop)",
            Category = "Медіа & Софт",
            Description = "Відкритий клієнт віддаленого керування ПК із прямим підключенням без лімітів часу (заміна AnyDesk).",
            Url = "https://rustdesk.com/",
            WingetId = "RustDesk.RustDesk"
        },

        // =========================================================================
        // 10. ІГРОВІ ЛАУНЧЕРИ
        // =========================================================================
        new()
        {
            Name = "Steam",
            Category = "Ігрові лаунчери",
            Description = "Офіційний клієнт найбільшої ігрової платформи Steam для завантаження, оновлення та запуску ігор.",
            Url = "https://store.steampowered.com/about/",
            WingetId = "Valve.Steam"
        },
        new()
        {
            Name = "Epic Games Launcher",
            Category = "Ігрові лаунчери",
            Description = "Офіційний лаунчер Epic Games для Fortnite, Unreal Engine та регулярних роздач ліцензійних ігор.",
            Url = "https://store.epicgames.com/",
            WingetId = "EpicGames.EpicGamesLauncher"
        },
        new()
        {
            Name = "Battle.net",
            Category = "Ігрові лаунчери",
            Description = "Ігрова платформа Blizzard: Call of Duty (Warzone), Diablo IV, Overwatch 2, World of Warcraft.",
            Url = "https://www.blizzard.com/apps/battle.net/desktop",
            WingetId = "Blizzard.BattleNet"
        },
        new()
        {
            Name = "EA App (Electronic Arts)",
            Category = "Ігрові лаунчери",
            Description = "Швидкісний лаунчер нового покоління для ігор Battlefield, EA SPORTS FC (FIFA), Apex Legends.",
            Url = "https://www.ea.com/ea-app",
            WingetId = "ElectronicArts.EADesktop"
        },
        new()
        {
            Name = "GOG GALAXY",
            Category = "Ігрові лаунчери",
            Description = "Універсальний лаунчер від CD Projekt для ігор без DRM та об'єднання бібліотек з усіх інших платформ.",
            Url = "https://www.gog.com/galaxy",
            WingetId = "GOG.Galaxy"
        },

        // =========================================================================
        // 11. RUNTIME, БІБЛІОТЕКИ ТА АКТИВАЦІЯ
        // =========================================================================
        new()
        {
            Name = "Visual C++ Redistributable (All-in-One)",
            Category = "Runtime & Активація",
            Description = "Пакетне встановлення всіх версій Microsoft Visual C++ (2005–2026 x86/x64). Усуває помилки відсутніх .dll в іграх.",
            Url = "https://github.com/abbodi1406/vcredist",
            SpecialAction = "VCREDIST"
        },
        new()
        {
            Name = "DirectX End-User Runtime Web Installer",
            Category = "Runtime & Активація",
            Description = "Офіційне онлайн-оновлення бібліотек DirectX 9/10/11 (d3dx9_*.dll, XAudio), необхідних для стабільного запуску ігор.",
            Url = "https://www.microsoft.com/en-us/download/details.aspx?id=35",
            SpecialAction = "DIRECTX"
        },
        new()
        {
            Name = ".NET Desktop Runtime 8",
            Category = "Runtime & Активація",
            Description = "Офіційне середовище виконання Microsoft .NET 8 (LTS) для роботи сучасних Windows-додатків.",
            Url = "https://dotnet.microsoft.com/download/dotnet/8.0",
            WingetId = "Microsoft.DotNet.DesktopRuntime.8"
        },
        new()
        {
            Name = "Java Runtime (Eclipse Temurin JRE 17)",
            Category = "Runtime & Активація",
            Description = "Швидке та стабільне середовище виконання Adoptium Java 17 для запуску серверів та Minecraft.",
            Url = "https://adoptium.net/",
            WingetId = "EclipseAdoptium.Temurin.17.JRE"
        },
        new()
        {
            Name = "Microsoft Activation Scripts (MAS)",
            Category = "Runtime & Активація",
            Description = "Офіційний відкритий інструмент для цифрової HWID-активації Windows 10/11 та Office без вірусів.",
            Url = "https://massgrave.dev/",
            SpecialAction = "MAS"
        }
    };

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

        tool.StatusText = success ? "✓ Встановлено" : "⚠️ Помилка";
        tool.IsBusy = false;
        return success;
    }

    public static void RunMasActivation()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoExit -Command \"irm https://get.activated.win | iex\"",
                UseShellExecute = true
            });
        }
        catch { }
    }

    public static async Task<bool> InstallVcRedistAllAsync(ToolItem tool)
    {
        tool.IsBusy = true;
        tool.StatusText = "⏳ Завантаження...";

        bool success = await Task.Run(async () =>
        {
            try
            {
                string tempZip = Path.Combine(Path.GetTempPath(), "vcredist_aio.zip");
                string extractDir = Path.Combine(Path.GetTempPath(), "vcredist_aio_extracted");

                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromMinutes(3);
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
        return success;
    }

    public static async Task<bool> InstallDirectXWebAsync(ToolItem tool)
    {
        tool.IsBusy = true;
        tool.StatusText = "⏳ Завантаження...";

        bool success = await Task.Run(async () =>
        {
            try
            {
                string setupExe = Path.Combine(Path.GetTempPath(), "dxwebsetup.exe");

                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromMinutes(2);
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