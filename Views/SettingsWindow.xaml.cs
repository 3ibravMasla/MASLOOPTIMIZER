using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;

namespace MASLOOPTIMIZER;

/// <summary>Модель прев'ю-картки теми у вікні налаштувань.</summary>
public class ThemeCard
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string PreviewBg { get; set; } = "#0B0C10";
    public string PreviewCard { get; set; } = "#151722";
    public string PreviewAccent { get; set; } = "#00FF9D";
}

public partial class SettingsWindow : Window
{
    private bool _suppressScaleEvents = false;
    private bool _suppressCheckEvents = false;

    public SettingsWindow()
    {
        InitializeComponent();

        ApplyLocalizedUi();
        LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
        Closed += OnSettingsWindowClosed;

        double scale = SettingsManager.ReadUiScalePercent();
        _suppressScaleEvents = true;
        ScaleSlider.Value = scale;
        _suppressScaleEvents = false;
        ScaleValueText.Text = $"{scale:0}%";

        BuildThemeCards();
        RefreshLanguageButton();

        _suppressCheckEvents = true;
        ChkWidgetOnly.IsChecked = SettingsManager.IsWidgetOnlyAutostartEnabled();
        ChkSilent.IsChecked = SettingsManager.IsSilentAutostartEnabled();
        _suppressCheckEvents = false;
    }

    private void ApplyLocalizedUi()
    {
        var loc = LocalizationManager.Instance;
        Title = loc.T("Settings.WindowTitle");
        TxtScaleLabel.Text = loc.T("Settings.ScaleLabel");
        TxtScaleHint.Text = loc.T("Settings.ScaleHint");
        TxtThemeLabel.Text = loc.T("Settings.ThemeLabel");
        TxtAutostartLabel.Text = loc.T("Settings.AutostartLabel");
        ChkWidgetOnly.Content = loc.T("Settings.AutostartWidgetOnly");
        ChkSilent.Content = loc.T("Settings.AutostartSilent");
        TxtAutostartHint.Text = loc.T("Settings.AutostartHint");
        TxtLanguageLabel.Text = loc.T("Settings.LanguageLabel");
        BtnClose.Content = loc.T("Settings.Close");
    }

    private void OnLanguageChanged(object? sender, LanguageChangedEventArgs e)
    {
        ApplyLocalizedUi();
        RefreshLanguageButton();
    }

    private void OnSettingsWindowClosed(object? sender, EventArgs e)
    {
        LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
    }

    private void BuildThemeCards()
    {
        var cards = new ObservableCollection<ThemeCard>();
        foreach (var theme in ThemeEngine.AppThemes)
        {
            theme.Brushes.TryGetValue("WindowBg", out var bg);
            theme.Brushes.TryGetValue("CardBg", out var card);

            cards.Add(new ThemeCard
            {
                Key = theme.Key,
                DisplayName = theme.DisplayName,
                Category = theme.Category,
                PreviewBg = string.IsNullOrWhiteSpace(bg) ? "#0B0C10" : bg,
                PreviewCard = string.IsNullOrWhiteSpace(card) ? "#151722" : card,
                PreviewAccent = theme.Accent
            });
        }
        ThemeItemsControl.ItemsSource = cards;
    }

    private void ThemeCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string key)
        {
            ThemeEngine.ApplyAppThemeWithWidget(key);
            if (Application.Current.MainWindow is MainWindow mw)
            {
                mw.OnThemeChangedExternally();
            }
        }
    }

    private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Під час InitializeComponent() Slider може змінювати значення ще до того,
        // як ScaleValueText буде створений (він оголошений у XAML після повзунка).
        // Без цієї перевірки виникає NullReferenceException, і вікно не відкривається.
        if (_suppressScaleEvents || ScaleValueText == null) return;

        double percent = e.NewValue;
        ScaleValueText.Text = $"{percent:0}%";
        SettingsManager.SaveUiScalePercent(percent);

        if (Application.Current.MainWindow is MainWindow mw)
        {
            mw.ApplyUiScale(percent);
        }
    }

    private void ChkWidgetOnly_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressCheckEvents) return;

        _suppressCheckEvents = true;
        if (ChkWidgetOnly.IsChecked == true)
        {
            ChkSilent.IsChecked = false;
        }
        _suppressCheckEvents = false;

        SettingsManager.SetWidgetOnlyAutostart(ChkWidgetOnly.IsChecked == true);
        SettingsManager.SetSilentAutostart(ChkSilent.IsChecked == true);
    }

    private void ChkSilent_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressCheckEvents) return;

        _suppressCheckEvents = true;
        if (ChkSilent.IsChecked == true)
        {
            ChkWidgetOnly.IsChecked = false;
        }
        _suppressCheckEvents = false;

        SettingsManager.SetWidgetOnlyAutostart(ChkWidgetOnly.IsChecked == true);
        SettingsManager.SetSilentAutostart(ChkSilent.IsChecked == true);
    }

    private void RefreshLanguageButton()
    {
        var loc = LocalizationManager.Instance;
        bool locked = SettingsManager.ReadLanguageLocked();

        if (locked)
        {
            // Мова заблокована політикою (RU-система → UA): зміни заборонені.
            BtnLanguage.IsEnabled = false;
            BtnLanguage.Content = loc.Format("Settings.LanguageButtonLocked", loc.CurrentLanguageName);
            return;
        }

        BtnLanguage.IsEnabled = true;
        string next = loc.GetLanguageName(loc.NextLanguage());
        BtnLanguage.Content = string.IsNullOrWhiteSpace(next)
            ? loc.Format("Settings.LanguageButtonCurrent", loc.CurrentLanguageName)
            : loc.Format("Settings.LanguageButtonNext", loc.CurrentLanguageName, next);
    }

    private void BtnLanguage_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsManager.ReadLanguageLocked()) return;

        var loc = LocalizationManager.Instance;
        loc.LoadLanguage(loc.NextLanguage());
        RefreshLanguageButton();

        if (Application.Current.MainWindow is MainWindow mw)
        {
            mw.RefreshLocalizedChromePublic();
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
