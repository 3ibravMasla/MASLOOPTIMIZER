using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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

public partial class MainWindow : Window
{
    private string _currentRisk = "UI";
    private string _currentCategory = "Всі";
    private string _searchQuery = string.Empty;
    private HardwareInfo? _currentHwInfo;

    public ObservableCollection<TweakModel> FilteredTweaks { get; set; } = new();

    public MainWindow()
    {
        InitializeComponent();

        TweaksItemsControl.ItemsSource = FilteredTweaks;
        DnsItemsControl.ItemsSource = DnsEngine.Catalog;
        CleanerItemsControl.ItemsSource = CleanerEngine.Cleaners;
        DebloatItemsControl.ItemsSource = DebloatEngine.Catalog;
        StartupItemsControl.ItemsSource = StartupEngine.GetStartupEntries();
        ToolsItemsControl.ItemsSource = ToolsEngine.Catalog;

        Loaded += MainWindow_Loaded;
        Closing += (s, e) =>
        {
            e.Cancel = true;
            Hide();
        };
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
        HwBadgeTweaksCount.Text = $"{TweakEngine.Instance.AllTweaks.Count} твіків";
        UpdateNavigationAndFilter();

        _ = ToolsEngine.DetectInstalledToolsAsync();

        StatusText.Text = "Перевірка активних параметрів системи...";
        await TweakEngine.Instance.EvaluateAllStatusesAsync((percent, name) =>
        {
            Dispatcher.Invoke(() =>
            {
                AppProgressBar.Value = percent;
                ProgressPercentText.Text = $"{percent}%";
                StatusText.Text = $"Аналіз: {name}";
            });
        });

        StatusText.Text = "Діагностика завершена. Система готова до роботи.";
        AppProgressBar.Value = 100;
        ProgressPercentText.Text = "100%";

        _ = UpdateManager.CheckForUpdateAsync().ContinueWith(task =>
        {
            if (task.IsCompletedSuccessfully && task.Result.UpdateAvailable)
            {
                Dispatcher.Invoke(() =>
                {
                    BtnCheckUpdates.Content = $"🚀 Оновлення ({task.Result.NewVersion})";
                    BtnCheckUpdates.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#107C41"));
                    StatusText.Text = $"Доступна нова версія {task.Result.NewVersion}!";
                });
            }
        });
    }

    #region Навігація та Фільтри

    private void UpdateNavigationAndFilter()
    {
        CategoryChipsPanel.Children.Clear();

        var categories = TweakEngine.Instance.GetCategories(_currentRisk);

        var tweaksInRisk = TweakEngine.Instance.AllTweaks
            .Where(t => string.Equals(t.Risk, _currentRisk, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var cat in categories)
        {
            int count = cat == "Всі" ? tweaksInRisk.Count : tweaksInRisk.Count(t => string.Equals(t.Category, cat, StringComparison.OrdinalIgnoreCase));

            var chip = new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 5, 12, 5),
                Margin = new Thickness(0, 0, 6, 4),
                Cursor = Cursors.Hand,
                Tag = cat,
                Background = (Brush)FindResource(string.Equals(cat, _currentCategory, StringComparison.OrdinalIgnoreCase) ? "ChipActiveBg" : "ChipBg"),
                BorderBrush = (Brush)FindResource("ChipBorder"),
                Child = new TextBlock
                {
                    Text = cat == "Всі" ? $"🌟 Всі ({count})" : $"{cat} ({count})",
                    FontSize = 11.5,
                    FontWeight = string.Equals(cat, _currentCategory, StringComparison.OrdinalIgnoreCase) ? FontWeights.Bold : FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("TextPrimary")
                }
            };

            chip.MouseDown += (s, e) =>
            {
                _currentCategory = (string)chip.Tag;
                UpdateNavigationAndFilter();
            };

            CategoryChipsPanel.Children.Add(chip);
        }

        FilteredTweaks.Clear();
        var filtered = TweakEngine.Instance.GetFilteredAndSortedTweaks(
            riskLevel: _currentRisk,
            category: _currentCategory,
            searchQuery: _searchQuery,
            sortMode: TweakSortMode.Default
        );

        foreach (var tweak in filtered)
        {
            FilteredTweaks.Add(tweak);
        }
    }

    private async void NavBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
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
                StatusText.Text = "Замір пінгу DNS-серверів...";
                await DnsEngine.MeasureAllPingsAsync();
                StatusText.Text = "DNS-сервери відсортовано за найменшим пінгом.";
            }
            else if (tag == "CLEAN")
            {
                CleanerView.Visibility = Visibility.Visible;
                StatusText.Text = "Сканування дискового простору...";
                await CleanerEngine.CalculateSizesAsync();
                long total = CleanerEngine.Cleaners.Sum(c => c.BytesFound);
                CleanerTotalText.Text = $"Виявлено для очищення: {FormatBytes(total)}";
                StatusText.Text = "Аналіз кешів завершено.";
            }
            else if (tag == "DEBLOAT")
            {
                DebloatView.Visibility = Visibility.Visible;
                StatusText.Text = "Сканування встановлених UWP-пакетів...";
                await DebloatEngine.ScanInstalledPackagesAsync();
                StatusText.Text = "Деблоат-менеджер готовий.";
            }
            else if (tag == "STARTUP")
            {
                StartupView.Visibility = Visibility.Visible;
                StartupItemsControl.ItemsSource = await StartupEngine.GetStartupEntriesAsync();
                StatusText.Text = "Список автозавантаження оновлено.";
            }
            else if (tag == "TOOLS")
            {
                ToolsView.Visibility = Visibility.Visible;
                await ToolsEngine.DetectInstalledToolsAsync();
                StatusText.Text = "Бібліотека софту та інструментів готова.";
            }
            else
            {
                TweaksView.Visibility = Visibility.Visible;
                _currentRisk = tag;
                _currentCategory = "Всі";
                UpdateNavigationAndFilter();
            }
        }
    }

    private void SearchInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchQuery = SearchInput.Text.Trim();
        UpdateNavigationAndFilter();
    }

    #endregion

    #region Твіки

    private async void ApplyTweak_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TweakModel tweak)
        {
            try
            {
                StatusText.Text = $"Застосування: {tweak.Name}...";
                AppLogger.Log($"Застосування твіка: {tweak.Name}");
                bool res = await TweakEngine.Instance.ExecuteTweakAsync(tweak, isApply: true);
                StatusText.Text = res ? $"Успішно застосовано: {tweak.Name}" : $"Помилка виконання: {tweak.Name}";
                AppLogger.Log($"Результат {tweak.Name}: {res}", res ? "SUCCESS" : "ERROR");
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Збій: {ex.Message}";
                AppLogger.Log($"Помилка при застосуванні {tweak.Name}: {ex.Message}", "ERROR");
            }
        }
    }

    private async void RestoreTweak_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TweakModel tweak)
        {
            try
            {
                StatusText.Text = $"Відновлення: {tweak.Name}...";
                AppLogger.Log($"Відновлення твіка: {tweak.Name}");
                bool res = await TweakEngine.Instance.ExecuteTweakAsync(tweak, isApply: false);
                StatusText.Text = res ? $"Відновлено: {tweak.Name}" : $"Помилка відновлення: {tweak.Name}";
                AppLogger.Log($"Результат відновлення {tweak.Name}: {res}", res ? "SUCCESS" : "ERROR");
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Збій: {ex.Message}";
                AppLogger.Log($"Помилка при відновленні {tweak.Name}: {ex.Message}", "ERROR");
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
                StatusText.Text = $"Оптимізація: {tweak.Name}";

                await TweakEngine.Instance.ExecuteTweakAsync(tweak, isApply: true);
            }
            StatusText.Text = "Усі безпечні твіки успішно застосовано!";
            AppLogger.Log("1-Click Safe Pack повністю застосовано", "SUCCESS");
            UpdateNavigationAndFilter();
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
            UpdateNavigationAndFilter();
        }
    }

    #endregion

    #region DNS

    private void ApplyDns_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is DnsPreset preset)
        {
            bool ok = DnsEngine.ApplyDns(preset.Primary, preset.Secondary);
            StatusText.Text = ok ? $"DNS встановлено: {preset.Name}" : "Помилка встановлення DNS.";
            AppLogger.Log($"Встановлення DNS {preset.Name}: {ok}", ok ? "SUCCESS" : "ERROR");
        }
    }

    private async void BtnFastestDns_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Пошук найшвидшого DNS...";
        await DnsEngine.MeasureAllPingsAsync();
        var fastest = DnsEngine.GetFastestPreset();
        if (fastest != null)
        {
            DnsEngine.ApplyDns(fastest.Primary, fastest.Secondary);
            StatusText.Text = $"Встановлено найшвидший DNS: {fastest.Name} ({fastest.Ping} ms)";
            MessageBox.Show($"Найшвидший сервер: {fastest.Name}\nЗатримка (Ping): {fastest.Ping} ms\n\nСервер успішно активовано!", "DNS Оптимізатор", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnResetDns_Click(object sender, RoutedEventArgs e)
    {
        bool ok = DnsEngine.RestoreOriginalDns();
        StatusText.Text = ok ? "DNS успішно повернуто до початкового стану (DHCP)." : "Помилка відновлення DNS.";
        AppLogger.Log("DNS скинуто до DHCP", ok ? "SUCCESS" : "WARN");
    }

    #endregion

    #region Cleaner

    private async void CleanItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is CleanerItem item)
        {
            try
            {
                StatusText.Text = $"Очищення: {item.Name}...";
                long freed = await CleanerEngine.CleanItemAsync(item);
                StatusText.Text = $"Звільнено: {FormatBytes(freed)}";
                AppLogger.Log($"Очищення {item.Name}: {FormatBytes(freed)}", "SUCCESS");

                long totalRemaining = CleanerEngine.Cleaners.Sum(c => c.BytesFound);
                CleanerTotalText.Text = $"Виявлено для очищення: {FormatBytes(totalRemaining)}";
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Помилка очищення {item.Name}: {ex.Message}", "ERROR");
            }
        }
    }

    private async void BtnCleanAll_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Очистити всі виявлені безпечні кеші та файли?", "1-Click Очищення", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            try
            {
                var progress = new Progress<(int Percent, string Name, long Freed)>(p =>
                {
                    AppProgressBar.Value = p.Percent;
                    ProgressPercentText.Text = $"{p.Percent}%";
                    StatusText.Text = $"Очищено: {p.Name} (+{FormatBytes(p.Freed)})";
                });

                long totalFreed = await CleanerEngine.CleanAllSafeAsync(progress);
                CleanerTotalText.Text = "Очищення завершено!";
                StatusText.Text = $"Успішно звільнено: {FormatBytes(totalFreed)}";
                AppLogger.Log($"Повне очищення: звільнено {FormatBytes(totalFreed)}", "SUCCESS");
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Помилка повного очищення: {ex.Message}", "ERROR");
            }
        }
    }

    private async void BtnRescanCleaner_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Пересканування накопичувачів...";
        await CleanerEngine.CalculateSizesAsync();
        long total = CleanerEngine.Cleaners.Sum(c => c.BytesFound);
        CleanerTotalText.Text = $"Виявлено для очищення: {FormatBytes(total)}";
        StatusText.Text = "Дисковий простір перескановано.";
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
        }
    }

    private async void RestoreDebloat_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is DebloatItem item)
        {
            StatusText.Text = $"Відновлення {item.Name}...";
            bool ok = await DebloatEngine.RestorePackageAsync(item);
            StatusText.Text = ok ? $"Відновлено: {item.Name}" : $"Відкрито сторінку в Store.";
        }
    }

    private async void BtnRescanDebloat_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Пересканування UWP...";
        await DebloatEngine.ScanInstalledPackagesAsync();
        StatusText.Text = "Статус UWP пакетів оновлено.";
    }

    private void ToggleStartup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is StartupEntry item)
        {
            StartupEngine.ToggleStartupState(item);
            StatusText.Text = $"Автозапуск для {item.Name}: {(item.IsEnabled ? "Увімкнено" : "Призупинено")}";
        }
    }

    private async void BtnRescanStartup_Click(object sender, RoutedEventArgs e)
    {
        StartupItemsControl.ItemsSource = await StartupEngine.GetStartupEntriesAsync();
        StatusText.Text = "Список автозавантаження перескановано.";
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
                }
                else if (tool.SpecialAction == "DIRECTX")
                {
                    StatusText.Text = "Оновлення бібліотек DirectX...";
                    bool ok = await ToolsEngine.InstallDirectXWebAsync(tool);
                    StatusText.Text = ok ? "Бібліотеки DirectX успішно оновлено!" : "Помилка оновлення DirectX.";
                }
                else if (!string.IsNullOrWhiteSpace(tool.WingetId))
                {
                    StatusText.Text = $"Встановлення {tool.Name} через Winget...";
                    bool ok = await ToolsEngine.InstallWingetPackageAsync(tool);
                    StatusText.Text = ok ? $"Успішно встановлено: {tool.Name}" : $"Помилка встановлення {tool.Name}.";
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Помилка встановлення {tool.Name}: {ex.Message}", "ERROR");
            }
        }
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
            UpdateNavigationAndFilter();
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
        bool nextTheme = !ThemeManager.IsDarkTheme;
        ThemeManager.ApplyTheme(nextTheme);
        BtnThemeToggle.Content = nextTheme ? "🌙 Темна тема" : "☀️ Світла тема";
        UpdateNavigationAndFilter();
    }

    #endregion

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):N2} ГБ";
        if (bytes >= 1024 * 1024) return $"{bytes / (1024.0 * 1024):N2} МБ";
        if (bytes >= 1024) return $"{bytes / 1024.0:N2} КБ";
        return $"{bytes} Байт";
    }
}