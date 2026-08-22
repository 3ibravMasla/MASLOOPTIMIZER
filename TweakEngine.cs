using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace MASLOOPTIMIZER;

public class TweakEngine
{
    private static readonly Lazy<TweakEngine> _instance = new(() => new TweakEngine());
    public static TweakEngine Instance => _instance.Value;

    public List<TweakModel> AllTweaks { get; private set; } = new();

    #region Завантаження бази твіків

    public void LoadTweaks()
    {
        string jsonContent = string.Empty;

        // 1. Спроба завантажити з вбудованих ресурсів збірки
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
                    AllTweaks = bundle.Tweaks.OrderBy(t => t.Name).ToList();
                }
            }
            catch { }
        }
    }

    #endregion

    #region Швидкісна паралельна перевірка статусу системи

    /// <summary>
    /// Паралельно перевіряє активність усіх твіків у системі без зависань UI
    /// </summary>
    public async Task EvaluateAllStatusesAsync(Action<int, string>? progressCallback = null)
    {
        if (AllTweaks.Count == 0) return;

        await Task.Run(async () =>
        {
            int total = AllTweaks.Count;
            int completed = 0;

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8)
            };

            await Parallel.ForEachAsync(AllTweaks, parallelOptions, async (tweak, token) =>
            {
                if (!string.IsNullOrWhiteSpace(tweak.CheckScript))
                {
                    tweak.IsApplied = await Task.Run(() => ExecuteCheckScriptSafe(tweak.CheckScript));
                }

                int currentCount = System.Threading.Interlocked.Increment(ref completed);
                int percent = (int)((currentCount / (double)total) * 100);
                progressCallback?.Invoke(percent, tweak.Name);
            });
        });
    }

    /// <summary>
    /// Безпечне виконання скрипту перевірки з надійною інтерпретацією булевих результатів
    /// </summary>
    private static bool ExecuteCheckScriptSafe(string checkScript)
    {
        try
        {
            using var runspace = RunspaceFactory.CreateRunspace();
            runspace.Open();

            using var ps = PowerShell.Create();
            ps.Runspace = runspace;

            // Обгортаємо у блок вимкнення виводу зайвих помилок
            string wrappedScript = $@"
                $ErrorActionPreference = 'SilentlyContinue'
                try {{
                    {checkScript}
                }} catch {{
                    $false
                }}
            ";

            ps.AddScript(wrappedScript);
            var results = ps.Invoke();

            if (results != null && results.Count > 0)
            {
                foreach (var res in results)
                {
                    if (res != null && ConvertToBoolean(res.BaseObject))
                    {
                        return true;
                    }
                }
            }
        }
        catch { }

        return false;
    }

    /// <summary>
    /// Універсальний конвертер результатів PowerShell у чистий boolean
    /// </summary>
    private static bool ConvertToBoolean(object? obj)
    {
        if (obj == null) return false;

        if (obj is bool b) return b;

        if (obj is int i) return i != 0;
        if (obj is long l) return l != 0;
        if (obj is byte by) return by != 0;
        if (obj is short s) return s != 0;
        if (obj is uint ui) return ui != 0;

        string str = obj.ToString()?.Trim() ?? string.Empty;
        if (string.Equals(str, "true", StringComparison.OrdinalIgnoreCase) || str == "1")
            return true;

        if (bool.TryParse(str, out bool parsedBool))
            return parsedBool;

        return false;
    }

    #endregion

    #region Застосування та Відкат окремого твіка

    public async Task<bool> ExecuteTweakAsync(TweakModel tweak, bool isApply)
    {
        tweak.IsBusy = true;
        string script = isApply ? tweak.ApplyScript : tweak.RestoreScript;

        if (string.IsNullOrWhiteSpace(script))
        {
            tweak.IsBusy = false;
            return false;
        }

        bool success = await Task.Run(() =>
        {
            try
            {
                using var runspace = RunspaceFactory.CreateRunspace();
                runspace.Open();

                using var ps = PowerShell.Create();
                ps.Runspace = runspace;

                string executionScript = $@"
                    $ErrorActionPreference = 'SilentlyContinue'
                    {script}
                ";

                ps.AddScript(executionScript);
                ps.Invoke();

                return true;
            }
            catch
            {
                return false;
            }
        });

        // Миттєва перевірка нового стану після виконання
        if (!string.IsNullOrWhiteSpace(tweak.CheckScript))
        {
            tweak.IsApplied = await Task.Run(() => ExecuteCheckScriptSafe(tweak.CheckScript));
        }
        else
        {
            tweak.IsApplied = isApply;
        }

        tweak.IsBusy = false;
        return success;
    }

    #endregion
}