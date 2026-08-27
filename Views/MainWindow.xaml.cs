using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using Cursors = System.Windows.Input.Cursors;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace MASLOOPTIMIZER;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private string _currentTab = "UI";
    private string _currentRisk = "UI";
    private string _searchQuery = string.Empty;
    private HardwareInfo? _currentHwInfo;
    private double _uiScale = 1.0;

    // Фоновий модуль оновлень (GitHub Releases)
    private readonly DispatcherTimer _updateCheckTimer = new();
    private bool _updateBannerDismissed;
    private bool _updateDownloading;
    private string? _pendingUpdateVersion;
    private string? _pendingUpdateUrl;

    // Стан фільтрації для Твіків
    private string _currentTweakCategory = "Всі";
    private TweakSortMode _currentTweakSort = TweakSortMode.Default;

    // Стан фільтрації для Деблоату
    private string _currentDebloatCategory = "Всі";
    private DebloatSortMode _currentDebloatSort = DebloatSortMode.Default;

    // Стан фільтрації для Софту
    private string _currentToolsCategory = "Всі";
    private ToolSortMode _currentToolsSort = ToolSortMode.Default;

    // Стан фільтрації для DNS (чіпси-групи та сортування)
    private DnsGroup _currentDnsGroup = DnsGroup.All;
    private DnsSortMode _currentDnsSort = DnsSortMode.FastestFirst;

    // Стан фільтрації для Автозапуску (джерела)
    private string _currentStartupSource = "Всі";
    private List<StartupEntry>? _allStartupEntries;

    public ObservableCollection<TweakModel> FilteredTweaks { get; set; } = new();
    public ObservableCollection<DebloatItem> FilteredDebloat { get; set; } = new();
    public ObservableCollection<ToolItem> FilteredTools { get; set; } = new();
    public ObservableCollection<PciMsiDevice> FilteredMsiDevices { get; set; } = new();
    public ObservableCollection<DnsPreset> FilteredDns { get; set; } = new();
    public ObservableCollection<StartupEntry> FilteredStartup { get; set; } = new();

    /// <summary>Масштаб інтерфейсу у вигляді множника (1.0 = 100%).</summary>
    public double UiScale
    {
        get => _uiScale;
        set
        {
            if (Math.Abs(_uiScale - value) > 0.001)
            {
                _uiScale = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public MainWindow()
    {
        InitializeComponent();

        DataContext = this;
        UiScale = SettingsManager.ReadUiScalePercent() / 100.0;

        TweaksItemsControl.ItemsSource = FilteredTweaks;
        DebloatItemsControl.ItemsSource = FilteredDebloat;
        ToolsItemsControl.ItemsSource = FilteredTools;
        MsiItemsControl.ItemsSource = FilteredMsiDevices;

        DnsItemsControl.ItemsSource = FilteredDns;
        CleanerItemsControl.ItemsSource = CleanerEngine.Cleaners;
        StartupItemsControl.ItemsSource = FilteredStartup;

        Loaded += MainWindow_Loaded;
        Closing += (s, e) =>
        {
            e.Cancel = true;
            Hide();
        };

        // Періодична перевірка оновлень на GitHub (раз на 30 хвилин).
        _updateCheckTimer.Interval = TimeSpan.FromMinutes(30);
        _updateCheckTimer.Tick += (s, e) => _ = CheckForUpdatesBackgroundAsync();
        _updateCheckTimer.Start();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        TrayManager.Initialize();

        if (!SafetyWindow.CheckConsentGiven())
        {
            var safety = new SafetyWindow { Owner = this };
            safety.ShowDialog();
            if (!safety.IsConsentGranted)
            {
                Application.Current.Shutdown();
                return;
            }
        }

        try
        {
            var uri = new Uri("pack://application:,,,/icon/maslo.jpg", UriKind.Absolute);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = uri;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            AppLogoImage.Source = bitmap;
        }
        catch
        {
            string logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon", "maslo.jpg");
            if (File.Exists(logoPath))
            {
                AppLogoImage.Source = new BitmapImage(new Uri(logoPath, UriKind.Absolute));
            }
        }

        try
        {
            _currentHwInfo = await DiagnosticEngine.GetQuickHardwareInfoAsync();
            HwBadgeOS.Text = _currentHwInfo.OS;
            HwBadgeCPU.Text = _currentHwInfo.CPU;
            HwBadgeGPU.Text = _currentHwInfo.GPU;
            HwBadgeRAM.Text = _currentHwInfo.RAM;
            HwBadgeDisk.Text = _currentHwInfo.DiskFree;
        }
        catch
        {
            HwBadgeOS.Text = "Windows 11 / 10 x64";
            HwBadgeCPU.Text = "CPU Ready";
            HwBadgeGPU.Text = "GPU Ready";
            HwBadgeRAM.Text = "16 GB";
            HwBadgeDisk.Text = "OK";
        }

        TweakEngine.Instance.LoadTweaks();
        HwBadgeTweaksCount.Text = LocalizationManager.Instance.Format("Common.TweaksCount", TweakEngine.Instance.AllTweaks.Count);

        // Застосовуємо збережену мову до всього інтерфейсу одразу після завантаження даних.
        RefreshLocalizedChrome();

        UpdateTweakChipsAndFilter();
        UpdateDebloatChipsAndFilter();
        UpdateToolsChipsAndFilter();
        UpdateDnsChipsAndFilter();
        UpdateStartupChipsAndFilter();
        _ = ToolsEngine.DetectInstalledToolsAsync().ContinueWith(_ => Dispatcher.Invoke(UpdateToolsChipsAndFilter));
        _ = DebloatEngine.ScanInstalledPackagesAsync().ContinueWith(_ => Dispatcher.Invoke(UpdateDebloatChipsAndFilter));

        StatusText.Text = LocalizationManager.Instance["Footer.Scanning"];
        await TweakEngine.Instance.EvaluateAllStatusesAsync((percent, name) =>
        {
            Dispatcher.Invoke(() =>
            {
                AppProgressBar.Value = percent;
                ProgressPercentText.Text = $"{percent}%";
                StatusText.Text = LocalizationManager.Instance.Format("Footer.Analyzing", name);
            });
        });

        StatusText.Text = LocalizationManager.Instance["Footer.ScanDone"];
        AppProgressBar.Value = 100;
        ProgressPercentText.Text = "100%";

        _ = CheckForUpdatesBackgroundAsync();

        _ = RunHealthScoreRefreshAsync();
    }

    private async Task RunHealthScoreRefreshAsync()
    {
        try
        {
            var report = await HealthEngine.RunHealthAuditAsync();
            Dispatcher.Invoke(() => UpdateHealthScoreBadge(report.TotalScore));
        }
        catch
        {
            // фоновий аудит не має ламати запуск програми
        }
    }

    #region Фоновий модуль оновлень (GitHub Toast/Banner)

    /// <summary>Періодична фонова перевірка останнього релізу на GitHub.</summary>
    private async Task CheckForUpdatesBackgroundAsync()
    {
        if (_updateBannerDismissed || _updateDownloading) return;

        try
        {
            var (available, newVer, url) = await UpdateManager.CheckForUpdateAsync();
            if (!available || string.IsNullOrWhiteSpace(url) || _updateBannerDismissed) return;

            _pendingUpdateVersion = newVer;
            _pendingUpdateUrl = url;

            Dispatcher.Invoke(ShowUpdateToast);
        }
        catch { }
    }

    /// <summary>Плавно показує тост-банер оновлення в нижній частині вікна.</summary>
    private void ShowUpdateToast()
    {
        if (_updateBannerDismissed || _updateDownloading) return;
        if (UpdateToast.Visibility == Visibility.Visible) return;

        var loc = LocalizationManager.Instance;
        UpdateToastText.Text = loc.Format("Update.ToastTitle", _pendingUpdateVersion ?? UpdateManager.CurrentVersion);
        UpdateToastSub.Text = loc["Update.ToastSub"];
        BtnUpdateNow.Content = loc["Update.BtnNow"];
        BtnUpdateDismiss.Content = loc["Update.BtnClose"];

        UpdateToast.Visibility = Visibility.Visible;

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var slide = new DoubleAnimation(24, 0, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        UpdateToast.BeginAnimation(OpacityProperty, fadeIn);
        if (UpdateToast.RenderTransform is TranslateTransform tr)
        {
            tr.BeginAnimation(TranslateTransform.YProperty, slide);
        }
    }

    /// <summary>Плавно приховує тост-банер оновлення.</summary>
    private void HideUpdateToast()
    {
        if (UpdateToast.Visibility != Visibility.Visible) return;

        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOut.Completed += (s, e) => UpdateToast.Visibility = Visibility.Collapsed;

        UpdateToast.BeginAnimation(OpacityProperty, fadeOut);
    }

    private async void BtnUpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_pendingUpdateUrl) || _updateDownloading) return;

        _updateDownloading = true;
        var loc = LocalizationManager.Instance;

        // Перемикаємо тост у режим завантаження.
        UpdateToastText.Text = loc.Format("Update.DownloadingTitle", _pendingUpdateVersion ?? UpdateManager.CurrentVersion);
        UpdateToastSub.Text = loc["Update.DownloadDone"];
        BtnUpdateNow.IsEnabled = false;
        BtnUpdateDismiss.IsEnabled = false;
        UpdateProgressPanel.Visibility = Visibility.Visible;
        UpdateProgressBar.Value = 0;
        UpdateProgressText.Text = "0%";

        var progress = new Progress<double>(pct =>
        {
            if (double.IsFinite(pct))
            {
                UpdateProgressBar.Value = pct;
                UpdateProgressText.Text = $"{pct:0}%";
            }
        });

        await UpdateManager.DownloadAndInstallUpdateAsync(_pendingUpdateUrl, progress);

        // Якщо метод повернувся без Environment.Exit(0) — сталася помилка завантаження.
        if (!_updateDownloading) return;
        _updateDownloading = false;
        BtnUpdateNow.IsEnabled = true;
        BtnUpdateDismiss.IsEnabled = true;
        StatusText.Text = loc["Common.Error"];
        AppLogger.Log("Помилка завантаження оновлення (метод повернувся без перезапуску)", "ERROR");
    }

    private void BtnUpdateDismiss_Click(object sender, RoutedEventArgs e)
    {
        _updateBannerDismissed = true; // Сховати до наступного перезапуску програми.
        HideUpdateToast();
    }

    #endregion

    private void UpdateHealthScoreBadge(int score)
    {
        BtnHealthScore.Content = $"🏆 Health Score: {score}%";
    }

    /// <summary>Застосовує та зберігає масштаб інтерфейсу (percent: 50–200).</summary>
    public void ApplyUiScale(double percent)
    {
        double clamped = Math.Clamp(percent, SettingsManager.MinScalePercent, SettingsManager.MaxScalePercent);
        UiScale = clamped / 100.0;
        SettingsManager.SaveUiScalePercent(clamped);
    }

    public void OnThemeChangedExternally()
    {
        UpdateTweakChipsAndFilter();
        UpdateDebloatChipsAndFilter();
        UpdateToolsChipsAndFilter();
        UpdateDnsChipsAndFilter();
        UpdateStartupChipsAndFilter();
    }

    public void RefreshLocalizedChromePublic()
    {
        RefreshLocalizedChrome();
        UpdateTweakChipsAndFilter();
        UpdateDebloatChipsAndFilter();
        UpdateToolsChipsAndFilter();
        UpdateDnsChipsAndFilter();
        UpdateStartupChipsAndFilter();
    }

    #region Фільтрація ТВІКІВ

    private void UpdateTweakChipsAndFilter()
    {
        CategoryChipsPanel.Children.Clear();

        var categories = TweakEngine.Instance.GetCategories(_currentRisk);
        var tweaksInRisk = TweakEngine.Instance.AllTweaks
            .Where(t => string.Equals(t.Risk, _currentRisk, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var cat in categories)
        {
            int count = cat == "Всі"
                ? tweaksInRisk.Count
                : tweaksInRisk.Count(t => string.Equals(t.Category, cat, StringComparison.OrdinalIgnoreCase));

            bool isSelected = string.Equals(cat, _currentTweakCategory, StringComparison.OrdinalIgnoreCase);

            string catDisplay = LocalizeCategory(cat);
            var chip = CreateFilterChip(
                text: cat == "Всі" ? $"🌟 {catDisplay} ({count})" : $"{catDisplay} ({count})",
                tag: cat,
                isSelected: isSelected,
                onClick: (selectedCat) =>
                {
                    _currentTweakCategory = selectedCat;
                    UpdateTweakChipsAndFilter();
                });

            CategoryChipsPanel.Children.Add(chip);
        }

        FilteredTweaks.Clear();
        var filtered = TweakEngine.Instance.GetFilteredAndSortedTweaks(
            riskLevel: _currentRisk,
            category: _currentTweakCategory,
            searchQuery: _searchQuery,
            sortMode: _currentTweakSort
        );

        foreach (var tweak in filtered)
        {
            FilteredTweaks.Add(tweak);
        }
    }

    private void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SortComboBox?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            _currentTweakSort = tag switch
            {
                "AppliedFirst" => TweakSortMode.AppliedFirst,
                "UnappliedFirst" => TweakSortMode.UnappliedFirst,
                "Risk" => TweakSortMode.RiskAscending,
                "Name" => TweakSortMode.NameAscending,
                _ => TweakSortMode.Default
            };
            UpdateTweakChipsAndFilter();
        }
    }

    #endregion

    #region Фільтрація ДЕБЛОАТУ

    private void UpdateDebloatChipsAndFilter()
    {
        if (DebloatCategoryChipsPanel == null) return;
        DebloatCategoryChipsPanel.Children.Clear();

        var categories = DebloatEngine.GetCategories();
        var allItems = DebloatEngine.Catalog;

        foreach (var cat in categories)
        {
            int count = cat == "Всі"
                ? allItems.Count
                : allItems.Count(d => string.Equals(d.Category, cat, StringComparison.OrdinalIgnoreCase));

            bool isSelected = string.Equals(cat, _currentDebloatCategory, StringComparison.OrdinalIgnoreCase);

            string catDisplay = LocalizeCategory(cat);
            var chip = CreateFilterChip(
                text: cat == "Всі" ? $"🌟 {catDisplay} ({count})" : $"{catDisplay} ({count})",
                tag: cat,
                isSelected: isSelected,
                onClick: (selectedCat) =>
                {
                    _currentDebloatCategory = selectedCat;
                    UpdateDebloatChipsAndFilter();
                });

            DebloatCategoryChipsPanel.Children.Add(chip);
        }

        FilteredDebloat.Clear();
        var filtered = DebloatEngine.GetFilteredAndSortedItems(
            category: _currentDebloatCategory,
            searchQuery: _searchQuery,
            sortMode: _currentDebloatSort
        );

        foreach (var item in filtered)
        {
            FilteredDebloat.Add(item);
        }

        var stats = DebloatEngine.GetStatistics();
        DebloatStatsText.Text = LocalizationManager.Instance.Format("Debloat.StatsSummary", stats.Installed, stats.Removed, stats.Total);
    }

    private void DebloatSortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DebloatSortComboBox?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            _currentDebloatSort = tag switch
            {
                "InstalledFirst" => DebloatSortMode.InstalledFirst,
                "UninstalledFirst" => DebloatSortMode.UninstalledFirst,
                "Name" => DebloatSortMode.NameAscending,
                _ => DebloatSortMode.Default
            };
            UpdateDebloatChipsAndFilter();
        }
    }

    #endregion

    #region Фільтрація СОФТУ ТА ІНСТРУМЕНТІВ

    private void UpdateToolsChipsAndFilter()
    {
        if (ToolsCategoryChipsPanel == null) return;
        ToolsCategoryChipsPanel.Children.Clear();

        var categories = ToolsEngine.GetCategories();
        var allTools = ToolsEngine.Catalog;

        foreach (var cat in categories)
        {
            int count = cat == "Всі"
                ? allTools.Count
                : allTools.Count(t => string.Equals(t.Category, cat, StringComparison.OrdinalIgnoreCase));

            bool isSelected = string.Equals(cat, _currentToolsCategory, StringComparison.OrdinalIgnoreCase);

            string catDisplay = LocalizeCategory(cat);
            var chip = CreateFilterChip(
                text: cat == "Всі" ? $"🌟 {catDisplay} ({count})" : $"{catDisplay} ({count})",
                tag: cat,
                isSelected: isSelected,
                onClick: (selectedCat) =>
                {
                    _currentToolsCategory = selectedCat;
                    UpdateToolsChipsAndFilter();
                });

            ToolsCategoryChipsPanel.Children.Add(chip);
        }

        FilteredTools.Clear();
        var filtered = ToolsEngine.GetFilteredAndSortedTools(
            category: _currentToolsCategory,
            searchQuery: _searchQuery,
            sortMode: _currentToolsSort
        );

        foreach (var tool in filtered)
        {
            FilteredTools.Add(tool);
        }

        var stats = ToolsEngine.GetStatistics();
        ToolsStatsText.Text = LocalizationManager.Instance.Format("Tools.StatsSummary", stats.Installed, stats.Available, stats.Total);
    }

    private void ToolsSortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ToolsSortComboBox?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            _currentToolsSort = tag switch
            {
                "InstalledFirst" => ToolSortMode.InstalledFirst,
                "NotInstalledFirst" => ToolSortMode.NotInstalledFirst,
                "Name" => ToolSortMode.NameAscending,
                _ => ToolSortMode.Default
            };
            UpdateToolsChipsAndFilter();
        }
    }

    #endregion

    #region Фільтрація DNS (чіпси-групи + сортування)

    private void UpdateDnsChipsAndFilter()
    {
        if (DnsCategoryChipsPanel == null) return;
        DnsCategoryChipsPanel.Children.Clear();

        var groups = DnsEngine.GetGroups();
        foreach (var group in groups)
        {
            int count = DnsEngine.Catalog.Count(d => DnsEngine.PresetMatchesGroup(d, group));
            bool isSelected = group == _currentDnsGroup;

            string labelKey = group switch
            {
                DnsGroup.All => "Dns.ChipAll",
                DnsGroup.Speed => "Dns.ChipSpeed",
                DnsGroup.Security => "Dns.ChipSecurity",
                DnsGroup.Gaming => "Dns.ChipGaming",
                _ => "Dns.ChipAll"
            };

            var chip = CreateFilterChip(
                text: $"{LocalizationManager.Instance[labelKey]} ({count})",
                tag: group.ToString(),
                isSelected: isSelected,
                onClick: (selectedTag) =>
                {
                    _currentDnsGroup = Enum.TryParse(selectedTag, out DnsGroup g) ? g : DnsGroup.All;
                    UpdateDnsChipsAndFilter();
                });

            DnsCategoryChipsPanel.Children.Add(chip);
        }

        FilteredDns.Clear();
        var filtered = DnsEngine.GetFilteredAndSortedPresets(
            searchQuery: _searchQuery,
            sortMode: _currentDnsSort,
            group: _currentDnsGroup);

        foreach (var preset in filtered)
        {
            FilteredDns.Add(preset);
        }
    }

    private void DnsSortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DnsSortComboBox?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            _currentDnsSort = tag switch
            {
                "NameAscending" => DnsSortMode.NameAscending,
                _ => DnsSortMode.FastestFirst
            };
            UpdateDnsChipsAndFilter();
        }
    }

    #endregion

    #region Фільтрація АВТОЗАПУСКУ (джерела)

    private void UpdateStartupChipsAndFilter()
    {
        if (StartupSourceChipsPanel == null) return;
        StartupSourceChipsPanel.Children.Clear();

        var allItems = _allStartupEntries;
        if (allItems == null) return;

        var chips = new (string Tag, string Key)[]
        {
            ("Всі", "Startup.ChipAll"),
            ("user", "Startup.ChipUser"),
            ("system", "Startup.ChipSystem"),
            ("task", "Startup.ChipTasks"),
            ("folder", "Startup.ChipFolder")
        };

        foreach (var (tag, key) in chips)
        {
            int count = tag == "Всі"
                ? allItems.Count
                : allItems.Count(e => string.Equals(e.SourceGroup, tag, StringComparison.OrdinalIgnoreCase));

            bool isSelected = string.Equals(_currentStartupSource, tag, StringComparison.OrdinalIgnoreCase);

            var chip = CreateFilterChip(
                text: $"{LocalizationManager.Instance[key]} ({count})",
                tag: tag,
                isSelected: isSelected,
                onClick: (selectedTag) =>
                {
                    _currentStartupSource = selectedTag;
                    UpdateStartupChipsAndFilter();
                });

            StartupSourceChipsPanel.Children.Add(chip);
        }

        FilteredStartup.Clear();
        var filtered = StartupEngine.GetFilteredAndSortedEntries(
            allItems,
            searchQuery: _searchQuery,
            sourceGroup: _currentStartupSource);

        foreach (var entry in filtered)
        {
            FilteredStartup.Add(entry);
        }
    }

    #endregion

    #region Фабрика чіпсів інтерфейсу

    private Border CreateFilterChip(string text, string tag, bool isSelected, Action<string> onClick)
    {
        var chip = new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(0, 0, 6, 4),
            Cursor = Cursors.Hand,
            Tag = tag,
            Background = isSelected ? (Brush)FindResource("ChipActiveBg") : (Brush)FindResource("ChipBg"),
            BorderBrush = isSelected ? (Brush)FindResource("ChipActiveBorder") : (Brush)FindResource("ChipBorder"),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11.5,
                FontWeight = isSelected ? FontWeights.Bold : FontWeights.SemiBold,
                Foreground = isSelected ? (Brush)FindResource("ChipActiveText") : (Brush)FindResource("TextPrimary")
            }
        };

        chip.MouseEnter += (s, e) =>
        {
            if (!isSelected) chip.Background = (Brush)FindResource("ChipHoverBg");
        };

        chip.MouseLeave += (s, e) =>
        {
            if (!isSelected) chip.Background = (Brush)FindResource("ChipBg");
        };

        chip.MouseDown += (s, e) => onClick(tag);

        return chip;
    }

    #endregion

    #region Навігація сайдбара

    private async void NavBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            _currentTab = tag;

            TweaksView.Visibility = Visibility.Collapsed;
            DnsView.Visibility = Visibility.Collapsed;
            CleanerView.Visibility = Visibility.Collapsed;
            DebloatView.Visibility = Visibility.Collapsed;
            StartupView.Visibility = Visibility.Collapsed;
            ToolsView.Visibility = Visibility.Collapsed;
            GameMsiView.Visibility = Visibility.Collapsed;
            NetworkView.Visibility = Visibility.Collapsed;

            if (tag == "DNS")
            {
                DnsView.Visibility = Visibility.Visible;
                DnsEngine.DetectActiveDns();
                StatusText.Text = LocalizationManager.Instance["Dns.Measuring"];
                await DnsEngine.MeasureAllPingsAsync();
                UpdateDnsChipsAndFilter();
                StatusText.Text = LocalizationManager.Instance["Dns.Measured"];
            }
            else if (tag == "CLEAN")
            {
                CleanerView.Visibility = Visibility.Visible;
                StatusText.Text = LocalizationManager.Instance["Cleaner.Scanning"];
                await CleanerEngine.CalculateSizesAsync();
                long total = CleanerEngine.Cleaners.Sum(c => c.BytesFound);
                CleanerTotalText.Text = LocalizationManager.Instance.Format("Cleaner.FoundTotal", FormatBytes(total));
                StatusText.Text = LocalizationManager.Instance["Cleaner.ScanDone"];
            }
            else if (tag == "DEBLOAT")
            {
                DebloatView.Visibility = Visibility.Visible;
                UpdateDebloatChipsAndFilter();
                StatusText.Text = "Деблоат-менеджер готовий.";
            }
            else if (tag == "STARTUP")
            {
                StartupView.Visibility = Visibility.Visible;
                _allStartupEntries = await StartupEngine.GetStartupEntriesAsync();
                UpdateStartupChipsAndFilter();
                StatusText.Text = LocalizationManager.Instance["Startup.ListRefreshed"];
            }
            else if (tag == "TOOLS")
            {
                ToolsView.Visibility = Visibility.Visible;
                UpdateToolsChipsAndFilter();
                StatusText.Text = "Бібліотека софту та інструментів готова.";
            }
            else if (tag == "GAMEMSI")
            {
                GameMsiView.Visibility = Visibility.Visible;
                RefreshGameModeUi();
                if (FilteredMsiDevices.Count == 0)
                {
                    await ScanMsiDevicesAsync();
                }
                StatusText.Text = "Game Mode & MSI готовий.";
            }
            else if (tag == "NETWORK")
            {
                NetworkView.Visibility = Visibility.Visible;
                await RefreshNetworkStatusAsync();
            }
            else
            {
                TweaksView.Visibility = Visibility.Visible;
                _currentRisk = tag;
                _currentTweakCategory = "Всі";
                UpdateTweakChipsAndFilter();
            }
        }
    }

    private void SearchInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchQuery = SearchInput.Text.Trim();

        if (TweaksView.Visibility == Visibility.Visible)
        {
            UpdateTweakChipsAndFilter();
        }
        else if (DebloatView.Visibility == Visibility.Visible)
        {
            UpdateDebloatChipsAndFilter();
        }
        else if (ToolsView.Visibility == Visibility.Visible)
        {
            UpdateToolsChipsAndFilter();
        }
        else if (DnsView.Visibility == Visibility.Visible)
        {
            UpdateDnsChipsAndFilter();
        }
        else if (StartupView.Visibility == Visibility.Visible)
        {
            UpdateStartupChipsAndFilter();
        }
    }

    #endregion

    #region Твіки

    private async void ApplyTweak_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TweakModel tweak)
        {
            try
            {
                StatusText.Text = $"Застосування: {tweak.LocalizedName}...";
                AppLogger.Log($"Застосування твіка: {tweak.LocalizedName}");
                bool res = await TweakEngine.Instance.ExecuteTweakAsync(tweak, isApply: true);
                StatusText.Text = res ? $"Успішно застосовано: {tweak.LocalizedName}" : $"Помилка виконання: {tweak.LocalizedName}";
                AppLogger.Log($"Результат {tweak.LocalizedName}: {res}", res ? "SUCCESS" : "ERROR");
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Збій: {ex.Message}";
                AppLogger.Log($"Помилка при застосуванні {tweak.LocalizedName}: {ex.Message}", "ERROR");
            }
        }
    }

    private async void RestoreTweak_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TweakModel tweak)
        {
            try
            {
                StatusText.Text = $"Відновлення: {tweak.LocalizedName}...";
                AppLogger.Log($"Відновлення твіка: {tweak.LocalizedName}");
                bool res = await TweakEngine.Instance.ExecuteTweakAsync(tweak, isApply: false);
                StatusText.Text = res ? $"Відновлено: {tweak.LocalizedName}" : $"Помилка відновлення: {tweak.LocalizedName}";
                AppLogger.Log($"Результат відновлення {tweak.LocalizedName}: {res}", res ? "SUCCESS" : "ERROR");
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Збій: {ex.Message}";
                AppLogger.Log($"Помилка при відновленні {tweak.LocalizedName}: {ex.Message}", "ERROR");
            }
        }
    }

    private async void BtnBatchApply_Click(object sender, RoutedEventArgs e)
    {
        var safeTweaks = TweakEngine.Instance.AllTweaks
            .Where(t => string.Equals(t.Risk, "Safe", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (MessageBox.Show($"Застосувати всі безпечні твіки ({safeTweaks.Count} шт.)?", "1-Click Safe Pack", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            int i = 0;
            foreach (var tweak in safeTweaks)
            {
                i++;
                int pct = (int)((i / (double)safeTweaks.Count) * 100);
                AppProgressBar.Value = pct;
                ProgressPercentText.Text = $"{pct}%";
                StatusText.Text = $"Оптимізація: {tweak.LocalizedName}";

                await TweakEngine.Instance.ExecuteTweakAsync(tweak, isApply: true);
            }
            StatusText.Text = "Усі безпечні твіки успішно застосовано!";
            AppLogger.Log("1-Click Safe Pack повністю застосовано", "SUCCESS");
            UpdateTweakChipsAndFilter();
        }
    }

    private async void BtnSafeMasloPack_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Застосувати рекомендований комплексний Maslo Pack (безпечні твіки + деблоат мотлоху)?", "1-Click Safe Maslo Pack", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            var res = await PresetEngine.ApplyMasloSignaturePackAsync(
                TweakEngine.Instance.AllTweaks,
                DebloatEngine.Catalog,
                (pct, msg) => Dispatcher.Invoke(() =>
                {
                    AppProgressBar.Value = pct;
                    ProgressPercentText.Text = $"{pct}%";
                    StatusText.Text = msg;
                }));

            StatusText.Text = res.Message;
            AppLogger.Log(res.Message, "SUCCESS");
            UpdateTweakChipsAndFilter();
            UpdateDebloatChipsAndFilter();
        }
    }

    #endregion

    #region DNS

    private void ApplyDns_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is DnsPreset preset)
        {
            bool ok = DnsEngine.ApplyDns(preset.Primary, preset.Secondary);
            var loc = LocalizationManager.Instance;
            StatusText.Text = ok ? loc.Format("Dns.ApplyDone", preset.NameLocalized) : loc["Dns.ApplyFailed"];
            AppLogger.Log($"DNS {preset.NameLocalized}: {ok}", ok ? "SUCCESS" : "ERROR");
            UpdateDnsChipsAndFilter();
        }
    }

    private async void BtnFastestDns_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        StatusText.Text = loc["Dns.SearchingFastest"];
        await DnsEngine.MeasureAllPingsAsync();
        var fastest = DnsEngine.GetFastestPreset();
        if (fastest != null)
        {
            DnsEngine.ApplyDns(fastest.Primary, fastest.Secondary);
            StatusText.Text = loc.Format("Dns.FastestApplied", fastest.NameLocalized, fastest.Ping);
            MessageBox.Show(loc.Format("Dns.FastestFound", fastest.NameLocalized, fastest.Ping),
                "DNS Optimizer", MessageBoxButton.OK, MessageBoxImage.Information);
            UpdateDnsChipsAndFilter();
        }
    }

    private void BtnResetDns_Click(object sender, RoutedEventArgs e)
    {
        bool ok = DnsEngine.RestoreOriginalDns();
        var loc = LocalizationManager.Instance;
        StatusText.Text = ok ? loc["Dns.ResetDone"] : loc["Dns.ResetFailed"];
        AppLogger.Log("DNS reset to DHCP", ok ? "SUCCESS" : "WARN");
        UpdateDnsChipsAndFilter();
    }

    #endregion

    #region Cleaner

    private async void CleanItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is CleanerItem item)
        {
            var loc = LocalizationManager.Instance;
            try
            {
                StatusText.Text = loc.Format("Cleaner.CleaningItem", item.NameLocalized);
                long freed = await CleanerEngine.CleanItemAsync(item);
                StatusText.Text = loc.Format("Cleaner.Freed", FormatBytes(freed));
                AppLogger.Log($"Clean {item.NameLocalized}: {FormatBytes(freed)}", "SUCCESS");

                long totalRemaining = CleanerEngine.Cleaners.Sum(c => c.BytesFound);
                CleanerTotalText.Text = loc.Format("Cleaner.FoundSummary", FormatBytes(totalRemaining));
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Clean error {item.NameLocalized}: {ex.Message}", "ERROR");
            }
        }
    }

    private async void BtnCleanAll_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        if (MessageBox.Show(loc["Cleaner.ConfirmAll"], loc["Cleaner.ConfirmAllTitle"], MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            try
            {
                var progress = new Progress<(int Percent, string Name, long Freed)>(p =>
                {
                    AppProgressBar.Value = p.Percent;
                    ProgressPercentText.Text = $"{p.Percent}%";
                    StatusText.Text = loc.Format("Cleaner.CleanedOne", p.Name, FormatBytes(p.Freed));
                });

                long totalFreed = await CleanerEngine.CleanAllSafeAsync(progress);
                CleanerTotalText.Text = loc["Cleaner.CleanDone"];
                StatusText.Text = loc.Format("Cleaner.TotalFreed", FormatBytes(totalFreed));
                AppLogger.Log($"Full clean: freed {FormatBytes(totalFreed)}", "SUCCESS");
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Full clean error: {ex.Message}", "ERROR");
            }
        }
    }

    private async void BtnRescanCleaner_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        StatusText.Text = loc["Cleaner.Scanning"];
        await CleanerEngine.CalculateSizesAsync();
        long total = CleanerEngine.Cleaners.Sum(c => c.BytesFound);
        CleanerTotalText.Text = loc.Format("Cleaner.FoundSummary", FormatBytes(total));
        StatusText.Text = loc["Cleaner.ScanDone"];
    }

    #endregion

    #region Debloat & Startup

    private async void UninstallDebloat_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is DebloatItem item)
        {
            StatusText.Text = $"Видалення {item.Name}...";
            bool ok = await DebloatEngine.UninstallPackageAsync(item);
            StatusText.Text = ok ? $"Видалено: {item.Name}" : $"Помилка видалення: {item.Name}";
            AppLogger.Log($"Видалення UWP {item.Name}: {ok}", ok ? "SUCCESS" : "ERROR");
            UpdateDebloatChipsAndFilter();
        }
    }

    private async void RestoreDebloat_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is DebloatItem item)
        {
            StatusText.Text = $"Відновлення {item.Name}...";
            bool ok = await DebloatEngine.RestorePackageAsync(item);
            StatusText.Text = ok ? $"Відновлено: {item.Name}" : $"Відкрито сторінку в Store.";
            AppLogger.Log($"Відновлення UWP {item.Name}: {ok}", ok ? "SUCCESS" : "WARN");
            UpdateDebloatChipsAndFilter();
        }
    }

    private async void BtnRescanDebloat_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Пересканування UWP...";
        await DebloatEngine.ScanInstalledPackagesAsync();
        UpdateDebloatChipsAndFilter();
        StatusText.Text = "Статус UWP пакетів оновлено.";
    }

    private async void ToggleStartup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is StartupEntry item)
        {
            await Task.Run(() => StartupEngine.ToggleStartupState(item));
            var loc = LocalizationManager.Instance;
            string state = item.IsEnabled ? loc["Startup.EnabledWord"] : loc["Startup.PausedWord"];
            StatusText.Text = loc.Format("Startup.ToggleDone", item.Name, state);
            UpdateStartupChipsAndFilter();
        }
    }

    private async void BtnRescanStartup_Click(object sender, RoutedEventArgs e)
    {
        _allStartupEntries = await StartupEngine.GetStartupEntriesAsync();
        UpdateStartupChipsAndFilter();
        StatusText.Text = LocalizationManager.Instance["Startup.ListRescanned"];
    }

    #endregion

    #region Інструменти та Софт

    private async void InstallTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ToolItem tool)
        {
            try
            {
                if (tool.SpecialAction == "MAS")
                {
                    ToolsEngine.RunMasActivation();
                    StatusText.Text = "Запущено Microsoft Activation Scripts.";
                }
                else if (tool.SpecialAction == "VCREDIST")
                {
                    StatusText.Text = "Встановлення Visual C++ All-in-One...";
                    bool ok = await ToolsEngine.InstallVcRedistAllAsync(tool);
                    StatusText.Text = ok ? "Всі пакети Visual C++ успішно встановлено!" : "Помилка встановлення VC++.";
                    UpdateToolsChipsAndFilter();
                }
                else if (tool.SpecialAction == "DIRECTX")
                {
                    StatusText.Text = "Оновлення бібліотек DirectX...";
                    bool ok = await ToolsEngine.InstallDirectXWebAsync(tool);
                    StatusText.Text = ok ? "Бібліотеки DirectX успішно оновлено!" : "Помилка оновлення DirectX.";
                    UpdateToolsChipsAndFilter();
                }
                else if (!string.IsNullOrWhiteSpace(tool.WingetId))
                {
                    StatusText.Text = $"Встановлення {tool.Name} через Winget...";
                    bool ok = await ToolsEngine.InstallWingetPackageAsync(tool);
                    StatusText.Text = ok ? $"Успішно встановлено: {tool.Name}" : $"Помилка встановлення {tool.Name}.";
                    UpdateToolsChipsAndFilter();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Помилка встановлення {tool.Name}: {ex.Message}", "ERROR");
            }
        }
    }

    private async void BtnRescanTools_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Сканування встановленого софту...";
        await ToolsEngine.DetectInstalledToolsAsync();
        UpdateToolsChipsAndFilter();
        StatusText.Text = "Статуси програм оновлено.";
    }

    private void OpenToolSite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ToolItem tool)
        {
            ToolsEngine.OpenUrl(tool.Url);
        }
    }

    #endregion

    #region Хедер: Захист, Пресети, Відкат, Оновлення, Діагностика

    private void BtnSpecDialog_Click(object sender, RoutedEventArgs e)
    {
        var diagWin = new DiagnosticWindow { Owner = this };
        diagWin.ShowDialog();
    }

    private async void BtnVssPoint_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Створити нову контрольну точку відновлення системи Windows (VSS)?", "Захист системи", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            StatusText.Text = "Створення системної точки VSS...";
            var res = await BackupEngine.CreateVssRestorePointAsync();
            StatusText.Text = res.Message;
            MessageBox.Show(res.Message, "Точка відновлення", MessageBoxButton.OK, res.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
    }

    private async void BtnRegBackup_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Створення резервної копії реєстру...";
        var res = await BackupEngine.ExportRegistryBackupAsync();
        StatusText.Text = res.Message;
        MessageBox.Show(res.Message, "Резервна копія реєстру", MessageBoxButton.OK, res.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void BtnRestoreRollback_Click(object sender, RoutedEventArgs e)
    {
        var restoreWin = new RestoreWindow { Owner = this };
        restoreWin.ShowDialog();
    }

    private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Перевірка оновлень на GitHub...";
        var (available, newVer, url) = await UpdateManager.CheckForUpdateAsync();

        if (available)
        {
            if (MessageBox.Show($"Доступна нова версія {newVer}!\n\nОновити програму зараз автоматично?", "Оновлення MASLOOPTIMIZER", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                StatusText.Text = "Завантаження та встановлення оновлення...";
                await UpdateManager.DownloadAndInstallUpdateAsync(url);
            }
        }
        else
        {
            MessageBox.Show($"У вас встановлена остання версія ({UpdateManager.CurrentVersion})!", "Оновлення", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusText.Text = "Остання версія програми.";
        }
    }

    private async void BtnPresetMenu_Click(object sender, RoutedEventArgs e)
    {
        // Контекстне меню «Пресети»: Tag = Save / Load → одразу виконуємо дію.
        if (sender is MenuItem mi && mi.Tag is string tag)
        {
            if (tag.Equals("Save", StringComparison.OrdinalIgnoreCase))
            {
                var saveRes = PresetEngine.ExportFullProfile(TweakEngine.Instance.AllTweaks, DebloatEngine.Catalog);
                StatusText.Text = saveRes.Message;
                if (saveRes.Success) MessageBox.Show(saveRes.Message, "Експорт конфігурації", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (tag.Equals("Load", StringComparison.OrdinalIgnoreCase))
            {
                StatusText.Text = "Розгортання профілю...";
                var loadRes = await PresetEngine.ImportAndApplyProfileAsync(
                    TweakEngine.Instance.AllTweaks,
                    DebloatEngine.Catalog,
                    (pct, msg) => Dispatcher.Invoke(() =>
                    {
                        AppProgressBar.Value = pct;
                        ProgressPercentText.Text = $"{pct}%";
                        StatusText.Text = msg;
                    }));

                StatusText.Text = loadRes.Message;
                MessageBox.Show(loadRes.Message, "Імпорт конфігурації", MessageBoxButton.OK, loadRes.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
                UpdateTweakChipsAndFilter();
                UpdateDebloatChipsAndFilter();
                return;
            }
        }

        var choice = MessageBox.Show(
            "Натисніть [ТАК] щоб зберегти ваш поточний конфіг у файл.\nНатисніть [НІ] щоб завантажити та розгорнути існуючий пресет.",
            "Менеджер конфігурацій (Пресетів)",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (choice == MessageBoxResult.Yes)
        {
            var res = PresetEngine.ExportFullProfile(TweakEngine.Instance.AllTweaks, DebloatEngine.Catalog);
            StatusText.Text = res.Message;
            if (res.Success) MessageBox.Show(res.Message, "Експорт конфігурації", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else if (choice == MessageBoxResult.No)
        {
            StatusText.Text = "Розгортання профілю...";
            var res = await PresetEngine.ImportAndApplyProfileAsync(
                TweakEngine.Instance.AllTweaks,
                DebloatEngine.Catalog,
                (pct, msg) => Dispatcher.Invoke(() =>
                {
                    AppProgressBar.Value = pct;
                    ProgressPercentText.Text = $"{pct}%";
                    StatusText.Text = msg;
                }));

            StatusText.Text = res.Message;
            MessageBox.Show(res.Message, "Імпорт конфігурації", MessageBoxButton.OK, res.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            UpdateTweakChipsAndFilter();
            UpdateDebloatChipsAndFilter();
        }
    }

    private void BtnOpenLogs_Click(object sender, RoutedEventArgs e)
    {
        var logWin = new LogWindow { Owner = this };
        logWin.ShowDialog();
    }

    private void BtnToggleWidget_Click(object sender, RoutedEventArgs e)
    {
        TrayManager.ToggleWidget();
    }

    private void BtnThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = BtnThemeToggle,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };

        foreach (var group in ThemeEngine.AppThemes.GroupBy(t => t.Category))
        {
            var sub = new MenuItem { Header = group.Key };

            foreach (var theme in group)
            {
                var item = new MenuItem { Header = theme.DisplayName, Tag = theme.Key };
                item.Icon = new Border
                {
                    Width = 12,
                    Height = 12,
                    CornerRadius = new CornerRadius(3),
                    Background = HexBrush(theme.Accent),
                    BorderBrush = HexBrush(theme.Accent),
                    BorderThickness = new Thickness(1)
                };
                item.Click += ThemeMenuItem_Click;
                sub.Items.Add(item);
            }

            menu.Items.Add(sub);
        }

        menu.IsOpen = true;
    }

    private void ThemeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item && item.Tag is string key)
        {
            ThemeEngine.ApplyAppThemeWithWidget(key);
            UpdateThemeButtonLabel();
            UpdateTweakChipsAndFilter();
            UpdateDebloatChipsAndFilter();
            UpdateToolsChipsAndFilter();
            UpdateDnsChipsAndFilter();
            UpdateStartupChipsAndFilter();
        }
    }

    private void UpdateThemeButtonLabel()
    {
        var loc = LocalizationManager.Instance;
        var theme = ThemeEngine.CurrentAppTheme;
        BtnThemeToggle.Content = $"{loc["Header.BtnTheme"]}: {theme?.DisplayName ?? "—"}";
    }

    private static SolidColorBrush HexBrush(string hex)
    {
        if (new BrushConverter().ConvertFromString(hex) is SolidColorBrush brush)
        {
            return brush;
        }
        return new SolidColorBrush(Colors.White);
    }

    /// <summary>Циклічно перемикає доступні мови (будь-які *.json у папці Languages).</summary>
    private void BtnLangToggle_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        loc.LoadLanguage(loc.NextLanguage());
        RefreshLocalizedChrome();
        UpdateTweakChipsAndFilter();
        UpdateDebloatChipsAndFilter();
        UpdateToolsChipsAndFilter();
        UpdateDnsChipsAndFilter();
        UpdateStartupChipsAndFilter();
    }

    /// <summary>Оновлює всі локалізовані статичні елементи інтерфейсу за поточною мовою.</summary>
    private void RefreshLocalizedChrome()
    {
        var loc = LocalizationManager.Instance;

        // Шапка
        LblCoreVersion.Text = loc["Header.CoreVersion"];
        BtnSpecDialog.Content = loc["Header.BtnSpecs"];
        BtnVssPoint.Content = loc["Header.BtnVss"];
        BtnRegBackup.Content = loc["Header.BtnBackup"];
        BtnRestoreRollback.Content = loc["Header.BtnRestore"];
        BtnCheckUpdates.Content = loc["Header.BtnUpdates"];
        BtnToggleWidget.Content = loc["Header.BtnWidget"];
        BtnBackupMenu.Content = loc["Header.BtnBackupMenu"];
        BtnSettings.Content = loc["Header.BtnSettings"];
        MiVss.Header = loc["Header.BtnVss"];
        MiRegBackup.Header = loc["Header.BtnBackup"];
        MiRollback.Header = loc["Header.BtnRestore"];
        UpdateThemeButtonLabel();
        UpdateLangButtonLabel();

        // Бейджі апаратної інформації
        LblHwOs.Text = loc["Header.BadgeOS"];
        LblHwCpu.Text = loc["Header.BadgeCPU"];
        LblHwGpu.Text = loc["Header.BadgeGPU"];
        LblHwRam.Text = loc["Header.BadgeRAM"];
        LblHwDisk.Text = loc["Header.BadgeDisk"];
        LblHwDb.Text = loc["Header.BadgeTweaks"];
        HwBadgeTweaksCount.Text = loc.Format("Common.TweaksCount", TweakEngine.Instance.AllTweaks.Count);

        // Сайдбар
        LblNavUi.Text = loc["Sidebar.NavUI"];
        LblNavSafe.Text = loc["Sidebar.NavSafe"];
        LblNavMed.Text = loc["Sidebar.NavMedium"];
        LblNavHigh.Text = loc["Sidebar.NavHigh"];
        LblNavDns.Text = loc["Sidebar.NavDns"];
        LblNavDebloat.Text = loc["Sidebar.NavDebloat"];
        LblNavStartup.Text = loc["Sidebar.NavStartup"];
        LblNavTools.Text = loc["Sidebar.NavTools"];
        LblNavClean.Text = loc["Sidebar.NavCleaner"];
        LblAutoTitle.Text = loc["Sidebar.AutomationTitle"];
        BtnBatchApply.Content = loc["Sidebar.BtnSafePack"];
        BtnSafeMasloPack.Content = loc["Sidebar.BtnMasloPack"];
        BtnGameBoost.Content = loc["Sidebar.BtnGameBoost"];
        BtnPresetMenu.Content = loc["Sidebar.BtnPresets"];
        LblNavGameMode.Text = loc["Sidebar.NavGameMode"];
        LblNavNetwork.Text = loc["Sidebar.NavNetwork"];

        // Підказки (ToolTip) кнопок автоматизації
        TtipGameBoost.Text = loc["Sidebar.BtnGameBoostTip"];
        TtipPresets.Text = loc["Sidebar.BtnPresetsTip"];

        // Контекстне меню пресетів
        MiSafePack.Header = loc["Sidebar.BtnSafePack"];
        MiMasloPack.Header = loc["Sidebar.BtnMasloPack"];
        MiSaveProfile.Header = loc["Sidebar.MiSaveProfile"];
        MiLoadProfile.Header = loc["Sidebar.MiLoadProfile"];

        // Тост-банер оновлення (якщо видимий)
        if (UpdateToast.Visibility == Visibility.Visible)
        {
            UpdateToastText.Text = loc.Format("Update.ToastTitle", _pendingUpdateVersion ?? UpdateManager.CurrentVersion);
            UpdateToastSub.Text = _updateDownloading ? loc["Update.DownloadDone"] : loc["Update.ToastSub"];
            BtnUpdateNow.Content = loc["Update.BtnNow"];
            BtnUpdateDismiss.Content = loc["Update.BtnClose"];
        }

        // Сортування
        LblSortTweaks.Text = loc["Common.SortLabel"];
        LblSortDebloat.Text = loc["Common.SortLabel"];
        LblSortTools.Text = loc["Common.SortLabel"];
        LblSortDns.Text = loc["Dns.SortLabel"];
        ApplyLocalizedSortItems();
        ApplyDnsSortItems();

        // Заголовки та кнопки секцій
        DnsTitleText.Text = loc["Dns.Title"];
        DnsDescText.Text = loc["Dns.Description"];
        BtnFastestDns.Content = loc["Dns.BtnFastest"];
        BtnResetDns.Content = loc["Dns.BtnReset"];

        DebloatTitleText.Text = loc["Debloat.Title"];
        BtnRescanDebloat.Content = loc["Debloat.BtnRescan"];

        StartupTitleText.Text = loc["Startup.Title"];
        StartupDescText.Text = loc["Startup.Description"];
        BtnRescanStartup.Content = loc["Startup.BtnRescan"];

        ToolsTitleText.Text = loc["Tools.Title"];
        BtnRescanTools.Content = loc["Tools.BtnRescan"];

        CleanerTitleText.Text = loc["Cleaner.Title"];
        CleanerTotalText.Text = loc["Cleaner.Calculating"];
        BtnCleanAll.Content = loc["Cleaner.BtnCleanAll"];
        BtnRescanCleaner.Content = loc["Cleaner.BtnRescan"];

        // Game Mode & MSI
        GameMsiTitleText.Text = loc["GameMode.Title"];
        GameMsiDescText.Text = loc["GameMode.Description"];
        TglGameMode.Content = loc["GameMode.ToggleOff"];
        BtnPurgeStandby.Content = loc["GameMode.BtnPurge"];
        LblGameModeHow.Text = loc["GameMode.HowItWorksTitle"];
        TxtGameModeHow.Text = loc["GameMode.HowItWorks"];
        LblStandbyPurge.Text = loc["GameMode.StandbyTitle"];
        TxtStandbyPurge.Text = loc["GameMode.StandbyDesc"];

        MsiTitleText.Text = loc["Msi.Title"];
        MsiStatsText.Text = loc["Msi.Scanning"];
        BtnScanMsi.Content = loc["Msi.BtnScan"];
        BtnMsiOptimize.Content = loc["Msi.BtnOptimize"];
        BtnMsiRestore.Content = loc["Msi.BtnRestore"];
        RefreshGameModeUi();

        // Футер
        StatusText.Text = loc["Footer.ReadyStatus"];
        BtnOpenLogs.Content = loc["Footer.BtnLogs"];
        LblAuthor.Text = loc["Footer.Author"];
    }

    /// <summary>Оновлює підпис кнопки перемикання мови: показує назву наступної мови.</summary>
    private void UpdateLangButtonLabel()
    {
        var loc = LocalizationManager.Instance;
        string name = loc.GetLanguageName(loc.NextLanguage());
        BtnLangToggle.Content = string.IsNullOrWhiteSpace(name) ? "🌐" : $"🌐 {name}";
    }

    /// <summary>Локалізує пункти випадаючих списків сортування за їх Tag.</summary>
    private void ApplyLocalizedSortItems()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Default"] = "Common.SortDefault",
            ["AppliedFirst"] = "Common.SortAppliedFirst",
            ["UnappliedFirst"] = "Common.SortUnappliedFirst",
            ["InstalledFirst"] = "Common.SortInstalledFirst",
            ["UninstalledFirst"] = "Common.SortUninstalledFirst",
            ["NotInstalledFirst"] = "Common.SortNotInstalledFirst",
            ["Risk"] = "Common.SortRisk",
            ["Name"] = "Common.SortName",
        };

        ApplySortItems(SortComboBox, map);
        ApplySortItems(DebloatSortComboBox, map);
        ApplySortItems(ToolsSortComboBox, map);
    }

    private static void ApplySortItems(System.Windows.Controls.ComboBox combo, Dictionary<string, string> map)
    {
        if (combo == null) return;
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string tag && map.TryGetValue(tag, out var key))
            {
                item.Content = LocalizationManager.Instance[key];
            }
        }
    }

    /// <summary>Локалізує пункти сортування DNS (Найменший пінг / За алфавітом).</summary>
    private void ApplyDnsSortItems()
    {
        if (DnsSortComboBox == null) return;
        foreach (var item in DnsSortComboBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string tag)
            {
                item.Content = tag switch
                {
                    "NameAscending" => LocalizationManager.Instance["Dns.SortName"],
                    _ => LocalizationManager.Instance["Dns.SortFastest"]
                };
            }
        }
    }

    /// <summary>Локалізація назви категорії: "Всі" → Common.AllCategories, інакше Categories.{name} з fallback на назву з бандлу.</summary>
    private static string LocalizeCategory(string category)
    {
        if (string.Equals(category, "Всі", StringComparison.Ordinal))
            return LocalizationManager.Instance["Common.AllCategories"];
        return LocalizationManager.Instance.TryGet($"Categories.{category}", out var localized) ? localized : category;
    }

    #endregion

    private static string FormatBytes(long bytes)
    {
        var loc = LocalizationManager.Instance;
        if (bytes >= 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):N2} {loc["Common.UnitGB"]}";
        if (bytes >= 1024 * 1024) return $"{bytes / (1024.0 * 1024):N2} {loc["Common.UnitMB"]}";
        if (bytes >= 1024) return $"{bytes / 1024.0:N2} {loc["Common.UnitKB"]}";
        return $"{bytes} {loc["Common.UnitBytes"]}";
    }

    #region Game Mode, MSI, Мережа, Health та Налаштування

    private void RefreshGameModeUi()
    {
        var loc = LocalizationManager.Instance;
        bool active = GameModeEngine.IsGameModeActive;
        TglGameMode.IsChecked = active;
        GameModeStatusText.Text = active ? loc["GameMode.StatusActive"] : loc["GameMode.StatusInactive"];
        GameModeStatusText.Foreground = active ? HexBrush("#00FF9D") : (Brush)FindResource("TextMuted");
        TglGameMode.Content = active ? loc["GameMode.ToggleOn"] : loc["GameMode.ToggleOff"];
    }

    private async void TglGameMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!GameModeEngine.IsGameModeActive)
        {
            GameModeStatusText.Text = LocalizationManager.Instance["GameMode.Activating"];
            await GameModeEngine.ActivateGameModeAsync();
            RefreshGameModeUi();
        }
    }

    private async void TglGameMode_Unchecked(object sender, RoutedEventArgs e)
    {
        if (GameModeEngine.IsGameModeActive)
        {
            GameModeStatusText.Text = LocalizationManager.Instance["GameMode.Deactivating"];
            await GameModeEngine.DeactivateGameModeAsync();
            RefreshGameModeUi();
        }
    }

    private async void BtnPurgeStandby_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        BtnPurgeStandby.Content = loc["GameMode.PurgeBusy"];
        var (success, freed) = await GameModeEngine.PurgeStandbyListAsync();
        BtnPurgeStandby.Content = success ? loc.Format("GameMode.PurgeFreed", freed) : loc["GameMode.PurgeError"];
        GameModeStatusText.Text = success
            ? loc.Format("GameMode.PurgeOk", freed)
            : loc["GameMode.PurgeFail"];
    }

    private async Task ScanMsiDevicesAsync()
    {
        var loc = LocalizationManager.Instance;
        BtnScanMsi.IsEnabled = false;
        BtnScanMsi.Content = loc["Msi.ScanBusy"];
        MsiStatsText.Text = loc["Msi.Scanning"];
        try
        {
            await MsiEngine.ScanPciDevicesAsync();
            FilteredMsiDevices.Clear();
            foreach (var d in MsiEngine.Devices)
                FilteredMsiDevices.Add(d);

            var stats = MsiEngine.GetStatistics();
            MsiStatsText.Text = loc.Format("Msi.StatsFormat", stats.TotalDevices, stats.MsiEnabledCount, stats.MsiPercentage);
        }
        finally
        {
            BtnScanMsi.IsEnabled = true;
            BtnScanMsi.Content = loc["Msi.BtnScan"];
        }
    }

    private async void BtnScanMsi_Click(object sender, RoutedEventArgs e) => await ScanMsiDevicesAsync();

    private async void BtnMsiOptimize_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        BtnMsiOptimize.Content = "⏳...";
        int n = await MsiEngine.ApplyOptimalGamingMsiAsync();
        await ScanMsiDevicesAsync();
        MsiStatsText.Text = loc.Format("Msi.OptimizeDone", n);
        BtnMsiOptimize.Content = loc["Msi.BtnOptimize"];
    }

    private async void BtnMsiRestore_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        BtnMsiRestore.Content = "⏳...";
        int n = await MsiEngine.RestoreAllToDefaultAsync();
        await ScanMsiDevicesAsync();
        MsiStatsText.Text = loc.Format("Msi.RestoreDone", n);
        BtnMsiRestore.Content = loc["Msi.BtnRestore"];
    }

    private async void MsiToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is PciMsiDevice device)
        {
            bool enable = !device.IsMsiSupported;
            bool ok = await Task.Run(() => MsiEngine.SetMsiState(device, enable));
            if (ok)
            {
                await ScanMsiDevicesAsync();
            }
            else
            {
                MsiStatsText.Text = LocalizationManager.Instance.Format("Msi.ToggleFail", device.Name);
            }
        }
    }


    private async Task RefreshNetworkStatusAsync()
    {
        NetworkStatusText.Text = "⏳ Зчитування стану мережі...";
        try
        {
            var s = await NetworkEngine.GetTuningStatusAsync();
            var parts = new List<string>
            {
                s.IsNagleDisabled ? "🟢 Nagle вимкнено" : "⚪ Nagle активний",
                s.IsEeeDisabled ? "🟢 EEE вимкнено" : "⚪ EEE активний",
                s.IsQosUnlocked ? "🟢 QoS 100%" : "⚪ QoS 80%",
                s.IsDnsCacheOptimized ? "🟢 DNS кеш" : "⚪ DNS кеш"
            };
            NetworkStatusText.Text = "Статус: " + string.Join(" | ", parts);
            NetworkDetailsText.Text = $"Активні адаптери ({s.ActiveAdaptersCount}): {string.Join(", ", s.ActiveAdaptersNames)}";
        }
        catch (Exception ex)
        {
            NetworkStatusText.Text = "Помилка: " + ex.Message;
        }
    }

    private async void BtnNagle_Click(object sender, RoutedEventArgs e)
    {
        BtnNagle.Content = "⏳...";
        bool ok = await NetworkEngine.OptimizeTcpLatencyAsync(true);
        await RefreshNetworkStatusAsync();
        BtnNagle.Content = ok ? "✓ Nagle вимкнено" : "⚠️ Помилка";
    }

    private async void BtnEee_Click(object sender, RoutedEventArgs e)
    {
        BtnEee.Content = "⏳...";
        bool ok = await NetworkEngine.OptimizeNicPowerSavingAsync(true);
        await RefreshNetworkStatusAsync();
        BtnEee.Content = ok ? "✓ EEE вимкнено" : "⚠️ Помилка";
    }

    private async void BtnQos_Click(object sender, RoutedEventArgs e)
    {
        BtnQos.Content = "⏳...";
        bool ok = await NetworkEngine.OptimizeDnsAndQosAsync(true);
        await RefreshNetworkStatusAsync();
        BtnQos.Content = ok ? "✓ QoS 100%" : "⚠️ Помилка";
    }

    private async void BtnNetworkReset_Click(object sender, RoutedEventArgs e)
    {
        BtnNetworkReset.Content = "⏳...";
        bool ok = await NetworkEngine.ResetNetworkStackAsync();
        await RefreshNetworkStatusAsync();
        BtnNetworkReset.Content = ok ? "✓ Мережу відновлено" : "⚠️ Помилка";
    }

    private async void BtnGameBoost_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        BtnGameBoost.Content = loc["Sidebar.BtnGameBoostBusy"];
        try
        {
            var purge = await GameModeEngine.PurgeStandbyListAsync();
            bool activated = await GameModeEngine.ActivateGameModeAsync();
            RefreshGameModeUi();
            StatusText.Text = activated
                ? $"🚀 Game Boost застосовано (Standby: {purge.FreedMB:N0} MB)"
                : "Game Boost частково застосовано.";

            MessageBox.Show(
                $"Game Boost завершено.\nЗвільнено Standby RAM: {purge.FreedMB:N0} MB\nGame Mode активний: {(activated ? "Так" : "Ні")}",
                "1-Click Game Boost", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Помилка Game Boost: " + ex.Message, "Game Boost", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        BtnGameBoost.Content = loc["Sidebar.BtnGameBoost"];
    }

    private void BtnHealthScore_Click(object sender, RoutedEventArgs e)
    {
        var health = new HealthWindow { Owner = this };
        health.ShowDialog();
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var settings = new SettingsWindow { Owner = this };
        settings.ShowDialog();
    }

    private void BtnBackupMenu_Click(object sender, RoutedEventArgs e)
    {
        BackupMenu.PlacementTarget = BtnBackupMenu;
        BackupMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        BackupMenu.IsOpen = true;
    }

    #endregion

}