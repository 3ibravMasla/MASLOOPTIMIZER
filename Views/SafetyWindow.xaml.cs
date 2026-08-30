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
    private ModuleStrings Loc => LocalizationManager.Instance.For("BackupEngine");

    private bool _restoreDone = false;
    private bool _registryDone = false;
    private string _cheatBuffer = string.Empty;
    private static readonly string ConsentFile = Path.Combine(BackupEngine.BackupsDirectory, "safety_consent.json");

    public bool IsConsentGranted { get; private set; } = false;

    public SafetyWindow()
    {
        InitializeComponent();
        ApplyLocalizedUi();
        Loaded += SafetyWindow_Loaded;
        KeyDown += SafetyWindow_KeyDown;
    }

    private void ApplyLocalizedUi()
    {
        Title = Loc["SafetyTitle"];
        TxtSafetyHeading.Text = Loc["SafetyHeading"];
        TxtSafetySubtitle.Text = Loc["SafetySubtitle"];
        TxtDisclaimerTitle.Text = Loc["DisclaimerTitle"];
        TxtDisclaimerBody.Text = Loc["DisclaimerBody"];
        TxtStepVssTitle.Text = Loc["StepVssTitle"];
        TxtRestoreStatus.Text = Loc["StepNotDone"];
        BtnCreateRestore.Content = Loc["BtnCreateRestore"];
        TxtStepRegTitle.Text = Loc["StepRegTitle"];
        TxtRegistryStatus.Text = Loc["StepNotDone"];
        BtnCreateRegBackup.Content = Loc["BtnSaveRegistry"];
        TxtLog.Text = Loc["TxtLogIdle"];
        BtnExit.Content = Loc["BtnExit"];
        BtnProceed.Content = Loc["BtnProceed"];
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

                TxtRestoreStatus.Text = Loc["StatusCheatSkipped"];
                TxtRestoreStatus.Foreground = ThemeEngine.Brush("SuccessText");
                BtnCreateRestore.Content = Loc["BtnDev"];
                BtnCreateRestore.Background = ThemeEngine.Brush("SuccessBrush");
                BtnCreateRestore.IsEnabled = false;

                TxtRegistryStatus.Text = Loc["StatusCheatSkipped"];
                TxtRegistryStatus.Foreground = ThemeEngine.Brush("SuccessText");
                BtnCreateRegBackup.Content = Loc["BtnDev"];
                BtnCreateRegBackup.Background = ThemeEngine.Brush("SuccessBrush");
                BtnCreateRegBackup.IsEnabled = false;

                TxtLog.Text = Loc["CheatActivated"];
                TxtLog.Foreground = ThemeEngine.Brush("SuccessText");

                _restoreDone = true;
                _registryDone = true;

                SaveConsentAndExit(bypassMethod: "CheatCode_MASLO");
            }
        }
    }

    private async void BtnCreateRestore_Click(object sender, RoutedEventArgs e)
    {
        BtnCreateRestore.IsEnabled = false;
        TxtRestoreStatus.Text = Loc["StatusVssBusy"];
        TxtRestoreStatus.Foreground = ThemeEngine.Brush("WarningText");

        var res = await BackupEngine.CreateVssRestorePointAsync("MASLOOPTIMIZER_FirstRun_SafePoint");
        _restoreDone = true;

        if (res.Success)
        {
            TxtRestoreStatus.Text = Loc["StatusVssOk"];
            TxtRestoreStatus.Foreground = ThemeEngine.Brush("SuccessText");
            BtnCreateRestore.Content = Loc["BtnCreated"];
            BtnCreateRestore.Background = ThemeEngine.Brush("SuccessBrush");
        }
        else
        {
            TxtRestoreStatus.Text = Loc["StatusVssLimited"];
            TxtRestoreStatus.Foreground = ThemeEngine.Brush("WarningText");
            BtnCreateRestore.Content = Loc["BtnSkipped"];
            BtnCreateRestore.Background = ThemeEngine.Brush("StatusNeutralBrush");
        }

        CheckBothSteps();
    }

    private async void BtnCreateRegBackup_Click(object sender, RoutedEventArgs e)
    {
        BtnCreateRegBackup.IsEnabled = false;
        TxtRegistryStatus.Text = Loc["StatusRegBusy"];
        TxtRegistryStatus.Foreground = ThemeEngine.Brush("WarningText");

        var res = await BackupEngine.ExportRegistryBackupAsync("FirstRun_FullBackup");
        _registryDone = true;

        if (res.Success)
        {
            TxtRegistryStatus.Text = Loc["StatusRegOk"];
            TxtRegistryStatus.Foreground = ThemeEngine.Brush("SuccessText");
            BtnCreateRegBackup.Content = Loc["BtnSaved"];
            BtnCreateRegBackup.Background = ThemeEngine.Brush("SuccessBrush");
        }
        else
        {
            TxtRegistryStatus.Text = Loc["StatusRegPartial"];
            TxtRegistryStatus.Foreground = ThemeEngine.Brush("WarningText");
            BtnCreateRegBackup.Content = Loc["BtnPartial"];
            BtnCreateRegBackup.Background = ThemeEngine.Brush("StatusNeutralBrush");
        }

        CheckBothSteps();
    }

    private void CheckBothSteps()
    {
        if (_restoreDone && _registryDone)
        {
            BtnProceed.IsEnabled = true;
            TxtLog.Text = Loc["StatusAllDone"];
            TxtLog.Foreground = ThemeEngine.Brush("SuccessText");
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