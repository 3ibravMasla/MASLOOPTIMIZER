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
    public RestoreWindow()
    {
        InitializeComponent();
        Loaded += async (s, e) => await RefreshBackupsListAsync();
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
        try
        {
            var backups = await BackupEngine.GetAvailableBackupsAsync();
            BackupItemsControl.ItemsSource = backups;
            EmptyBackupsNotice.Visibility = (backups != null && backups.Count > 0) ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Помилка завантаження списку бекапів: {ex.Message}", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnRestoreItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not BackupEntry item) return;

        if (MessageBox.Show($"Відновити всі параметри реєстру з вибраного бекапу?\n\nПапка: {item.Name}\nКлючів: {item.KeyCount}",
            "Підтвердження відновлення", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        IsEnabled = false;
        try
        {
            var res = await BackupEngine.RestoreRegistryFromFolderAsync(item.FolderPath);
            MessageBox.Show(res.Message, "Відновлення реєстру", MessageBoxButton.OK, res.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не вдалося відновити реєстр: {ex.Message}", "Критична помилка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private async void BtnDeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not BackupEntry item) return;

        if (MessageBox.Show($"Видалити цю копію реєстру ({item.Name})?",
            "Видалення бекапу", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
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
            MessageBox.Show($"Помилка видалення бекапу: {ex.Message}", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show($"Не вдалося запустити відновлення системи: {ex.Message}", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show($"Не вдалося відкрити папку: {ex.Message}", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}