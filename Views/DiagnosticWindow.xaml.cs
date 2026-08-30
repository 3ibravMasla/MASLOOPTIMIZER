using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

// Явні аліаси для виключення колізій із WinForms
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using Clipboard = System.Windows.Clipboard;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace MASLOOPTIMIZER;

public partial class DiagnosticWindow : Window
{
    private DetailedHardwareInfo? _cachedData;

    public DiagnosticWindow()
    {
        InitializeComponent();
        Loaded += async (s, e) =>
        {
            ApplyLocalizedLabels();
            await RefreshTelemetryAsync();
        };
        LocalizationManager.Instance.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == "Item[]")
            {
                Dispatcher.Invoke(() =>
                {
                    ApplyLocalizedLabels();
                    if (_cachedData != null) UpdateUiWithTelemetry(_cachedData);
                });
            }
        };
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    /// <summary>Локалізує статичні мітки вікна діагностики.</summary>
    private void ApplyLocalizedLabels()
    {
        var loc = LocalizationManager.Instance;

        TxtTitle.Text = loc["Diagnostic.Title"];
        TxtSubtitle.Text = loc["Diagnostic.Subtitle"];
        TxtLiveBadge.Text = loc["Diagnostic.LiveBadge"];

        LblCpuSection.Text = loc["Diagnostic.CpuSection"];
        LblModel.Text = loc["Diagnostic.LblModel"];
        LblConfig.Text = loc["Diagnostic.LblConfig"];
        LblSocket.Text = loc["Diagnostic.LblSocket"];
        LblBaseFreq.Text = loc["Diagnostic.LblBaseFreq"];
        LblMaxFreq.Text = loc["Diagnostic.LblMaxFreq"];
        LblCache.Text = loc["Diagnostic.LblCache"];
        LblVirtual.Text = loc["Diagnostic.LblVirtual"];

        LblGpuSection.Text = loc["Diagnostic.GpuSection"];
        LblGpuModel.Text = loc["Diagnostic.LblGpuModel"];
        LblGpuVram.Text = loc["Diagnostic.LblGpuVram"];
        LblGpuBus.Text = loc["Diagnostic.LblGpuBus"];
        LblGpuDriver.Text = loc["Diagnostic.LblGpuDriver"];
        LblGpuClockPower.Text = loc["Diagnostic.LblGpuClockPower"];
        LblGpuFan.Text = loc["Diagnostic.LblGpuFan"];
        LblGpuDisplays.Text = loc["Diagnostic.LblGpuDisplays"];

        LblRamSection.Text = loc["Diagnostic.RamSection"];
        LblRamCapacity.Text = loc["Diagnostic.LblRamCapacity"];
        LblRamLoad.Text = loc["Diagnostic.LblRamLoad"];
        LblRamSlots.Text = loc["Diagnostic.LblRamSlots"];
        LblRamModules.Text = loc["Diagnostic.LblRamModules"];

        LblStorageSection.Text = loc["Diagnostic.StorageSection"];

        LblBoardSection.Text = loc["Diagnostic.BoardSection"];
        LblBoardModel.Text = loc["Diagnostic.LblBoardModel"];
        LblBios.Text = loc["Diagnostic.LblBios"];
        LblNetAdapter.Text = loc["Diagnostic.LblNetAdapter"];
        LblNetIp.Text = loc["Diagnostic.LblNetIp"];
        LblSecurity.Text = loc["Diagnostic.LblSecurity"];

        BtnRefresh.Content = loc["Diagnostic.BtnRefresh"];
        BtnCopyReport.Content = loc["Diagnostic.BtnCopy"];
        BtnSaveReport.Content = loc["Diagnostic.BtnSave"];
        BtnCloseWindow.Content = loc["Diagnostic.BtnClose"];
    }

    private async Task RefreshTelemetryAsync()
    {
        try
        {
            _cachedData = await DiagnosticEngine.GetDetailedHardwareInfoAsync();
            UpdateUiWithTelemetry(_cachedData);
        }
        catch (Exception ex)
        {
            var loc = LocalizationManager.Instance;
            MessageBox.Show(loc.Format("Diagnostic.ErrorCollect", ex.Message), loc["Diagnostic.ErrorTitle"],
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateUiWithTelemetry(DetailedHardwareInfo data)
    {
        var loc = LocalizationManager.Instance;

        // CPU
        TxtCpuModel.Text = data.CPUModel;
        TxtCpuCores.Text = loc.Format("Diagnostic.CoresFormat", data.CPUCores, data.CPUThreads);
        TxtCpuSocket.Text = data.CPUSocket;
        TxtCpuBaseClock.Text = data.CPUBaseClockGHz;
        TxtCpuClock.Text = data.CPUMaxClockGHz;
        TxtCpuCache.Text = loc.Format("Diagnostic.L3L2Format", data.CPUL3Cache, data.CPUL2Cache);
        TxtCpuVirtual.Text = data.CPUVirtual;

        TxtCpuLoad.Text = loc.Format("Diagnostic.CpuLoadFormat", data.CPULoadPercent);
        TxtCpuTemp.Text = loc.Format("Diagnostic.CpuTempFormat", data.CPUTemp);
        TxtVrmTemp.Text = loc.Format("Diagnostic.VrmTempFormat", data.VRMTemp);
        TxtBoardTemp.Text = loc.Format("Diagnostic.BoardTempFormat", data.BoardTemp);

        // GPU
        if (data.Gpus != null && data.Gpus.Count > 0)
        {
            TxtGpuModel.Text = string.Join(Environment.NewLine,
                data.Gpus.Select(g => $"{g.Name} [{g.KindDisplay}] — {g.VramDisplay}"));
        }
        else
        {
            TxtGpuModel.Text = data.GPUModel;
        }

        TxtGpuVram.Text = $"{data.GPUVRAM} ({data.GPUVRAMUsed})";
        TxtGpuBus.Text = $"{data.GPUPCIeLink} | {data.GPUReBAR}";
        TxtGpuDriver.Text = data.GPUDriver;
        TxtGpuPowerClock.Text = $"{data.GPUClock} / {data.GPUPower}";
        TxtGpuFan.Text = data.GPUFan;

        TxtGpuLoad.Text = loc.Format("Diagnostic.GpuLoadFormat", data.GPULoad);
        TxtGpuCoreTemp.Text = loc.Format("Diagnostic.GpuCoreTempFormat", data.GPUTemp);
        TxtGpuHotspotTemp.Text = loc.Format("Diagnostic.GpuHotspotTempFormat", data.GPUHotspotTemp);
        TxtGpuVramTemp.Text = loc.Format("Diagnostic.GpuVramTempFormat", data.GPUVramTemp);

        if (data.Displays != null && data.Displays.Count > 0)
        {
            TxtGpuDisplays.Text = string.Join(Environment.NewLine, data.Displays.Select(d => $"• {d}"));
        }
        else
        {
            TxtGpuDisplays.Text = loc["Diagnostic.PrimaryDisplay"];
        }

        // RAM
        TxtRamCapacity.Text = loc.Format("Diagnostic.FreeRamFormat",
            data.RAMCapacityDisplay, data.RAMType, data.RAMFreeDisplay);
        TxtRamLoad.Text = loc.Format("Diagnostic.LoadFormat", $"{data.RAMUsedGB:0.#}", data.RAMLoadPercent);
        TxtRamSlots.Text = loc.Format("Diagnostic.SlotsFormat", data.RAMSlotsUsed, data.RAMSlotsTotal);
        TxtRamSpeedBadge.Text = $"{data.RAMType} @ {data.RAMSpeedMHz}";

        if (data.RAMModules != null && data.RAMModules.Count > 0)
        {
            TxtRamModulesList.Text = string.Join(Environment.NewLine, data.RAMModules.Select(m => $"• {m}"));
        }
        else
        {
            TxtRamModulesList.Text = loc["Diagnostic.ModulesEmpty"];
        }

        // Накопичувачі
        var sbStorage = new StringBuilder();
        foreach (var v in data.Volumes)
        {
            sbStorage.AppendLine(data.FormatVolume(v));
        }
        foreach (var d in data.Disks)
        {
            sbStorage.AppendLine(data.FormatPhysicalDisk(d));
        }
        TxtStorageVolumes.Text = sbStorage.ToString().TrimEnd();

        // Плата, BIOS, Мережа
        TxtBoardModel.Text = $"{data.BoardVendor} {data.BoardModel}".Trim();
        TxtBiosVersion.Text = $"{data.BIOSVersion} ({data.BIOSDate})";
        TxtNetAdapter.Text = $"{data.NetAdapterName} ({data.NetLinkSpeed})";
        TxtNetIpPing.Text = $"{data.NetIPv4} ({data.NetGateway}, {data.GatewayPing})";
        TxtSecurityStatus.Text = $"SecureBoot: {data.SecureBoot} | {data.VBSStatus}";
    }


    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshTelemetryAsync();
    }

    private void BtnCopyReport_Click(object sender, RoutedEventArgs e)
    {
        if (_cachedData != null)
        {
            string report = DiagnosticEngine.GenerateTextReport(_cachedData);
            Clipboard.SetText(report);
            var loc = LocalizationManager.Instance;
            MessageBox.Show(loc["Diagnostic.ReportCopied"], "MASLOOPTIMIZER", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnSaveReport_Click(object sender, RoutedEventArgs e)
    {
        if (_cachedData == null) return;

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
                string report = DiagnosticEngine.GenerateTextReport(_cachedData);
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

