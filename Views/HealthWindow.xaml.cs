using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;

using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace MASLOOPTIMIZER;

public partial class HealthWindow : Window
{
    public HealthWindow()
    {
        InitializeComponent();
        Loaded += HealthWindow_Loaded;
    }

    private async void HealthWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RunAuditAsync();
    }

    private async void BtnRescan_Click(object sender, RoutedEventArgs e)
    {
        await RunAuditAsync();
    }

    private async void BtnFixAll_Click(object sender, RoutedEventArgs e)
    {
        BtnFixAll.IsEnabled = false;
        BtnFixAll.Content = "⏳ Виправляю...";
        try
        {
            int fixedCount = await HealthEngine.FixAllIssuesAsync();
            MessageBox.Show($"Успішно оптимізовано параметрів: {fixedCount}", "MASL Health Fix",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            BtnFixAll.IsEnabled = true;
            BtnFixAll.Content = "⚡ 1-Click Fix All";
        }
        await RunAuditAsync();
    }

    private async System.Threading.Tasks.Task RunAuditAsync()
    {
        BtnRescan.IsEnabled = false;
        BtnRescan.Content = "⏳ Аналіз...";
        try
        {
            var report = await HealthEngine.RunHealthAuditAsync();
            ChecksItemsControl.ItemsSource = report.Checks;
            ScoreText.Text = $"Health Score: {report.TotalScore}%";
            ScoreText.Foreground = HexBrush(report.ScoreColor);
            SummaryText.Text = $"{report.StatusSummary} — {report.Grade}";
        }
        catch (Exception ex)
        {
            SummaryText.Text = $"Помилка аудиту: {ex.Message}";
        }
        finally
        {
            BtnRescan.IsEnabled = true;
            BtnRescan.Content = "🔍 Запустити аудит";
        }
    }

    private static SolidColorBrush HexBrush(string hex)
    {
        if (new BrushConverter().ConvertFromString(hex) is SolidColorBrush brush)
        {
            return brush;
        }
        return new SolidColorBrush(Colors.White);
    }
}
