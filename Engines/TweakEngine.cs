using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.ServiceProcess;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MASLOOPTIMIZER;

public enum TweakSortMode
{
    Default,
    AppliedFirst,
    UnappliedFirst,
    RiskAscending,
    RiskDescending,
    NameAscending,
    NameDescending,
    Category
}

public class TweakEngine
{
    private static readonly Lazy<TweakEngine> _instance = new(() => new TweakEngine());
    public static TweakEngine Instance => _instance.Value;

    public List<TweakModel> AllTweaks { get; private set; } = new();

    #region Завантаження бази твіків

    public void LoadTweaks()
    {
        string jsonContent = string.Empty;

        // 1. Спроба завантажити з вбудованих ресурсів
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(r => r.EndsWith("tweaks.bundle.json", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(resourceName))
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                jsonContent = reader.ReadToEnd();
            }
        }

        // 2. Резервне завантаження з файлу на диску
        if (string.IsNullOrWhiteSpace(jsonContent))
        {
            string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tweaks.bundle.json");
            if (File.Exists(localPath))
            {
                jsonContent = File.ReadAllText(localPath);
            }
        }

        // 3. Десеріалізація
        if (!string.IsNullOrWhiteSpace(jsonContent))
        {
            try
            {
                var bundle = JsonSerializer.Deserialize<TweakBundle>(jsonContent);
                if (bundle?.Tweaks != null)
                {
                    AllTweaks = bundle.Tweaks.ToList();

                    // Категорію прибрано з бандлу — підтягуємо її ключ з мовних файлів,
                    // щоб фільтри/сортування по категоріях продовжували працювати.
                    foreach (var t in AllTweaks)
                    {
                        t.ResolveCategoryFromLocalization();
                    }

                    AllTweaks = AllTweaks.OrderBy(t => t.LocalizedName).ToList();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Помилка десеріалізації бандлу твіків: {ex.Message}", "ERROR");
            }
        }
    }

    #endregion

    #region Контекстна фільтрація та категорії

    public IEnumerable<TweakModel> GetFilteredAndSortedTweaks(
        string? riskLevel = null,
        string? category = null,
        string? searchQuery = null,
        TweakSortMode sortMode = TweakSortMode.Default)
    {
        var query = AllTweaks.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(riskLevel) && !riskLevel.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(t => string.Equals(t.Risk, riskLevel, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(category) && !category.Equals("Всі", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(t => string.Equals(t.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            string q = searchQuery.Trim();
            query = query.Where(t =>
                t.LocalizedName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                t.LocalizedDescription.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                t.LocalizedBenefits.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                t.LocalizedSideEffects.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                t.LocalizedCategory.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                t.Id.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        return sortMode switch
        {
            TweakSortMode.AppliedFirst => query.OrderByDescending(t => t.IsApplied).ThenBy(t => t.LocalizedName),
            TweakSortMode.UnappliedFirst => query.OrderBy(t => t.IsApplied).ThenBy(t => t.LocalizedName),
            TweakSortMode.RiskAscending => query.OrderBy(t => GetRiskWeight(t.Risk)).ThenBy(t => t.LocalizedName),
            TweakSortMode.RiskDescending => query.OrderByDescending(t => GetRiskWeight(t.Risk)).ThenBy(t => t.LocalizedName),
            TweakSortMode.NameAscending => query.OrderBy(t => t.LocalizedName),
            TweakSortMode.NameDescending => query.OrderByDescending(t => t.LocalizedName),
            TweakSortMode.Category => query.OrderBy(t => t.Category).ThenBy(t => t.LocalizedName),
            _ => query.OrderBy(t => t.LocalizedName)
        };
    }

    public List<string> GetCategories(string? riskLevel = null)
    {
        var query = AllTweaks.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(riskLevel) && !riskLevel.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(t => string.Equals(t.Risk, riskLevel, StringComparison.OrdinalIgnoreCase));
        }

        var categories = query.Select(t => t.Category).Distinct().OrderBy(c => c).ToList();
        categories.Insert(0, "Всі");
        return categories;
    }

    private static int GetRiskWeight(string? risk)
    {
        return risk?.ToUpperInvariant() switch
        {
            "UI" => 0,
            "SAFE" => 1,
            "MEDIUM" => 2,
            "HIGH" => 3,
            _ => 4
        };
    }

    #endregion

    #region Нативна та миттєва перевірка стану (100% C#)

    public async Task EvaluateAllStatusesAsync(Action<int, string>? progressCallback = null)
    {
        if (AllTweaks.Count == 0) return;

        await Task.Run(() =>
        {
            int total = AllTweaks.Count;
            int completed = 0;

            Parallel.ForEach(AllTweaks, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, tweak =>
            {
                tweak.IsApplied = CheckTweakStatusNative(tweak);

                int currentCount = System.Threading.Interlocked.Increment(ref completed);
                int percent = (int)((currentCount / (double)total) * 100);
                progressCallback?.Invoke(percent, tweak.LocalizedName);
            });
        });
    }

    private static bool CheckTweakStatusNative(TweakModel tweak)
    {
        try
        {
            // 1. Перевірка за нативними записами Реєстру
            if (tweak.RegistryActions != null && tweak.RegistryActions.Count > 0)
            {
                bool allMatch = true;
                foreach (var action in tweak.RegistryActions)
                {
                    if (!CheckRegistryValueMatch(action))
                    {
                        allMatch = false;
                        break;
                    }
                }
                return allMatch;
            }

            // 2. Перевірка служб через ServiceController
            if (tweak.ServiceActions != null && tweak.ServiceActions.Count > 0)
            {
                bool allMatch = true;
                foreach (var svc in tweak.ServiceActions)
                {
                    if (!CheckServiceStatusMatch(svc))
                    {
                        allMatch = false;
                        break;
                    }
                }
                return allMatch;
            }

            // 3. Перевірка через консольні команди (bcdedit, powercfg, fsutil)
            if (tweak.CommandAction != null && !string.IsNullOrWhiteSpace(tweak.CommandAction.CheckCmd))
            {
                string output = RunCommandCapture(tweak.CommandAction.CheckCmd);
                return output.Contains(tweak.CommandAction.CheckExpected, StringComparison.OrdinalIgnoreCase);
            }

            // 4. Fallback: прямий виклик powershell.exe без вбудованого SDK
            if (!string.IsNullOrWhiteSpace(tweak.CheckScript))
            {
                string res = RunPowerShellCapture(tweak.CheckScript);
                return res.Equals("True", StringComparison.OrdinalIgnoreCase) || res.Equals("1");
            }
        }
        catch { }

        return false;
    }

    private static bool CheckRegistryValueMatch(RegistryAction action)
    {
        try
        {
            var root = action.Hive.Equals("HKLM", StringComparison.OrdinalIgnoreCase) ? Registry.LocalMachine : Registry.CurrentUser;
            using var key = root.OpenSubKey(action.KeyPath);
            if (key == null) return false;

            object? actualVal = key.GetValue(action.ValueName);
            if (actualVal == null) return false;

            string expectedStr = action.ApplyValue?.ToString() ?? string.Empty;
            string actualStr = actualVal.ToString() ?? string.Empty;

            return string.Equals(expectedStr, actualStr, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool CheckServiceStatusMatch(ServiceAction action)
    {
        try
        {
            using var sc = new ServiceController(action.ServiceName);
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{action.ServiceName}");
            if (key == null) return false;

            int startType = (int)(key.GetValue("Start") ?? -1);
            int expectedType = action.ApplyStartup switch
            {
                "Disabled" => 4,
                "Manual" => 3,
                "Automatic" => 2,
                _ => 4
            };

            return startType == expectedType;
        }
        catch { return false; }
    }

    #endregion

    #region Нативне виконання твіка (Застосування та Відкат)

    public async Task<bool> ExecuteTweakAsync(TweakModel tweak, bool isApply)
    {
        tweak.IsBusy = true;

        bool success = await Task.Run(() =>
        {
            try
            {
                // 1. Нативні операції з Реєстром
                if (tweak.RegistryActions != null && tweak.RegistryActions.Count > 0)
                {
                    foreach (var action in tweak.RegistryActions)
                    {
                        ExecuteRegistryActionNative(action, isApply);
                    }
                    return true;
                }

                // 2. Нативне керування Службами Windows
                if (tweak.ServiceActions != null && tweak.ServiceActions.Count > 0)
                {
                    foreach (var svc in tweak.ServiceActions)
                    {
                        ExecuteServiceActionNative(svc, isApply);
                    }
                    return true;
                }

                // 3. Виконання команд через cmd.exe
                if (tweak.CommandAction != null)
                {
                    string cmd = isApply ? tweak.CommandAction.ApplyCmd : tweak.CommandAction.RestoreCmd;
                    if (!string.IsNullOrWhiteSpace(cmd))
                    {
                        RunCommandQuiet(cmd);
                        return true;
                    }
                }

                // 4. Fallback через чистий процес powershell.exe
                string? script = isApply ? tweak.ApplyScript : tweak.RestoreScript;
                if (!string.IsNullOrWhiteSpace(script))
                {
                    RunPowerShellQuiet(script);
                    return true;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Помилка у твіку {tweak.LocalizedName}: {ex.Message}", "ERROR");
                return false;
            }

            return false;
        });

        tweak.IsApplied = CheckTweakStatusNative(tweak);
        tweak.IsBusy = false;

        if (success)
        {
            AppLogger.Log(isApply
                ? $"Застосовано системний твік: {tweak.LocalizedName}"
                : $"Відновлено початковий стан твіка: {tweak.LocalizedName}", "SUCCESS");
        }
        return success;
    }

    private static void ExecuteRegistryActionNative(RegistryAction action, bool isApply)
    {
        var root = action.Hive.Equals("HKLM", StringComparison.OrdinalIgnoreCase) ? Registry.LocalMachine : Registry.CurrentUser;

        if (isApply)
        {
            using var key = root.CreateSubKey(action.KeyPath);
            if (key == null) return;

            var kind = action.ValueKind.Equals("String", StringComparison.OrdinalIgnoreCase)
                ? RegistryValueKind.String
                : RegistryValueKind.DWord;

            object valToSet = kind == RegistryValueKind.DWord
                ? Convert.ToInt32(action.ApplyValue)
                : action.ApplyValue?.ToString() ?? string.Empty;

            key.SetValue(action.ValueName, valToSet, kind);
        }
        else
        {
            if (action.DeleteOnRestore)
            {
                using var key = root.OpenSubKey(action.KeyPath, true);
                key?.DeleteValue(action.ValueName, false);
            }
            else if (action.RestoreValue != null)
            {
                using var key = root.CreateSubKey(action.KeyPath);
                var kind = action.ValueKind.Equals("String", StringComparison.OrdinalIgnoreCase)
                    ? RegistryValueKind.String
                    : RegistryValueKind.DWord;

                object valToSet = kind == RegistryValueKind.DWord
                    ? Convert.ToInt32(action.RestoreValue)
                    : action.RestoreValue?.ToString() ?? string.Empty;

                key?.SetValue(action.ValueName, valToSet, kind);
            }
        }
    }

    private static void ExecuteServiceActionNative(ServiceAction action, bool isApply)
    {
        try
        {
            string targetStartup = isApply ? action.ApplyStartup : action.RestoreStartup;
            int startType = targetStartup switch
            {
                "Disabled" => 4,
                "Manual" => 3,
                "Automatic" => 2,
                _ => 3
            };

            using (var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{action.ServiceName}", true))
            {
                key?.SetValue("Start", startType, RegistryValueKind.DWord);
            }

            using var sc = new ServiceController(action.ServiceName);
            if (isApply && action.StopOnApply && sc.Status == ServiceControllerStatus.Running)
            {
                sc.Stop();
            }
            else if (!isApply && action.StartOnRestore && sc.Status != ServiceControllerStatus.Running)
            {
                sc.Start();
            }
        }
        catch { }
    }

    #endregion

    #region Легкі допоміжні виклики системних CLI процесів

    private static void RunCommandQuiet(string command)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            proc?.WaitForExit(4000);
        }
        catch { }
    }

    private static string RunCommandCapture(string command)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            string outStr = proc?.StandardOutput.ReadToEnd() ?? string.Empty;
            proc?.WaitForExit(3000);
            return outStr;
        }
        catch { return string.Empty; }
    }

    private static void RunPowerShellQuiet(string script)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            proc?.WaitForExit(6000);
        }
        catch { }
    }

    private static string RunPowerShellCapture(string script)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            string outStr = proc?.StandardOutput.ReadToEnd()?.Trim() ?? string.Empty;
            proc?.WaitForExit(4000);
            return outStr;
        }
        catch { return string.Empty; }
    }

    #endregion
}