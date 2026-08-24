using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;

namespace MASLOOPTIMIZER;

public partial class LogWindow : Window
{
    public LogWindow()
    {
        InitializeComponent();
        LogsItemsControl.ItemsSource = AppLogger.SessionLogs;

        // Автоскрол до останнього рядка при додаванні нового логу
        if (AppLogger.SessionLogs is INotifyCollectionChanged notifyCollection)
        {
            notifyCollection.CollectionChanged += (s, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Add)
                {
                    Dispatcher.InvokeAsync(() => LogsScrollViewer.ScrollToEnd());
                }
            };
        }

        // Початкова прокрутка вниз при відкритті вікна
        Loaded += (s, e) => LogsScrollViewer.ScrollToEnd();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}