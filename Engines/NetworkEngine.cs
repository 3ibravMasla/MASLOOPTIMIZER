using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MASLOOPTIMIZER;

public class NetworkTuningStatus
{
    public bool IsNagleDisabled { get; set; }
    public bool IsAutotuningNormal { get; set; }
    public bool IsEeeDisabled { get; set; }
    public bool IsQosUnlocked { get; set; }
    public bool IsDnsCacheOptimized { get; set; }
    public bool IsNicBuffersOptimized { get; set; }
    public bool IsLsoDisabled { get; set; }
    public bool IsNetworkThrottlingDisabled { get; set; }
    public int ActiveAdaptersCount { get; set; }
    public List<string> ActiveAdaptersNames { get; set; } = new();

    public bool IsFullyOptimized => IsNagleDisabled && IsAutotuningNormal && IsEeeDisabled &&
        IsQosUnlocked && IsDnsCacheOptimized && IsNicBuffersOptimized && IsLsoDisabled && IsNetworkThrottlingDisabled;
}

/// <summary>Результат заміру затримки/джиттера мережі до хосту (реальні вимірювання, не хардкод).</summary>
public class NetworkLatencyResult
{
    public string Host { get; set; } = string.Empty;
    public bool IsReachable { get; set; }
    public int SamplesSent { get; set; }
    public int SamplesReceived { get; set; }
    public double LossPercent { get; set; }
    public long MinMs { get; set; }
    public long MaxMs { get; set; }
    public double AvgMs { get; set; }
    public double JitterMs { get; set; }

    public string Summary => IsReachable
        ? $"Min {MinMs} ms | Avg {AvgMs:F1} ms | Max {MaxMs} ms | Jitter {JitterMs:F1} ms | Loss {LossPercent:F1}%"
        : "Host unavailable";
}

public static class NetworkEngine
{
    // Снапшот оригінальних значень мережевих адаптерів для коректного відновлення
    // (повертаємо саме ті значення, що були до перезапису, а не хардкод-«дефолти»).
    private static readonly object _nicSnapshotLock = new();
    private static readonly Dictionary<string, Dictionary<string, NicRegSnapshot>> _nicSnapshots =
        new(StringComparer.OrdinalIgnoreCase);

    private sealed class NicRegSnapshot
    {
        public RegistryValueKind Kind = RegistryValueKind.Unknown;
        public object? Value; // null => значення не існувало до оптимізації
    }

    #region 1. Оптимізація TCP/IP стека та затримок Nagle

    public static async Task<bool> OptimizeTcpLatencyAsync(bool isApply)
    {
        return await Task.Run(() =>
        {
            try
            {
                var targetNics = GetPhysicalActiveAdapters();
                var activeGuids = targetNics.Select(n => n.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

                // 1. Тюнінг алгоритму Nagle та частоти ACK ЛИШЕ для активних фізичних інтерфейсів.
                // NetworkInterface.Id і підключі реєстру `...\Interfaces` — це той самий GUID у
                // фігурних дужках ({GUID}), тож порівнюємо напряму без урахування регістру.
                string baseInterfacesPath = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
                if (activeGuids.Count > 0)
                {
                    using (var interfacesKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(baseInterfacesPath, true))
                    {
                        if (interfacesKey != null)
                        {
                            foreach (var sub in interfacesKey.GetSubKeyNames())
                            {
                                if (!activeGuids.Contains(sub))
                                    continue;

                                using var subKey = interfacesKey.OpenSubKey(sub, true);
                                if (subKey == null)
                                    continue;

                                if (isApply)
                                {
                                    subKey.SetValue("TcpAckFrequency", 1, RegistryValueKind.DWord);
                                    subKey.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord);
                                    subKey.SetValue("TcpDelAckTicks", 0, RegistryValueKind.DWord);
                                    subKey.SetValue("TcpInitialRTT", 300, RegistryValueKind.DWord);
                                }
                                else
                                {
                                    subKey.DeleteValue("TcpAckFrequency", false);
                                    subKey.DeleteValue("TCPNoDelay", false);
                                    subKey.DeleteValue("TcpDelAckTicks", false);
                                    subKey.DeleteValue("TcpInitialRTT", false);
                                }
                            }
                        }
                    }
                }

                // 2. Глобальні системні параметри сокетів та продуктивності TCP/IP
                using (var tcpParams = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).CreateSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters"))
                {
                    if (tcpParams != null)
                    {
                        if (isApply)
                        {
                            tcpParams.SetValue("DefaultTTL", 64, RegistryValueKind.DWord);
                            tcpParams.SetValue("EnableTCPA", 1, RegistryValueKind.DWord);
                            tcpParams.SetValue("EnableDCA", 1, RegistryValueKind.DWord);
                            tcpParams.SetValue("EnableWsd", 0, RegistryValueKind.DWord);
                            tcpParams.SetValue("MaxUserPort", 65534, RegistryValueKind.DWord);
                            tcpParams.SetValue("TcpTimedWaitDelay", 30, RegistryValueKind.DWord);
                            tcpParams.SetValue("SynAttackProtect", 1, RegistryValueKind.DWord);
                            tcpParams.SetValue("EnableICMPRedirect", 0, RegistryValueKind.DWord);
                            tcpParams.SetValue("Tcp1323Opts", 1, RegistryValueKind.DWord); // Window Scaling ON, Timestamps OFF
                        }
                        else
                        {
                            tcpParams.DeleteValue("DefaultTTL", false);
                            tcpParams.DeleteValue("EnableTCPA", false);
                            tcpParams.DeleteValue("EnableDCA", false);
                            tcpParams.DeleteValue("EnableWsd", false);
                            tcpParams.DeleteValue("MaxUserPort", false);
                            tcpParams.DeleteValue("TcpTimedWaitDelay", false);
                            tcpParams.DeleteValue("SynAttackProtect", false);
                            tcpParams.DeleteValue("EnableICMPRedirect", false);
                            tcpParams.DeleteValue("Tcp1323Opts", false);
                        }
                    }
                }

                // 3. Зняття мережевого троттлінгу у LanmanWorkstation
                using (var lanmanKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).CreateSubKey(@"SYSTEM\CurrentControlSet\Services\LanmanWorkstation\Parameters"))
                {
                    if (lanmanKey != null)
                    {
                        if (isApply)
                        {
                            lanmanKey.SetValue("DisableBandwidthThrottling", 1, RegistryValueKind.DWord);
                            lanmanKey.SetValue("DisableLargeMtu", 0, RegistryValueKind.DWord);
                        }
                        else
                        {
                            lanmanKey.DeleteValue("DisableBandwidthThrottling", false);
                            lanmanKey.DeleteValue("DisableLargeMtu", false);
                        }
                    }
                }

                // 3.1. Вимкнення мережевого троттлінгу планувальника MMCSS.
                // За замовчуванням Windows може обмежувати пропускну здатність для «не-медійної»
                // активності; 0xFFFFFFFF знімає це обмеження — критично для онлайн-ігор та стримінгу.
                using (var mmcssKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"))
                {
                    if (mmcssKey != null)
                    {
                        if (isApply)
                        {
                            mmcssKey.SetValue("NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord);
                        }
                        else
                        {
                            mmcssKey.DeleteValue("NetworkThrottlingIndex", false);
                        }
                    }
                }

                // 4. Системне налаштування стека через netsh
                if (isApply)
                {
                    RunSystemTool("netsh.exe", "int tcp set global autotuninglevel=normal");
                    RunSystemTool("netsh.exe", "int tcp set global ecncapability=disabled");
                    RunSystemTool("netsh.exe", "int tcp set global timestamps=disabled");
                    RunSystemTool("netsh.exe", "int tcp set global rss=enabled");
                    RunSystemTool("netsh.exe", "int tcp set global rsc=disabled"); // Вимикає коалесценцію пакетів для зниження джиттеру
                    RunSystemTool("netsh.exe", "int tcp set global nonsackrttresiliency=disabled");
                    RunSystemTool("netsh.exe", "int tcp set global maxsynretransmissions=2");
                    RunSystemTool("netsh.exe", "int tcp set global fastopen=enabled");
                    RunSystemTool("netsh.exe", "int tcp set global fastopenfallback=enabled");

                    // Активація оптимізованого алгоритму керування перевантаженням
                    RunSystemTool("netsh.exe", "int tcp set supplemental template=internet congestionprovider=ctcp");
                    RunSystemTool("netsh.exe", "int tcp set global congestionprovider=ctcp");
                }
                else
                {
                    RunSystemTool("netsh.exe", "int tcp set global autotuninglevel=normal");
                    RunSystemTool("netsh.exe", "int tcp set global congestionprovider=default");
                    RunSystemTool("netsh.exe", "int tcp set supplemental template=internet congestionprovider=default");
                    RunSystemTool("netsh.exe", "int tcp set global timestamps=allowed");
                    RunSystemTool("netsh.exe", "int tcp set global rsc=enabled");
                    RunSystemTool("netsh.exe", "int tcp set global ecncapability=default");
                    RunSystemTool("netsh.exe", "int tcp set global fastopen=default");
                }

                FlushDnsAndRouteCache();

                AppLogger.Log(isApply
                    ? "Мережевий стек оптимізовано: алгоритм Nagle вимкнено, TCP NoDelay та CTCP активовано"
                    : "Параметри TCP/IP повернуто до стандартних значень Windows", "SUCCESS");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Помилка оптимізації TCP: {ex.Message}", "ERROR");
                return false;
            }
        });
    }

    #endregion

    #region 2. Оптимізація енергозбереження, затримок та LSO адаптера (NIC)

    public static async Task<bool> OptimizeNicPowerSavingAsync(bool isApply = true)
    {
        return await Task.Run(() =>
        {
            try
            {
                // Відновлення виконується виключно зі снапшоту оригіналів (не хардкод-значень)
                if (!isApply)
                {
                    return RestoreNicPowerSaving();
                }

                string keyPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(keyPath, true);
                if (baseKey == null) return false;

                int modifiedAdapters = 0;

                // Карта активних адаптерів: GUID (без дужок) -> NetworkInterface.
                // Тюнінг застосовується ЛИШЕ до активних фізичних адаптерів, щоб не чіпати
                // вимкнені/віртуальні пристрої та не ламати Wi-Fi значеннями для Ethernet.
                var activeById = GetPhysicalActiveAdapters()
                    .ToDictionary(n => NormalizeGuid(n.Id), StringComparer.OrdinalIgnoreCase);

                foreach (var sub in baseKey.GetSubKeyNames().Where(s => s.Length == 4 && s.All(char.IsDigit)))
                {
                    using var subKey = baseKey.OpenSubKey(sub, true);
                    if (subKey == null) continue;

                    string? driverDesc = subKey.GetValue("DriverDesc")?.ToString();
                    if (string.IsNullOrWhiteSpace(driverDesc) ||
                        driverDesc.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                        driverDesc.Contains("Miniport", StringComparison.OrdinalIgnoreCase) ||
                        driverDesc.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Зіставляємо пристрій реєстру з активним адаптером через NetCfgInstanceId.
                    string cfgId = NormalizeGuid(subKey.GetValue("NetCfgInstanceId")?.ToString());
                    if (cfgId.Length == 0 || !activeById.TryGetValue(cfgId, out var nic))
                        continue;

                    bool isEthernet = IsEthernetType(nic.NetworkInterfaceType);

                    // Набір параметрів залежить від типу: EEE/LSO/буфери/модерація переривань — лише Ethernet.
                    // Для Wi-Fi вимикаємо лише переведення в сон і відключення пристрою для економії.
                    var touchedNames = new List<string> { "*SelectiveSuspend", "PnPCapabilities" };
                    if (isEthernet)
                    {
                        touchedNames.AddRange(new[]
                        {
                            "*EEE", "EEELinkAdvertisement", "GreenEthernet", "GigaLite", "PowerSavingMode",
                            "EnergyEfficientEthernet", "EnablePME", "AutoPowerSaveModeEnabled", "SavePowerNowEnabled",
                            "*FlowControl", "*InterruptModeration", "InterruptModeration",
                            "*LsoV2IPv4", "*LsoV2IPv6", "*ReceiveBuffers", "*TransmitBuffers", "ReceiveBuffers", "TransmitBuffers"
                        });
                    }

                    // Снапшот оригіналів ПЕРЕД першим перезаписом (не перезаписуємо, якщо вже є).
                    lock (_nicSnapshotLock)
                    {
                        if (!_nicSnapshots.ContainsKey(subKey.Name))
                        {
                            var snapshot = new Dictionary<string, NicRegSnapshot>(StringComparer.OrdinalIgnoreCase);
                            foreach (var vn in touchedNames)
                            {
                                object? existing = null;
                                RegistryValueKind kind = RegistryValueKind.Unknown;
                                try
                                {
                                    existing = subKey.GetValue(vn);
                                    if (existing != null) kind = subKey.GetValueKind(vn);
                                }
                                catch { existing = null; }

                                snapshot[vn] = new NicRegSnapshot { Kind = kind, Value = existing };
                            }
                            _nicSnapshots[subKey.Name] = snapshot;
                        }
                    }

                    // 1. Заборона переведення пристрою в сон (для Wi-Fi критично для стабільного пінгу)
                    SetOrForceRegValue(subKey, "*SelectiveSuspend", "0");

                    if (isEthernet)
                    {
                        // 2. Повне вимкнення енергозберігаючих станів
                        SetOrForceRegValue(subKey, "*EEE", "0");
                        SetOrForceRegValue(subKey, "EEELinkAdvertisement", "0");
                        SetOrForceRegValue(subKey, "GreenEthernet", "0");
                        SetOrForceRegValue(subKey, "GigaLite", "0");
                        SetOrForceRegValue(subKey, "PowerSavingMode", "0");
                        SetOrForceRegValue(subKey, "EnergyEfficientEthernet", "0");
                        SetOrForceRegValue(subKey, "EnablePME", "0");
                        SetOrForceRegValue(subKey, "AutoPowerSaveModeEnabled", "0");
                        SetOrForceRegValue(subKey, "SavePowerNowEnabled", "0");

                        // 3. Зниження апаратного Input Lag та DPC-затримок
                        SetOrForceRegValue(subKey, "*FlowControl", "0");
                        SetOrForceRegValue(subKey, "*InterruptModeration", "0"); // Вимикає модерацію переривань для миттєвої реакції
                        SetOrForceRegValue(subKey, "InterruptModeration", "0");

                        // 4. Вимкнення Large Send Offload (усуває фризи мережевого драйвера)
                        SetOrForceRegValue(subKey, "*LsoV2IPv4", "0");
                        SetOrForceRegValue(subKey, "*LsoV2IPv6", "0");

                        // 5. Збільшення кільцевих буферів (безпечні значення, підтримуються більшістю драйверів)
                        SetOrForceRegValue(subKey, "*ReceiveBuffers", "2048");
                        SetOrForceRegValue(subKey, "*TransmitBuffers", "1024");
                        SetOrForceRegValue(subKey, "ReceiveBuffers", "2048");
                        SetOrForceRegValue(subKey, "TransmitBuffers", "1024");
                    }

                    // 6. Заборона відключення пристрою для економії енергії
                    subKey.SetValue("PnPCapabilities", 24, RegistryValueKind.DWord);

                    modifiedAdapters++;
                }

                AppLogger.Log($"Енергозбереження мережевих адаптерів вимкнено, буфери збільшено (Оптимізовано пристроїв: {modifiedAdapters})", "SUCCESS");
                return modifiedAdapters > 0;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Помилка конфігурації мережевого адаптера: {ex.Message}", "ERROR");
                return false;
            }
        });
    }

    /// <summary>Повертає адаптерам саме ті значення реєстру, що були до оптимізації.</summary>
    private static bool RestoreNicPowerSaving()
    {
        Dictionary<string, Dictionary<string, NicRegSnapshot>> snapshots;
        lock (_nicSnapshotLock)
        {
            if (_nicSnapshots.Count == 0)
            {
                AppLogger.Log("Немає збережених оригіналів — відновлення мережевих адаптерів не виконується", "WARN");
                return false;
            }

            snapshots = new Dictionary<string, Dictionary<string, NicRegSnapshot>>(_nicSnapshots, StringComparer.OrdinalIgnoreCase);
            _nicSnapshots.Clear();
        }

        int restoredAdapters = 0;
        foreach (var kv in snapshots)
        {
            try
            {
                string fullName = kv.Key; // Напр.: HKEY_LOCAL_MACHINE\SYSTEM\...
                int sep = fullName.IndexOf('\\');
                if (sep <= 0 || sep >= fullName.Length - 1) continue;

                string hivePrefix = fullName.Substring(0, sep);
                string relativePath = fullName.Substring(sep + 1);
                var hive = hivePrefix.Equals("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase)
                    ? RegistryHive.LocalMachine
                    : RegistryHive.CurrentUser;

                using var subKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(relativePath, writable: true);
                if (subKey == null) continue;

                foreach (var vn in kv.Value)
                {
                    try
                    {
                        if (vn.Value.Value != null)
                        {
                            subKey.SetValue(vn.Key, vn.Value.Value, vn.Value.Kind);
                        }
                        else
                        {
                            subKey.DeleteValue(vn.Key, throwOnMissingValue: false);
                        }
                    }
                    catch { }
                }

                restoredAdapters++;
            }
            catch { }
        }

        AppLogger.Log(restoredAdapters > 0
            ? $"Параметри мережевих адаптерів відновлено зі снапшоту ({restoredAdapters} пристроїв)"
            : "Не вдалося відновити мережеві адаптери зі снапшоту", restoredAdapters > 0 ? "SUCCESS" : "ERROR");
        return restoredAdapters > 0;
    }

    /// <summary>Реальна перевірка рівня автоналаштування TCP-вікна через netsh (не хардкод).</summary>
    private static bool IsTcpAutotuningNormal()
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = "int tcp show global",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            if (proc == null) return false;

            // Читаємо вивід асинхронно, щоб stderr/stdout не заблокували потік (deadlock) навіть
            // при великому обсязі даних; після тайм-ауту процес примусово завершується.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            if (!proc.WaitForExit(5000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                try { proc.WaitForExit(2000); } catch { }
                return false;
            }

            string output = stdoutTask.GetAwaiter().GetResult() ?? string.Empty;
            _ = stderrTask.GetAwaiter().GetResult();
            if (proc.ExitCode != 0) return false;

            string lower = output.ToLowerInvariant();

            // Англійська локалізація: "Receive Window Auto-Tuning Level : Normal"
            if (lower.Contains("autotun", StringComparison.OrdinalIgnoreCase))
            {
                if (lower.Contains("normal", StringComparison.OrdinalIgnoreCase)) return true;
                if (lower.Contains("disabled", StringComparison.OrdinalIgnoreCase)) return false;
                if (lower.Contains("highly", StringComparison.OrdinalIgnoreCase)) return false;
                if (lower.Contains("experimental", StringComparison.OrdinalIgnoreCase)) return false;
            }

            // Російсько-/українськомовні системи: "обычный" / "звичайний" тощо
            if (lower.Contains("настройк", StringComparison.OrdinalIgnoreCase) ||
                lower.Contains("налаштув", StringComparison.OrdinalIgnoreCase))
            {
                return lower.Contains("обычный", StringComparison.OrdinalIgnoreCase) ||
                       lower.Contains("звичайн", StringComparison.OrdinalIgnoreCase) ||
                       lower.Contains("нормальн", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch { }
        return false;
    }

    #endregion

    #region 3. Тюнінг системного кешу DNS та розблокування QoS

    public static async Task<bool> OptimizeDnsAndQosAsync(bool isApply)
    {
        return await Task.Run(() =>
        {
            try
            {
                // 1. Кеш DNS (прискорене повторне резолвлення доменів без мережевих затримок)
                using (var dnsKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).CreateSubKey(@"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters"))
                {
                    if (dnsKey != null)
                    {
                        if (isApply)
                        {
                            dnsKey.SetValue("MaxCacheTtl", 86400, RegistryValueKind.DWord);
                            dnsKey.SetValue("MaxNegativeCacheTtl", 5, RegistryValueKind.DWord);
                            dnsKey.SetValue("NetFailureCacheTime", 0, RegistryValueKind.DWord);
                            dnsKey.SetValue("NegativeSOACacheTime", 0, RegistryValueKind.DWord);
                        }
                        else
                        {
                            dnsKey.DeleteValue("MaxCacheTtl", false);
                            dnsKey.DeleteValue("MaxNegativeCacheTtl", false);
                            dnsKey.DeleteValue("NetFailureCacheTime", false);
                            dnsKey.DeleteValue("NegativeSOACacheTime", false);
                        }
                    }
                }

                // 2. Зняття 20% резервування пропускної здатності планувальника QoS
                using (var pschedKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Psched"))
                {
                    if (pschedKey != null)
                    {
                        if (isApply)
                        {
                            pschedKey.SetValue("NonBestEffortLimit", 0, RegistryValueKind.DWord);
                        }
                        else
                        {
                            pschedKey.DeleteValue("NonBestEffortLimit", false);
                        }
                    }
                }

                AppLogger.Log(isApply
                    ? "Кеш DNS оптимізовано, ліміт 20% каналу QoS успішно знято"
                    : "Параметри DNS-кешу та QoS відновлено", "SUCCESS");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Помилка налаштування DNS/QoS: {ex.Message}", "ERROR");
                return false;
            }
        });
    }

    #endregion

    #region 4. Аварійне скидання мережевого стека (1-Click Network Repair)

    public static async Task<bool> ResetNetworkStackAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                AppLogger.Log("Запуск повного скидання мережевого стека Windows...", "INFO");

                bool critical = true;
                critical &= RunSystemTool("netsh.exe", "winsock reset");
                critical &= RunSystemTool("netsh.exe", "int ip reset");
                critical &= RunSystemTool("netsh.exe", "int tcp reset");
                critical &= RunSystemTool("netsh.exe", "int ipv4 reset");
                critical &= RunSystemTool("netsh.exe", "int ipv6 reset");
                critical &= RunSystemTool("netsh.exe", "branchcache reset");

                // Бест-ефорт: /release та /renew можуть повернути помилку на статичному IP — не критично.
                RunSystemTool("ipconfig.exe", "/flushdns");
                RunSystemTool("ipconfig.exe", "/release");
                RunSystemTool("ipconfig.exe", "/renew");
                RunSystemTool("nbtstat.exe", "-R");
                RunSystemTool("nbtstat.exe", "-RR");
                RunSystemTool("arp.exe", "-d *");

                if (critical)
                    AppLogger.Log("Мережевий стек, таблицю маршрутизації, ARP та Winsock успішно відновлено", "SUCCESS");
                else
                    AppLogger.Log("Скидання мережевого стека завершено з помилками (перевірте права адміністратора)", "WARN");
                return critical;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Помилка скидання мережі: {ex.Message}", "ERROR");
                return false;
            }
        });
    }

    #endregion

    #region 5. Діагностика стану мережі

    public static async Task<NetworkTuningStatus> GetTuningStatusAsync()
    {
        return await Task.Run(() =>
        {
            var status = new NetworkTuningStatus();
            var physicalNics = GetPhysicalActiveAdapters();
            status.ActiveAdaptersCount = physicalNics.Count;
            status.ActiveAdaptersNames = physicalNics.Select(n => n.Description.Replace("(R)", "").Trim()).ToList();

            // 1. Перевірка Nagle / TCPNoDelay
            try
            {
                using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces");
                if (key != null)
                {
                    foreach (var sub in key.GetSubKeyNames())
                    {
                        using var subKey = key.OpenSubKey(sub);
                        if (Convert.ToInt32(subKey?.GetValue("TCPNoDelay") ?? 0) == 1 &&
                            Convert.ToInt32(subKey?.GetValue("TcpAckFrequency") ?? 0) == 1)
                        {
                            status.IsNagleDisabled = true;
                            break;
                        }
                    }
                }
            }
            catch { }

            // 2. Перевірка QoS
            try
            {
                using var psched = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\Psched");
                status.IsQosUnlocked = Convert.ToInt32(psched?.GetValue("NonBestEffortLimit") ?? -1) == 0;
            }
            catch { }

            // 3. Перевірка DNS Cache
            try
            {
                using var dns = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters");
                status.IsDnsCacheOptimized = Convert.ToInt32(dns?.GetValue("MaxCacheTtl") ?? 0) >= 86400;
            }
            catch { }

            // 4. Перевірка EEE, Buffers та LSO
            try
            {
                string classPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";
                using var baseClass = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(classPath);
                if (baseClass != null)
                {
                    foreach (var sub in baseClass.GetSubKeyNames().Where(s => s.Length == 4 && s.All(char.IsDigit)))
                    {
                        using var sKey = baseClass.OpenSubKey(sub);
                        string? eee = sKey?.GetValue("*EEE")?.ToString();
                        string? green = sKey?.GetValue("GreenEthernet")?.ToString();
                        string? rx = sKey?.GetValue("*ReceiveBuffers")?.ToString();
                        string? lso = sKey?.GetValue("*LsoV2IPv4")?.ToString();

                        if (eee == "0" || green == "0") status.IsEeeDisabled = true;
                        if (rx == "2048") status.IsNicBuffersOptimized = true;
                        if (lso == "0") status.IsLsoDisabled = true;

                        if (status.IsEeeDisabled && status.IsNicBuffersOptimized && status.IsLsoDisabled) break;
                    }
                }
            }
            catch { }

            // 5. Перевірка зняття мережевого троттлінгу MMCSS (NetworkThrottlingIndex = 0xFFFFFFFF)
            try
            {
                using var mmcss = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile");
                long idx = Convert.ToInt64(mmcss?.GetValue("NetworkThrottlingIndex") ?? 10);
                status.IsNetworkThrottlingDisabled = idx == -1 || idx == 0xFFFFFFFFL;
            }
            catch { }

            status.IsAutotuningNormal = IsTcpAutotuningNormal();
            return status;
        });
    }

    #endregion

    #region 6. Замір затримки та джиттера мережі (для ігор)

    /// <summary>
    /// Реальний замір затримки до хосту (ігровий сервер/CDN): min/avg/max, джиттер та % втрат.
    /// Дозволяє чесно оцінити ефект твіків і діагностувати проблеми зі зв'язком.
    /// </summary>
    public static async Task<NetworkLatencyResult> MeasureLatencyAsync(string host, int samples = 10, int timeoutMs = 1000)
    {
        return await Task.Run(async () =>
        {
            var result = new NetworkLatencyResult { Host = host };
            if (string.IsNullOrWhiteSpace(host))
                return result;

            samples = Math.Clamp(samples, 3, 50);
            timeoutMs = Math.Clamp(timeoutMs, 200, 5000);

            var rtts = new List<long>(samples);
            int sent = 0, received = 0;

            try
            {
                using var ping = new Ping();
                for (int i = 0; i < samples; i++)
                {
                    try
                    {
                        sent++;
                        var reply = await ping.SendPingAsync(host, timeoutMs);
                        if (reply.Status == IPStatus.Success)
                        {
                            received++;
                            rtts.Add(reply.RoundtripTime);
                        }
                    }
                    catch (PingException) { }
                    catch (InvalidOperationException) { } // повторний виклик на тому ж Ping
                }
            }
            catch { }

            result.SamplesSent = sent;
            result.SamplesReceived = received;
            result.LossPercent = sent > 0 ? (sent - received) * 100.0 / sent : 100.0;

            if (rtts.Count > 0)
            {
                result.IsReachable = true;
                result.MinMs = rtts.Min();
                result.MaxMs = rtts.Max();
                result.AvgMs = rtts.Average();

                if (rtts.Count >= 2)
                {
                    double mean = result.AvgMs;
                    result.JitterMs = rtts.Select(r => Math.Abs(r - mean)).Average();
                }
            }

            return result;
        });
    }

    #endregion

    #region Допоміжні методи

    private static List<NetworkInterface> GetPhysicalActiveAdapters()
    {
        var list = new List<NetworkInterface>();
        try
        {
            var all = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var nic in all)
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                string desc = nic.Description.ToLowerInvariant();
                if (desc.Contains("virtual") || desc.Contains("vmware") || desc.Contains("hyper-v") ||
                    desc.Contains("wsl") || desc.Contains("tailscale") || desc.Contains("zerotier") ||
                    desc.Contains("tap") || desc.Contains("vpn") || desc.Contains("npcap") ||
                    desc.Contains("bluetooth") || desc.Contains("ndis") || desc.Contains("wan miniport"))
                    continue;

                list.Add(nic);
            }

            if (list.Count == 0)
            {
                list = all.Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback).ToList();
            }
        }
        catch { }
        return list;
    }

    private static string NormalizeGuid(string? guid)
    {
        if (string.IsNullOrWhiteSpace(guid)) return string.Empty;
        return guid.Trim().Trim('{', '}').Trim().ToLowerInvariant();
    }

    private static bool IsEthernetType(NetworkInterfaceType type) =>
        type == NetworkInterfaceType.Ethernet ||
        type == NetworkInterfaceType.GigabitEthernet ||
        type == NetworkInterfaceType.FastEthernetT ||
        type == NetworkInterfaceType.FastEthernetFx;

    private static void SetOrForceRegValue(RegistryKey key, string valueName, string value)
    {
        try
        {
            key.SetValue(valueName, value, RegistryValueKind.String);
        }
        catch { }
    }

    private static bool FlushDnsAndRouteCache()
    {
        bool ok = RunSystemTool("ipconfig.exe", "/flushdns");
        ok &= RunSystemTool("nbtstat.exe", "-R");
        return ok;
    }

    private static bool RunSystemTool(string exeName, string arguments)
    {
        try
        {
            string fullPath = Path.Combine(Environment.SystemDirectory, exeName);
            if (!File.Exists(fullPath)) fullPath = exeName;

            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = fullPath,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            if (proc == null) return false;

            // Читаємо вивід асинхронно, щоб завислий процес не блокував потік назавжди.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            if (!proc.WaitForExit(10000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                try { proc.WaitForExit(2000); } catch { }
                AppLogger.Log($"Тайм-аут виконання {Path.GetFileName(exeName)} {arguments}", "WARN");
                return false;
            }

            _ = stdoutTask.GetAwaiter().GetResult();
            string stderr = stderrTask.GetAwaiter().GetResult() ?? string.Empty;

            if (proc.ExitCode != 0)
            {
                string detail = string.IsNullOrWhiteSpace(stderr) ? $"код {proc.ExitCode}" : stderr.Trim();
                AppLogger.Log($"Помилка виконання {Path.GetFileName(exeName)} {arguments}: {detail}", "WARN");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Не вдалося запустити {exeName}: {ex.Message}", "ERROR");
            return false;
        }
    }

    #endregion
}