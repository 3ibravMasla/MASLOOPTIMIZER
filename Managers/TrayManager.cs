using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace MASLOOPTIMIZER;

public static class TrayManager
{
    private static NotifyIcon? _notifyIcon;
    public static WidgetWindow? Widget { get; private set; }

    #region Win32 API

    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    private const uint SHERB_NOCONFIRMATION = 0x00000001;
    private const uint SHERB_NOPROGRESSUI = 0x00000002;
    private const uint SHERB_NOSOUND = 0x00000004;

    #endregion

    public static void Initialize()
    {
        if (_notifyIcon != null) return;

        _notifyIcon = new NotifyIcon();

        try
        {
            _notifyIcon.Icon = LoadTrayIcon() ?? SystemIcons.Application;
        }
        catch
        {
            _notifyIcon.Icon = SystemIcons.Application;
        }

        _notifyIcon.Text = "MASLOOPTIMIZER";
        _notifyIcon.Visible = true;

        var contextMenu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false
        };

        var mOpenApp = new ToolStripMenuItem("⚡ Відкрити оптимізатор", null, (s, e) => ShowMainWindow());
        mOpenApp.Font = new Font("Segoe UI", 9.5f, System.Drawing.FontStyle.Bold);

        var mToggleWidget = new ToolStripMenuItem("📌 Віджет моніторингу", null, (s, e) => ToggleWidget());
        var mFlushRam = new ToolStripMenuItem("🧹 Очистити пам'ять ОЗП", null, async (s, e) => await FlushRamQuickAsync());
        var mEmptyTrash = new ToolStripMenuItem("🗑️ Очистити весь кошик", null, (s, e) => EmptyTrashQuick());
        var mHygiene = new ToolStripMenuItem("🧼 Гігієна сесії (DNS + буфер обміну)", null, OnHygieneClicked);
        var mExit = new ToolStripMenuItem("🛑 Повний вихід", null, (s, e) => FullExit());
        mExit.ForeColor = ColorTranslator.FromHtml("#F87171");

        contextMenu.Items.Add(mOpenApp);
        contextMenu.Items.Add(mToggleWidget);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(mFlushRam);
        contextMenu.Items.Add(mEmptyTrash);
        contextMenu.Items.Add(mHygiene);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(mExit);

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();
    }

    private static Icon? LoadTrayIcon()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(r => r.EndsWith("icon.maslo.ico", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(resourceName))
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null) return new Icon(stream);
            }
        }
        catch { }

        try
        {
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon", "maslo.ico");
            if (File.Exists(iconPath)) return new Icon(iconPath);
        }
        catch { }

        try
        {
            string? exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath)) return Icon.ExtractAssociatedIcon(exePath);
        }
        catch { }

        return null;
    }

    public static void ShowMainWindow()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var mainWin = Application.Current.MainWindow;
            if (mainWin != null)
            {
                mainWin.Show();
                if (mainWin.WindowState == WindowState.Minimized)
                {
                    mainWin.WindowState = WindowState.Normal;
                }
                mainWin.Activate();
            }
        });
    }

    public static void ToggleWidget()
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (Widget == null || !Widget.IsLoaded)
            {
                Widget = new WidgetWindow();
                Widget.Closed += (s, e) => Widget = null;
                Widget.Show();
            }
            else
            {
                if (Widget.IsVisible) Widget.Hide();
                else
                {
                    Widget.Show();
                    Widget.Activate();
                }
            }
        });
    }

    private static async Task FlushRamQuickAsync()
    {
        // Фаза 3.6: під час гри/Game Mode блокуюча збірка GC неприпустима (stutter) — дія відкладається.
        if (AppActivityCoordinator.IsQuietMode)
        {
            _notifyIcon?.ShowBalloonTip(1500, "MASLOOPTIMIZER", "ОЗП буде очищено після завершення гри (відкладено)", ToolTipIcon.Info);
            return;
        }

        // Фаза 6: standalone-очистка Standby RAM (спільна з віджетом) — звітуємо фактично звільнені МБ.
        long freedBytes = 0;
        try { freedBytes = await MonitorEngine.PurgeStandbyMemoryAsync(); } catch { }

        double freedMb = freedBytes / (1024.0 * 1024.0);
        _notifyIcon?.ShowBalloonTip(1500, "MASLOOPTIMIZER",
            freedMb >= 1 ? $"ОЗП очищено — звільнено ≈{freedMb:N0} МБ" : "ОЗП успішно оптимізовано та очищено!",
            ToolTipIcon.Info);
    }

    private static void EmptyTrashQuick()
    {
        try
        {
            SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
            _notifyIcon?.ShowBalloonTip(1500, "MASLOOPTIMIZER", "Кошик на всіх дисках повністю очищено!", ToolTipIcon.Info);
        }
        catch { }
    }

    /// <summary>
    /// Ручна гігієна сесії (safe defaults): на відміну від минулої поведінки,
    /// не виконується на старті/логіні — лише явно з меню трею.
    /// </summary>
    private static async void OnHygieneClicked(object? sender, EventArgs e)
    {
        try
        {
            await MonitorEngine.RunSessionHygieneAsync();
            _notifyIcon?.ShowBalloonTip(1500, "MASLOOPTIMIZER", "Гігієну сесії виконано (DNS + буфер обміну)", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Трей: помилка гігієни сесії: {ex.Message}", "WARN");
        }
    }

    public static void FullExit()
    {
        Shutdown();

        Application.Current?.Dispatcher.Invoke(() =>
        {
            Application.Current.Shutdown();
        });
    }

    /// <summary>
    /// Ідемпотентне звільнення ресурсів трею та віджета: DisposeResources віджета
    /// (таймери/лічильники/підписки) та диспозиція NotifyIcon. Викликається з FullExit і App.OnExit.
    /// </summary>
    public static void Shutdown()
    {
        try
        {
            Widget?.DisposeResources();
            Widget = null;
        }
        catch { }

        if (_notifyIcon != null)
        {
            try { _notifyIcon.Visible = false; } catch { }
            try { _notifyIcon.Dispose(); } catch { }
            _notifyIcon = null;
        }
    }

    #region Кастомний рендерер темного меню трею

    private class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected)
            {
                using var b = new SolidBrush(ColorTranslator.FromHtml("#1E293B"));
                e.Graphics.FillRectangle(b, new Rectangle(System.Drawing.Point.Empty, e.Item.Size));
            }
            else
            {
                using var b = new SolidBrush(ColorTranslator.FromHtml("#10131E"));
                e.Graphics.FillRectangle(b, new Rectangle(System.Drawing.Point.Empty, e.Item.Size));
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.ForeColor.ToArgb() == ColorTranslator.FromHtml("#F87171").ToArgb()
                ? ColorTranslator.FromHtml("#F87171")
                : ColorTranslator.FromHtml("#F8FAFC");
            base.OnRenderItemText(e);
        }
    }

    private class DarkColorTable : ProfessionalColorTable
    {
        public override System.Drawing.Color ToolStripDropDownBackground => ColorTranslator.FromHtml("#10131E");
        public override System.Drawing.Color MenuBorder => ColorTranslator.FromHtml("#334155");
        public override System.Drawing.Color MenuItemBorder => System.Drawing.Color.Transparent;
        public override System.Drawing.Color MenuItemSelected => ColorTranslator.FromHtml("#1E293B");
        public override System.Drawing.Color SeparatorDark => ColorTranslator.FromHtml("#21283B");
        public override System.Drawing.Color SeparatorLight => ColorTranslator.FromHtml("#21283B");
    }

    #endregion
}
