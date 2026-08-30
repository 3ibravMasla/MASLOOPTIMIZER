using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace MASLOOPTIMIZER;

public partial class RestoreWindow : Window
{
    private ModuleStrings Loc => LocalizationManager.Instance.For("BackupEngine");

    private BackupSortMode _currentBackupSort = BackupSortMode.DateDescending;

    /// <summary>Localized labels for buttons inside the DataTemplate (bound against the window).</summary>
    public string RestoreButtonLabel => Loc["BtnRestore"];
    public string DeleteButtonLabel => Loc["BtnDelete"];

    public RestoreWindow()
    {
        InitializeComponent();
        DataContext = this;
        ApplyLocalizedUi();
        Loaded += async (s, e) => await RefreshBackupsListAsync();
    }

    private void ApplyLocalizedUi()
    {
        Title = Loc["Title"];
        TitleText.Text = Loc["RestoreTitle"];
        SubtitleText.Text = Loc["RestoreSubtitle"];
        EmptyBackupsNotice.Text = Loc["EmptyNotice"];
        BtnSystemRestore.Content = Loc["BtnSystemRestore"];
        BtnOpenFolder.Content = Loc["BtnOpenFolder"];
        BtnClose.Content = Loc["BtnClose"];

        LblSortBackup.Text = LocalizationManager.Instance["Common.SortLabel"];
        foreach (var item in BackupSortComboBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string tag)
            {
                item.Content = tag switch
                {
                    "DateAscending" => Loc["SortDateAsc"],
                    "SizeDescending" => Loc["SortSizeDesc"],
                    "KeyCountDescending" => Loc["SortKeyCount"],
                    "NameAscending" => Loc["SortName"],
                    _ => Loc["SortDateDesc"]
                };
            }
        }
    }

    private async void BackupSortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BackupSortComboBox?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            _currentBackupSort = tag switch
            {
                "DateAscending" => BackupSortMode.DateAscending,
                "SizeDescending" => BackupSortMode.SizeDescending,
                "KeyCountDescending" => BackupSortMode.KeyCountDescending,
                "NameAscending" => BackupSortMode.NameAscending,
                _ => BackupSortMode.DateDescending
            };
            await RefreshBackupsListAsync();
        }
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    private async Task RefreshBackupsListAsync()
    {
        if (BackupItemsControl == null) return;
        try
        {
            var backups = await BackupEngine.GetAvailableBackupsAsync(_currentBackupSort);
            BackupItemsControl.ItemsSource = backups;
            EmptyBackupsNotice.Visibility = (backups != null && backups.Count > 0) ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.Format("LoadError", ex.Message), Loc["ErrorTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnRestoreItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not BackupEntry item) return;

        if (MessageBox.Show(Loc.Format("ConfirmRestoreMessage", item.Name, item.KeyCount),
            Loc["ConfirmRestoreTitle"], MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        IsEnabled = false;
        try
        {
            var res = await BackupEngine.RestoreRegistryFromFolderAsync(item.FolderPath);
            MessageBox.Show(res.Message, Loc["RestoreDoneTitle"], MessageBoxButton.OK, res.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.Format("RestoreFail", ex.Message), Loc["RestoreFailTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private async void BtnDeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not BackupEntry item) return;

        if (MessageBox.Show(Loc.Format("ConfirmDeleteMessage", item.Name),
            Loc["ConfirmDeleteTitle"], MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        IsEnabled = false;
        try
        {
            await BackupEngine.DeleteBackupAsync(item.FolderPath);
            await RefreshBackupsListAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.Format("DeleteError", ex.Message), Loc["ErrorTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void BtnSystemRestoreUI_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BackupEngine.OpenSystemRestoreUI();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.Format("SystemRestoreFail", ex.Message), Loc["ErrorTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BackupEngine.OpenBackupsFolder();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.Format("OpenFolderFail", ex.Message), Loc["ErrorTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}