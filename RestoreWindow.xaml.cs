using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

// Явні аліаси для усунення колізій із WinForms
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace MASLOOPTIMIZER;

public partial class RestoreWindow : Window
{
    public RestoreWindow()
    {
        InitializeComponent();
        Loaded += async (s, e) => await RefreshBackupsListAsync();
    }

    private async Task RefreshBackupsListAsync()
    {
        var backups = await BackupEngine.GetAvailableBackupsAsync();
        BackupItemsControl.ItemsSource = backups;
    }

    private async void BtnRestoreItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is BackupEntry item)
        {
            if (MessageBox.Show($"Відновити всі параметри реєстру з вибраного бекапу?\n\nПапка: {item.Name}\nКлючів: {item.KeyCount}", "Підтвердження відновлення", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                var res = await BackupEngine.RestoreRegistryFromFolderAsync(item.FolderPath);
                MessageBox.Show(res.Message, "Відновлення реєстру", MessageBoxButton.OK, res.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
                Close();
            }
        }
    }

    private async void BtnDeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is BackupEntry item)
        {
            if (MessageBox.Show($"Видалити цю копію реєстру ({item.Name})?", "Видалення бекапу", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                await BackupEngine.DeleteBackupAsync(item.FolderPath);
                await RefreshBackupsListAsync();
            }
        }
    }

    private void BtnSystemRestoreUI_Click(object sender, RoutedEventArgs e)
    {
        BackupEngine.OpenSystemRestoreUI();
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        BackupEngine.OpenBackupsFolder();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}