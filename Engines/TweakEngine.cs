using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using System.Threading;
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

    // Глобальний "шлюз" для створення зовнішніх процесів (cmd.exe / powershell.exe).
    // Запобігає fork-bomb: Parallel.ForEach не може одночасно породити десятки CLI-процесів.
    private static readonly SemaphoreSlim ProcessGate = new(3, 3);

    // Фаза 7.3: під час гри/Game Mode нові CLI не спавняться; запобіжник очікування — 6 годин.
    private static readonly TimeSpan CliQuietWaitTimeout = TimeSpan.FromHours(6);


    // Снапшоти початкового стану для коректного відкату:
    // HKCU\Software\MASLOOPTIMIZER\TweakState\<tweakId> -> JSON-запис значення/служби.
    private const string StateRootPath = @"Software\MASLOOPTIMIZER\TweakState";

    private const int CmdCaptureTimeoutMs = 12_000;
    private const int CmdExecTimeoutMs = 120_000;
    private const int PowerShellTimeoutMs = 120_000;
    private const int ServiceTimeoutSeconds = 10;

    #region Завантаження бази твіків

    public void LoadTweaks()
    {
        string jsonContent = string.Empty;

        // 1. Основний шлях: tweaks.bundle.json поруч з exe (можна оновлювати без перезбірки)
        string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tweaks.bundle.json");
        if (File.Exists(localPath))
        {
            try
            {
                jsonContent = File.ReadAllText(localPath);
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Не вдалося прочитати {localPath}: {ex.Message}", "WARN");
            }
        }

        // 2. Резервний шлях: вбудований ресурс (страховка, якщо файл видалено)
        if (string.IsNullOrWhiteSpace(jsonContent))
        {
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
        }

        // 3. Десеріалізація
        if (!string.IsNullOrWhiteSpace(jsonContent))
        {
            try
            {
                // PropertyNameCaseInsensitive — на випадок бандла з іншим регістром ключів
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var bundle = JsonSerializer.Deserialize<TweakBundle>(jsonContent, options);
                if (bundle?.Tweaks != null)
                {
                    AllTweaks = bundle.Tweaks.ToList();

                    // v3.0: Category у бандлі — стабільний ключ (CPU/GPU/Gaming/Telemetry/UI/Security/Power).
                    // Відображувана назва береться з Languages -> Categories.{key} (TweakModel.LocalizedCategory),
                    // тому категорії не дублюються в мовних файлах твіків.
                    // Для старих банлів без Category підтягуємо значення з мовного файлу.
                    foreach (var t in AllTweaks.Where(t => string.IsNullOrWhiteSpace(t.Category) || t.Category == "Загальні"))
                    {
                        t.ResolveCategoryFromLocalization();
                    }

                    AllTweaks = AllTweaks.OrderBy(t => t.LocalizedName).ToList();

                    foreach (var t in AllTweaks.Where(t => !HasAnyAction(t)))
                    {
                        AppLogger.Log($"Твік '{t.Id}' не має жодної дії — буде показано як такий, що не застосовується", "WARN");
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Помилка десеріалізації бандлу твіків: {ex.Message}", "ERROR");
            }
        }
    }

    private static bool HasAnyAction(TweakModel t) =>
        !string.IsNullOrWhiteSpace(t.ScriptPath) ||
        (t.RegistryActions is { Count: > 0 }) ||
        (t.ServiceActions is { Count: > 0 }) ||
        (t.CommandAction != null && (!string.IsNullOrWhiteSpace(t.CommandAction.ApplyCmd) || !string.IsNullOrWhiteSpace(t.CommandAction.CheckCmd))) ||
        !string.IsNullOrWhiteSpace(t.ApplyScript) ||
        !string.IsNullOrWhiteSpace(t.CheckScript);

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

    /// <summary>Порядок чіпсів у панелі категорій (решта — за алфавітом після них).</summary>
    private static readonly string[] PreferredCategoryOrder =
        { "CPU", "GPU", "Gaming", "Telemetry", "UI", "Security", "Power" };

    public List<string> GetCategories(string? riskLevel = null)
    {
        var query = AllTweaks.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(riskLevel) && !riskLevel.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(t => string.Equals(t.Risk, riskLevel, StringComparison.OrdinalIgnoreCase));
        }

        var categories = query.Select(t => t.Category).Distinct()
            .OrderBy(CategoryOrder)
            .ThenBy(c => c, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        categories.Insert(0, "Всі");
        return categories;
    }

    private static int CategoryOrder(string category)
    {
        int index = Array.FindIndex(PreferredCategoryOrder,
            p => string.Equals(p, category, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? int.MaxValue : index;
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

    #region Нативна та миттєва перевірка стану

    public async Task EvaluateAllStatusesAsync(Action<int, string>? progressCallback = null)
    {
        // Фаза 7.3: скан не стартує під час гри/Game Mode — чекаємо виходу (без polling,
        // прокидаємось від AppActivityCoordinator.ModeChanged), після чого продовжуємо.
        if (AppActivityCoordinator.IsQuietMode)
        {
            AppLogger.Log("Конвеєр твіків: скан відкладено — активний quiet-режим (гра/Game Mode)", "INFO");
            await AppActivityCoordinator.WaitWhileQuietAsync();
        }

        if (AllTweaks.Count == 0) return;


        await Task.Run(() =>
        {
            int total = AllTweaks.Count;
            int completed = 0;

            // Нативні перевірки (Реєстр / Служби) виконуються паралельно, але
            // кількість потоків обмежена. Запуск консольних процесів
            // додатково серіалізується через ProcessGate у Run* методах.
            int workerThreads = Math.Max(1, Math.Min(4, Environment.ProcessorCount));
            Parallel.ForEach(AllTweaks, new ParallelOptions { MaxDegreeOfParallelism = workerThreads }, tweak =>
            {
                bool applied;
                try { applied = CheckTweakStatusNative(tweak); }
                catch { applied = false; }

                SetAppliedSafe(tweak, applied);

                int currentCount = Interlocked.Increment(ref completed);
                int percent = (int)((currentCount / (double)total) * 100);
                progressCallback?.Invoke(percent, tweak.LocalizedName);
            });
        });
    }

    /// <summary>Оновлення UI-моделі з фонового потоку через Dispatcher (WPF thread-safety).</summary>
    private static void SetAppliedSafe(TweakModel tweak, bool value)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => tweak.IsApplied = value));
            return;
        }

        tweak.IsApplied = value;
    }

    private static bool CheckTweakStatusNative(TweakModel tweak)
    {
        try
        {
            // 0. Явний реєстровий зонд з бандлу — найшвидший шлях (жодного cmd.exe)
            if (!string.IsNullOrWhiteSpace(tweak.CheckRegKey))
            {
                return CheckRegistryProbe(tweak);
            }

            // 1. Перевірка за нативними записами Реєстру
            if (tweak.RegistryActions is { Count: > 0 })
            {
                foreach (var action in tweak.RegistryActions)
                {
                    if (!CheckRegistryActionMatch(action)) return false;
                }
                return true;
            }

            // 2. Перевірка служб через ServiceController
            if (tweak.ServiceActions is { Count: > 0 })
            {
                foreach (var svc in tweak.ServiceActions)
                {
                    if (!CheckServiceStatusMatch(svc)) return false;
                }
                return true;
            }

            // 3. Скриптова перевірка (locale-independent; має пріоритет над CLI-рядком)
            if (!string.IsNullOrWhiteSpace(tweak.CheckScript))
            {
                return RunPowerShellBool(tweak.CheckScript!);
            }

            // 4. Перевірка через консольні команди (bcdedit, powercfg, fsutil)
            if (tweak.CommandAction != null && !string.IsNullOrWhiteSpace(tweak.CommandAction.CheckCmd))
            {
                string output = RunCommandCapture(tweak.CommandAction.CheckCmd);
                return ContainsNormalized(output, tweak.CommandAction.CheckExpected);
            }

            // 5. Твік без реєстрового зонда (powercfg / schtasks / .bat-flag) —
            //    дивимось прапорець, який TweakEngine виставляє після виконання.
            var stateFlag = ReadAppliedStateFlag(tweak.Id);
            if (stateFlag.HasValue) return stateFlag.Value;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Перевірка стану твіка '{tweak.Id}': {ex.Message}", "WARN");
        }

        return false;
    }

    /// <summary>
    /// Швидкий зонд стану: CheckRegHive/CheckRegKey/CheckRegValue/CheckExpectedValue.
    /// Підтримує wildcard-шляхи; "__ABSENT__" = "застосовано, коли значення відсутнє".
    /// </summary>
    private static bool CheckRegistryProbe(TweakModel tweak)
    {
        try
        {
            var root = RootKey(tweak.CheckRegHive);
            bool expectAbsent = IsAbsentExpected(tweak.CheckExpectedValue);
            bool anyPath = false;
            bool anyValue = false;

            foreach (var path in ExpandPaths(root, tweak.CheckRegKey))
            {
                anyPath = true;

                using var key = root.OpenSubKey(path);
                if (key == null) continue;

                object? actual = key.GetValue(tweak.CheckRegValue, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (actual == null) continue;

                anyValue = true;
                if (expectAbsent) return false;
                if (!ProbeValueMatches(actual, tweak.CheckExpectedValue)) return false;
            }

            if (!anyPath) return true;      // ключа немає в системі — твік не стосується цієї машини
            if (expectAbsent) return true;  // значення ніде не знайдено — мета досягнута
            return anyValue;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Перевірка стану твіка '{tweak.Id}': {ex.Message}", "WARN");
            return false;
        }
    }

    private static bool IsAbsentExpected(string? expected) =>
        string.Equals((expected ?? string.Empty).Trim(), "__ABSENT__", StringComparison.OrdinalIgnoreCase);

    private static bool ProbeValueMatches(object actual, string? expected)
    {
        string expectedText = (expected ?? string.Empty).Trim();
        if (expectedText.Length == 0) return true; // порожнє очікування = "значення існує"

        if (!long.TryParse(expectedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long expectedNum))
        {
            return string.Equals(expectedText, ValueToText(actual).Trim(), StringComparison.OrdinalIgnoreCase);
        }

        // REG_DWORD читається як int — 0x80000001 приходить від'ємним, тому порівнюємо як беззнакове
        if (actual is int i) return expectedNum == unchecked((uint)i);
        if (actual is uint u) return expectedNum == u;

        return long.TryParse(ValueToText(actual).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long actualNum)
               && expectedNum == actualNum;
    }

    /// <summary>Прапорець застосованого стану для твіків без реєстрового зонда.</summary>
    private static void SaveAppliedStateFlag(string tweakId, bool isApply)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(AppliedFlagRoot);
            key?.SetValue(SanitizeKeyName(tweakId), isApply ? "1" : "0", RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Не вдалося зберегти прапорець стану твіка '{tweakId}': {ex.Message}", "WARN");
        }
    }

    private static bool? ReadAppliedStateFlag(string tweakId)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppliedFlagRoot);
            if (key?.GetValue(SanitizeKeyName(tweakId)) is not string flag) return null;
            return flag == "1";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Порівняння без урахування кількості пробілів (bcdedit/fsutil вирівнюють колонки по-різному).</summary>
    private static bool ContainsNormalized(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(needle)) return false;
        if (string.IsNullOrEmpty(haystack)) return false;

        string h = CollapseWhitespace(haystack);
        string n = CollapseWhitespace(needle);
        return h.Contains(n, StringComparison.OrdinalIgnoreCase);
    }

    private static string CollapseWhitespace(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool prevSpace = false;
        foreach (char c in s)
        {
            bool isSpace = char.IsWhiteSpace(c);
            if (isSpace)
            {
                if (!prevSpace) sb.Append(' ');
            }
            else sb.Append(c);
            prevSpace = isSpace;
        }
        return sb.ToString().Trim();
    }

    private static bool CheckRegistryActionMatch(RegistryAction action)
    {
        try
        {
            var root = RootKey(action.Hive);

            // Твік, що прибирає цілий ключ (тека «Цей ПК», пункт меню): застосовано = ключа немає
            if (action.DeleteKeyOnApply)
            {
                foreach (var path in ExpandPaths(root, action.KeyPath))
                {
                    using var k = root.OpenSubKey(path);
                    if (k != null) return false;
                }
                return true;
            }

            if (action.ApplyValue == null) return true; // немає що перевіряти

            string expected = ValueToText(action.ApplyValue);
            bool numeric = ResolveKind(action.ValueKind, action.ApplyValue) is RegistryValueKind.DWord or RegistryValueKind.QWord;

            foreach (var path in ExpandPaths(root, action.KeyPath))
            {
                using var key = root.OpenSubKey(path);
                if (key == null) return false;

                object? actual = key.GetValue(action.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (actual == null) return false;

                if (numeric)
                {
                    if (!long.TryParse(ValueToText(actual), NumberStyles.Integer, CultureInfo.InvariantCulture, out long a)) return false;
                    if (!long.TryParse(expected, NumberStyles.Integer, CultureInfo.InvariantCulture, out long e)) return false;
                    if (a != e) return false;
                }
                else
                {
                    if (!string.Equals(expected, ValueToText(actual), StringComparison.OrdinalIgnoreCase)) return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool CheckServiceStatusMatch(ServiceAction action)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{action.ServiceName}");
            if (key == null) return true; // служби немає в системі — твік не стосується цієї машини

            int startType = key.GetValue("Start") is int v ? v : -1;
            if (startType != StartupToStartType(action.ApplyStartup)) return false;

            if (!action.StopOnApply) return true;

            try
            {
                using var sc = new ServiceController(action.ServiceName);
                return sc.Status != ServiceControllerStatus.Running;
            }
            catch
            {
                return true; // статус непідконтрольний (protected service) — не вважаємо невиконаним
            }
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Виконання твіка (Застосування та Відкат)

    public async Task<bool> ExecuteTweakAsync(TweakModel tweak, bool isApply)
    {
        if (tweak.IsBusy) return false;

        tweak.IsBusy = true;
        bool operationOk = false;

        try
        {
            try
            {
                operationOk = await Task.Run(() => ExecuteInternal(tweak, isApply));
            }
            catch (UnauthorizedAccessException ex)
            {
                AppLogger.Log($"Твік {tweak.LocalizedName}: Недостатньо прав. Можливо, потрібні права TrustedInstaller або вимкнення Tamper Protection ({ex.Message})", "ERROR");
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Помилка у твіку {tweak.LocalizedName}: {ex.Message}", "ERROR");
            }
        }
        finally
        {
            tweak.IsBusy = false;
        }

        // Джерело істини — фактичний стан у системі після дії, а не ExitCode процесу.
        bool applied = false;
        try { applied = CheckTweakStatusNative(tweak); } catch { }
        SetAppliedSafe(tweak, applied);

        bool success = isApply ? applied : !applied;

        if (operationOk && !success)
        {
            AppLogger.Log($"Твік {tweak.LocalizedName}: команди виконано, але перевірка стану не підтвердила результат. Можливо, потрібне перезавантаження.", "WARN");
        }
        else if (!operationOk && success)
        {
            AppLogger.Log($"Твік {tweak.LocalizedName}: частину операцій виконано (стан системи вже відповідає цілі).", "WARN");
        }

        if (success)
        {
            AppLogger.Log(isApply
                ? $"Застосовано системний твік: {tweak.LocalizedName}"
                : $"Відновлено початковий стан твіка: {tweak.LocalizedName}", "SUCCESS");
        }
        else
        {
            AppLogger.Log(isApply
                ? $"Не вдалося застосувати твік: {tweak.LocalizedName}"
                : $"Не вдалося відкотити твік: {tweak.LocalizedName}", "ERROR");
        }

        // Оновлення оболонки (іконки, контекстні меню, теми) без перезапуску Explorer
        if (success && tweak.RefreshShell)
        {
            RefreshShell();
        }

        return success;
    }

    private static bool ExecuteInternal(TweakModel tweak, bool isApply)
    {
        bool anyAction = false;
        bool ok = true;

        // v3.0: зовнішній .bat-скрипт має найвищий пріоритет.
        // RegistryActions/ServiceActions у такому твіку — лише швидкий зонд стану.
        if (!string.IsNullOrWhiteSpace(tweak.ScriptPath))
        {
            bool scriptOk = RunScriptChecked(tweak, isApply);
            SaveAppliedStateFlag(tweak.Id, isApply);
            return scriptOk;
        }

        if (tweak.RegistryActions is { Count: > 0 })
        {
            anyAction = true;
            foreach (var action in tweak.RegistryActions)
            {
                if (!ExecuteRegistryAction(tweak.Id, action, isApply)) ok = false;
            }
            return ok;
        }

        if (tweak.ServiceActions is { Count: > 0 })
        {
            anyAction = true;
            foreach (var svc in tweak.ServiceActions)
            {
                if (!ExecuteServiceAction(tweak.Id, svc, isApply)) ok = false;
            }
            return ok;
        }

        string? script = isApply ? tweak.ApplyScript : tweak.RestoreScript;
        if (!string.IsNullOrWhiteSpace(script))
        {
            anyAction = true;
            ok = RunPowerShellChecked(script!);
            return ok;
        }

        if (tweak.CommandAction != null)
        {
            string cmd = isApply ? tweak.CommandAction.ApplyCmd : tweak.CommandAction.RestoreCmd;
            if (!string.IsNullOrWhiteSpace(cmd))
            {
                anyAction = true;
                ok = RunCommandChecked(cmd);
                return ok;
            }
        }

        if (!anyAction)
        {
            AppLogger.Log($"Твік '{tweak.Id}' не має дій для виконання", "WARN");
        }

        return ok;
    }

    #endregion

    #region Реєстр: wildcard-шляхи, снапшоти, видалення ключів

    private static RegistryKey RootKey(string hive) =>
        hive.Equals("HKLM", StringComparison.OrdinalIgnoreCase) ? Registry.LocalMachine : Registry.CurrentUser;

    private static bool ExecuteRegistryAction(string tweakId, RegistryAction action, bool isApply)
    {
        bool ok = true;
        bool anyPath = false;

        foreach (var keyPath in ExpandPaths(RootKey(action.Hive), action.KeyPath))
        {
            anyPath = true;
            if (isApply)
            {
                if (!ApplyRegistryAction(tweakId, action, keyPath)) ok = false;
            }
            else
            {
                if (!RestoreRegistryAction(tweakId, action, keyPath)) ok = false;
            }
        }

        if (!anyPath && action.DeleteKeyOnApply)
        {
            return true; // нічого видаляти — мета вже досягнута
        }

        if (!anyPath)
        {
            AppLogger.Log($"Твік '{tweakId}': жодного підключеного об'єкта за маскою '{action.KeyPath}' — дію пропущено", "WARN");
        }

        return ok;
    }

    /// <summary>
    /// Розкриття wildcard-шляхів (наприклад, ...\Tcpip\Parameters\Interfaces\*),
    /// щоб значення потрапляли в кожен підключ, а не в батьківський ключ.
    /// </summary>
    private static IEnumerable<string> ExpandPaths(RegistryKey root, string keyPath)
    {
        if (string.IsNullOrWhiteSpace(keyPath)) yield break;

        if (!keyPath.Contains('*') && !keyPath.Contains('?'))
        {
            yield return keyPath;
            yield break;
        }

        string[] parts = keyPath.Split('\\');
        int starIndex = Array.FindIndex(parts, p => p.Contains('*') || p.Contains('?'));
        if (starIndex < 0)
        {
            yield return keyPath;
            yield break;
        }

        string basePath = string.Join("\\", parts.Take(starIndex));
        string pattern = parts[starIndex];
        string tail = starIndex + 1 < parts.Length ? string.Join("\\", parts.Skip(starIndex + 1)) : string.Empty;

        using var baseKey = root.OpenSubKey(basePath);
        if (baseKey == null) yield break;

        foreach (var name in baseKey.GetSubKeyNames())
        {
            if (!WildcardMatch(name, pattern)) continue;
            yield return string.IsNullOrEmpty(tail) ? $"{basePath}\\{name}" : $"{basePath}\\{name}\\{tail}";
        }
    }

    private static bool WildcardMatch(string input, string pattern)
    {
        string regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(input, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static bool ApplyRegistryAction(string tweakId, RegistryAction action, string keyPath)
    {
        var root = RootKey(action.Hive);

        try
        {
            if (action.DeleteKeyOnApply)
            {
                root.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
                AppLogger.Log($"Твік '{tweakId}': видалено ключ {action.Hive}\\{keyPath}", "INFO");
                return true;
            }

            if (action.ApplyValue == null)
            {
                AppLogger.Log($"Твік '{tweakId}': для {action.Hive}\\{keyPath} немає ApplyValue — дію пропущено", "WARN");
                return true;
            }

            // 1. Снапшот поточного стану (для точного відкату)
            SaveRegistrySnapshot(tweakId, SnapshotName(keyPath, action.ValueName), CaptureCurrentValue(root, keyPath, action.ValueName));

            // 2. Запис
            var kind = ResolveKind(action.ValueKind, action.ApplyValue);
            object value = ToRegistryValue(kind, action.ApplyValue);

            using var key = root.CreateSubKey(keyPath);
            if (key == null)
            {
                AppLogger.Log($"Твік '{tweakId}': не вдалося створити ключ {action.Hive}\\{keyPath}", "ERROR");
                return false;
            }

            key.SetValue(action.ValueName, value, kind);
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLogger.Log($"Твік '{tweakId}': доступ заборонено для {action.Hive}\\{keyPath}\\{action.ValueName} ({ex.Message})", "ERROR");
            return false;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Твік '{tweakId}': помилка запису {action.Hive}\\{keyPath}\\{action.ValueName} ({ex.Message})", "ERROR");
            return false;
        }
    }

    private static bool RestoreRegistryAction(string tweakId, RegistryAction action, string keyPath)
    {
        var root = RootKey(action.Hive);
        string snapName = SnapshotName(keyPath, action.ValueName);

        try
        {
            if (action.DeleteKeyOnRestore)
            {
                root.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
                return true;
            }

            // 1. Точний відкат зі снапшоту (найвищий пріоритет)
            var snap = LoadRegistrySnapshot(tweakId, snapName);
            if (snap != null)
            {
                RestoreFromSnapshot(root, keyPath, action.ValueName, snap);
                return true;
            }

            // 2. Відкат твіка, який видаляв ключ: відновлюємо порожній ключ
            if (action.DeleteKeyOnApply)
            {
                using var recreated = root.CreateSubKey(keyPath);
                if (recreated != null && action.RestoreValue != null)
                {
                    var k = ResolveKind(action.ValueKind, action.RestoreValue);
                    recreated.SetValue(action.ValueName, ToRegistryValue(k, action.RestoreValue), k);
                }
                return true;
            }

            // 3. Класичні правила з JSON
            if (action.DeleteOnRestore)
            {
                using var key = root.OpenSubKey(keyPath, true);
                key?.DeleteValue(action.ValueName, false);
                return true;
            }

            if (action.RestoreValue != null)
            {
                var kind = ResolveKind(action.ValueKind, action.RestoreValue);
                using var key = root.CreateSubKey(keyPath);
                key?.SetValue(action.ValueName, ToRegistryValue(kind, action.RestoreValue), kind);
                return true;
            }

            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Твік '{tweakId}': помилка відкату {action.Hive}\\{keyPath}\\{action.ValueName} ({ex.Message})", "ERROR");
            return false;
        }
    }

    #region Снапшоти

    private sealed class RegistrySnapshot
    {
        public bool Existed { get; set; }
        public int Kind { get; set; } = (int)RegistryValueKind.String;
        public string? Text { get; set; }
        public string[]? Multi { get; set; }
    }

    private sealed class ServiceSnapshot
    {
        public int Start { get; set; } = -1;
        public int? DelayedAutoStart { get; set; }
    }

    private static string SnapshotName(string keyPath, string valueName) =>
        "r" + Fnv1a(keyPath.ToLowerInvariant() + "|" + valueName.ToLowerInvariant()).ToString("x8", CultureInfo.InvariantCulture);

    private static string ServiceSnapshotName(string serviceName) =>
        "s" + Fnv1a(serviceName.ToLowerInvariant()).ToString("x8", CultureInfo.InvariantCulture);

    private static uint Fnv1a(string s)
    {
        uint hash = 2166136261;
        foreach (char c in s)
        {
            hash ^= c;
            hash *= 16777619;
        }
        return hash;
    }

    private static string SanitizeKeyName(string name)
    {
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars().Concat(new[] { '\\', '/' }));
        var sb = new StringBuilder(name.Length);
        foreach (char c in name) sb.Append(invalid.Contains(c) ? '_' : c);
        return sb.Length == 0 ? "unknown" : sb.ToString();
    }

    private static void SaveRegistrySnapshot(string tweakId, string snapName, RegistrySnapshot snapshot)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(StateRootPath + "\\" + SanitizeKeyName(tweakId));
            key?.SetValue(snapName, JsonSerializer.Serialize(snapshot), RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Не вдалося зберегти снапшот твіка '{tweakId}': {ex.Message}", "WARN");
        }
    }

    private static RegistrySnapshot? LoadRegistrySnapshot(string tweakId, string snapName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StateRootPath + "\\" + SanitizeKeyName(tweakId));
            if (key?.GetValue(snapName) is not string json || string.IsNullOrWhiteSpace(json)) return null;
            return JsonSerializer.Deserialize<RegistrySnapshot>(json);
        }
        catch
        {
            return null;
        }
    }

    private static void SaveServiceSnapshot(string tweakId, string serviceName, ServiceSnapshot snapshot)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(StateRootPath + "\\" + SanitizeKeyName(tweakId));
            key?.SetValue(ServiceSnapshotName(serviceName), JsonSerializer.Serialize(snapshot), RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Не вдалося зберегти снапшот служби '{serviceName}': {ex.Message}", "WARN");
        }
    }

    private static ServiceSnapshot? LoadServiceSnapshot(string tweakId, string serviceName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StateRootPath + "\\" + SanitizeKeyName(tweakId));
            if (key?.GetValue(ServiceSnapshotName(serviceName)) is not string json || string.IsNullOrWhiteSpace(json)) return null;
            return JsonSerializer.Deserialize<ServiceSnapshot>(json);
        }
        catch
        {
            return null;
        }
    }

    private static RegistrySnapshot CaptureCurrentValue(RegistryKey root, string keyPath, string valueName)
    {
        var snapshot = new RegistrySnapshot();
        try
        {
            using var key = root.OpenSubKey(keyPath);
            if (key == null) return snapshot; // ключа не було -> значення не існувало

            object? value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value == null) return snapshot;

            snapshot.Existed = true;
            try { snapshot.Kind = (int)key.GetValueKind(valueName); } catch { }

            switch ((RegistryValueKind)snapshot.Kind)
            {
                case RegistryValueKind.MultiString:
                    snapshot.Multi = value as string[] ?? new[] { ValueToText(value) };
                    break;
                case RegistryValueKind.Binary:
                    snapshot.Text = value is byte[] bytes ? Convert.ToBase64String(bytes) : string.Empty;
                    break;
                case RegistryValueKind.DWord:
                    snapshot.Text = Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                    break;
                case RegistryValueKind.QWord:
                    snapshot.Text = Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                    break;
                default:
                    snapshot.Text = ValueToText(value);
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Не вдалося зняти снапшот {keyPath}\\{valueName}: {ex.Message}", "WARN");
        }

        return snapshot;
    }

    private static void RestoreFromSnapshot(RegistryKey root, string keyPath, string valueName, RegistrySnapshot snapshot)
    {
        if (!snapshot.Existed)
        {
            using var key = root.OpenSubKey(keyPath, true);
            key?.DeleteValue(valueName, false);
            return;
        }

        var kind = (RegistryValueKind)snapshot.Kind;
        object value = kind switch
        {
            RegistryValueKind.MultiString => snapshot.Multi ?? Array.Empty<string>(),
            RegistryValueKind.Binary => SafeFromBase64(snapshot.Text),
            RegistryValueKind.DWord => int.TryParse(snapshot.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : 0,
            RegistryValueKind.QWord => long.TryParse(snapshot.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long q) ? q : 0L,
            _ => snapshot.Text ?? string.Empty
        };

        using var writeKey = root.CreateSubKey(keyPath);
        writeKey?.SetValue(valueName, value, kind);
    }

    private static byte[] SafeFromBase64(string? text)
    {
        try { return Convert.FromBase64String(text ?? string.Empty); }
        catch { return Array.Empty<byte>(); }
    }

    #endregion

    #region Перетворення значень (JsonElement -> реєстр)

    /// <summary>
    /// System.Text.Json матеріалізує object-властивості як JsonElement, тому пряме
    /// Convert.ToInt32(...) кидає InvalidCastException — усе проходить через текст.
    /// </summary>
    private static string ValueToText(object? value)
    {
        switch (value)
        {
            case null:
                return string.Empty;
            case JsonElement je:
                return je.ValueKind switch
                {
                    JsonValueKind.String => je.GetString() ?? string.Empty,
                    JsonValueKind.Number => je.ToString(),
                    JsonValueKind.True => "1",
                    JsonValueKind.False => "0",
                    JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
                    JsonValueKind.Array => string.Join("\n", je.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())),
                    _ => je.ToString()
                };
            case bool b:
                return b ? "1" : "0";
            case string s:
                return s;
            default:
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }

    private static long ValueToLong(object? value)
    {
        string text = ValueToText(value).Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long hex))
        {
            return hex;
        }

        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : 0L;
    }

    private static RegistryValueKind ResolveKind(string? kindName, object? value)
    {
        string kind = (kindName ?? string.Empty).Trim();
        if (kind.Equals("DWord", StringComparison.OrdinalIgnoreCase)) return RegistryValueKind.DWord;
        if (kind.Equals("QWord", StringComparison.OrdinalIgnoreCase)) return RegistryValueKind.QWord;
        if (kind.Equals("MultiString", StringComparison.OrdinalIgnoreCase)) return RegistryValueKind.MultiString;
        if (kind.Equals("Binary", StringComparison.OrdinalIgnoreCase)) return RegistryValueKind.Binary;
        if (kind.Equals("ExpandString", StringComparison.OrdinalIgnoreCase)) return RegistryValueKind.ExpandString;

        // String: %VAR% -> REG_EXPAND_SZ, інакше REG_SZ
        string text = ValueToText(value);
        return text.Contains('%') ? RegistryValueKind.ExpandString : RegistryValueKind.String;
    }

    private static object ToRegistryValue(RegistryValueKind kind, object? value)
    {
        switch (kind)
        {
            case RegistryValueKind.DWord:
                return unchecked((int)ValueToLong(value));
            case RegistryValueKind.QWord:
                return ValueToLong(value);
            case RegistryValueKind.MultiString:
                {
                    string text = ValueToText(value);
                    return text.Length == 0 ? Array.Empty<string>() : text.Split('\n');
                }
            case RegistryValueKind.Binary:
                return SafeFromBase64(ValueToText(value));
            default:
                return ValueToText(value);
        }
    }

    #endregion

    #endregion

    #region Служби Windows

    private static int StartupToStartType(string? startup) => (startup ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "automatic" or "auto" => 2,
        "manual" => 3,
        "disabled" => 4,
        _ => 3
    };

    private static bool ExecuteServiceAction(string tweakId, ServiceAction action, bool isApply)
    {
        string keyPath = $@"SYSTEM\CurrentControlSet\Services\{action.ServiceName}";

        try
        {
            using var svcKey = Registry.LocalMachine.OpenSubKey(keyPath, true);
            if (svcKey == null)
            {
                // Служби немає (інший вендор/редакція ОС) — твік не стосується цієї машини
                AppLogger.Log($"Служба '{action.ServiceName}' відсутня в системі — твік '{tweakId}' пропущено", "INFO");
                return true;
            }

            if (isApply)
            {
                SaveServiceSnapshot(tweakId, action.ServiceName, new ServiceSnapshot
                {
                    Start = svcKey.GetValue("Start") is int s ? s : -1,
                    DelayedAutoStart = svcKey.GetValue("DelayedAutoStart") is int d ? d : null
                });

                svcKey.SetValue("Start", StartupToStartType(action.ApplyStartup), RegistryValueKind.DWord);

                // Служби, які ОС тримає "automatic delayed", стартують із затримкою —
                // примусово знімаємо прапорець, якщо потрібен звичайний Automatic
                if (StartupToStartType(action.ApplyStartup) == 2 && svcKey.GetValue("DelayedAutoStart") is int delayed && delayed != 0)
                {
                    svcKey.SetValue("DelayedAutoStart", 0, RegistryValueKind.DWord);
                }
            }
            else
            {
                var snapshot = LoadServiceSnapshot(tweakId, action.ServiceName);
                if (snapshot is { Start: >= 0 })
                {
                    svcKey.SetValue("Start", snapshot.Start, RegistryValueKind.DWord);
                    if (snapshot.DelayedAutoStart is int das)
                    {
                        svcKey.SetValue("DelayedAutoStart", das, RegistryValueKind.DWord);
                    }
                }
                else
                {
                    svcKey.SetValue("Start", StartupToStartType(action.RestoreStartup), RegistryValueKind.DWord);
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLogger.Log($"Служба {action.ServiceName}: доступ заборонено ({ex.Message})", "ERROR");
            return false;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Служба {action.ServiceName}: помилка встановлення типу запуску ({ex.Message})", "ERROR");
            return false;
        }

        // Зміна стану "зараз" — не критично: тип запуску вже застосовано,
        // ОС підхопить його після перезавантаження навіть якщо Stop/Start не вдався.
        try { ToggleServiceRunning(action, isApply); }
        catch (Exception ex)
        {
            AppLogger.Log($"Служба {action.ServiceName}: не вдалося {(isApply ? "зупинити" : "запустити")} негайно ({ex.Message}) — застосується після перезавантаження", "WARN");
        }

        return true;
    }

    private static void ToggleServiceRunning(ServiceAction action, bool isApply)
    {
        TimeSpan timeout = TimeSpan.FromSeconds(ServiceTimeoutSeconds);
        using var sc = new ServiceController(action.ServiceName);

        if (isApply && action.StopOnApply && sc.Status != ServiceControllerStatus.Stopped)
        {
            sc.Stop();
            try { sc.WaitForStatus(ServiceControllerStatus.Stopped, timeout); } catch { }
        }
        else if (!isApply && action.StartOnRestore && sc.Status != ServiceControllerStatus.Running)
        {
            sc.Start();
            try { sc.WaitForStatus(ServiceControllerStatus.Running, timeout); } catch { }
        }
    }

    #endregion

    #region Легкі допоміжні виклики системних CLI процесів

    private static bool RunCommandChecked(string command)
    {
        // Фаза 7.3: тихий режим (гра/Game Mode) — нові CLI не запускаємо; чекаємо виходу
        // без polling. Після захоплення шлюзу повторно перевіряємо стан (гра могла початися).
        while (true)
        {
            if (!AppActivityCoordinator.WaitUntilNotQuiet(CliQuietWaitTimeout))
            {
                AppLogger.Log("CLI-операцію скасовано: quiet-режим триває занадто довго (запобіжник)", "WARN");
                return false;
            }

            ProcessGate.Wait();
            if (!AppActivityCoordinator.IsQuietMode) break;
            ProcessGate.Release();
        }

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

            if (proc == null) return false;

            if (!proc.WaitForExit(CmdExecTimeoutMs))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                AppLogger.Log($"Команда перевищила таймаут {CmdExecTimeoutMs / 1000} с: {command}", "ERROR");
                return false;
            }

            if (proc.ExitCode != 0)
            {
                AppLogger.Log($"Команда завершилась з кодом {proc.ExitCode}: {command}", "WARN");
            }

            return proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Не вдалося виконати команду '{command}': {ex.Message}", "ERROR");
            return false;
        }
        finally { ProcessGate.Release(); }
    }

    private static string RunCommandCapture(string command)
    {
        // Фаза 7.3: тихий режим (гра/Game Mode) — нові CLI не запускаємо; чекаємо виходу
        // без polling. Після захоплення шлюзу повторно перевіряємо стан (гра могла початися).
        while (true)
        {
            if (!AppActivityCoordinator.WaitUntilNotQuiet(CliQuietWaitTimeout))
            {
                AppLogger.Log("CLI-операцію скасовано: quiet-режим триває занадто довго (запобіжник)", "WARN");
                return string.Empty;
            }

            ProcessGate.Wait();
            if (!AppActivityCoordinator.IsQuietMode) break;
            ProcessGate.Release();
        }

        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c chcp 65001 >nul & {command}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (proc == null) return string.Empty;

            string outStr = proc.StandardOutput.ReadToEnd();
            _ = proc.StandardError.ReadToEnd();

            if (!proc.WaitForExit(CmdCaptureTimeoutMs))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return string.Empty;
            }

            return outStr;
        }
        catch { return string.Empty; }
        finally { ProcessGate.Release(); }
    }

    #region Зовнішні .bat-скрипти твіків (Scripts\Tweaks)

    private const int ScriptTimeoutMs = 30_000;
    private const string AppliedFlagRoot = @"Software\MASLOOPTIMIZER\AppliedTweaks";

    /// <summary>
    /// Виконує Scripts\Tweaks\<ScriptPath> з аргументом apply|restore у фоновому cmd.exe:
    /// без вікна, з перехопленням stdout/stderr та таймаутом 30 с.
    /// </summary>
    private static bool RunScriptChecked(TweakModel tweak, bool isApply)
    {
        string scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts", "Tweaks", tweak.ScriptPath);
        string verb = isApply ? "apply" : "restore";

        if (!File.Exists(scriptPath))
        {
            AppLogger.Log($"Твік '{tweak.Id}': скрипт не знайдено — {scriptPath}", "ERROR");
            return false;
        }

        // Фаза 7.3: під час гри/Game Mode нові CLI не спавняться; чекаємо виходу без polling.
        while (true)
        {
            if (!AppActivityCoordinator.WaitUntilNotQuiet(CliQuietWaitTimeout))
            {
                AppLogger.Log("Запуск .bat-скрипта скасовано: quiet-режим триває занадто довго (запобіжник)", "WARN");
                return false;
            }

            ProcessGate.Wait();
            if (!AppActivityCoordinator.IsQuietMode) break;
            ProcessGate.Release();
        }

        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{scriptPath}\" {verb}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            });

            if (proc == null)
            {
                AppLogger.Log($"Твік '{tweak.Id}': не вдалося запустити {tweak.ScriptPath}", "ERROR");
                return false;
            }

            var outTask = proc.StandardOutput.ReadToEndAsync();
            var errTask = proc.StandardError.ReadToEndAsync();

            if (!proc.WaitForExit(ScriptTimeoutMs))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                AppLogger.Log($"Твік '{tweak.Id}': скрипт {tweak.ScriptPath} перевищив таймаут {ScriptTimeoutMs / 1000} с", "ERROR");
                return false;
            }

            try { Task.WaitAll(new Task[] { outTask, errTask }, 2000); } catch { }

            string stdout = outTask.Status == TaskStatus.RanToCompletion ? outTask.Result.Trim() : string.Empty;
            string stderr = errTask.Status == TaskStatus.RanToCompletion ? errTask.Result.Trim() : string.Empty;

            if (stdout.Length > 0) AppLogger.Log($"Скрипт {tweak.ScriptPath} [{verb}]: {stdout}", "INFO");
            if (stderr.Length > 0) AppLogger.Log($"Скрипт {tweak.ScriptPath} [{verb}] (stderr): {stderr}", "WARN");
            if (proc.ExitCode != 0) AppLogger.Log($"Скрипт {tweak.ScriptPath} завершився з кодом {proc.ExitCode}", "WARN");

            return proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Помилка запуску {tweak.ScriptPath}: {ex.Message}", "ERROR");
            return false;
        }
        finally
        {
            ProcessGate.Release();
        }
    }

    #endregion

    private static string BuildPowerShellArguments(string script)
    {
        // -EncodedCommand (Base64/UTF-16LE) — повністю виключає проблеми з лапками,
        // апострофами та переносами рядків у багаторядкових скриптах.
        byte[] bytes = Encoding.Unicode.GetBytes(script);
        string encoded = Convert.ToBase64String(bytes);
        return $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}";
    }

    private static bool RunPowerShellChecked(string script)
    {
        // Фаза 7.3: тихий режим (гра/Game Mode) — нові CLI не запускаємо; чекаємо виходу
        // без polling. Після захоплення шлюзу повторно перевіряємо стан (гра могла початися).
        while (true)
        {
            if (!AppActivityCoordinator.WaitUntilNotQuiet(CliQuietWaitTimeout))
            {
                AppLogger.Log("CLI-операцію скасовано: quiet-режим триває занадто довго (запобіжник)", "WARN");
                return false;
            }

            ProcessGate.Wait();
            if (!AppActivityCoordinator.IsQuietMode) break;
            ProcessGate.Release();
        }

        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = BuildPowerShellArguments(script),
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            if (proc == null) return false;

            if (!proc.WaitForExit(PowerShellTimeoutMs))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                AppLogger.Log("PowerShell-скрипт перевищив таймаут", "ERROR");
                return false;
            }

            if (proc.ExitCode != 0)
            {
                AppLogger.Log($"PowerShell-скрипт завершився з кодом {proc.ExitCode}", "WARN");
            }

            return proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Помилка запуску PowerShell: {ex.Message}", "ERROR");
            return false;
        }
        finally { ProcessGate.Release(); }
    }

    private static string RunPowerShellCapture(string script)
    {
        // Фаза 7.3: тихий режим (гра/Game Mode) — нові CLI не запускаємо; чекаємо виходу
        // без polling. Після захоплення шлюзу повторно перевіряємо стан (гра могла початися).
        while (true)
        {
            if (!AppActivityCoordinator.WaitUntilNotQuiet(CliQuietWaitTimeout))
            {
                AppLogger.Log("CLI-операцію скасовано: quiet-режим триває занадто довго (запобіжник)", "WARN");
                return string.Empty;
            }

            ProcessGate.Wait();
            if (!AppActivityCoordinator.IsQuietMode) break;
            ProcessGate.Release();
        }
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = BuildPowerShellArguments(script),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (proc == null) return string.Empty;

            string outStr = proc.StandardOutput.ReadToEnd() ?? string.Empty;
            _ = proc.StandardError.ReadToEnd();

            if (!proc.WaitForExit(CmdCaptureTimeoutMs))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return string.Empty;
            }

            return outStr.Trim();
        }
        catch { return string.Empty; }
        finally { ProcessGate.Release(); }
    }

    /// <summary>CheckScript має повернути True/1/yes — тоді твік вважається застосованим.</summary>
    private static bool RunPowerShellBool(string script)
    {
        string result = RunPowerShellCapture(script);
        if (string.IsNullOrWhiteSpace(result)) return false;

        string normalized = result.Trim().Trim('"');
        return normalized.Equals("True", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Оновлення оболонки Windows

    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    /// <summary>
    /// Повідомляє Explorer про зміну налаштувань (WM_SETTINGCHANGE + SHChangeNotify),
    /// щоб UI-твіки застосовувались без повторного входу в систему.
    /// </summary>
    public static void RefreshShell()
    {
        try
        {
            var broadcast = new IntPtr(0xFFFF);
            SendMessageTimeout(broadcast, WM_SETTINGCHANGE, IntPtr.Zero, "Environment", SMTO_ABORTIFHUNG, 3000, out _);
            SendMessageTimeout(broadcast, WM_SETTINGCHANGE, IntPtr.Zero, "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer", SMTO_ABORTIFHUNG, 3000, out _);
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Не вдалося оновити оболонку Windows: {ex.Message}", "WARN");
        }
    }

    #endregion
}
