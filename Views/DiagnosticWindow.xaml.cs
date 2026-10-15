using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

// Явні аліаси для виключення колізій із WinForms
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using Clipboard = System.Windows.Clipboard;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace MASLOOPTIMIZER;

public partial class DiagnosticWindow : Window
{
    private StaticHardwareSpecs? _static;
    private LiveTelemetrySnapshot _live = new();
    private DispatcherTimer? _timer;
    private bool _polling;
    private readonly PropertyChangedEventHandler _localizationHandler;

    private const double PollIntervalSeconds = 1.5;

    public DiagnosticWindow()
    {
        InitializeComponent();

        _localizationHandler = (s, e) =>
        {
            if (e.PropertyName == "Item[]")
            {
                Dispatcher.Invoke(() =>
                {
                    ApplyLocalizedLabels();
                    if (_static != null) RenderStatic(_static);
                    RenderLive(_live);
                });
            }
        };
        LocalizationManager.Instance.PropertyChanged += _localizationHandler;

        Loaded += DiagnosticWindow_Loaded;
        Closed += DiagnosticWindow_Closed;
    }

    private async void DiagnosticWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyLocalizedLabels();

        // 1. Відкриваємо постійну LHM-сесію (Ring0-драйвер один раз на час життя вікна).
        DiagnosticEngine.StartLiveSession();

        // 2. Статичний аудіт + перше живе опитування.
        await RefreshAsync();

        // 3. Живий цикл 1.5 с.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(PollIntervalSeconds) };
        _timer.Tick += async (_, _) => await PollLiveAsync();
        _timer.Start();
    }

    private void DiagnosticWindow_Closed(object? sender, EventArgs e)
    {
        _timer?.Stop();
        _timer = null;
        LocalizationManager.Instance.PropertyChanged -= _localizationHandler;
        DiagnosticEngine.StopLiveSession();
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    private void ApplyLocalizedLabels()
    {
        var loc = LocalizationManager.Instance;

        TxtTitle.Text = loc["Diagnostic.Title"];
        TxtSubtitle.Text = loc["Diagnostic.Subtitle"];
        TxtLiveBadge.Text = loc["Diagnostic.LiveBadge"];

        // ОС та безпека
        LblOsSection.Text = loc["Diagnostic.OsSection"];
        LblOsVersion.Text = loc["Diagnostic.LblOsVersion"];
        LblOsUptime.Text = loc["Diagnostic.LblUptime"];
        LblDirectX.Text = loc["Diagnostic.LblDirectX"];
        LblPowerPlan.Text = loc["Diagnostic.LblPowerPlan"];
        LblVbs.Text = loc["Diagnostic.LblVbs"];
        LblSecureBoot.Text = loc["Diagnostic.LblSecureBoot"];
        LblTpm.Text = loc["Diagnostic.LblTpm"];

        // Процесор
        LblCpuSection.Text = loc["Diagnostic.CpuSection"];
        LblModel.Text = loc["Diagnostic.LblModel"];
        LblConfig.Text = loc["Diagnostic.LblConfig"];
        LblSocket.Text = loc["Diagnostic.LblSocket"];
        LblCpuArch.Text = loc["Diagnostic.LblCpuArch"];
        LblBaseFreq.Text = loc["Diagnostic.LblBaseFreq"];
        LblBoostFreq.Text = loc["Diagnostic.LblBoostFreq"];
        LblCache.Text = loc["Diagnostic.LblCache"];
        LblVirtual.Text = loc["Diagnostic.LblVirtual"];

        // Відеокарта
        LblGpuSection.Text = loc["Diagnostic.GpuSection"];
        LblGpuModel.Text = loc["Diagnostic.LblGpuModel"];
        LblGpuVram.Text = loc["Diagnostic.LblGpuVram"];
        LblGpuDriver.Text = loc["Diagnostic.LblGpuDriver"];
        LblGpuBus.Text = loc["Diagnostic.LblGpuBus"];
        LblGpuDisplays.Text = loc["Diagnostic.LblGpuDisplays"];

        // Пам'ять
        LblRamSection.Text = loc["Diagnostic.RamSection"];
        LblRamCapacity.Text = loc["Diagnostic.LblRamCapacity"];
        LblRamType.Text = loc["Diagnostic.LblRamType"];
        LblRamSlots.Text = loc["Diagnostic.LblRamSlots"];
        LblRamUsed.Text = loc["Diagnostic.LblRamUsed"];
        LblRamAvailable.Text = loc["Diagnostic.LblRamAvailable"];
        LblRamUsage.Text = loc["Diagnostic.LblRamUsage"];
        LblRamCommit.Text = loc["Diagnostic.LblRamCommit"];
        LblDimmSlots.Text = loc["Diagnostic.LblDimmSlots"];

        // Накопичувачі / плата / мережа
        LblStorageSection.Text = loc["Diagnostic.StorageSection"];
        LblStorageDisks.Text = loc["Diagnostic.LblStorageDisks"];
        LblVolumes.Text = loc["Diagnostic.LblVolumes"];
        LblBoardSection.Text = loc["Diagnostic.BoardSection"];
        LblBoardModel.Text = loc["Diagnostic.LblBoardModel"];
        LblBoardSensors.Text = loc["Diagnostic.LblBoardSensors"];
        LblBoardFans.Text = loc["Diagnostic.LblBoardFans"];
        LblBios.Text = loc["Diagnostic.LblBios"];
        LblNetAdapter.Text = loc["Diagnostic.LblNetAdapter"];
        LblNetIp.Text = loc["Diagnostic.LblNetIp"];

        BtnRefresh.Content = loc["Diagnostic.BtnRefresh"];
        BtnCopyReport.Content = loc["Diagnostic.BtnCopy"];
        BtnSaveReport.Content = loc["Diagnostic.BtnSave"];
        BtnCloseWindow.Content = loc["Diagnostic.BtnClose"];
    }

    private async Task RefreshAsync()
    {
        try
        {
            _static = await DiagnosticEngine.GetStaticHardwareSpecsAsync();
            RenderStatic(_static);
            await PollLiveAsync();
        }
        catch (Exception ex)
        {
            var loc = LocalizationManager.Instance;
            MessageBox.Show(loc.Format("Diagnostic.ErrorCollect", ex.Message), loc["Diagnostic.ErrorTitle"],
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task PollLiveAsync()
    {
        if (_polling) return;
        _polling = true;
        try
        {
            _live = await DiagnosticEngine.PollLiveSensorsAsync();
            RenderLive(_live);
        }
        catch { }
        finally { _polling = false; }
    }

    private void RenderStatic(StaticHardwareSpecs s)
    {
        var loc = LocalizationManager.Instance;

        // ОС
        TxtOsVersion.Text = s.OsTitleDisplay;
        TxtOsUptime.Text = s.Uptime;
        TxtDirectX.Text = s.DirectXVersion;
        TxtPowerPlan.Text = s.PowerPlan;
        TxtVbs.Text = s.VBS;
        TxtSecureBoot.Text = s.SecureBoot;
        TxtTpm.Text = s.TPM;

        // CPU
        TxtCpuModel.Text = s.CPUModel;
        TxtCpuCores.Text = s.CpuCoresDisplay;
        TxtCpuSocket.Text = s.CPUSocket;
        TxtCpuArch.Text = s.CPUArchitecture;
        TxtCpuBaseClock.Text = FmtMhz(s.CPUBaseClockMHz);
        TxtCpuBoostClock.Text = FmtMhz(s.CPUMaxClockMHz);
        TxtCpuCache.Text = s.CpuCacheDisplay;
        TxtCpuVirtual.Text = s.CPUVirtual;

        // GPU
        if (s.Gpus.Count > 0)
        {
            TxtGpuModel.Text = string.Join(Environment.NewLine,
                s.Gpus.Select(g => $"{g.Name} [{g.KindDisplay}]"));
        }
        else
        {
            TxtGpuModel.Text = "—";
        }

        var primary = s.PrimaryGpu;
        if (primary != null)
        {
            TxtGpuVram.Text = primary.VramDisplay;
            TxtGpuDriver.Text = $"{primary.DriverVersion} ({primary.DriverDate})";
            TxtGpuBus.Text = $"{primary.BusInterface} | ReBAR: {primary.ReBar}";
        }
        else
        {
            TxtGpuVram.Text = "—";
            TxtGpuDriver.Text = "—";
            TxtGpuBus.Text = "—";
        }

        TxtGpuDisplays.Text = s.Displays.Count > 0
            ? string.Join(Environment.NewLine, s.Displays.Select(d => $"• {d}"))
            : "—";

        // RAM
        TxtRamCapacity.Text = $"{s.RAMTotalGB:N1} {loc["Common.UnitGB"]}";
        TxtRamType.Text = string.IsNullOrWhiteSpace(s.RAMFormFactor) || s.RAMFormFactor == "—"
            ? s.RAMType
            : $"{s.RAMType} ({s.RAMFormFactor})";
        TxtRamSlots.Text = $"{s.RAMSlotsUsed} / {s.RAMSlotsTotal}";
        TxtRamSpeedBadge.Text = s.RamSpeedDisplay;

        TxtDimmList.Text = s.Dimms.Count > 0
            ? string.Join(Environment.NewLine, s.Dimms.Select(d => $"• {FormatDimm(d)}"))
            : loc["Diagnostic.ModulesEmpty"];

        // Накопичувачі
        TxtStorageList.Text = s.Disks.Count > 0
            ? string.Join(Environment.NewLine, s.Disks.Select(FormatDisk))
            : loc["Diagnostic.DisksEmpty"];

        TxtVolumeList.Text = s.Volumes.Count > 0
            ? string.Join(Environment.NewLine, s.Volumes.Select(v =>
                loc.Format("Diagnostic.VolumeFormat", v.Name, string.IsNullOrWhiteSpace(v.Label) ? loc["Diagnostic.LocalDisk"] : v.Label,
                    v.FreeGB.ToString("N1"), v.TotalGB.ToString("N1"), v.PercentUsed, v.Format)))
            : "—";

        // Плата / BIOS / Мережа
        TxtBoardModel.Text = $"{s.BoardVendor} {s.BoardModel}".Replace("  ", " ").Trim();
        TxtBiosVersion.Text = $"{s.BIOSVendor} {s.BIOSVersion} ({s.BIOSDate})".Replace("  ", " ").Trim();
        TxtNetAdapter.Text = $"{s.NetAdapterName} ({s.NetLinkSpeed})";
        TxtNetIp.Text = $"{s.NetIPv4} / {s.NetMac}";
    }

    private void RenderLive(LiveTelemetrySnapshot live)
    {
        var loc = LocalizationManager.Instance;

        // CPU
        TxtCpuLoad.Text = $"{loc["Diagnostic.BadgeLoad"]} {live.CpuLoadDisplay}";
        TxtCpuTemp.Text = $"{loc["Diagnostic.LblTemp"]} {live.CpuPackageTempDisplay}";
        TxtCpuPower.Text = $"{loc["Diagnostic.LblPower"]} {live.CpuPowerDisplay}";
        TxtCpuVcore.Text = $"{loc["Diagnostic.LblVcore"]} {live.CpuVcoreDisplay}";
        TxtCpuClock.Text = $"{loc["Diagnostic.LblClock"]} {live.CpuClockDisplay}";

        // GPU
        TxtGpuLoad.Text = $"{loc["Diagnostic.BadgeLoad"]} {live.GpuLoadDisplay}";
        TxtGpuCoreTemp.Text = $"{loc["Diagnostic.LblGpuTemp"]} {live.GpuCoreTempDisplay}";
        TxtGpuHotspotTemp.Text = $"{loc["Diagnostic.LblHotspot"]} {live.GpuHotspotTempDisplay}";
        TxtGpuVramTemp.Text = $"{loc["Diagnostic.LblVramTemp"]} {live.GpuVramTempDisplay}";
        TxtGpuPower.Text = $"{loc["Diagnostic.LblPower"]} {live.GpuPowerDisplay}";
        TxtGpuCoreClock.Text = $"{loc["Diagnostic.LblCoreClock"]} {live.GpuCoreClockDisplay}";
        TxtGpuMemClock.Text = $"{loc["Diagnostic.LblMemClock"]} {live.GpuMemClockDisplay}";
        TxtGpuFan.Text = $"{loc["Diagnostic.LblFan"]} {live.GpuFanDisplay}";
        TxtGpuVramUsed.Text = $"{loc["Diagnostic.LblVram"]} {live.GpuVramUsedDisplay}";

        // Плата / кулери
        TxtBoardSensors.Text = $"VRM {live.VrmTempDisplay} | {loc["Diagnostic.LblTemp"]} {live.SystemTempDisplay}";
        TxtBoardFans.Text = $"CPU {live.CpuFanDisplay} | Sys {live.SystemFanDisplay}";

        // RAM live
        TxtRamUsed.Text = live.RamUsedDisplay;
        TxtRamAvailable.Text = live.RamAvailableDisplay;
        TxtRamUsage.Text = live.RamUsageDisplay;
        TxtRamCommit.Text = live.CommitDisplay;
    }

    private static string FmtMhz(double mhz) => mhz > 0 ? $"{mhz:0} MHz" : "—";

    private static string FormatDimm(DimmSlotInfo d)
    {
        var loc = LocalizationManager.Instance;
        var sb = new StringBuilder();
        sb.Append($"{d.Slot}: {d.CapacityGB:0} {loc["Common.UnitGB"]}");
        if (d.MemoryType.Length > 0) sb.Append($" {d.MemoryType}");
        if (d.SpeedMTs > 0) sb.Append($" @ {d.SpeedMTs:0} {loc["Diagnostic.MtsUnit"]}");
        if (d.Manufacturer.Length > 0) sb.Append($" — {d.Manufacturer}");
        if (d.PartNumber.Length > 0) sb.Append($" ({d.PartNumber})");
        return sb.ToString();
    }

    private static string FormatDisk(PhysicalDiskInfo d)
    {
        var loc = LocalizationManager.Instance;
        return $"{d.Model} — {d.SizeGB:0} {loc["Common.UnitGB"]} ({d.TypeLabel}) | " +
               $"{d.TempDisplay} | S.M.A.R.T.: {d.SmartLabel} | Life: {d.LifeDisplay}";
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private void BtnCopyReport_Click(object sender, RoutedEventArgs e)
    {
        if (_static == null) return;
        string report = DiagnosticEngine.GenerateTextReport(_static, _live);
        Clipboard.SetText(report);
        var loc = LocalizationManager.Instance;
        MessageBox.Show(loc["Diagnostic.ReportCopied"], "MASLOOPTIMIZER", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnSaveReport_Click(object sender, RoutedEventArgs e)
    {
        if (_static == null) return;

        var loc = LocalizationManager.Instance;
        var dlg = new SaveFileDialog
        {
            Filter = "Text (*.txt)|*.txt",
            FileName = $"HardwareReport_{Environment.MachineName}_{DateTime.Now:yyyy-MM-dd}.txt"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                string report = DiagnosticEngine.GenerateTextReport(_static, _live);
                File.WriteAllText(dlg.FileName, report, Encoding.UTF8);
                MessageBox.Show(loc.Format("Diagnostic.ReportSaved", dlg.FileName), "MASLOOPTIMIZER",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(loc.Format("Diagnostic.SaveError", ex.Message), loc["Diagnostic.ErrorTitle"],
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
