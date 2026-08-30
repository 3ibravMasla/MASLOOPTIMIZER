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
    private static ToolStripMenuItem? _gameModeItem;
    public static WidgetWindow? Widget { get; private set; }

    #region Win32 API

    [DllImport("psapi.dll")]
    private static extern int EmptyWorkingSet(IntPtr hwProc);

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
        var mGameMode = new ToolStripMenuItem("🎮 Game Mode: Вимкнено", null, OnGameModeTrayClicked);
        _gameModeItem = mGameMode;
        var mFlushRam = new ToolStripMenuItem("🧹 Очистити пам'ять ОЗП", null, async (s, e) => await FlushRamQuickAsync());
        var mEmptyTrash = new ToolStripMenuItem("🗑️ Очистити весь кошик", null, (s, e) => EmptyTrashQuick());
        var mExit = new ToolStripMenuItem("🛑 Повний вихід", null, (s, e) => FullExit());
        mExit.ForeColor = ColorTranslator.FromHtml("#F87171");

        contextMenu.Items.Add(mOpenApp);
        contextMenu.Items.Add(mToggleWidget);
        contextMenu.Items.Add(mGameMode);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(mFlushRam);
        contextMenu.Items.Add(mEmptyTrash);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(mExit);

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();

        // Синхронізація пункту трею з реальним станом Game Mode (подія спрацьовує з фонового потоку).
        GameModeEngine.OnGameModeStateChanged += OnGameModeStateChanged;
        UpdateGameModeTrayItem(GameModeEngine.IsGameModeActive);
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
        await Task.Run(() =>
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                EmptyWorkingSet(Process.GetCurrentProcess().Handle);

                foreach (var proc in Process.GetProcesses())
                {
                    try
                    {
                        if (!proc.HasExited && proc.Id > 4 && !proc.ProcessName.Equals("System", StringComparison.OrdinalIgnoreCase))
                        {
                            EmptyWorkingSet(proc.Handle);
                        }
                    }
                    catch { }
                    finally
                    {
                        proc.Dispose(); // Звільнення нативних системних дескрипторів
                    }
                }
            }
            catch { }
        });

        _notifyIcon?.ShowBalloonTip(1500, "MASLOOPTIMIZER", "ОЗП успішно оптимізовано та очищено!", ToolTipIcon.Info);
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

    public static void FullExit()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        Application.Current?.Dispatcher.Invoke(() =>
        {
            Application.Current.Shutdown();
        });
    }

    private static async void OnGameModeTrayClicked(object? sender, EventArgs e)
    {
        if (_gameModeItem != null)
            _gameModeItem.Text = "⏳ Game Mode...";

        try
        {
            await GameModeEngine.ToggleGameModeAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Помилка перемикання Game Mode з трею: {ex.Message}", "ERROR");
        }
        finally
        {
            UpdateGameModeTrayItem(GameModeEngine.IsGameModeActive);
        }
    }

    private static void OnGameModeStateChanged(bool isActive)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
            return;

        if (!dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => OnGameModeStateChanged(isActive));
            return;
        }

        UpdateGameModeTrayItem(isActive);
    }

    private static void UpdateGameModeTrayItem(bool isActive)
    {
        if (_gameModeItem != null)
            _gameModeItem.Text = isActive ? "🎮 Game Mode: Увімкнено" : "🎮 Game Mode: Вимкнено";
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