using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Win32;

// Явні аліаси для усунення конфлікту між WPF та WinForms
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace MASLOOPTIMIZER;

public class MasloProfileConfig
{
    [JsonPropertyName("Version")]
    public string Version { get; set; } = "0.3.2";

    [JsonPropertyName("Name")]
    public string Name { get; set; } = "Мій профіль";

    [JsonPropertyName("Description")]
    public string Description { get; set; } = "Знімок поточної оптимізації MASLOOPTIMIZER";

    [JsonPropertyName("Author")]
    public string Author { get; set; } = "3ibravMasla";

    [JsonPropertyName("CreatedAt")]
    public string CreatedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    [JsonPropertyName("MachineName")]
    public string MachineName { get; set; } = Environment.MachineName;

    [JsonPropertyName("TweakStates")]
    public Dictionary<string, bool> TweakStates { get; set; } = new();

    [JsonPropertyName("DebloatUninstalledIds")]
    public List<string> DebloatUninstalledIds { get; set; } = new();
}

public class PresetItemInfo
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName => Path.GetFileName(FilePath);
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int ActiveTweaksCount { get; set; }
    public int RemovedDebloatCount { get; set; }
    public string FormattedDate => CreatedAt.ToString("yyyy-MM-dd HH:mm");
    public string FileSizeFormatted { get; set; } = "0 КБ";
}

public static class PresetEngine
{
    public static string PresetsDirectory
    {
        get
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "presets");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }
    }

    #region Зчитування списку збережених пресетів

    public static List<PresetItemInfo> GetAvailablePresets()
    {
        var list = new List<PresetItemInfo>();

        try
        {
            if (!Directory.Exists(PresetsDirectory)) return list;

            var files = Directory.GetFiles(PresetsDirectory, "*.json");
            foreach (var file in files)
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var cfg = JsonSerializer.Deserialize<MasloProfileConfig>(json);
                    var fileInfo = new FileInfo(file);

                    if (cfg != null)
                    {
                        list.Add(new PresetItemInfo
                        {
                            FilePath = file,
                            Name = string.IsNullOrWhiteSpace(cfg.Name) ? Path.GetFileNameWithoutExtension(file) : cfg.Name,
                            Description = cfg.Description,
                            Author = cfg.Author,
                            CreatedAt = fileInfo.CreationTime,
                            ActiveTweaksCount = cfg.TweakStates?.Count(x => x.Value) ?? 0,
                            RemovedDebloatCount = cfg.DebloatUninstalledIds?.Count ?? 0,
                            FileSizeFormatted = $"{fileInfo.Length / 1024.0:N1} КБ"
                        });
                    }
                }
                catch { }
            }
        }
        catch { }

        return list.OrderByDescending(p => p.CreatedAt).ToList();
    }

    #endregion

    #region Експорт профілю

    public static (bool Success, string Message) ExportFullProfile(
        IEnumerable<TweakModel> tweaks,
        IEnumerable<DebloatItem> debloatItems,
        string profileName = "Власний профіль",
        string description = "Збережена конфігурація робочої системи")
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Maslo Preset (*.json)|*.json",
            FileName = $"MasloConfig_{DateTime.Now:yyyy-MM-dd_HH-mm}.json",
            InitialDirectory = PresetsDirectory
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                var config = new MasloProfileConfig
                {
                    Name = profileName,
                    Description = description,
                    CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };

                foreach (var t in tweaks)
                {
                    config.TweakStates[t.Id] = t.IsApplied;
                }

                foreach (var d in debloatItems.Where(d => !d.IsInstalled))
                {
                    config.DebloatUninstalledIds.Add(string.IsNullOrWhiteSpace(d.Id) ? d.Name : d.Id);
                }

                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(dlg.FileName, json);

                int appliedTweaks = config.TweakStates.Count(x => x.Value);
                int removedApps = config.DebloatUninstalledIds.Count;

                AppLogger.Log($"Експортовано пресет: {Path.GetFileName(dlg.FileName)} ({appliedTweaks} твіків)", "SUCCESS");
                return (true, $"Конфігурацію збережено ({appliedTweaks} активних твіків, {removedApps} видалених UWP)!");
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Помилка експорту конфігурації: {ex.Message}", "ERROR");
                return (false, $"Помилка збереження: {ex.Message}");
            }
        }

        return (false, "Експорт скасовано.");
    }

    #endregion

    #region Імпорт та застосування

    public static async Task<(bool Success, string Message)> ImportAndApplyProfileAsync(
        IEnumerable<TweakModel> tweaks,
        IEnumerable<DebloatItem> debloatItems,
        Action<int, string>? progressCallback = null)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Maslo Preset (*.json)|*.json",
            InitialDirectory = PresetsDirectory
        };

        if (dlg.ShowDialog() == true)
        {
            return await ApplyProfileFromFileAsync(dlg.FileName, tweaks, debloatItems, progressCallback);
        }

        return (false, "Імпорт скасовано.");
    }

    public static async Task<(bool Success, string Message)> ApplyProfileFromFileAsync(
        string filePath,
        IEnumerable<TweakModel> tweaks,
        IEnumerable<DebloatItem> debloatItems,
        Action<int, string>? progressCallback = null)
    {
        try
        {
            if (!File.Exists(filePath)) return (false, "Файл пресету не знайдено.");

            string json = await File.ReadAllTextAsync(filePath);
            var config = JsonSerializer.Deserialize<MasloProfileConfig>(json);

            if (config == null || config.TweakStates == null)
            {
                return (false, "Файл конфігурації пошкоджений або має невірну структуру.");
            }

            return await ApplyConfigObjectAsync(config, tweaks, debloatItems, progressCallback);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Помилка читання пресету: {ex.Message}", "ERROR");
            return (false, $"Помилка імпорту: {ex.Message}");
        }
    }

    private static async Task<(bool Success, string Message)> ApplyConfigObjectAsync(
        MasloProfileConfig config,
        IEnumerable<TweakModel> tweaks,
        IEnumerable<DebloatItem> debloatItems,
        Action<int, string>? progressCallback = null)
    {
        var tweakList = tweaks.ToList();
        var debloatList = debloatItems.ToList();
        int totalOperations = tweakList.Count + (config.DebloatUninstalledIds?.Count ?? 0);
        int currentOp = 0;

        int appliedCount = 0;
        int restoredCount = 0;
        int debloatedCount = 0;

        foreach (var tweak in tweakList)
        {
            currentOp++;
            int pct = totalOperations > 0 ? (int)((currentOp / (double)totalOperations) * 100) : 100;

            if (config.TweakStates.TryGetValue(tweak.Id, out bool shouldBeApplied))
            {
                if (shouldBeApplied && !tweak.IsApplied)
                {
                    progressCallback?.Invoke(pct, $"Оптимізація: {tweak.Name}");
                    bool ok = await TweakEngine.Instance.ExecuteTweakAsync(tweak, isApply: true);
                    if (ok) appliedCount++;
                }
                else if (!shouldBeApplied && tweak.IsApplied)
                {
                    progressCallback?.Invoke(pct, $"Відновлення: {tweak.Name}");
                    bool ok = await TweakEngine.Instance.ExecuteTweakAsync(tweak, isApply: false);
                    if (ok) restoredCount++;
                }
            }
        }

        if (config.DebloatUninstalledIds != null)
        {
            foreach (var debloatId in config.DebloatUninstalledIds)
            {
                currentOp++;
                int pct = totalOperations > 0 ? (int)((currentOp / (double)totalOperations) * 100) : 100;

                var targetApp = debloatList.FirstOrDefault(d =>
                    (!string.IsNullOrWhiteSpace(d.Id) && d.Id.Equals(debloatId, StringComparison.OrdinalIgnoreCase)) ||
                    d.Name.Equals(debloatId, StringComparison.OrdinalIgnoreCase));

                if (targetApp != null && targetApp.IsInstalled)
                {
                    progressCallback?.Invoke(pct, $"Деблоат: {targetApp.Name}...");
                    bool ok = await DebloatEngine.UninstallPackageAsync(targetApp);
                    if (ok) debloatedCount++;
                }
            }
        }

        progressCallback?.Invoke(100, "Конфігурацію розгорнуто!");
        string resMsg = $"Профіль '{config.Name}' застосовано! (+{appliedCount} оптимізовано, -{restoredCount} відновлено, {debloatedCount} UWP видалено).";
        AppLogger.Log(resMsg, "SUCCESS");
        return (true, resMsg);
    }

    #endregion

    #region Фірмовий Maslo Pack

    public static async Task<(bool Success, string Message)> ApplyMasloSignaturePackAsync(
        IEnumerable<TweakModel> tweaks,
        IEnumerable<DebloatItem> debloatItems,
        Action<int, string>? progressCallback = null)
    {
        try
        {
            string localSignaturePath = Path.Combine(PresetsDirectory, "maslo_signature.json");
            if (File.Exists(localSignaturePath))
            {
                return await ApplyProfileFromFileAsync(localSignaturePath, tweaks, debloatItems, progressCallback);
            }

            var assembly = Assembly.GetExecutingAssembly();
            var resName = assembly.GetManifestResourceNames()
                .FirstOrDefault(r => r.EndsWith("maslo_signature.json", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(resName))
            {
                using var stream = assembly.GetManifestResourceStream(resName);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    string json = await reader.ReadToEndAsync();
                    var cfg = JsonSerializer.Deserialize<MasloProfileConfig>(json);
                    if (cfg != null)
                    {
                        return await ApplyConfigObjectAsync(cfg, tweaks, debloatItems, progressCallback);
                    }
                }
            }

            // Стандартна поведінка при відсутності файлу
            var targetTweaks = tweaks.Where(t => t.Risk == "Safe" || t.Risk == "UI").ToList();
            var targetBloat = debloatItems.Where(d =>
                d.IsInstalled && (
                    d.Category == "ШІ & Телеметрія" ||
                    d.Category == "Новини & Віджети" ||
                    d.Id == "uwp_solitaire" ||
                    d.Id == "uwp_clipchamp" ||
                    d.Id == "uwp_promostubs"
                )).ToList();

            int total = targetTweaks.Count + targetBloat.Count;
            int current = 0;
            int appliedTweaks = 0;
            int removedApps = 0;

            foreach (var tweak in targetTweaks)
            {
                current++;
                int pct = (int)((current / (double)total) * 100);
                progressCallback?.Invoke(pct, $"Maslo Pack: {tweak.Name}");

                if (!tweak.IsApplied)
                {
                    bool ok = await TweakEngine.Instance.ExecuteTweakAsync(tweak, isApply: true);
                    if (ok) appliedTweaks++;
                }
            }

            foreach (var bloat in targetBloat)
            {
                current++;
                int pct = (int)((current / (double)total) * 100);
                progressCallback?.Invoke(pct, $"Деблоат: {bloat.Name}...");

                bool ok = await DebloatEngine.UninstallPackageAsync(bloat);
                if (ok) removedApps++;
            }

            progressCallback?.Invoke(100, "1-Click Safe Maslo Pack успішно застосовано!");
            string msg = $"Maslo Pack активовано! (+{appliedTweaks} перевірених твіків, -{removedApps} додатків).";
            AppLogger.Log(msg, "SUCCESS");
            return (true, msg);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Помилка Maslo Pack: {ex.Message}", "ERROR");
            return (false, $"Помилка: {ex.Message}");
        }
    }

    #endregion
}