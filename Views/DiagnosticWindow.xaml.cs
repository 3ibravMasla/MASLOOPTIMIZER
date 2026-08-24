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
        Loaded += async (s, e) => await RefreshTelemetryAsync();
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
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
            MessageBox.Show($"Помилка опитування сенсорів: {ex.Message}", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateUiWithTelemetry(DetailedHardwareInfo data)
    {
        // CPU
        TxtCpuModel.Text = data.CPUModel;
        TxtCpuCores.Text = $"{data.CPUCores} ядер / {data.CPUThreads} потоків";
        TxtCpuSocket.Text = data.CPUSocket;
        TxtCpuClock.Text = data.CPUMaxClockGHz;
        TxtCpuCache.Text = $"L3: {data.CPUL3Cache} | L2: {data.CPUL2Cache}";
        TxtCpuVirtual.Text = data.CPUVirtual;

        TxtCpuTemp.Text = $"Пакет: {data.CPUTemp}";
        TxtVrmTemp.Text = $"VRM: {data.VRMTemp}";
        TxtBoardTemp.Text = $"Плата: {data.BoardTemp}";

        // GPU
        TxtGpuModel.Text = data.GPUModel;
        TxtGpuVram.Text = $"{data.GPUVRAM} ({data.GPUVRAMUsed})";
        TxtGpuBus.Text = $"{data.GPUPCIeLink} | {data.GPUReBAR}";
        TxtGpuDriver.Text = data.GPUDriver;
        TxtGpuPowerClock.Text = $"{data.GPUClock} / {data.GPUPower}";
        TxtGpuFan.Text = data.GPUFan;

        TxtGpuCoreTemp.Text = $"GPU: {data.GPUTemp}";
        TxtGpuHotspotTemp.Text = $"Hotspot: {data.GPUHotspotTemp}";
        TxtGpuVramTemp.Text = $"VRAM: {data.GPUVramTemp}";

        if (data.Displays != null && data.Displays.Count > 0)
        {
            TxtGpuDisplays.Text = string.Join(Environment.NewLine, data.Displays.Select(d => $"• {d}"));
        }
        else
        {
            TxtGpuDisplays.Text = "Основний монітор";
        }

        // RAM
        TxtRamCapacity.Text = $"{data.RAMTotalGB} ГБ {data.RAMType} (Вільно: {data.RAMFreeGB} ГБ)";
        TxtRamLoad.Text = $"{data.RAMUsedGB} ГБ ({data.RAMLoadPercent}%)";
        TxtRamSlots.Text = $"{data.RAMSlotsUsed} з {data.RAMSlotsTotal} слотів";
        TxtRamSpeedBadge.Text = $"{data.RAMType} @ {data.RAMSpeedMHz}";

        if (data.RAMModules != null && data.RAMModules.Count > 0)
        {
            TxtRamModulesList.Text = string.Join(Environment.NewLine, data.RAMModules.Select(m => $"• {m}"));
        }
        else
        {
            TxtRamModulesList.Text = $"• Dual-Channel @ {data.RAMSpeedMHz}";
        }

        // Накопичувачі
        var sbStorage = new StringBuilder();
        foreach (var v in data.Volumes)
        {
            sbStorage.AppendLine($"📁 {v.Name}\\ [{v.Label}] — {v.FreeGB} ГБ вільно з {v.TotalGB} ГБ ({v.PercentUsed}%, {v.Format})");
        }
        foreach (var pd in data.PhysicalDisks)
        {
            sbStorage.AppendLine(pd);
        }
        TxtStorageVolumes.Text = sbStorage.ToString().TrimEnd();

        // Плата, BIOS, Мережа
        TxtBoardModel.Text = $"{data.BoardVendor} {data.BoardModel}".Trim();
        TxtBiosVersion.Text = $"{data.BIOSVersion} ({data.BIOSDate})";
        TxtNetAdapter.Text = $"{data.NetAdapterName} ({data.NetLinkSpeed})";
        TxtNetIpPing.Text = $"{data.NetIPv4} (Шлюз: {data.GatewayPing})";
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
            MessageBox.Show("Повний апаратний звіт успішно скопійовано в буфер обміну!", "MASLOOPTIMIZER", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnSaveReport_Click(object sender, RoutedEventArgs e)
    {
        if (_cachedData == null) return;

        var dlg = new SaveFileDialog
        {
            Filter = "Текстовий файл (*.txt)|*.txt",
            FileName = $"HardwareReport_{Environment.MachineName}_{DateTime.Now:yyyy-MM-dd}.txt"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                string report = DiagnosticEngine.GenerateTextReport(_cachedData);
                File.WriteAllText(dlg.FileName, report, Encoding.UTF8);
                MessageBox.Show($"Звіт успішно збережено:\n{dlg.FileName}", "MASLOOPTIMIZER", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка збереження файлу: {ex.Message}", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}