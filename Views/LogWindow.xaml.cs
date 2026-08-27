using System;
using System.Diagnostics;
using System.Windows;

namespace MASLOOPTIMIZER;

public partial class LogWindow : Window
{
    public LogWindow()
    {
        InitializeComponent();
        LogsListView.ItemsSource = AppLogger.LogEntries;
    }

    private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AppPaths.EnsureDirectories();
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{AppPaths.Logs}\"",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.ClearHistory();
    }
}