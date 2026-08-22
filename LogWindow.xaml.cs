using System.Windows;

namespace MASLOOPTIMIZER;

public partial class LogWindow : Window
{
    public LogWindow()
    {
        InitializeComponent();
        LogsItemsControl.ItemsSource = AppLogger.SessionLogs;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}