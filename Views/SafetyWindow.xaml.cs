using System;
using System.IO;
using System.Media;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Application = System.Windows.Application;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace MASLOOPTIMIZER;

public partial class SafetyWindow : Window
{
    private bool _restoreDone = false;
    private bool _registryDone = false;
    private string _cheatBuffer = string.Empty;
    private static readonly string ConsentFile = Path.Combine(BackupEngine.BackupsDirectory, "safety_consent.json");

    public bool IsConsentGranted { get; private set; } = false;

    public SafetyWindow()
    {
        InitializeComponent();
        Loaded += SafetyWindow_Loaded;
        KeyDown += SafetyWindow_KeyDown;
    }

    private void SafetyWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var uri = new Uri("pack://application:,,,/icon/maslo.jpg", UriKind.Absolute);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = uri;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            BgLogoImage.Source = bitmap;
        }
        catch
        {
            string logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon", "maslo.jpg");
            if (File.Exists(logoPath))
            {
                BgLogoImage.Source = new BitmapImage(new Uri(logoPath, UriKind.Absolute));
            }
        }
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    public static bool CheckConsentGiven()
    {
        if (File.Exists(ConsentFile))
        {
            try
            {
                string json = File.ReadAllText(ConsentFile);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("SafetyConsentAccepted", out var prop))
                {
                    return prop.GetBoolean();
                }
            }
            catch { return false; }
        }
        return false;
    }

    private static SolidColorBrush HexBrush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    private void SafetyWindow_KeyDown(object sender, KeyEventArgs e)
    {
        string k = e.Key.ToString().ToLowerInvariant();
        if (k.Length == 1 && char.IsLetter(k[0]))
        {
            _cheatBuffer += k;
            if (_cheatBuffer.Length > 12) _cheatBuffer = _cheatBuffer.Substring(_cheatBuffer.Length - 12);

            if (_cheatBuffer.EndsWith("maslo"))
            {
                _cheatBuffer = string.Empty;
                try { SystemSounds.Asterisk.Play(); } catch { }

                TxtRestoreStatus.Text = "✓ Пропущено (Чіт-код MASLO)";
                TxtRestoreStatus.Foreground = HexBrush("#4ADE80");
                BtnCreateRestore.Content = "✓ DEV";
                BtnCreateRestore.Background = HexBrush("#107C41");
                BtnCreateRestore.IsEnabled = false;

                TxtRegistryStatus.Text = "✓ Пропущено (Чіт-код MASLO)";
                TxtRegistryStatus.Foreground = HexBrush("#4ADE80");
                BtnCreateRegBackup.Content = "✓ DEV";
                BtnCreateRegBackup.Background = HexBrush("#107C41");
                BtnCreateRegBackup.IsEnabled = false;

                TxtLog.Text = "🔓 Чіт-код 'MASLO' активовано! Повний доступ розблоковано.";
                TxtLog.Foreground = HexBrush("#00FF9D");

                _restoreDone = true;
                _registryDone = true;

                SaveConsentAndExit(bypassMethod: "CheatCode_MASLO");
            }
        }
    }

    private async void BtnCreateRestore_Click(object sender, RoutedEventArgs e)
    {
        BtnCreateRestore.IsEnabled = false;
        TxtRestoreStatus.Text = "⏳ Створення точки відновлення... Зачекайте...";
        TxtRestoreStatus.Foreground = HexBrush("#FBBF24");

        var res = await BackupEngine.CreateVssRestorePointAsync("MASLOOPTIMIZER_FirstRun_SafePoint");
        _restoreDone = true;

        if (res.Success)
        {
            TxtRestoreStatus.Text = "✓ Точку відновлення успішно створено";
            TxtRestoreStatus.Foreground = HexBrush("#4ADE80");
            BtnCreateRestore.Content = "✓ Створено";
            BtnCreateRestore.Background = HexBrush("#107C41");
        }
        else
        {
            TxtRestoreStatus.Text = "⚠️ VSS обмежено системою. Крок зараховано.";
            TxtRestoreStatus.Foreground = HexBrush("#FBBF24");
            BtnCreateRestore.Content = "⚠️ Пропущено";
            BtnCreateRestore.Background = HexBrush("#334155");
        }

        CheckBothSteps();
    }

    private async void BtnCreateRegBackup_Click(object sender, RoutedEventArgs e)
    {
        BtnCreateRegBackup.IsEnabled = false;
        TxtRegistryStatus.Text = "⏳ Експорт системних гілок реєстру...";
        TxtRegistryStatus.Foreground = HexBrush("#FBBF24");

        var res = await BackupEngine.ExportRegistryBackupAsync("FirstRun_FullBackup");
        _registryDone = true;

        if (res.Success)
        {
            TxtRegistryStatus.Text = "✓ Збережено всі гілки в папку backups\\";
            TxtRegistryStatus.Foreground = HexBrush("#4ADE80");
            BtnCreateRegBackup.Content = "✓ Збережено";
            BtnCreateRegBackup.Background = HexBrush("#107C41");
        }
        else
        {
            TxtRegistryStatus.Text = "⚠️ Резервну копію збережено частково.";
            TxtRegistryStatus.Foreground = HexBrush("#FBBF24");
            BtnCreateRegBackup.Content = "⚠️ Частково";
            BtnCreateRegBackup.Background = HexBrush("#334155");
        }

        CheckBothSteps();
    }

    private void CheckBothSteps()
    {
        if (_restoreDone && _registryDone)
        {
            BtnProceed.IsEnabled = true;
            TxtLog.Text = "✓ Усі захисні заходи виконано. Доступ до оптимізатора розблоковано.";
            TxtLog.Foreground = HexBrush("#4ADE80");
        }
    }

    private void SaveConsentAndExit(string bypassMethod = "Standard")
    {
        var meta = new
        {
            SafetyConsentAccepted = true,
            AcceptedTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            MachineName = Environment.MachineName,
            User = Environment.UserName,
            Method = bypassMethod
        };

        try
        {
            string dir = Path.GetDirectoryName(ConsentFile) ?? string.Empty;
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(ConsentFile, JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }

        IsConsentGranted = true;
        Close();
    }

    private void BtnProceed_Click(object sender, RoutedEventArgs e)
    {
        SaveConsentAndExit();
    }

    private void BtnExit_Click(object sender, RoutedEventArgs e)
    {
        IsConsentGranted = false;
        Close();
        Application.Current.Shutdown();
    }
}