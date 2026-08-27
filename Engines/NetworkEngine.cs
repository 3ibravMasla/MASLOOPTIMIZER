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
    public int ActiveAdaptersCount { get; set; }
    public List<string> ActiveAdaptersNames { get; set; } = new();

    public bool IsFullyOptimized => IsNagleDisabled && IsAutotuningNormal && IsEeeDisabled && IsQosUnlocked && IsDnsCacheOptimized;
}

public static class NetworkEngine
{
    #region 1. Оптимізація TCP/IP стека та затримок Nagle

    public static async Task<bool> OptimizeTcpLatencyAsync(bool isApply)
    {
        return await Task.Run(() =>
        {
            try
            {
                var targetNics = GetPhysicalActiveAdapters();
                var activeGuids = targetNics.Select(n => n.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

                // 1. Тюнінг алгоритму Nagle та частоти ACK для активних інтерфейсів
                string baseInterfacesPath = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
                using (var interfacesKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(baseInterfacesPath, true))
                {
                    if (interfacesKey != null)
                    {
                        foreach (var sub in interfacesKey.GetSubKeyNames())
                        {
                            if (activeGuids.Count > 0 && !activeGuids.Contains(sub) && sub.Length != 38)
                                continue;

                            using var subKey = interfacesKey.OpenSubKey(sub, true);
                            if (subKey != null)
                            {
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
                string keyPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(keyPath, true);
                if (baseKey == null) return false;

                int modifiedAdapters = 0;

                foreach (var sub in baseKey.GetSubKeyNames().Where(s => s.Length == 4 && char.IsDigit(s[0])))
                {
                    using var subKey = baseKey.OpenSubKey(sub, true);
                    if (subKey == null) continue;

                    string? driverDesc = subKey.GetValue("DriverDesc")?.ToString();
                    if (string.IsNullOrWhiteSpace(driverDesc) ||
                        driverDesc.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                        driverDesc.Contains("Miniport", StringComparison.OrdinalIgnoreCase) ||
                        driverDesc.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (isApply)
                    {
                        // 1. Повне вимкнення енергозберігаючих станів
                        SetOrForceRegValue(subKey, "*EEE", "0");
                        SetOrForceRegValue(subKey, "EEELinkAdvertisement", "0");
                        SetOrForceRegValue(subKey, "GreenEthernet", "0");
                        SetOrForceRegValue(subKey, "GigaLite", "0");
                        SetOrForceRegValue(subKey, "PowerSavingMode", "0");
                        SetOrForceRegValue(subKey, "EnergyEfficientEthernet", "0");
                        SetOrForceRegValue(subKey, "EnablePME", "0");
                        SetOrForceRegValue(subKey, "AutoPowerSaveModeEnabled", "0");
                        SetOrForceRegValue(subKey, "SavePowerNowEnabled", "0");
                        SetOrForceRegValue(subKey, "*SelectiveSuspend", "0");

                        // 2. Зниження апаратного Input Lag та DPC-затримок
                        SetOrForceRegValue(subKey, "*FlowControl", "0");
                        SetOrForceRegValue(subKey, "*InterruptModeration", "0"); // Вимикає модерацію переривань для миттєвої реакції
                        SetOrForceRegValue(subKey, "InterruptModeration", "0");

                        // 3. Вимкнення Large Send Offload (усуває фризи мережевого драйвера)
                        SetOrForceRegValue(subKey, "*LsoV2IPv4", "0");
                        SetOrForceRegValue(subKey, "*LsoV2IPv6", "0");

                        // 4. Збільшення кільцевих буферів (виключає втрату пакетів при піковому навантаженні)
                        SetOrForceRegValue(subKey, "*ReceiveBuffers", "2048");
                        SetOrForceRegValue(subKey, "*TransmitBuffers", "2048");
                        SetOrForceRegValue(subKey, "ReceiveBuffers", "2048");
                        SetOrForceRegValue(subKey, "TransmitBuffers", "2048");

                        // 5. Заборона відключення пристрою для економії енергії
                        subKey.SetValue("PnPCapabilities", 24, RegistryValueKind.DWord);
                    }
                    else
                    {
                        SetOrForceRegValue(subKey, "*EEE", "1");
                        SetOrForceRegValue(subKey, "GreenEthernet", "1");
                        SetOrForceRegValue(subKey, "*InterruptModeration", "1");
                        SetOrForceRegValue(subKey, "*LsoV2IPv4", "1");
                        SetOrForceRegValue(subKey, "*LsoV2IPv6", "1");
                        SetOrForceRegValue(subKey, "*ReceiveBuffers", "512");
                        SetOrForceRegValue(subKey, "*TransmitBuffers", "512");
                        subKey.DeleteValue("PnPCapabilities", false);
                    }

                    modifiedAdapters++;
                }

                AppLogger.Log(isApply
                    ? $"Енергозбереження мережевих адаптерів вимкнено, буфери збільшено (Оптимізовано пристроїв: {modifiedAdapters})"
                    : "Параметри мережевих адаптерів повернено до стандартних", "SUCCESS");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Помилка конфігурації мережевого адаптера: {ex.Message}", "ERROR");
                return false;
            }
        });
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

                RunSystemTool("netsh.exe", "winsock reset");
                RunSystemTool("netsh.exe", "int ip reset");
                RunSystemTool("netsh.exe", "int tcp reset");
                RunSystemTool("netsh.exe", "int ipv4 reset");
                RunSystemTool("netsh.exe", "int ipv6 reset");
                RunSystemTool("netsh.exe", "branchcache reset");
                RunSystemTool("ipconfig.exe", "/flushdns");
                RunSystemTool("ipconfig.exe", "/release");
                RunSystemTool("ipconfig.exe", "/renew");
                RunSystemTool("nbtstat.exe", "-R");
                RunSystemTool("nbtstat.exe", "-RR");
                RunSystemTool("arp.exe", "-d *");

                AppLogger.Log("Мережевий стек, таблицю маршрутизації, ARP та Winsock успішно відновлено", "SUCCESS");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Помилка скидання мережі: {ex.Message}", "ERROR");
                return false;
            }
        });
    }

    #endregion

    #region 5. Діагностика стану мережі для HealthEngine та UI

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
                    foreach (var sub in baseClass.GetSubKeyNames().Where(s => s.Length == 4 && char.IsDigit(s[0])))
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

            status.IsAutotuningNormal = true;
            return status;
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

    private static void SetOrForceRegValue(RegistryKey key, string valueName, string value)
    {
        try
        {
            key.SetValue(valueName, value, RegistryValueKind.String);
        }
        catch { }
    }

    private static void FlushDnsAndRouteCache()
    {
        RunSystemTool("ipconfig.exe", "/flushdns");
        RunSystemTool("nbtstat.exe", "-R");
    }

    private static void RunSystemTool(string exeName, string arguments)
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

            if (proc != null)
            {
                _ = proc.StandardOutput.ReadToEnd();
                _ = proc.StandardError.ReadToEnd();
                proc.WaitForExit(3000);
            }
        }
        catch { }
    }

    #endregion
}