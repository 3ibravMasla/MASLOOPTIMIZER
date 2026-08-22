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
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

// Явні аліаси для усунення колізій типів між WPF та WinForms
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Brushes = System.Windows.Media.Brushes;
using Brush = System.Windows.Media.Brush;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using MenuItem = System.Windows.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;
using Separator = System.Windows.Controls.Separator;

namespace MASLOOPTIMIZER;

public partial class WidgetWindow : Window
{
    private readonly DispatcherTimer _timer = new();
    private readonly DispatcherTimer _rgbTimer = new();
    private readonly DispatcherTimer _pulseTimer = new();
    private readonly DispatcherTimer _rebootCancelTimer = new();
    private readonly DispatcherTimer _shutdownCancelTimer = new();

    private PerformanceCounter? _cpuCounter;
    private double _rgbHue = 0;
    private double _pulseAngle = 0;
    private string _currentTheme = "Emerald";

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
        Loaded += WidgetWindow_Loaded;

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
            BtnShutdown.Content = "🛑 Вимкнути ПК";
            BtnShutdown.Background = HexBrush("#141824");
            BtnShutdown.Foreground = HexBrush("#F8FAFC");
        };

        Closing += (s, e) =>
        {
            e.Cancel = true;
            Hide();
        };
    }

    private void WidgetWindow_Loaded(object sender, RoutedEventArgs e)
    {
        LoadEmbeddedLogo();

        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue();
        }
        catch { }

        RestoreStateFromRegistry();
        UpdateNetworkBaseline();
        _timer.Start();
    }

    private void LoadEmbeddedLogo()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/icon/maslo.jpg", UriKind.Absolute);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = uri;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            WidgetLogo.Source = bitmap;
        }
        catch
        {
            string logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon", "maslo.jpg");
            if (File.Exists(logoPath))
            {
                WidgetLogo.Source = new BitmapImage(new Uri(logoPath, UriKind.Absolute));
            }
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
        if (bytesPerSec >= 1024 * 1024) return $"{bytesPerSec / (1024 * 1024):N1} MB/s";
        if (bytesPerSec >= 1024) return $"{bytesPerSec / (1024 * 1024):N0} KB/s";
        return $"{bytesPerSec:N0} B/s";
    }

    #endregion

    #region Теми та Реєстр

    public void ApplyTheme(string themeName)
    {
        _currentTheme = themeName;
        SaveStateToRegistry();

        _rgbTimer.Stop();
        _pulseTimer.Stop();

        MainWidgetBorder.Background = HexBrush("#E80A0D14");
        MainWidgetBorder.BorderThickness = new Thickness(1.8);
        MainGlow.Opacity = 0.35;
        LogoGlow.Opacity = 0.70;

        switch (themeName)
        {
            case "Rainbow":
                _rgbTimer.Start();
                break;

            case "Pulse":
                SetThemeColors("#00FF9D");
                _pulseTimer.Start();
                break;

            case "ToxicLime":
                SetThemeColors("#76FF03");
                break;

            case "CyberCyan":
                SetThemeColors("#00F0FF");
                break;

            case "LavaOrange":
                SetThemeColors("#FF6D00");
                break;

            case "Crimson":
                SetThemeColors("#FF1744");
                break;

            case "Violet":
                SetThemeColors("#B026FF");
                break;

            case "AmberGold":
                SetThemeColors("#FFD700");
                break;

            case "GhostGlass":
                MainWidgetBorder.Background = HexBrush("#220A0D14");
                MainWidgetBorder.BorderBrush = HexBrush("#60FFFFFF");
                LogoBorder.BorderBrush = HexBrush("#60FFFFFF");
                TxtBrandLeft.Foreground = Brushes.White;
                ProgressRam.Foreground = Brushes.White;
                MainGlow.Opacity = 0;
                LogoGlow.Opacity = 0;
                break;

            case "Stealth":
                MainWidgetBorder.Background = HexBrush("#F4080B10");
                MainWidgetBorder.BorderBrush = HexBrush("#2A344A");
                LogoBorder.BorderBrush = HexBrush("#2A344A");
                TxtBrandLeft.Foreground = HexBrush("#94A3B8");
                ProgressRam.Foreground = HexBrush("#38BDF8");
                MainGlow.Opacity = 0;
                LogoGlow.Opacity = 0;
                break;

            default:
                SetThemeColors("#00FF9D");
                break;
        }
    }

    private void SetThemeColors(string hex)
    {
        var b = HexBrush(hex);
        var c = (Color)ColorConverter.ConvertFromString(hex);
        MainWidgetBorder.BorderBrush = b;
        MainGlow.Color = c;
        LogoBorder.BorderBrush = b;
        LogoGlow.Color = c;
        TxtBrandLeft.Foreground = b;
        ProgressRam.Foreground = b;
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
                _currentTheme = key.GetValue("Theme")?.ToString() ?? "Emerald";
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

    #region Керування вікном

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
            SaveStateToRegistry();
        }
    }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        Width = Math.Max(280, Width + e.HorizontalChange);
        Height = Math.Max(200, Height + e.VerticalChange);
        SaveStateToRegistry();
    }

    private void BtnCloseWidget_MouseDown(object sender, MouseButtonEventArgs e)
    {
        SaveStateToRegistry();
        Hide();
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var cm = BuildContextMenu();
        cm.PlacementTarget = BtnSettings;
        cm.Placement = PlacementMode.Bottom;
        cm.IsOpen = true;
    }

    private ContextMenu BuildContextMenu()
    {
        var cm = new ContextMenu
        {
            Background = HexBrush("#0D111A"),
            BorderBrush = HexBrush("#00FF9D"),
            BorderThickness = new Thickness(1.2)
        };

        var mThemes = new MenuItem { Header = "🎨 Підсвітка та Стиль віджета" };
        var themes = new[]
        {
            ("Emerald", "🌟 Смарагдовий Неон"),
            ("Rainbow", "🌈 RGB Райдуга (Жива анімація)"),
            ("Pulse", "💓 Пульсація (Pulse Neon)"),
            ("ToxicLime", "⚡ Токсичний Лайм"),
            ("CyberCyan", "💎 Кібер Неон"),
            ("LavaOrange", "🔥 Вогняний Вулкан"),
            ("Crimson", "🩸 Crimson Blood"),
            ("Violet", "🔮 Ультрафіолет"),
            ("AmberGold", "👑 Золотий Буст"),
            ("GhostGlass", "👻 Прозоре Скло"),
            ("Stealth", "🌑 Stealth Matte")
        };

        foreach (var (id, title) in themes)
        {
            var tItem = new MenuItem
            {
                Header = title,
                IsCheckable = true,
                IsChecked = _currentTheme == id
            };
            tItem.Click += (s, e) => ApplyTheme(id);
            mThemes.Items.Add(tItem);
        }

        var mAuto = new MenuItem
        {
            Header = "🚀 Автозапуск віджета разом з Windows",
            IsCheckable = true,
            IsChecked = IsAutostartEnabled()
        };
        mAuto.Click += (s, e) => SetAutostart(!IsAutostartEnabled());

        var mTop = new MenuItem
        {
            Header = "📌 Поверх усіх вікон (TopMost)",
            IsCheckable = true,
            IsChecked = Topmost
        };
        mTop.Click += (s, e) =>
        {
            Topmost = !Topmost;
            SaveStateToRegistry();
        };

        var mOpen = new MenuItem
        {
            Header = "⚡ Відкрити MASLOOPTIMIZER",
            FontWeight = FontWeights.Bold,
            Foreground = HexBrush("#00FF9D")
        };
        mOpen.Click += (s, e) => TrayManager.ShowMainWindow();

        var mClose = new MenuItem
        {
            Header = "✕ Сховати віджет",
            Foreground = HexBrush("#F87171")
        };
        mClose.Click += (s, e) => Hide();

        cm.Items.Add(mThemes);
        cm.Items.Add(new Separator());
        cm.Items.Add(mAuto);
        cm.Items.Add(mTop);
        cm.Items.Add(new Separator());
        cm.Items.Add(mOpen);
        cm.Items.Add(new Separator());
        cm.Items.Add(mClose);

        return cm;
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

    private void BtnFlushRam_Click(object sender, RoutedEventArgs e)
    {
        BtnFlushRam.Content = "⏳...";
        GC.Collect();
        GC.WaitForPendingFinalizers();

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (!proc.HasExited && proc.Id > 4) EmptyWorkingSet(proc.Handle);
            }
            catch { }
        }

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

    private static SolidColorBrush HexBrush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    #endregion
}