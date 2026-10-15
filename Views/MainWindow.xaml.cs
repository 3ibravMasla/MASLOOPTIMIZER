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
    private string _currentTab = "TWEAKS";
    private string _currentRisk = "ALL";
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
    private List<StartupItem>? _allStartupEntries;
    private StartupSortMode _currentStartupSort = StartupSortMode.Default;

    // Стан сортування для Очищення
    private CleanerSortMode _currentCleanerSort = CleanerSortMode.SizeDescending;

    // v0.6.0 Фаза 5: стан режиму CleanerView + аналізатор диска (WizTree).
    private bool _cleanerAnalyzerMode;
    private bool _analyzerDrivesLoaded;
    private List<DiskHeavyItem> _lastAnalyzerItems = new();

    // v0.6.0: ліниве завантаження важких сканів (усуває фриз холодного старту).
    private bool _toolsScanned;
    private bool _debloatScanned;

    public ObservableCollection<TweakModel> FilteredTweaks { get; set; } = new();
    public ObservableCollection<DebloatItem> FilteredDebloat { get; set; } = new();
    public ObservableCollection<ToolItem> FilteredTools { get; set; } = new();
    public ObservableCollection<DiskHeavyItem> FilteredAnalyzer { get; set; } = new();
    public ObservableCollection<DnsPreset> FilteredDns { get; set; } = new();
    public ObservableCollection<StartupItem> FilteredStartup { get; set; } = new();

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

        DnsItemsControl.ItemsSource = FilteredDns;
        CleanerItemsControl.ItemsSource = CleanerEngine.Cleaners;
        AnalyzerItemsControl.ItemsSource = FilteredAnalyzer;
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

    private bool _resourcesDisposed;

    /// <summary>
    /// Знімає підписки зі статичних подій та зупиняє таймери. Викликається один раз
    /// при виході з програми: вікно лише ховається (Closing скасовується), тому
    /// цей метод не залежить від Closed.
    /// </summary>
    public void DisposeResources()
    {
        if (_resourcesDisposed) return;
        _resourcesDisposed = true;

        _updateCheckTimer.Stop();
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
        HwBadgeTweaksCount.Text = LocalizationManager.Instance.Format("Header.DatabaseCount", TweakEngine.Instance.AllTweaks.Count);

        // Застосовуємо збережену мову до всього інтерфейсу одразу після завантаження даних.
        RefreshLocalizedChrome();

        UpdateTweakChipsAndFilter(rebuildChips: true);
        UpdateDebloatChipsAndFilter();
        UpdateToolsChipsAndFilter();
        UpdateDnsChipsAndFilter();
        UpdateStartupChipsAndFilter();

        // v0.6.0: важкі скани Tools/Debloat більше НЕ виконуються одразу — лише ліниво, при відкритті вкладки.
        // Оцінка статусів твіків виконується у фоні та не блокує холодний старт і не перебиває статусбар.
        AppProgressBar.Value = 0;
        ProgressPercentText.Text = "0%";
        _ = RunTweakStatusScanAsync();

        // Фаза 5.2/5.4: перша перевірка — не одразу після старту, а з відкладенням і jitter.
        _ = DeferredStartupUpdateCheckAsync();
    }

    /// <summary>
    /// v0.6.0: фонова оцінка статусів усіх твіків (не блокує холодний старт). Оновлює лише прогрес-бар.
    /// </summary>
    private async Task RunTweakStatusScanAsync()
    {
        try
        {
            await TweakEngine.Instance.EvaluateAllStatusesAsync((percent, _) =>
            {
                Dispatcher.Invoke(() =>
                {
                    AppProgressBar.Value = percent;
                    ProgressPercentText.Text = $"{percent}%";
                });
            });
            UpdateTweakChipsAndFilter();
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Фонова оцінка статусів твіків: {ex.Message}", "WARN");
        }
        finally
        {
            Dispatcher.Invoke(() =>
            {
                AppProgressBar.Value = 100;
                ProgressPercentText.Text = "100%";
            });
        }
    }

    #region Фоновий модуль оновлень (GitHub Toast/Banner)

    /// <summary>Фаза 5.2/5.4: відкладена стартова перевірка (N хвилин простою + випадковий jitter).</summary>
    private async Task DeferredStartupUpdateCheckAsync()
    {
        await UpdateManager.WaitStartupJitterAsync().ConfigureAwait(false);
        if (_resourcesDisposed) return;
        await CheckForUpdatesBackgroundAsync();
    }

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
        RefreshThemeBoundColors();
    }

    /// <summary>Перераховує кольори статусів Cleaner/MSI, які не оновлюються через чіпси категорій.</summary>
    private void RefreshThemeBoundColors()
    {
        foreach (var c in CleanerEngine.Cleaners) c.RefreshThemeColors();
    }

    public void RefreshLocalizedChromePublic()
    {
        RefreshLocalizedChrome();
        UpdateTweakChipsAndFilter(rebuildChips: true);
        UpdateDebloatChipsAndFilter();
        UpdateToolsChipsAndFilter();
        UpdateDnsChipsAndFilter();
        UpdateStartupChipsAndFilter();
    }

    #region Фільтрація ТВІКІВ

    // v0.6.0: чіпси категорій твіків кешуються — будуються лише при першому завантаженні або зміні мови;
    // при кліку оновлюється ТІЛЬКИ активний вигляд (без перебудови дерева елементів).
    private readonly Dictionary<string, Border> _tweakChipMap = new(StringComparer.OrdinalIgnoreCase);
    private bool _tweakChipsBuilt;

    private void UpdateTweakChipsAndFilter(bool rebuildChips = false)
    {
        if (rebuildChips || !_tweakChipsBuilt)
        {
            BuildTweakChips();
        }

        UpdateTweakChipStates();

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

    /// <summary>Будує чіпси категорій один раз (перше завантаження / зміна мови).</summary>
    private void BuildTweakChips()
    {
        CategoryChipsPanel.Children.Clear();
        _tweakChipMap.Clear();

        var categories = TweakEngine.Instance.GetCategories(_currentRisk);
        var tweaksInRisk = TweakEngine.Instance.AllTweaks
            .Where(t => _currentRisk.Equals("ALL", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(t.Risk, _currentRisk, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var cat in categories)
        {
            int count = cat == "Всі"
                ? tweaksInRisk.Count
                : tweaksInRisk.Count(t => string.Equals(t.Category, cat, StringComparison.OrdinalIgnoreCase));

            string catDisplay = LocalizeCategory(cat);
            string text = cat == "Всі" ? $"🌟 {catDisplay} ({count})" : $"{catDisplay} ({count})";

            var chip = CreateChipBorder(text, cat);
            string capturedCat = cat;

            chip.MouseEnter += (s, e) =>
            {
                if (!_tweakChipMap.TryGetValue(_currentTweakCategory, out var active) || !ReferenceEquals(active, chip))
                    chip.Background = (Brush)FindResource("ChipHoverBg");
            };
            chip.MouseLeave += (s, e) =>
            {
                if (!_tweakChipMap.TryGetValue(_currentTweakCategory, out var active) || !ReferenceEquals(active, chip))
                    chip.Background = (Brush)FindResource("ChipBg");
            };
            chip.MouseDown += (s, e) =>
            {
                _currentTweakCategory = capturedCat;
                UpdateTweakChipsAndFilter();
            };

            _tweakChipMap[cat] = chip;
            CategoryChipsPanel.Children.Add(chip);
        }

        _tweakChipsBuilt = true;
    }

    /// <summary>Оновлює лише активний вигляд кешованих чіпсів (без перебудови).</summary>
    private void UpdateTweakChipStates()
    {
        foreach (var pair in _tweakChipMap)
        {
            ApplyChipState(pair.Value, string.Equals(pair.Key, _currentTweakCategory, StringComparison.OrdinalIgnoreCase));
        }
    }

    private Border CreateChipBorder(string text, string tag)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(0, 0, 6, 4),
            Cursor = Cursors.Hand,
            Tag = tag,
            Child = new TextBlock { Text = text, FontSize = 11.5 }
        };
    }

    private void ApplyChipState(Border chip, bool isSelected)
    {
        chip.Background = isSelected ? (Brush)FindResource("ChipActiveBg") : (Brush)FindResource("ChipBg");
        chip.BorderBrush = isSelected ? (Brush)FindResource("ChipActiveBorder") : (Brush)FindResource("ChipBorder");
        if (chip.Child is TextBlock tb)
        {
            tb.FontWeight = isSelected ? FontWeights.Bold : FontWeights.SemiBold;
            tb.Foreground = isSelected ? (Brush)FindResource("ChipActiveText") : (Brush)FindResource("TextPrimary");
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

        UpdateDebloatStatsText();
    }

    /// <summary>Оновлює лише підсумок статистики без перебудови списку (без блимання).</summary>
    private void UpdateDebloatStatsText()
    {
        var stats = DebloatEngine.GetStatistics();
        if (DebloatStatsText != null)
        {
            DebloatStatsText.Text = LocalizationManager.Instance.Format("Debloat.StatsSummary", stats.Installed, stats.Removed, stats.Total);
        }
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
            sortMode: _currentStartupSort,
            sourceGroup: _currentStartupSource);

        foreach (var entry in filtered)
        {
            FilteredStartup.Add(entry);
        }
    }

    private void StartupSortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StartupSortComboBox?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            _currentStartupSort = tag switch
            {
                "EnabledFirst" => StartupSortMode.EnabledFirst,
                "DisabledFirst" => StartupSortMode.DisabledFirst,
                "Name" => StartupSortMode.NameAscending,
                "Category" => StartupSortMode.Category,
                "Source" => StartupSortMode.Source,
                _ => StartupSortMode.Default
            };
            UpdateStartupChipsAndFilter();
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
                UpdateCleanerList();
            }
            else if (tag == "DEBLOAT")
            {
                DebloatView.Visibility = Visibility.Visible;
                if (!_debloatScanned)
                {
                    StatusText.Text = LocalizationManager.Instance["Debloat.Rescanning"];
                    await DebloatEngine.ScanInstalledPackagesAsync();
                    _debloatScanned = true;
                }
                UpdateDebloatChipsAndFilter();
                StatusText.Text = LocalizationManager.Instance["Debloat.Ready"];
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
                if (!_toolsScanned)
                {
                    StatusText.Text = LocalizationManager.Instance["Tools.Rescanning"];
                    await ToolsEngine.DetectInstalledToolsAsync();
                    _toolsScanned = true;
                }
                UpdateToolsChipsAndFilter();
                StatusText.Text = LocalizationManager.Instance["Tools.Ready"];
            }
            else
            {
                // v0.6.0: уніфікований вью «Твіки системи» — усі твіки, категорії через CategoryChipsPanel.
                TweaksView.Visibility = Visibility.Visible;
                _currentRisk = "ALL";
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
                StatusText.Text = LocalizationManager.Instance.Format("Tweak.Applying", tweak.LocalizedName);
                AppLogger.Log($"Застосування твіка: {tweak.LocalizedName}");
                bool res = await TweakEngine.Instance.ExecuteTweakAsync(tweak, isApply: true);
                StatusText.Text = res ? LocalizationManager.Instance.Format("Tweak.ApplyDone", tweak.LocalizedName) : LocalizationManager.Instance.Format("Tweak.ApplyFailed", tweak.LocalizedName);
                AppLogger.Log($"Результат {tweak.LocalizedName}: {res}", res ? "SUCCESS" : "ERROR");
            }
            catch (Exception ex)
            {
                StatusText.Text = LocalizationManager.Instance.Format("Tweak.Crash", ex.Message);
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
                StatusText.Text = LocalizationManager.Instance.Format("Tweak.Restoring", tweak.LocalizedName);
                AppLogger.Log($"Відновлення твіка: {tweak.LocalizedName}");
                bool res = await TweakEngine.Instance.ExecuteTweakAsync(tweak, isApply: false);
                StatusText.Text = res ? LocalizationManager.Instance.Format("Tweak.RestoreDone", tweak.LocalizedName) : LocalizationManager.Instance.Format("Tweak.RestoreFailed", tweak.LocalizedName);
                AppLogger.Log($"Результат відновлення {tweak.LocalizedName}: {res}", res ? "SUCCESS" : "ERROR");
            }
            catch (Exception ex)
            {
                StatusText.Text = LocalizationManager.Instance.Format("Tweak.Crash", ex.Message);
                AppLogger.Log($"Помилка при відновленні {tweak.LocalizedName}: {ex.Message}", "ERROR");
            }
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

    private async void BtnFlushDns_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        bool ok = await DnsEngine.FlushDnsCacheAsync();
        StatusText.Text = ok ? loc["Dns.FlushDone"] : loc["Dns.FlushFailed"];
    }

    #endregion

    #region Cleaner

    private void UpdateCleanerList()
    {
        if (CleanerItemsControl == null) return;
        var filtered = CleanerEngine.GetFilteredAndSortedItems(sortMode: _currentCleanerSort);
        CleanerItemsControl.ItemsSource = filtered.ToList();
    }

    private void CleanerSortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CleanerSortComboBox?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            _currentCleanerSort = tag switch
            {
                "SizeAscending" => CleanerSortMode.SizeAscending,
                "SafeFirst" => CleanerSortMode.SafeFirst,
                "Name" => CleanerSortMode.NameAscending,
                "Category" => CleanerSortMode.Category,
                _ => CleanerSortMode.SizeDescending
            };
            UpdateCleanerList();
        }
    }

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
                UpdateCleanerList();
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

                long totalFreed = await CleanerEngine.CleanAllAsync(progress);
                CleanerTotalText.Text = loc["Cleaner.CleanDone"];
                StatusText.Text = loc.Format("Cleaner.TotalFreed", FormatBytes(totalFreed));
                AppLogger.Log($"Full clean: freed {FormatBytes(totalFreed)}", "SUCCESS");
                UpdateCleanerList();
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
        UpdateCleanerList();
    }

    #endregion

    #region Debloat & Startup

    private async void UninstallDebloat_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is DebloatItem item)
        {
            StatusText.Text = LocalizationManager.Instance.Format("Debloat.Uninstalling", item.Name);
            bool ok = await DebloatEngine.UninstallPackageAsync(item);
            StatusText.Text = ok ? LocalizationManager.Instance.Format("Debloat.Uninstalled", item.Name) : LocalizationManager.Instance.Format("Debloat.UninstallFailed", item.Name);
            AppLogger.Log($"Видалення UWP {item.Name}: {ok}", ok ? "SUCCESS" : "ERROR");
            UpdateDebloatStatsText();
        }
    }

    private async void RestoreDebloat_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is DebloatItem item)
        {
            StatusText.Text = LocalizationManager.Instance.Format("Debloat.Restoring", item.Name);
            bool ok = await DebloatEngine.RestorePackageAsync(item);
            StatusText.Text = ok ? LocalizationManager.Instance.Format("Debloat.Restored", item.Name) : LocalizationManager.Instance["Debloat.RestoreStoreOpened"];
            AppLogger.Log($"Відновлення UWP {item.Name}: {ok}", ok ? "SUCCESS" : "WARN");
            UpdateDebloatStatsText();
        }
    }

    private async void BtnRescanDebloat_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = LocalizationManager.Instance["Debloat.Rescanning"];
        await DebloatEngine.ScanInstalledPackagesAsync();
        UpdateDebloatChipsAndFilter();
        StatusText.Text = LocalizationManager.Instance["Debloat.RescanDone"];
    }

    /// <summary>Виділяє всі встановлені безпечні пакети (Risk = Safe) для пакетного видалення.</summary>
    private void BtnSelectSafeDebloat_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in DebloatEngine.Catalog)
        {
            item.IsSelected = item.IsInstalled && string.Equals(item.Risk, "Safe", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Пакетне видалення обраних пакетів в одному процесі PowerShell.</summary>
    private async void BtnRemoveSelectedDebloat_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        var selected = DebloatEngine.Catalog.Where(d => d.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusText.Text = loc["Debloat.NothingSelected"];
            return;
        }

        if (MessageBox.Show(loc.Format("Debloat.ConfirmBatch", selected.Count), loc["Debloat.ConfirmBatchTitle"],
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var progress = new Progress<string>(s => StatusText.Text = s);
            int removed = await DebloatEngine.RemoveBatchPackagesAsync(selected, progress);
            StatusText.Text = loc.Format("Debloat.BatchRemoved", removed);
            UpdateDebloatStatsText();
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Помилка пакетного видалення: {ex.Message}", "ERROR");
        }
    }

    /// <summary>Миттєве перемикання одного запису автозапуску без повного пересканування списку.</summary>
    private async void ToggleStartup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is StartupItem item)
        {
            bool target = !item.IsEnabled;
            await StartupEngine.ToggleStartupStateAsync(item, target);
            var loc = LocalizationManager.Instance;
            string state = item.IsEnabled ? loc["Startup.EnabledWord"] : loc["Startup.PausedWord"];
            StatusText.Text = loc.Format("Startup.ToggleDone", item.Name, state);
        }
    }

    private void OpenStartupLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is StartupItem item)
        {
            StartupEngine.OpenFileLocation(item);
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
                    StatusText.Text = LocalizationManager.Instance["Tools.MasLaunched"];
                }
                else if (tool.SpecialAction == "VCREDIST")
                {
                    StatusText.Text = LocalizationManager.Instance["Tools.VcRedistInstalling"];
                    bool ok = await ToolsEngine.InstallVcRedistAllAsync(tool);
                    StatusText.Text = ok ? LocalizationManager.Instance["Tools.VcRedistDone"] : LocalizationManager.Instance["Tools.VcRedistError"];
                    UpdateToolsChipsAndFilter();
                }
                else if (tool.SpecialAction == "DIRECTX")
                {
                    StatusText.Text = LocalizationManager.Instance["Tools.DirectXUpdating"];
                    bool ok = await ToolsEngine.InstallDirectXWebAsync(tool);
                    StatusText.Text = ok ? LocalizationManager.Instance["Tools.DirectXDone"] : LocalizationManager.Instance["Tools.DirectXError"];
                    UpdateToolsChipsAndFilter();
                }
                else if (!string.IsNullOrWhiteSpace(tool.WingetId))
                {
                    StatusText.Text = LocalizationManager.Instance.Format("Tools.WingetInstalling", tool.Name);
                    bool ok = await ToolsEngine.InstallWingetPackageAsync(tool);
                    StatusText.Text = ok ? LocalizationManager.Instance.Format("Tools.InstallDone", tool.Name) : LocalizationManager.Instance.Format("Tools.InstallError", tool.Name);
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
        StatusText.Text = LocalizationManager.Instance["Tools.Rescanning"];
        await ToolsEngine.DetectInstalledToolsAsync();
        UpdateToolsChipsAndFilter();
        StatusText.Text = LocalizationManager.Instance["Tools.RescanDone"];
    }

    private void OpenToolSite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ToolItem tool)
        {
            ToolsEngine.OpenUrl(tool.Url);
        }
    }

    private void OpenTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ToolItem tool)
        {
            bool ok = ToolsEngine.OpenInstalledTool(tool);
            StatusText.Text = ok ? LocalizationManager.Instance.Format("Tools.Launched", tool.Name) : LocalizationManager.Instance.Format("Tools.LaunchError", tool.Name);
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
        var loc = LocalizationManager.Instance.For("BackupEngine");
        if (MessageBox.Show(loc["MainVssConfirm"], loc["MainVssTitle"], MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            StatusText.Text = loc["MainVssBusy"];
            var res = await BackupEngine.CreateVssRestorePointAsync();
            StatusText.Text = res.Message;
            MessageBox.Show(res.Message, loc["MainVssResultTitle"], MessageBoxButton.OK, res.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
    }

    private async void BtnRegBackup_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance.For("BackupEngine");
        StatusText.Text = loc["MainBackupBusy"];
        var res = await BackupEngine.ExportRegistryBackupAsync();
        StatusText.Text = res.Message;
        MessageBox.Show(res.Message, loc["MainBackupResultTitle"], MessageBoxButton.OK, res.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void BtnRestoreRollback_Click(object sender, RoutedEventArgs e)
    {
        var restoreWin = new RestoreWindow { Owner = this };
        restoreWin.ShowDialog();
    }

    private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        StatusText.Text = loc["Update.Checking"];
        // Ручна перевірка (Фаза 5.2): ігнорує throttle і quiet-режим — це явна дія користувача.
        var (available, newVer, url) = await UpdateManager.CheckForUpdateAsync(manual: true);

        if (available)
        {
            if (MessageBox.Show(loc.Format("Update.Available", newVer), loc["Update.AvailableTitle"], MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                StatusText.Text = loc["Update.Downloading"];
                await UpdateManager.DownloadAndInstallUpdateAsync(url);
            }
        }
        else
        {
            MessageBox.Show(loc.Format("Update.UpToDate", UpdateManager.CurrentVersion), loc["Update.UpToDateTitle"], MessageBoxButton.OK, MessageBoxImage.Information);
            StatusText.Text = loc["Update.UpToDateStatus"];
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
            RefreshThemeBoundColors();
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
        UpdateTweakChipsAndFilter(rebuildChips: true);
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
        HwBadgeTweaksCount.Text = loc.Format("Header.DatabaseCount", TweakEngine.Instance.AllTweaks.Count);

        // Сайдбар
        LblNavTweaks.Text = loc["Sidebar.NavTweaks"];
        LblNavDns.Text = loc["Sidebar.NavDns"];
        LblNavDebloat.Text = loc["Sidebar.NavDebloat"];
        LblNavStartup.Text = loc["Sidebar.NavStartup"];
        LblNavTools.Text = loc["Sidebar.NavTools"];
        LblNavClean.Text = loc["Sidebar.NavCleaner"];

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
        LblSortStartup.Text = loc["Common.SortLabel"];
        LblSortCleaner.Text = loc["Common.SortLabel"];
        ApplyLocalizedSortItems();
        ApplyDnsSortItems();

        // Заголовки та кнопки секцій
        DnsTitleText.Text = loc["Dns.Title"];
        DnsDescText.Text = loc["Dns.Description"];
        BtnFastestDns.Content = loc["Dns.BtnFastest"];
        BtnResetDns.Content = loc["Dns.BtnReset"];
        BtnFlushDns.Content = loc["Dns.BtnFlush"];

        DebloatTitleText.Text = loc["Debloat.Title"];
        BtnRescanDebloat.Content = loc["Debloat.BtnRescan"];
        BtnSelectSafeDebloat.Content = loc["Debloat.SelectSafe"];
        BtnRemoveSelectedDebloat.Content = loc["Debloat.RemoveSelected"];

        StartupTitleText.Text = loc["Startup.Title"];
        StartupDescText.Text = loc["Startup.Description"];
        BtnRescanStartup.Content = loc["Startup.BtnRescan"];

        ToolsTitleText.Text = loc["Tools.Title"];
        BtnRescanTools.Content = loc["Tools.BtnRescan"];
        ChkCreateDesktopShortcut.Content = loc["Tools.ChkCreateShortcut"];
        ChkCreateDesktopShortcut.IsChecked = ToolsEngine.CreateDesktopShortcutOnInstall;

        CleanerTitleText.Text = loc["Cleaner.Title"];
        CleanerTotalText.Text = loc["Cleaner.Calculating"];
        BtnCleanAll.Content = loc["Cleaner.BtnCleanAll"];
        BtnRescanCleaner.Content = loc["Cleaner.BtnRescan"];

        // Режими CleanerView + аналізатор диска
        BtnCleanerModeCache.Content = loc["Cleaner.Analyzer.ModeCache"];
        BtnCleanerModeAnalyzer.Content = loc["Cleaner.Analyzer.ModeAnalyzer"];
        LblAnalyzerDrive.Text = loc["Cleaner.Analyzer.DriveLabelShort"];
        LblAnalyzerFilter.Text = loc["Cleaner.Analyzer.FilterLabel"];
        BtnAnalyzerScan.Content = loc["Common.BtnScan"];
        AnalyzerSummaryText.Text = loc["Cleaner.Analyzer.Ready"];
        ApplyAnalyzerSortItems();
        if (_cleanerAnalyzerMode) PopulateAnalyzerDrives();

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

        var startupMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Default"] = "Common.SortDefault",
            ["EnabledFirst"] = "Startup.SortEnabledFirst",
            ["DisabledFirst"] = "Startup.SortDisabledFirst",
            ["Name"] = "Common.SortName",
            ["Category"] = "Startup.SortCategory",
            ["Source"] = "Startup.SortSource",
        };
        ApplySortItems(StartupSortComboBox, startupMap);

        var cleanerMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SizeDescending"] = "Cleaner.SortSizeDesc",
            ["SizeAscending"] = "Cleaner.SortSizeAsc",
            ["SafeFirst"] = "Cleaner.SortSafeFirst",
            ["Name"] = "Common.SortName",
            ["Category"] = "Cleaner.SortCategory",
        };
        ApplySortItems(CleanerSortComboBox, cleanerMap);

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

    /// <summary>Локалізує пункти фільтра аналізатора диска (Топ папок / Топ файлів / Усі).</summary>
    private void ApplyAnalyzerSortItems()
    {
        if (AnalyzerFilterComboBox == null) return;
        foreach (var item in AnalyzerFilterComboBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string tag)
            {
                item.Content = tag switch
                {
                    "folders" => LocalizationManager.Instance["Cleaner.Analyzer.FilterFolders"],
                    "files" => LocalizationManager.Instance["Cleaner.Analyzer.FilterFiles"],
                    _ => LocalizationManager.Instance["Cleaner.Analyzer.FilterAll"]
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

    #region Аналізатор диска (WizTree) та ярлики Tools

    private void SetCleanerMode(bool analyzer)
    {
        _cleanerAnalyzerMode = analyzer;
        CleanerCacheModePanel.Visibility = analyzer ? Visibility.Collapsed : Visibility.Visible;
        CleanerAnalyzerModePanel.Visibility = analyzer ? Visibility.Visible : Visibility.Collapsed;

        BtnCleanerModeCache.Background = (Brush)FindResource(analyzer ? "ActionBtnBg" : "AccentGreen");
        BtnCleanerModeAnalyzer.Background = (Brush)FindResource(analyzer ? "AccentGreen" : "ActionBtnBg");

        if (analyzer) PopulateAnalyzerDrives();
    }

    private void BtnCleanerModeCache_Click(object sender, RoutedEventArgs e) => SetCleanerMode(false);

    private void BtnCleanerModeAnalyzer_Click(object sender, RoutedEventArgs e) => SetCleanerMode(true);

    /// <summary>Заповнює список дисків (fixed drives) + швидкі пресети (Профіль, Downloads).</summary>
    private void PopulateAnalyzerDrives()
    {
        if (_analyzerDrivesLoaded || AnalyzerDriveComboBox == null) return;
        var loc = LocalizationManager.Instance;

        AnalyzerDriveComboBox.Items.Clear();
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                double freeGb = drive.AvailableFreeSpace / 1073741824.0;
                double totalGb = drive.TotalSize / 1073741824.0;
                string label = loc.Format("Cleaner.Analyzer.DriveLabel", drive.Name, freeGb.ToString("0.#"), totalGb.ToString("0.#"));
                AnalyzerDriveComboBox.Items.Add(new ComboBoxItem { Content = label, Tag = drive.Name });
            }
        }
        catch { }

        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile))
        {
            AnalyzerDriveComboBox.Items.Add(new ComboBoxItem { Content = loc["Cleaner.Analyzer.PresetProfile"], Tag = profile });
            AnalyzerDriveComboBox.Items.Add(new ComboBoxItem { Content = loc["Cleaner.Analyzer.PresetDownloads"], Tag = Path.Combine(profile, "Downloads") });
        }

        if (AnalyzerDriveComboBox.Items.Count > 0) AnalyzerDriveComboBox.SelectedIndex = 0;
        _analyzerDrivesLoaded = true;
    }

    private async void BtnAnalyzerScan_Click(object sender, RoutedEventArgs e)
    {
        if (AnalyzerDriveComboBox?.SelectedItem is not ComboBoxItem sel || sel.Tag is not string root)
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        BtnAnalyzerScan.IsEnabled = false;
        FilteredAnalyzer.Clear();
        AnalyzerSummaryText.Text = loc["Cleaner.Analyzer.Scanning"];

        try
        {
            if (!Directory.Exists(root))
            {
                AnalyzerSummaryText.Text = loc["Cleaner.Analyzer.PathMissing"];
                return;
            }

            var progress = new Progress<string>(s => AnalyzerSummaryText.Text = s);
            var items = await CleanerEngine.ScanDriveHeavyItemsAsync(root, 50, 30, progress);

            _lastAnalyzerItems = items;
            ApplyAnalyzerFilter();

            string filter = (AnalyzerFilterComboBox?.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
            AnalyzerSummaryText.Text = loc.Format("Cleaner.Analyzer.ScanDone", root, FilteredAnalyzer.Count);
            AppLogger.Log($"Аналізатор диска: {root} → {items.Count} елементів (фільтр {filter})", "SUCCESS");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Помилка аналізу диска {root}: {ex.Message}", "ERROR");
            AnalyzerSummaryText.Text = loc["Cleaner.Analyzer.ScanFailed"];
        }
        finally
        {
            BtnAnalyzerScan.IsEnabled = true;
        }
    }

    private void AnalyzerFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyAnalyzerFilter();
    }

    private void ApplyAnalyzerFilter()
    {
        if (AnalyzerItemsControl == null) return;
        string filter = (AnalyzerFilterComboBox?.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";

        FilteredAnalyzer.Clear();
        foreach (var item in _lastAnalyzerItems)
        {
            if (filter == "folders" && !item.IsDirectory) continue;
            if (filter == "files" && item.IsDirectory) continue;
            FilteredAnalyzer.Add(item);
        }
    }

    private void AnalyzerOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is DiskHeavyItem item)
        {
            CleanerEngine.OpenInExplorer(item);
        }
    }

    private async void AnalyzerDeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not DiskHeavyItem item) return;

        var loc = LocalizationManager.Instance;
        var confirm = MessageBox.Show(
            loc.Format("Cleaner.Analyzer.ConfirmDelete", item.Name),
            loc["Cleaner.Analyzer.ConfirmTitle"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        bool ok = await CleanerEngine.DeleteHeavyItemAsync(item, sendToRecycleBin: true);
        if (ok)
        {
            FilteredAnalyzer.Remove(item);
            _lastAnalyzerItems.Remove(item);
            StatusText.Text = loc.Format("Cleaner.Analyzer.Deleted", item.Name);
        }
        else
        {
            StatusText.Text = loc["Cleaner.Analyzer.DeleteFailed"];
        }
    }

    private void ChkCreateDesktopShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox cb)
        {
            ToolsEngine.CreateDesktopShortcutOnInstall = cb.IsChecked == true;
        }
    }

    private void CreateShortcutTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ToolItem tool)
        {
            var loc = LocalizationManager.Instance;
            bool ok = ToolsEngine.CreateDesktopShortcut(tool);
            StatusText.Text = ok
                ? loc.Format("Tools.ShortcutCreated", tool.Name)
                : loc.Format("Tools.ShortcutFailed", tool.Name);
        }
    }

    #endregion

    #region Налаштування та бекап

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