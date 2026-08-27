using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MASLOOPTIMIZER;

public class HealthCheckItem
{
    public string Id { get; set; } = string.Empty;
    public string Category { get; set; } = "Загальне";
    public string Title { get; set; } = string.Empty;
    public string CurrentValue { get; set; } = string.Empty;
    public string OptimalValue { get; set; } = string.Empty;
    public bool IsOptimal { get; set; }
    public int Weight { get; set; } = 10;
    public string Description { get; set; } = string.Empty;
    public Func<Task<bool>>? FixAction { get; set; }

    public string StatusIcon => IsOptimal ? "🟢" : "⚠️";
    public string StatusColor => IsOptimal ? "#00FF9D" : "#F59E0B";
}

public class SystemHealthReport
{
    public int TotalScore { get; set; }
    public int OptimalCount => Checks.Count(c => c.IsOptimal);
    public int WarningCount => Checks.Count(c => !c.IsOptimal);
    public int TotalChecks => Checks.Count;
    public List<HealthCheckItem> Checks { get; set; } = new();

    public string Grade => TotalScore switch
    {
        >= 90 => "S+ (Ultra Gaming Ready)",
        >= 75 => "A (Висока продуктивність)",
        >= 50 => "B (Задовільно)",
        _ => "C (Потребує оптимізації)"
    };

    public string ScoreColor => TotalScore switch
    {
        >= 80 => "#00FF9D",
        >= 50 => "#F59E0B",
        _ => "#EF4444"
    };

    public string StatusSummary => $"{OptimalCount} з {TotalChecks} параметрів налаштовано ідеально ({TotalScore}%).";
}

public static class HealthEngine
{
    #region Win32 PowrProf API

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerGetActiveScheme(IntPtr UserRootPowerKey, out IntPtr ActivePolicyGuid);

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern uint PowerSetActiveScheme(IntPtr UserRootPowerKey, ref Guid SchemeGuid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private static readonly HashSet<Guid> StandardHighPerfGuids = new()
    {
        new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"), // High Performance
        new Guid("e9a42b02-d5df-448d-aa00-03f14749eb61"), // Ultimate Performance
        new Guid("99ade468-e179-4669-133c-5292bd210bbf"), // AMD Ryzen High Performance
        new Guid("90b72e53-9f56-402a-953e-526487e415b7")  // Custom Bitsum / Gaming
    };

    #endregion

    public static async Task<SystemHealthReport> RunHealthAuditAsync()
    {
        return await Task.Run(() =>
        {
            var report = new SystemHealthReport();
            var list = new List<HealthCheckItem>
            {
                // 1. Апаратне планування GPU (HAGS) — 10 балів
                AuditHags(),

                // 2. Ізоляція ядер (VBS / HVCI) — 15 балів
                AuditVbs(),

                // 3. Пріоритет планувальника процесора (SystemResponsiveness) — 10 балів
                AuditSystemResponsiveness(),

                // 4. План електроживлення (Win32 Native API) — 15 балів
                AuditPowerScheme(),

                // 5. Фоновий захоплювач GameDVR / Game Bar — 10 балів
                AuditGameDvr(),

                // 6. Стан команди TRIM для SSD/NVMe — 10 балів
                AuditTrimState(),

                // 7. Мережевий стек TCP NoDelay (Nagle) — 10 балів
                AuditNagleAlgorithm(),

                // 8. Режим переривань GPU (MSI Mode) — 10 балів
                AuditGpuMsiMode(),

                // 9. Оптимізації повноекранного режиму (FSO) — 5 балів
                AuditFullscreenOptimizations(),

                // 10. Пріоритет графічного конвеєра для ігор (Tasks/Games) — 5 балів
                AuditGamesTaskPriority()
            };

            int totalWeight = list.Sum(x => x.Weight);
            int earnedWeight = list.Where(x => x.IsOptimal).Sum(x => x.Weight);
            report.TotalScore = totalWeight > 0 ? (int)Math.Round((earnedWeight / (double)totalWeight) * 100) : 0;
            report.Checks = list;

            return report;
        });
    }

    #region Аудит 10 системних параметрів

    private static HealthCheckItem AuditHags()
    {
        bool isHagsOn = false;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
            isHagsOn = Convert.ToInt32(key?.GetValue("HwSchMode") ?? 1) == 2;
        }
        catch { }

        return new HealthCheckItem
        {
            Id = "hags",
            Category = "Графіка & FPS",
            Title = "Апаратне планування GPU (HAGS)",
            CurrentValue = isHagsOn ? "Увімкнено (HwSchMode 2)" : "Вимкнено",
            OptimalValue = "Увімкнено",
            IsOptimal = isHagsOn,
            Weight = 10,
            Description = "Необхідно для генерації кадрів DLSS 3 / FSR 3 та зниження затримок рендерингу.",
            FixAction = async () => await Task.Run(() =>
            {
                try
                {
                    using var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
                    key?.SetValue("HwSchMode", 2, RegistryValueKind.DWord);
                    AppLogger.Log("HAGS активовано (потрібне перезавантаження)", "SUCCESS");
                    return true;
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"Помилка активації HAGS: {ex.Message}", "ERROR");
                    return false;
                }
            })
        };
    }

    private static HealthCheckItem AuditVbs()
    {
        bool isVbsDisabled = true;
        try
        {
            using var keyDg = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard");
            int vbs = Convert.ToInt32(keyDg?.GetValue("EnableVirtualizationBasedSecurity") ?? 0);

            using var keyHvci = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity");
            int hvci = Convert.ToInt32(keyHvci?.GetValue("Enabled") ?? 0);

            isVbsDisabled = (vbs == 0 && hvci == 0);
        }
        catch { }

        return new HealthCheckItem
        {
            Id = "vbs",
            Category = "Безпека & Процесор",
            Title = "Ізоляція ядер (VBS / HVCI)",
            CurrentValue = isVbsDisabled ? "Вимкнено (Gaming Boost)" : "Увімкнено (Втрата 5-10% FPS)",
            OptimalValue = "Вимкнено",
            IsOptimal = isVbsDisabled,
            Weight = 15,
            Description = "Вимкнення VBS знімає накладні витрати емуляції коду ядра з CPU в іграх.",
            FixAction = async () => await Task.Run(() =>
            {
                try
                {
                    using var keyDg = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard");
                    keyDg?.SetValue("EnableVirtualizationBasedSecurity", 0, RegistryValueKind.DWord);

                    using var keyHvci = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity");
                    keyHvci?.SetValue("Enabled", 0, RegistryValueKind.DWord);

                    AppLogger.Log("VBS та цілісність пам'яті вимкнено для ігор", "SUCCESS");
                    return true;
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"Помилка вимкнення VBS: {ex.Message}", "ERROR");
                    return false;
                }
            })
        };
    }

    private static HealthCheckItem AuditSystemResponsiveness()
    {
        bool isOptimal = false;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile");
            int resp = Convert.ToInt32(key?.GetValue("SystemResponsiveness") ?? 20);
            int netIndex = Convert.ToInt32(key?.GetValue("NetworkThrottlingIndex") ?? 10);

            isOptimal = (resp == 0 && (netIndex == -1 || netIndex == unchecked((int)0xFFFFFFFF)));
        }
        catch { }

        return new HealthCheckItem
        {
            Id = "sys_resp",
            Category = "Продуктивність",
            Title = "Пріоритет процесора (System Responsiveness)",
            CurrentValue = isOptimal ? "100% CPU для ігор (0)" : "Резерв фонових служб (20%)",
            OptimalValue = "100% CPU для ігор",
            IsOptimal = isOptimal,
            Weight = 10,
            Description = "Знімає системне обмеження 20% резервування процесорного часу для фонових процесів під час геймінгу.",
            FixAction = async () => await Task.Run(() =>
            {
                try
                {
                    using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile");
                    key?.SetValue("SystemResponsiveness", 0, RegistryValueKind.DWord);
                    key?.SetValue("NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord);
                    AppLogger.Log("Встановлено 100% пріоритет відгуку для ігор", "SUCCESS");
                    return true;
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"Помилка налаштування SystemProfile: {ex.Message}", "ERROR");
                    return false;
                }
            })
        };
    }

    private static HealthCheckItem AuditPowerScheme()
    {
        bool isHighPower = false;
        string planName = "Збалансована / Невідома";

        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr pGuid) == 0 && pGuid != IntPtr.Zero)
            {
                try
                {
                    var guid = Marshal.PtrToStructure<Guid>(pGuid);
                    if (StandardHighPerfGuids.Contains(guid))
                    {
                        isHighPower = true;
                        planName = "Висока / Ultimate Performance";
                    }
                    else
                    {
                        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes\{guid}");
                        string? name = key?.GetValue("FriendlyName")?.ToString() ?? "";
                        if (name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("High", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Висока", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Максимальн", StringComparison.OrdinalIgnoreCase))
                        {
                            isHighPower = true;
                            planName = "Кастомна High / Ultimate";
                        }
                    }
                }
                finally
                {
                    LocalFree(pGuid);
                }
            }
        }
        catch { }

        return new HealthCheckItem
        {
            Id = "power_plan",
            Category = "Електроживлення",
            Title = "Схема електроживлення процесора",
            CurrentValue = isHighPower ? $"{planName} (Optimal)" : "Енергозберігаюча / Balanced",
            OptimalValue = "Ultimate / High Performance",
            IsOptimal = isHighPower,
            Weight = 15,
            Description = "Запобігає паркуванню ядер CPU, затримкам перемикання P-States та падінню базової тактової частоти.",
            FixAction = async () => await Task.Run(() =>
            {
                try
                {
                    var ultimateGuid = new Guid("e9a42b02-d5df-448d-aa00-03f14749eb61");
                    if (PowerSetActiveScheme(IntPtr.Zero, ref ultimateGuid) != 0)
                    {
                        var highGuid = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
                        if (PowerSetActiveScheme(IntPtr.Zero, ref highGuid) != 0)
                        {
                            using var proc = Process.Start(new ProcessStartInfo
                            {
                                FileName = "powercfg.exe",
                                Arguments = "-duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61",
                                RedirectStandardOutput = true,
                                CreateNoWindow = true,
                                UseShellExecute = false
                            });
                            string output = proc?.StandardOutput.ReadToEnd() ?? string.Empty;
                            proc?.WaitForExit(1500);

                            var match = Regex.Match(output, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
                            if (match.Success && Guid.TryParse(match.Value, out Guid newGuid))
                            {
                                PowerSetActiveScheme(IntPtr.Zero, ref newGuid);
                            }
                            else
                            {
                                PowerSetActiveScheme(IntPtr.Zero, ref highGuid);
                            }
                        }
                    }
                    AppLogger.Log("Активовано схему максимальної продуктивності CPU", "SUCCESS");
                    return true;
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"Помилка перемикання схеми живлення: {ex.Message}", "ERROR");
                    return false;
                }
            })
        };
    }

    private static HealthCheckItem AuditGameDvr()
    {
        bool isDvrOff = false;
        try
        {
            using var userKey = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore");
            int gameDvr = Convert.ToInt32(userKey?.GetValue("GameDVR_Enabled") ?? 1);

            using var dvrKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR");
            int appCapture = Convert.ToInt32(dvrKey?.GetValue("AppCaptureEnabled") ?? 1);

            isDvrOff = (gameDvr == 0 && appCapture == 0);
        }
        catch { }

        return new HealthCheckItem
        {
            Id = "gamedvr",
            Category = "Графіка & FPS",
            Title = "Фоновий запис GameDVR (Xbox Bar)",
            CurrentValue = isDvrOff ? "Вимкнено (0% оверхеду)" : "Увімкнено (Фоновий оверхед)",
            OptimalValue = "Вимкнено",
            IsOptimal = isDvrOff,
            Weight = 10,
            Description = "Фонове захоплення екрана постійно утримує буфер у відеопам'яті та споживає ресурси енкодера GPU.",
            FixAction = async () => await Task.Run(() =>
            {
                try
                {
                    using var k1 = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore");
                    k1?.SetValue("GameDVR_Enabled", 0, RegistryValueKind.DWord);
                    k1?.SetValue("GameDVR_FSEBehaviorMode", 2, RegistryValueKind.DWord);
                    k1?.SetValue("GameDVR_HonorUserFSEBehaviorMode", 1, RegistryValueKind.DWord);
                    k1?.SetValue("GameDVR_DXGIHonorFSEWindowsCompatible", 1, RegistryValueKind.DWord);

                    using var k2 = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR");
                    k2?.SetValue("AppCaptureEnabled", 0, RegistryValueKind.DWord);

                    using var k3 = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\GameDVR");
                    k3?.SetValue("AllowGameDVR", 0, RegistryValueKind.DWord);

                    AppLogger.Log("Фоновий запис GameDVR повністю вимкнено", "SUCCESS");
                    return true;
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"Помилка вимкнення GameDVR: {ex.Message}", "ERROR");
                    return false;
                }
            })
        };
    }

    private static HealthCheckItem AuditTrimState()
    {
        bool isTrimActive = false;
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "fsutil.exe",
                Arguments = "behavior query DisableDeleteNotify",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            string output = proc?.StandardOutput.ReadToEnd() ?? string.Empty;
            proc?.WaitForExit(1500);

            isTrimActive = output.Contains("= 0") || output.Contains(" 0");
        }
        catch { isTrimActive = true; }

        return new HealthCheckItem
        {
            Id = "trim",
            Category = "Дискова підсистема",
            Title = "Підтримка TRIM (NVMe / SSD)",
            CurrentValue = isTrimActive ? "Активно (Швидкість стабільна)" : "Вимкнено (Деградація швидкості)",
            OptimalValue = "Активно",
            IsOptimal = isTrimActive,
            Weight = 10,
            Description = "TRIM своєчасно очищає неактивні блоки флеш-пам'яті SSD, запобігаючи деградації швидкості запису.",
            FixAction = async () => await Task.Run(() =>
            {
                try
                {
                    using var proc = Process.Start(new ProcessStartInfo
                    {
                        FileName = "fsutil.exe",
                        Arguments = "behavior set DisableDeleteNotify 0",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                    proc?.WaitForExit(1500);
                    AppLogger.Log("Команду TRIM успішно активовано для всіх накопичувачів", "SUCCESS");
                    return true;
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"Помилка активації TRIM: {ex.Message}", "ERROR");
                    return false;
                }
            })
        };
    }

    private static HealthCheckItem AuditNagleAlgorithm()
    {
        bool isOpt = false;
        try
        {
            var activeNics = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .Select(n => n.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            using var baseKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces");
            if (baseKey != null)
            {
                foreach (var sub in baseKey.GetSubKeyNames())
                {
                    if (activeNics.Count > 0 && !activeNics.Contains(sub)) continue;

                    using var subKey = baseKey.OpenSubKey(sub);
                    if (Convert.ToInt32(subKey?.GetValue("TCPNoDelay") ?? 0) == 1 &&
                        Convert.ToInt32(subKey?.GetValue("TcpAckFrequency") ?? 0) == 1)
                    {
                        isOpt = true;
                        break;
                    }
                }
            }
        }
        catch { }

        return new HealthCheckItem
        {
            Id = "nagle",
            Category = "Мережа & Затримки",
            Title = "Мережеві затримки (TCP NoDelay / Nagle)",
            CurrentValue = isOpt ? "Оптимізовано (TCPNoDelay 1)" : "Стандартна буферизація",
            OptimalValue = "Оптимізовано",
            IsOptimal = isOpt,
            Weight = 10,
            Description = "Вимкнення затримки Nagle забезпечує негайну відправку дрібних мережевих пакетів без накопичення буфера в іграх.",
            FixAction = async () => await NetworkEngine.OptimizeTcpLatencyAsync(true)
        };
    }

    private static HealthCheckItem AuditGpuMsiMode()
    {
        bool isGpuMsi = false;
        try
        {
            using var pciKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\PCI");
            if (pciKey != null)
            {
                foreach (var dev in pciKey.GetSubKeyNames().Where(d => d.StartsWith("VEN_", StringComparison.OrdinalIgnoreCase)))
                {
                    using var devKey = pciKey.OpenSubKey(dev);
                    if (devKey == null) continue;

                    foreach (var inst in devKey.GetSubKeyNames())
                    {
                        using var instKey = devKey.OpenSubKey(inst);
                        string? classGuid = instKey?.GetValue("ClassGUID")?.ToString();
                        string? service = instKey?.GetValue("Service")?.ToString();

                        if (string.Equals(classGuid, "{4d36e968-e325-11ce-bfc1-08002be10318}", StringComparison.OrdinalIgnoreCase) ||
                            (service != null && (service.Contains("nvlddmkm", StringComparison.OrdinalIgnoreCase) || service.Contains("amdkmdag", StringComparison.OrdinalIgnoreCase))))
                        {
                            using var msiKey = instKey?.OpenSubKey(@"Device Parameters\Interrupt Management\MessageSignaledInterruptProperties");
                            if (Convert.ToInt32(msiKey?.GetValue("MSISupported") ?? 0) == 1)
                            {
                                isGpuMsi = true;
                                break;
                            }
                        }
                    }
                    if (isGpuMsi) break;
                }
            }
        }
        catch { }

        return new HealthCheckItem
        {
            Id = "gpu_msi",
            Category = "Графіка & FPS",
            Title = "Режим переривань GPU (MSI Mode)",
            CurrentValue = isGpuMsi ? "MSI Mode (High Priority)" : "Line-Based Interrupts",
            OptimalValue = "MSI Mode",
            IsOptimal = isGpuMsi,
            Weight = 10,
            Description = "Векторний режим Message Signaled Interrupts виключає конфлікти IRQ на шині PCIe та знижує коливання Frametime.",
            FixAction = async () => await Task.Run(async () =>
            {
                await MsiEngine.ScanPciDevicesAsync();
                var gpu = MsiEngine.Devices.FirstOrDefault(d => d.Category.Contains("GPU"));
                if (gpu != null)
                {
                    return MsiEngine.SetMsiState(gpu, true, "High");
                }
                return false;
            })
        };
    }

    private static HealthCheckItem AuditFullscreenOptimizations()
    {
        bool isFsoFixed = false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore");
            int fseMode = Convert.ToInt32(key?.GetValue("GameDVR_FSEBehavior") ?? 0);
            int honorMode = Convert.ToInt32(key?.GetValue("GameDVR_HonorUserFSEBehaviorMode") ?? 0);

            isFsoFixed = (fseMode == 2 || honorMode == 1);
        }
        catch { }

        return new HealthCheckItem
        {
            Id = "fso",
            Category = "Графіка & FPS",
            Title = "Оптимізація повного екрана (Exclusive Fullscreen)",
            CurrentValue = isFsoFixed ? "Оптимізовано (Native DWM DirectFlip)" : "Стандартний гібридний DWM",
            OptimalValue = "Оптимізовано",
            IsOptimal = isFsoFixed,
            Weight = 5,
            Description = "Запобігає примусовій вертикальній синхронізації та затримці композитора DWM у повноекранних іграх.",
            FixAction = async () => await Task.Run(() =>
            {
                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore");
                    key?.SetValue("GameDVR_FSEBehavior", 2, RegistryValueKind.DWord);
                    key?.SetValue("GameDVR_HonorUserFSEBehaviorMode", 1, RegistryValueKind.DWord);
                    AppLogger.Log("Налаштування повноекранного режиму оптимізовано", "SUCCESS");
                    return true;
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"Помилка налаштування FSO: {ex.Message}", "ERROR");
                    return false;
                }
            })
        };
    }

    private static HealthCheckItem AuditGamesTaskPriority()
    {
        bool isPrioritySet = false;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games");
            int gpuPriority = Convert.ToInt32(key?.GetValue("GPU Priority") ?? 0);
            int priority = Convert.ToInt32(key?.GetValue("Priority") ?? 0);
            string schedCategory = key?.GetValue("Scheduling Category")?.ToString() ?? "";

            isPrioritySet = (gpuPriority == 8 && priority == 6 && schedCategory.Equals("High", StringComparison.OrdinalIgnoreCase));
        }
        catch { }

        return new HealthCheckItem
        {
            Id = "games_task",
            Category = "Продуктивність",
            Title = "Пріоритет графічного планувальника (Tasks/Games)",
            CurrentValue = isPrioritySet ? "High Priority (GPU: 8, CPU: 6)" : "Стандартний пріоритет",
            OptimalValue = "High Priority",
            IsOptimal = isPrioritySet,
            Weight = 5,
            Description = "Підвищує пріоритет виділення апаратних ресурсів для ігрових 3D-процесів у підсистемі Multimedia.",
            FixAction = async () => await Task.Run(() =>
            {
                try
                {
                    using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games");
                    key?.SetValue("GPU Priority", 8, RegistryValueKind.DWord);
                    key?.SetValue("Priority", 6, RegistryValueKind.DWord);
                    key?.SetValue("Scheduling Category", "High", RegistryValueKind.String);
                    key?.SetValue("SFIO Priority", "High", RegistryValueKind.String);
                    AppLogger.Log("Пріоритети Multimedia Tasks/Games успішно підвищено", "SUCCESS");
                    return true;
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"Помилка налаштування Tasks/Games: {ex.Message}", "ERROR");
                    return false;
                }
            })
        };
    }

    #endregion

    #region 1-Click Виправлення всіх виявлених проблем

    public static async Task<int> FixAllIssuesAsync()
    {
        int fixedCount = 0;
        var report = await RunHealthAuditAsync();

        foreach (var check in report.Checks.Where(c => !c.IsOptimal && c.FixAction != null))
        {
            try
            {
                bool res = await check.FixAction!();
                if (res) fixedCount++;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Помилка виправлення {check.Title}: {ex.Message}", "ERROR");
            }
        }

        AppLogger.Log($"1-Click Health Fix: успішно оптимізовано {fixedCount} параметрів", "SUCCESS");
        return fixedCount;
    }

    #endregion
}