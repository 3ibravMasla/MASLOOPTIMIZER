using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

// Явні аліаси для виключення колізій із WinForms
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Brushes = System.Windows.Media.Brushes;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace MASLOOPTIMIZER;

public partial class WidgetWindow : Window
{
    private readonly DispatcherTimer _timer = new();
    private readonly DispatcherTimer _rebootCancelTimer = new();
    private readonly DispatcherTimer _shutdownCancelTimer = new();

    // Фонова перевірка оновлень GitHub (для режиму --widget-only)
    private readonly DispatcherTimer _updateCheckTimer = new();
    private bool _widgetUpdateDismissed;
    private bool _widgetUpdateDownloading;
    private string? _widgetUpdateVersion;
    private string? _widgetUpdateUrl;

    private PerformanceCounter? _cpuCounter;

    // Кешовані GPU PerformanceCounter'и ("GPU Engine" / "Utilization Percentage").
    // Фаза 3.4: кеш періодично перебудовується (30 с), мертві інстанси звільняються, нові
    // (гра, браузер) потрапляють у список — за зразком DiagnosticEngine.RefreshGpuEngineCountersLocked.
    private List<PerformanceCounter>? _gpuCounters;
    private string[] _gpuInstances = Array.Empty<string>();
    private bool _gpuCountersReady;
    private long _gpuLastRefreshTick;
    private const long GpuRefreshIntervalMs = 30000;
    private const int GpuCounterLimit = 32;
    private readonly object _gpuSync = new();

    // Захист від накладання (overlapping) тіків DispatcherTimer.
    private bool _isUpdatingTelemetry;

    // Фаза 3.2: безпечний тік — CTS із таймаутом і watchdog-розблокування «завислого» тіку.
    private CancellationTokenSource? _tickCts;
    private long _tickStartedTick;
    private const int TickTimeoutMs = 1000;

    private string _currentTheme = "Emerald";

    // Кешовані глобальні лічильники мережі ("Network Interface" / "_Total").
    // PerformanceCounter створюється один раз, тому на кожному тіку немає аллокацій
    // ітерації GetAllNetworkInterfaces() і пов'язаного GC-навантаження.
    private PerformanceCounter? _netRecvCounter;
    private PerformanceCounter? _netSentCounter;

    // Кешований DriveInfo для диска C:, щоб не викликати DriveInfo.GetDrives() щосекунди (GC Spikes).
    private readonly DriveInfo _cachedDrive = new("C");

    // Фаза 3.7: кеш останніх значень — форматування рядків лише коли значення реально змінилось.
    private int _lastCpuVal = -1;
    private int _lastRamPct = -1;
    private long _lastDiskRefreshTick;
    private long _lastUptimeRefreshTick;

    // Фаза 3.5: «зменшені ефекти» — вибір користувача + автовизначення слабкого/інтегрованого GPU.
    private bool _reduceEffectsUser;
    private bool _lowEndGpu;
    private bool ReduceEffects => _reduceEffectsUser || _lowEndGpu;

    private bool _rebootConfirm = false;
    private bool _shutdownConfirm = false;

    private const string AutoRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutoRunName = "MASLOOPTIMIZER_HUD_Widget";
    private const string WidgetRegKey = @"Software\MASLOOPTIMIZER\Widget";

    #region Win32 API

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
        public MEMORYSTATUSEX() => dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    #endregion

    public WidgetWindow()
    {
        InitializeComponent();
        BuildThemeChips();
        Loaded += WidgetWindow_Loaded;

        // Синхронізація теми віджета з темою головної програми
        ThemeEngine.WidgetThemeRequired += OnWidgetThemeRequired;

        // Фаза 2.3: таймер телеметрії та анімації керуються центральним координатором активності.
        AppActivityCoordinator.ModeChanged += OnActivityModeChanged;

        // Видимість віджета — джерело прапорця Visible/Hidden для координатора (Show/Hide з трею).
        // Фаза 3.1: тут же миттєво зупиняємо тіки/анімації при прихованні та робимо разовий рефреш при показі.
        IsVisibleChanged += OnWidgetVisibilityChanged;

        // Єдина точка звільнення ресурсів. Closing лише ховає вікно, тому Closed може
        // не спрацювати при виході через трей — TrayManager.Shutdown() викликає цей самий метод.
        Closed += (s, e) => DisposeResources();

        // Стартовий інтервал — fallback; робочий інтервал завжди задає координатор.
        _timer.Interval = TimeSpan.FromMilliseconds(1500);
        _timer.Tick += Timer_Tick;

        _rebootCancelTimer.Interval = TimeSpan.FromSeconds(4);
        _rebootCancelTimer.Tick += (s, e) =>
        {
            _rebootCancelTimer.Stop();
            _rebootConfirm = false;
            BtnReboot.Content = "🔄 Перезапуск";
            BtnReboot.Background = HexBrush("#141824");
            BtnReboot.Foreground = HexBrush("#F8FAFC");
        };

        _shutdownCancelTimer.Interval = TimeSpan.FromSeconds(4);
        _shutdownCancelTimer.Tick += (s, e) =>
        {
            _shutdownCancelTimer.Stop();
            _shutdownConfirm = false;
            BtnShutdown.Content = "🛑 Вимкнути";
            BtnShutdown.Background = HexBrush("#141824");
            BtnShutdown.Foreground = HexBrush("#F8FAFC");
        };

        Closing += (s, e) =>
        {
            // Фаза 3.1: закриття = приховання у трей — без тіків телеметрії та анімацій.
            e.Cancel = true;
            _timer.Stop();
            StopWidgetAnimations();
            Hide();
        };
    }

    private bool _resourcesDisposed;

    /// <summary>
    /// Ідемпотентна точка звільнення ресурсів віджета: таймери, анімації,
    /// PerformanceCounter та підписки на статичні події. Викликається з Closed
    /// (реальне закриття) і з TrayManager.Shutdown() під час виходу з програми.
    /// </summary>
    public void DisposeResources()
    {
        if (_resourcesDisposed) return;
        _resourcesDisposed = true;

        ThemeEngine.WidgetThemeRequired -= OnWidgetThemeRequired;
        AppActivityCoordinator.ModeChanged -= OnActivityModeChanged;
        AppActivityCoordinator.SetWidgetVisible(false);

        _timer.Stop();
        StopWidgetAnimations();
        try { _tickCts?.Cancel(); } catch { }
        try { _tickCts?.Dispose(); } catch { }
        _tickCts = null;
        _rebootCancelTimer.Stop();
        _shutdownCancelTimer.Stop();
        _updateCheckTimer.Stop();

        try { _cpuCounter?.Dispose(); } catch { }
        _cpuCounter = null;

        try { _netRecvCounter?.Dispose(); } catch { }
        try { _netSentCounter?.Dispose(); } catch { }
        _netRecvCounter = null;
        _netSentCounter = null;

        lock (_gpuSync)
        {
            if (_gpuCounters != null)
            {
                foreach (var c in _gpuCounters) { try { c.Dispose(); } catch { } }
                _gpuCounters = null;
            }
            _gpuInstances = Array.Empty<string>();
            _gpuLastRefreshTick = 0;
            _gpuCountersReady = false;
        }
    }

    private async void WidgetWindow_Loaded(object sender, RoutedEventArgs e)
    {
        LoadEmbeddedLogo();

        // Фаза 3.3: лічильники створюються у фоні з повторною спробою — UI-потік не фризиться.
        _ = InitializeCountersAsync();

        try
        {
            var hw = await DiagnosticEngine.GetQuickHardwareInfoAsync();
            TxtCpuModel.Text = hw.CPU;
            TxtGpuModel.Text = hw.GPU;
            // Фаза 3.5: автовизначення слабкого/інтегрованого GPU для зменшених ефектів.
            _lowEndGpu = IsLowEndGpuName(hw.GPU);
        }
        catch { }

        RestoreStateFromRegistry();
        ApplyEffectsProfile();

        // Фаза 2.3: реєструємо видимість і застосовуємо режим координатора
        // (таймер/анімації стартують лише у видимому звичайному режимі).
        AppActivityCoordinator.SetWidgetVisible(IsVisible);
        ApplyActivityState();

        // Фаза 5.4: періодична перевірка (30 хв) + відкладена стартова (N хвилин + jitter),
        // щоб --widget на логіні не бив по мережі одразу.
        _updateCheckTimer.Interval = TimeSpan.FromMinutes(30);
        _updateCheckTimer.Tick += (s, e) => _ = CheckForWidgetUpdatesAsync();
        _updateCheckTimer.Start();
        _ = DeferredStartupUpdateCheckAsync();
    }

    /// <summary>
    /// Фаза 3.3: створення PerformanceCounter'ів (CPU, мережа) у фоні з повторною спробою —
    /// важка ініціалізація PDH більше не блокує UI-потік при холодному старті віджета.
    /// </summary>
    private async Task InitializeCountersAsync()
    {
        for (int attempt = 1; attempt <= 3 && !_resourcesDisposed; attempt++)
        {
            bool ok = await Task.Run(() =>
            {
                try
                {
                    if (_cpuCounter == null)
                    {
                        var cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                        _ = cpu.NextValue(); // прогрів: перше значення завжди 0
                        _cpuCounter = cpu;
                    }

                    if (_netRecvCounter == null || _netSentCounter == null)
                    {
                        var recv = new PerformanceCounter("Network Interface", "Bytes Received/sec", "_Total");
                        var sent = new PerformanceCounter("Network Interface", "Bytes Sent/sec", "_Total");
                        _ = recv.NextValue();
                        _ = sent.NextValue();
                        _netRecvCounter = recv;
                        _netSentCounter = sent;
                    }

                    return true;
                }
                catch { return false; }
            });

            if (ok) return;
            await Task.Delay(2000);
        }
    }

    #region Фонова перевірка оновлень (GitHub Toast)

    private async Task CheckForWidgetUpdatesAsync()
    {
        if (_widgetUpdateDismissed || _widgetUpdateDownloading) return;

        try
        {
            var (available, newVer, url) = await UpdateManager.CheckForUpdateAsync();
            if (!available || string.IsNullOrWhiteSpace(url) || _widgetUpdateDismissed) return;

            _widgetUpdateVersion = newVer;
            _widgetUpdateUrl = url;

            Dispatcher.Invoke(ShowWidgetUpdateToast);
        }
        catch { }
    }

    /// <summary>Фаза 5.4: відкладена стартова перевірка для --widget на логіні (N хвилин + jitter).</summary>
    private async Task DeferredStartupUpdateCheckAsync()
    {
        await UpdateManager.WaitStartupJitterAsync().ConfigureAwait(false);
        if (_resourcesDisposed) return;
        await CheckForWidgetUpdatesAsync();
    }

    private void ShowWidgetUpdateToast()
    {
        if (WidgetUpdateToast.Visibility == Visibility.Visible || _widgetUpdateDismissed || _widgetUpdateDownloading) return;

        var loc = LocalizationManager.Instance;
        WidgetUpdateText.Text = loc.Format("Update.ToastTitle", _widgetUpdateVersion ?? UpdateManager.CurrentVersion);
        WidgetUpdateBtn.Content = loc["Update.BtnNow"];
        WidgetUpdateToast.Visibility = Visibility.Visible;

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var slide = new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        WidgetUpdateToast.BeginAnimation(OpacityProperty, fade);
        if (WidgetUpdateToast.RenderTransform is TranslateTransform tr)
        {
            tr.BeginAnimation(TranslateTransform.YProperty, slide);
        }
    }

    private void WidgetUpdateDismiss_Click(object sender, RoutedEventArgs e)
    {
        _widgetUpdateDismissed = true; // Приховати до наступного перезапуску.
        WidgetUpdateToast.Visibility = Visibility.Collapsed;
    }

    private async void WidgetUpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_widgetUpdateUrl) || _widgetUpdateDownloading) return;

        _widgetUpdateDownloading = true;
        var loc = LocalizationManager.Instance;

        WidgetUpdateText.Text = loc.Format("Update.DownloadingTitle", _widgetUpdateVersion ?? UpdateManager.CurrentVersion);
        WidgetUpdateBtn.IsEnabled = false;
        WidgetUpdateDismiss.IsEnabled = false;
        WidgetUpdateBtn.Content = "0%";

        var progress = new Progress<double>(pct =>
        {
            if (double.IsFinite(pct))
            {
                Dispatcher.Invoke(() => WidgetUpdateBtn.Content = $"{pct:0}%");
            }
        });

        await UpdateManager.DownloadAndInstallUpdateAsync(_widgetUpdateUrl, progress);

        if (!_widgetUpdateDownloading) return;
        _widgetUpdateDownloading = false;
        WidgetUpdateBtn.IsEnabled = true;
        WidgetUpdateDismiss.IsEnabled = true;
        WidgetUpdateBtn.Content = loc["Update.BtnNow"];
    }

    #endregion

    private void LoadEmbeddedLogo()
    {
        bool loaded = false;
        try
        {
            var uri = new Uri("pack://application:,,,/icon/maslo.jpg", UriKind.Absolute);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = uri;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            WidgetLogo.Source = bitmap;
            loaded = true;
        }
        catch { }

        if (!loaded)
        {
            try
            {
                string logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon", "maslo.jpg");
                if (File.Exists(logoPath))
                {
                    WidgetLogo.Source = new BitmapImage(new Uri(logoPath, UriKind.Absolute));
                    loaded = true;
                }
            }
            catch { }
        }

        if (!loaded)
        {
            WidgetLogo.Visibility = Visibility.Collapsed;
            TxtLogoFallback.Visibility = Visibility.Visible;
        }
    }

    #region Телеметрія

    private async void Timer_Tick(object? sender, EventArgs e)
    {
        // Гонка планування: якщо режим уже тихий/прихований — жодного виклику NextValue.
        if (AppActivityCoordinator.IsQuietMode || !AppActivityCoordinator.WidgetVisible)
        {
            AppActivityCoordinator.RegisterSuppressedTick(); // Фаза 9.4: діагностика пригнічень
            return;
        }

        // Фаза 3.2: попередній тік ще виконується — пропускаємо, але лише в межах таймауту;
        // «зависший» тік примусово розблоковується, щоб телеметрія не померла назавжди.
        if (_isUpdatingTelemetry)
        {
            if (Environment.TickCount64 - _tickStartedTick < TickTimeoutMs)
            {
                AppActivityCoordinator.RegisterSuppressedTick(); // Фаза 9.4
                return;
            }

            AppLogger.Log($"Віджет: тік перевищив таймаут {TickTimeoutMs} мс — примусове розблокування", "WARN");
            AppActivityCoordinator.RegisterSuppressedTick(); // Фаза 9.4: попередній тік скасовано
            try { _tickCts?.Cancel(); } catch { }
            try { _tickCts?.Dispose(); } catch { }
            _tickCts = null;
        }

        _isUpdatingTelemetry = true;
        _tickStartedTick = Environment.TickCount64;
        AppActivityCoordinator.RegisterTelemetryTick(); // Фаза 9.4: фактичний тік телеметрії

        using var cts = new CancellationTokenSource(TickTimeoutMs);
        _tickCts = cts;
        var token = cts.Token;

        try
        {
            if (_cpuCounter != null)
            {
                int cpuVal = (int)_cpuCounter.NextValue();
                if (cpuVal != _lastCpuVal)
                {
                    _lastCpuVal = cpuVal;
                    TxtCpuLoad.Text = $"{cpuVal}%";
                }
            }

            var mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem))
            {
                int pct = (int)mem.dwMemoryLoad;
                if (pct != _lastRamPct)
                {
                    _lastRamPct = pct;
                    double totalGb = Math.Round(mem.ullTotalPhys / (1024.0 * 1024 * 1024), 1);
                    double freeGb = Math.Round(mem.ullAvailPhys / (1024.0 * 1024 * 1024), 1);
                    double usedGb = Math.Round(totalGb - freeGb, 1);

                    TxtRamUsage.Text = $"{usedGb:N1} / {totalGb:N0} ГБ (Вільно: {freeGb:N1} ГБ)";
                    TxtRamPercent.Text = $"{pct}%";
                    ProgressRam.Value = pct;
                }
            }

            await UpdateGpuTelemetryAsync(token);
            if (token.IsCancellationRequested) return;

            long nowTick = Environment.TickCount64;

            // Фаза 3.7: дисковий рядок — не частіше ніж раз на 10 с.
            if (_cachedDrive.IsReady && nowTick - _lastDiskRefreshTick >= 10000)
            {
                _lastDiskRefreshTick = nowTick;
                double cFreeGb = Math.Round(_cachedDrive.TotalFreeSpace / (1024.0 * 1024 * 1024), 0);
                double cTotalGb = Math.Round(_cachedDrive.TotalSize / (1024.0 * 1024 * 1024), 0);
                SetTextIfChanged(TxtDiskUsage, $"{cFreeGb:N0} / {cTotalGb:N0} ГБ");
            }

            UpdateNetworkTraffic();

            // Фаза 3.7: uptime — не частіше ніж раз на 5 с.
            if (nowTick - _lastUptimeRefreshTick >= 5000)
            {
                _lastUptimeRefreshTick = nowTick;
                var uptime = TimeSpan.FromMilliseconds(nowTick);
                SetTextIfChanged(TxtUptime, uptime.Days > 0
                    ? $"{uptime.Days}д {uptime.Hours}г {uptime.Minutes}хв"
                    : $"{uptime.Hours}г {uptime.Minutes}хв");
            }
        }
        catch { }
        finally
        {
            // Розблоковуємо лише якщо це все ще «наш» тік (watchdog міг уже запустити новий).
            if (ReferenceEquals(_tickCts, cts))
            {
                _tickCts = null;
                _isUpdatingTelemetry = false;
            }
        }
    }

    private async Task UpdateGpuTelemetryAsync(CancellationToken token)
    {
        double? load;
        try
        {
            load = await Task.Run(() => ReadGpuEngineLoadPercent(), token);
        }
        catch
        {
            load = null;
        }

        if (token.IsCancellationRequested) return;
        SetTextIfChanged(TxtGpuLoad, load.HasValue ? $"{load.Value:0.#}%" : "Активна");
    }

    /// <summary>
    /// Загальне 3D/Compute-навантаження GPU через Performance Counter "GPU Engine" (Win10+)
    /// без запуску зовнішніх процесів. Фаза 3.4: кеш оновлюється періодично (30 с) або примусово
    /// після зниклих інстансів — список живих лічильників не «застигає» на стартовому наборі.
    /// </summary>
    private double? ReadGpuEngineLoadPercent()
    {
        lock (_gpuSync)
        {
            RefreshGpuCountersLocked();
            if (_gpuCounters == null || _gpuCounters.Count == 0) return null;

            // Перше значення лічильника завжди 0 — прогріваємо його один раз після (пере)будови.
            if (!_gpuCountersReady)
            {
                foreach (var c in _gpuCounters) { try { _ = c.NextValue(); } catch { } }
                _gpuCountersReady = true;
                return null;
            }

            double total = 0;
            bool anyStale = false;
            foreach (var c in _gpuCounters)
            {
                try { total += c.NextValue(); }
                catch { anyStale = true; }
            }

            // Інстанс зник (процес завершився) — примусова перебудова кеша наступного разу.
            if (anyStale)
            {
                _gpuLastRefreshTick = 0;
                _gpuCountersReady = false;
            }

            return Math.Clamp(Math.Round(total, 1), 0.0, 100.0);
        }
    }

    /// <summary>Фаза 3.4: періодична перебудова кеша GPU-лічильників (під _gpuSync).</summary>
    private void RefreshGpuCountersLocked()
    {
        long now = Environment.TickCount64;
        bool due = _gpuCounters == null || _gpuCounters.Count == 0 ||
                   (now - _gpuLastRefreshTick) > GpuRefreshIntervalMs;

        if (!due) return;

        _gpuLastRefreshTick = now;
        RebuildGpuCountersLocked();
    }

    /// <summary>Перелік render-інстансів змінився → звільняємо старі лічильники й створюємо нові.</summary>
    private void RebuildGpuCountersLocked()
    {
        string[] currentInstances;
        try
        {
            var category = new PerformanceCounterCategory("GPU Engine");
            currentInstances = category.GetInstanceNames()
                .Where(static n => n.IndexOf("engtype_3D", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   n.IndexOf("engtype_Compute", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(static n => n, StringComparer.OrdinalIgnoreCase)
                .Take(GpuCounterLimit)
                .ToArray();
        }
        catch { currentInstances = Array.Empty<string>(); }

        if (_gpuCounters != null && currentInstances.SequenceEqual(_gpuInstances, StringComparer.OrdinalIgnoreCase))
            return; // Набір не змінився — існуючі лічильники лишаються валідними.

        if (_gpuCounters != null)
        {
            foreach (var old in _gpuCounters) { try { old.Dispose(); } catch { } }
        }

        var rebuilt = new List<PerformanceCounter>(currentInstances.Length);
        foreach (string instance in currentInstances)
        {
            try { rebuilt.Add(new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, true)); }
            catch { }
        }

        _gpuCounters = rebuilt;
        _gpuInstances = currentInstances;
        _gpuCountersReady = false; // новий набір — знову прогріваємо
    }

    private void UpdateNetworkTraffic()
    {
        try
        {
            // Глобальні лічильники-швидкості рівня ядра ("_Total") — без ітерації по інтерфейсах.
            if (_netRecvCounter is null || _netSentCounter is null) return;

            double downSpeed = _netRecvCounter.NextValue();
            double upSpeed = _netSentCounter.NextValue();

            SetTextIfChanged(TxtNetSpeed, $"↓ {FormatSpeed(downSpeed)}  ↑ {FormatSpeed(upSpeed)}");
        }
        catch { }
    }

    private static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec >= 1024 * 1024) return $"{bytesPerSec / (1024.0 * 1024.0):N1} MB/s";
        if (bytesPerSec >= 1024) return $"{bytesPerSec / 1024.0:N0} KB/s";
        return $"{bytesPerSec:N0} B/s";
    }

    /// <summary>Фаза 3.7: оновлення тексту лише коли рядок реально змінився (без зайвих інвалідацій).</summary>
    private static void SetTextIfChanged(TextBlock target, string text)
    {
        if (!string.Equals(target.Text, text, StringComparison.Ordinal)) target.Text = text;
    }

    #endregion

    #region Теми та Реєстр

    public void ApplyTheme(string themeName)
    {
        var theme = ThemeEngine.GetWidgetTheme(themeName) ?? ThemeEngine.WidgetThemes[0];
        _currentTheme = theme.Key;
        SaveStateToRegistry();

        StopWidgetAnimations();

        MainWidgetBorder.Background = HexBrush(theme.Background ?? "#F00B0E17");
        MainWidgetBorder.BorderThickness = new Thickness(1.8);

        var accentColor = (Color)ColorConverter.ConvertFromString(theme.Accent);
        MainWidgetBorder.BorderBrush = HexBrush(theme.Border ?? theme.Accent);
        MainGlow.Color = accentColor;
        LogoBorder.BorderBrush = HexBrush(theme.Border ?? theme.Accent);
        LogoGlow.Color = accentColor;
        TxtBrandLeft.Foreground = HexBrush(theme.BrandForeground ?? theme.Accent);
        ProgressRam.Foreground = HexBrush(theme.ProgressForeground ?? theme.Accent);
        MainGlow.Opacity = theme.GlowOpacity;
        LogoGlow.Opacity = theme.LogoGlowOpacity;

        // Анімації — лише коли віджет видимий і не в quiet-режимі (Фаза 2.3).
        StartThemeAnimationIfAllowed();
    }

    private void StopWidgetAnimations()
    {
        try { if (FindResource("RainbowStoryboard") is Storyboard rainbow) rainbow.Stop(this); } catch { }
        try { if (FindResource("PulseStoryboard") is Storyboard pulse) pulse.Stop(this); } catch { }
    }

    /// <summary>
    /// Фаза 3.1: Show/Hide з трею або кнопки закриття. Приховання зупиняє таймер і анімації
    /// безпосередньо; показ одразу робить разовий рефреш і рестарт за режимом координатора.
    /// </summary>
    private void OnWidgetVisibilityChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        bool visible = IsVisible;

        if (!visible)
        {
            _timer.Stop();
            StopWidgetAnimations();
        }

        AppActivityCoordinator.SetWidgetVisible(visible);

        if (visible)
        {
            ApplyActivityState();
            Timer_Tick(null, EventArgs.Empty); // разовий рефреш, не чекаючи тіку таймера
        }
    }

    /// <summary>
    /// Реакція на зміну режиму координатора активності (Фаза 2.3).
    /// Викликається з UI-диспетчера; CheckAccess — додатковий захист.
    /// </summary>
    private void OnActivityModeChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(OnActivityModeChanged));
            return;
        }

        ApplyActivityState();
    }

    /// <summary>
    /// Застосовує поточний режим: у quiet-режимі/прихованому стані зупиняє телеметрію
    /// та постійні анімації; у видимому звичайному — запускає з інтервалом координатора.
    /// </summary>
    private void ApplyActivityState()
    {
        var interval = AppActivityCoordinator.TelemetryInterval;
        bool active = AppActivityCoordinator.WidgetVisible
                      && !AppActivityCoordinator.IsQuietMode
                      && interval > TimeSpan.Zero;

        StopWidgetAnimations();

        if (!active)
        {
            _timer.Stop();
            return;
        }

        _timer.Interval = interval;
        _timer.Start();
        StartThemeAnimationIfAllowed();
    }

    /// <summary>Сторіборд теми віджета запускається лише у видимому звичайному режимі.</summary>
    private void StartThemeAnimationIfAllowed()
    {
        if (!AppActivityCoordinator.WidgetVisible || AppActivityCoordinator.IsQuietMode) return;

        var theme = ThemeEngine.GetWidgetTheme(_currentTheme);
        string? animation = theme?.Animation;

        // Фаза 3.5: «зменшені ефекти» — без постійного пульсу (Rainbow лишається з нижчим BlurRadius).
        if (ReduceEffects && animation == "Pulse") return;

        if (animation == "Rainbow") StartWidgetAnimation("RainbowStoryboard");
        else if (animation == "Pulse") StartWidgetAnimation("PulseStoryboard");
    }

    private void StartWidgetAnimation(string key)
    {
        if (FindResource(key) is Storyboard storyboard) storyboard.Begin(this, true);
    }

    /// <summary>
    /// Фаза 3.5: застосовує «зменшені ефекти» (нижчий BlurRadius тіні, без пульсу) для слабких/
    /// інтегрованих GPU або коли користувач увімкнув опцію в налаштуваннях віджета.
    /// </summary>
    private void ApplyEffectsProfile()
    {
        try { MainGlow.BlurRadius = ReduceEffects ? 8 : 20; } catch { }
        try { LogoGlow.BlurRadius = ReduceEffects ? 6 : 18; } catch { }

        // Перезапуск анімацій за новим профілем (у quiet/прихованому стані нічого не стартує).
        StopWidgetAnimations();
        ApplyActivityState();
    }

    /// <summary>Фаза 3.5: евристика слабкого/інтегрованого GPU за назвою з кешу DiagnosticEngine.</summary>
    private static bool IsLowEndGpuName(string? gpuName)
    {
        if (string.IsNullOrWhiteSpace(gpuName)) return false;
        string n = gpuName.ToLowerInvariant();

        if (n.Contains("microsoft basic")) return true;
        if (n.Contains("intel") &&
            (n.Contains("hd graphics") || n.Contains("uhd") || n.Contains("iris") || n.Contains("gma")))
            return true;
        if (n.Contains("radeon graphics") || n.Contains("radeon(tm) graphics") ||
            n.Contains("vega 8") || n.Contains("vega 7"))
            return true;

        return false;
    }

    private void BuildThemeChips()
    {
        ThemeChipsPanel.Children.Clear();
        foreach (var theme in ThemeEngine.WidgetThemes)
        {
            var chip = new Button
            {
                Content = theme.DisplayName,
                Tag = theme.Key,
                Style = (Style)FindResource("ThemeChipBtn"),
                ToolTip = theme.Category
            };
            chip.Click += ThemeChip_Click;
            ThemeChipsPanel.Children.Add(chip);
        }
    }

    private void SaveStateToRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(WidgetRegKey);
            key?.SetValue("Theme", _currentTheme);
            key?.SetValue("PosX", (int)Left);
            key?.SetValue("PosY", (int)Top);
            key?.SetValue("Width", (int)Width);
            key?.SetValue("Height", (int)Height);
            key?.SetValue("Topmost", Topmost ? 1 : 0);
            key?.SetValue("ReduceEffects", _reduceEffectsUser ? 1 : 0);
        }
        catch { }
    }

    private void RestoreStateFromRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(WidgetRegKey);
            if (key != null)
            {
                _currentTheme = key.GetValue("Theme")?.ToString() ?? ThemeEngine.CurrentWidgetThemeKey;
                int posX = (int)(key.GetValue("PosX") ?? (int)Left);
                int posY = (int)(key.GetValue("PosY") ?? (int)Top);
                int w = (int)(key.GetValue("Width") ?? (int)Width);
                int h = (int)(key.GetValue("Height") ?? (int)Height);

                if (posX > -1000 && posX < SystemParameters.VirtualScreenWidth - 100) Left = posX;
                if (posY > -1000 && posY < SystemParameters.VirtualScreenHeight - 100) Top = posY;
                if (w >= 280) Width = w;
                if (h >= 200) Height = h;

                Topmost = (int)(key.GetValue("Topmost") ?? 1) == 1;
                _reduceEffectsUser = (int)(key.GetValue("ReduceEffects") ?? 0) == 1;
            }
        }
        catch { }

        ChkTopMost.IsChecked = Topmost;
        ChkAutostart.IsChecked = IsAutostartEnabled();
        ChkReduceEffects.IsChecked = _reduceEffectsUser;
        ApplyTheme(_currentTheme);
    }

    private static bool IsAutostartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoRunKey, false);
            return key?.GetValue(AutoRunName) != null;
        }
        catch { return false; }
    }

    private static void SetAutostart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(AutoRunKey);
            if (enable)
            {
                string? exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exe)) key?.SetValue(AutoRunName, $"\"{exe}\" --widget");
            }
            else
            {
                key?.DeleteValue(AutoRunName, false);
            }
        }
        catch { }
    }

    #endregion

    #region Керування вікном та Налаштування

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
            return;

        // Не починаємо DragMove при кліку на інтерактивні елементи (кнопки/чекбокси),
        // інакше клік поглинається і натискання «Налаштування» не спрацьовує.
        if (IsInteractiveClick(e.OriginalSource))
            return;

        DragMove();
        SaveStateToRegistry();
    }

    private bool IsInteractiveClick(object source)
    {
        if (source is not DependencyObject current)
            return false;

        while (current != null)
        {
            if (ReferenceEquals(current, BtnSettings) ||
                ReferenceEquals(current, BtnCloseWidget) ||
                current is System.Windows.Controls.Primitives.ButtonBase)
            {
                return true;
            }

            var next = VisualTreeHelper.GetParent(current);
            if (next == null)
                next = LogicalTreeHelper.GetParent(current);
            current = next;
        }

        return false;
    }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        Width = Math.Max(280, Width + e.HorizontalChange);
        Height = Math.Max(200, Height + e.VerticalChange);
    }

    private void ResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        SaveStateToRegistry();
    }

    private void BtnCloseWidget_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            SaveStateToRegistry();
            Hide();
        }
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        MainHudView.Visibility = Visibility.Collapsed;
        SettingsOverlay.Visibility = Visibility.Visible;
    }

    private void BtnBackFromSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsOverlay.Visibility = Visibility.Collapsed;
        MainHudView.Visibility = Visibility.Visible;
        SaveStateToRegistry();
    }

    private void ThemeChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            ApplyTheme(tag);
        }
    }

    /// <summary>Отримує подію зміни теми від головної програми та перезастосовує палітру віджета.</summary>
    private void OnWidgetThemeRequired(string key)
    {
        Dispatcher.Invoke(() => ApplyTheme(key));
    }

    private void ChkAutostart_Click(object sender, RoutedEventArgs e)
    {
        SetAutostart(ChkAutostart.IsChecked == true);
    }

    private void ChkTopMost_Click(object sender, RoutedEventArgs e)
    {
        Topmost = ChkTopMost.IsChecked == true;
        SaveStateToRegistry();
    }

    private void ChkReduceEffects_Click(object sender, RoutedEventArgs e)
    {
        // Фаза 3.5: ручний вибір зменшених ефектів; автовизначення слабкого GPU лишається активним.
        _reduceEffectsUser = ChkReduceEffects.IsChecked == true;
        SaveStateToRegistry();
        ApplyEffectsProfile();
    }

    #endregion

    #region Кнопки дій

    private void BtnOpenApp_Click(object sender, RoutedEventArgs e)
    {
        TrayManager.ShowMainWindow();
    }

    private void BtnTaskMgr_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo { FileName = "taskmgr.exe", UseShellExecute = true });
    }

    private async void BtnFlushRam_Click(object sender, RoutedEventArgs e)
    {
        // Фаза 3.6: під час гри/Game Mode блокуючий GC.Collect недопустимий (stutter) — дія відкладається.
        if (AppActivityCoordinator.IsQuietMode)
        {
            BtnFlushRam.Content = "⏳ Після гри";
            var tq = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            tq.Tick += (s, ev) => { tq.Stop(); BtnFlushRam.Content = "🧹 Очистити ОЗП"; };
            tq.Start();
            return;
        }

        BtnFlushRam.Content = "⏳...";

        // Фаза 6: standalone-очистка Standby RAM (EmptyWorkingSet усіх процесів +
        // NtSetSystemInformation), незалежна від GameModeEngine; показуємо фактично звільнені МБ.
        long freedBytes = 0;
        try { freedBytes = await MonitorEngine.PurgeStandbyMemoryAsync(); } catch { }

        double freedMb = freedBytes / (1024.0 * 1024.0);
        BtnFlushRam.Content = freedMb >= 1 ? $"✓ Звільнено {freedMb:N0} МБ" : "✓ ОЗП очищено";

        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
        t.Tick += (s, ev) => { t.Stop(); BtnFlushRam.Content = "🧹 Очистити ОЗП"; };
        t.Start();
    }

    private void BtnEmptyTrash_Click(object sender, RoutedEventArgs e)
    {
        BtnEmptyTrash.Content = "⏳...";
        SHEmptyRecycleBin(IntPtr.Zero, null, 7);
        BtnEmptyTrash.Content = "✓ Очищено";
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        t.Tick += (s, ev) => { t.Stop(); BtnEmptyTrash.Content = "🗑️ Кошик"; };
        t.Start();
    }

    private void BtnReboot_Click(object sender, RoutedEventArgs e)
    {
        if (!_rebootConfirm)
        {
            _rebootConfirm = true;
            BtnReboot.Content = "⚠️ ТОЧНО? (4с)";
            BtnReboot.Background = HexBrush("#F59E0B");
            BtnReboot.Foreground = HexBrush("#0A0D14");
            _rebootCancelTimer.Start();
        }
        else
        {
            _rebootCancelTimer.Stop();
            BtnReboot.Content = "⏳ Ребут...";
            Process.Start(new ProcessStartInfo { FileName = "shutdown.exe", Arguments = "/r /f /t 0", CreateNoWindow = true, UseShellExecute = false });
        }
    }

    private void BtnShutdown_Click(object sender, RoutedEventArgs e)
    {
        if (!_shutdownConfirm)
        {
            _shutdownConfirm = true;
            BtnShutdown.Content = "⚠️ ТОЧНО? (4с)";
            BtnShutdown.Background = HexBrush("#DC2626");
            BtnShutdown.Foreground = Brushes.White;
            _shutdownCancelTimer.Start();
        }
        else
        {
            _shutdownCancelTimer.Stop();
            BtnShutdown.Content = "⏳ Вимикання...";
            Process.Start(new ProcessStartInfo { FileName = "shutdown.exe", Arguments = "/s /f /t 0", CreateNoWindow = true, UseShellExecute = false });
        }
    }

    private static SolidColorBrush HexBrush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    #endregion
}