using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
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
    private readonly DispatcherTimer _rgbTimer = new();
    private readonly DispatcherTimer _pulseTimer = new();
    private readonly DispatcherTimer _rebootCancelTimer = new();
    private readonly DispatcherTimer _shutdownCancelTimer = new();

    // Фонова перевірка оновлень GitHub (для режиму --widget-only)
    private readonly DispatcherTimer _updateCheckTimer = new();
    private bool _widgetUpdateDismissed;
    private bool _widgetUpdateDownloading;
    private string? _widgetUpdateVersion;
    private string? _widgetUpdateUrl;

    private PerformanceCounter? _cpuCounter;
    private double _rgbHue = 0;
    private double _pulseAngle = 0;
    private string _currentTheme = "Emerald";

    // Поточний профіль живлення сегментного перемикача віджета (Eco → Std → Turbo).
    private SystemPowerMode _widgetPowerMode = SystemPowerMode.OriginalSnapshot;

    // Захист від повторного застосування під час програмної синхронізації сегментів.
    private bool _syncingPowerUi;

    private long _lastBytesReceived = 0;
    private long _lastBytesSent = 0;
    private DateTime _lastNetCheck = DateTime.Now;

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

    [DllImport("psapi.dll")]
    private static extern int EmptyWorkingSet(IntPtr hwProc);

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

        // Синхронізація кнопки Game Mode з реальним станом ядра (подія спрацьовує з фонового потоку).
        GameModeEngine.OnGameModeStateChanged += OnGameModeStateChanged;

        Closed += (s, e) =>
        {
            ThemeEngine.WidgetThemeRequired -= OnWidgetThemeRequired;
            GameModeEngine.OnGameModeStateChanged -= OnGameModeStateChanged;

            // Зупиняємо всі таймери та звільняємо PerformanceCounter,
            // щоб не лишати фонові оновлення/дескриптори після закриття.
            _timer.Stop();
            _rgbTimer.Stop();
            _pulseTimer.Stop();
            _rebootCancelTimer.Stop();
            _shutdownCancelTimer.Stop();
            _updateCheckTimer.Stop();

            try { _cpuCounter?.Dispose(); } catch { }
            _cpuCounter = null;
        };

        _timer.Interval = TimeSpan.FromMilliseconds(1500);
        _timer.Tick += Timer_Tick;

        _rgbTimer.Interval = TimeSpan.FromMilliseconds(35);
        _rgbTimer.Tick += (s, e) =>
        {
            _rgbHue = (_rgbHue + 2.5) % 360;
            var color = HsvToRgb(_rgbHue, 1.0, 1.0);
            var brush = new SolidColorBrush(color);
            MainWidgetBorder.BorderBrush = brush;
            MainGlow.Color = color;
            LogoBorder.BorderBrush = brush;
            LogoGlow.Color = color;
            TxtBrandLeft.Foreground = brush;
            ProgressRam.Foreground = brush;
        };

        _pulseTimer.Interval = TimeSpan.FromMilliseconds(40);
        _pulseTimer.Tick += (s, e) =>
        {
            _pulseAngle += 0.08;
            double val = (Math.Sin(_pulseAngle) + 1) / 2.0;
            MainGlow.Opacity = 0.15 + (val * 0.55);
            LogoGlow.Opacity = 0.25 + (val * 0.65);
        };

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
            e.Cancel = true;
            Hide();
        };
    }

    private async void WidgetWindow_Loaded(object sender, RoutedEventArgs e)
    {
        LoadEmbeddedLogo();

        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue();
        }
        catch { }

        try
        {
            var hw = await DiagnosticEngine.GetQuickHardwareInfoAsync();
            TxtCpuModel.Text = hw.CPU;
            TxtGpuModel.Text = hw.GPU;
        }
        catch { }

        RestoreStateFromRegistry();
        UpdatePowerCycleControl();
        RefreshWidgetGameModeToggle();
        UpdateNetworkBaseline();
        _timer.Start();

        // Періодична перевірка оновлень (раз на 30 хвилин) + перевірка при старті.
        _updateCheckTimer.Interval = TimeSpan.FromMinutes(30);
        _updateCheckTimer.Tick += (s, e) => _ = CheckForWidgetUpdatesAsync();
        _updateCheckTimer.Start();
        _ = CheckForWidgetUpdatesAsync();
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

    private void Timer_Tick(object? sender, EventArgs e)
    {
        try
        {
            if (_cpuCounter != null)
            {
                int cpuVal = (int)_cpuCounter.NextValue();
                TxtCpuLoad.Text = $"{cpuVal}%";
            }

            var mem = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(mem))
            {
                double totalGb = Math.Round(mem.ullTotalPhys / (1024.0 * 1024 * 1024), 1);
                double freeGb = Math.Round(mem.ullAvailPhys / (1024.0 * 1024 * 1024), 1);
                double usedGb = Math.Round(totalGb - freeGb, 1);
                int pct = (int)mem.dwMemoryLoad;

                TxtRamUsage.Text = $"{usedGb:N1} / {totalGb:N0} ГБ (Вільно: {freeGb:N1} ГБ)";
                TxtRamPercent.Text = $"{pct}%";
                ProgressRam.Value = pct;
            }

            UpdateGpuTelemetry();

            var cDrive = DriveInfo.GetDrives().FirstOrDefault(d => d.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase) && d.IsReady);
            if (cDrive != null)
            {
                double cFreeGb = Math.Round(cDrive.TotalFreeSpace / (1024.0 * 1024 * 1024), 0);
                double cTotalGb = Math.Round(cDrive.TotalSize / (1024.0 * 1024 * 1024), 0);
                TxtDiskUsage.Text = $"{cFreeGb:N0} / {cTotalGb:N0} ГБ";
            }

            UpdateNetworkTraffic();

            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            TxtUptime.Text = uptime.Days > 0
                ? $"{uptime.Days}д {uptime.Hours}г {uptime.Minutes}хв"
                : $"{uptime.Hours}г {uptime.Minutes}хв";
        }
        catch { }
    }

    private void UpdateGpuTelemetry()
    {
        Task.Run(() =>
        {
            try
            {
                string nvsmi = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"NVIDIA Corporation\NVSMI\nvidia-smi.exe");
                if (!File.Exists(nvsmi)) nvsmi = "nvidia-smi";

                var psi = new ProcessStartInfo
                {
                    FileName = nvsmi,
                    Arguments = "--query-gpu=utilization.gpu,temperature.gpu --format=csv,noheader,nounits",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string outStr = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(800);
                    if (!string.IsNullOrWhiteSpace(outStr))
                    {
                        var parts = outStr.Split(',');
                        if (parts.Length >= 2)
                        {
                            string load = parts[0].Trim();
                            string temp = parts[1].Trim();
                            Dispatcher.Invoke(() => TxtGpuLoad.Text = $"{load}% • {temp}°C");
                            return;
                        }
                    }
                }
            }
            catch { }

            Dispatcher.Invoke(() => TxtGpuLoad.Text = "Активна");
        });
    }

    private void UpdateNetworkBaseline()
    {
        try
        {
            var nics = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback);

            _lastBytesReceived = nics.Sum(n => n.GetIPv4Statistics().BytesReceived);
            _lastBytesSent = nics.Sum(n => n.GetIPv4Statistics().BytesSent);
            _lastNetCheck = DateTime.Now;
        }
        catch { }
    }

    private void UpdateNetworkTraffic()
    {
        try
        {
            var nics = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback).ToList();

            long curRecv = nics.Sum(n => n.GetIPv4Statistics().BytesReceived);
            long curSent = nics.Sum(n => n.GetIPv4Statistics().BytesSent);
            var now = DateTime.Now;
            double seconds = (now - _lastNetCheck).TotalSeconds;

            if (seconds > 0.5)
            {
                double downSpeed = (curRecv - _lastBytesReceived) / seconds;
                double upSpeed = (curSent - _lastBytesSent) / seconds;

                _lastBytesReceived = curRecv;
                _lastBytesSent = curSent;
                _lastNetCheck = now;

                TxtNetSpeed.Text = $"↓ {FormatSpeed(downSpeed)}  ↑ {FormatSpeed(upSpeed)}";
            }
        }
        catch { }
    }

    private static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec >= 1024 * 1024) return $"{bytesPerSec / (1024.0 * 1024.0):N1} MB/s";
        if (bytesPerSec >= 1024) return $"{bytesPerSec / 1024.0:N0} KB/s";
        return $"{bytesPerSec:N0} B/s";
    }

    #endregion

    #region Теми та Реєстр

    public void ApplyTheme(string themeName)
    {
        var theme = ThemeEngine.GetWidgetTheme(themeName) ?? ThemeEngine.WidgetThemes[0];
        _currentTheme = theme.Key;
        SaveStateToRegistry();

        _rgbTimer.Stop();
        _pulseTimer.Stop();

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

        if (theme.Animation == "Rainbow") _rgbTimer.Start();
        else if (theme.Animation == "Pulse") _pulseTimer.Start();
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

    private static Color HsvToRgb(double h, double s, double v)
    {
        int hi = (int)Math.Floor(h / 60) % 6;
        double f = (h / 60) - Math.Floor(h / 60);
        double p = v * (1 - s);
        double q = v * (1 - f * s);
        double t = v * (1 - (1 - f) * s);
        double r = 0, g = 0, b = 0;
        switch (hi)
        {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            case 5: r = v; g = p; b = q; break;
        }
        return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
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
            }
        }
        catch { }

        ChkTopMost.IsChecked = Topmost;
        ChkAutostart.IsChecked = IsAutostartEnabled();
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
        BtnFlushRam.Content = "⏳...";
        GC.Collect();
        GC.WaitForPendingFinalizers();

        await Task.Run(() =>
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (!proc.HasExited && proc.Id > 4 && !proc.ProcessName.Equals("System", StringComparison.OrdinalIgnoreCase))
                    {
                        EmptyWorkingSet(proc.Handle);
                    }
                }
                catch { }
                finally
                {
                    proc.Dispose();
                }
            }
        });

        BtnFlushRam.Content = "✓ Звільнено";
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
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

    private async void PowerSegment_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncingPowerUi) return;
        if (sender is not System.Windows.Controls.RadioButton rb || rb.Tag is not string tag) return;
        if (!Enum.TryParse(tag, out SystemPowerMode mode)) return;
        if (mode == _widgetPowerMode) return;

        _widgetPowerMode = mode;
        PowerSegmentPanel.IsEnabled = false;
        try
        {
            // Перед відновленням зліпка переконуємось, що він існує (інакше Std нічого не відновить).
            if (mode == SystemPowerMode.OriginalSnapshot)
                await PowerEngine.CaptureInitialSnapshotIfNeededAsync();

            await PowerEngine.ApplyProfileAsync(mode);
        }
        finally
        {
            PowerSegmentPanel.IsEnabled = true;
        }
    }

    private void UpdatePowerCycleControl()
    {
        _syncingPowerUi = true;
        try
        {
            RbEco.IsChecked = _widgetPowerMode == SystemPowerMode.EcoPowerSaver;
            RbStd.IsChecked = _widgetPowerMode == SystemPowerMode.OriginalSnapshot;
            RbTurbo.IsChecked = _widgetPowerMode == SystemPowerMode.UltraPerformance;
        }
        finally
        {
            _syncingPowerUi = false;
        }
    }

    private async void BtnWidgetGameMode_Click(object sender, RoutedEventArgs e)
    {
        ToggleButton? btn = sender as ToggleButton;
        if (btn is not null)
            btn.IsEnabled = false;

        try
        {
            await GameModeEngine.ToggleGameModeAsync();
        }
        finally
        {
            RefreshWidgetGameModeToggle();
            if (btn is not null)
                btn.IsEnabled = true;
        }
    }

    private void RefreshWidgetGameModeToggle()
    {
        bool active = GameModeEngine.IsGameModeActive;
        BtnWidgetGameMode.IsChecked = active;
        BtnWidgetGameMode.ToolTip = active
            ? "Game Mode увімкнено — натисніть, щоб вимкнути"
            : "Game Mode вимкнено — натисніть, щоб увімкнути";
    }

    private void OnGameModeStateChanged(bool isActive)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnGameModeStateChanged(isActive));
            return;
        }

        RefreshWidgetGameModeToggle();
    }

    private static SolidColorBrush HexBrush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    #endregion
}