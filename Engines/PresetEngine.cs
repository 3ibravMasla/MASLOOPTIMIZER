using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MASLOOPTIMIZER;

public class MasloProfileConfig
{
    [JsonPropertyName("Version")]
    public string Version { get; set; } = "0.4.6";

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
            AppPaths.EnsureDirectories();
            return AppPaths.Presets;
        }
    }

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

    public static (bool Success, string Message) ExportFullProfile(
        IEnumerable<TweakModel> tweaks,
        IEnumerable<DebloatItem> debloatItems,
        string profileName = "Власний профіль",
        string description = "Збережена конфігурація системи")
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
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
                return (true, LocalizationManager.Instance.Format("Dialogs.PresetSaveDone", appliedTweaks, removedApps));
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Помилка експорту конфігурації: {ex.Message}", "ERROR");
                return (false, LocalizationManager.Instance.Format("Dialogs.PresetSaveError", ex.Message));
            }
        }

        return (false, LocalizationManager.Instance["Dialogs.PresetExportCancelled"]);
    }

    public static async Task<(bool Success, string Message)> ImportAndApplyProfileAsync(
        IEnumerable<TweakModel> tweaks,
        IEnumerable<DebloatItem> debloatItems,
        Action<int, string>? progressCallback = null)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Maslo Preset (*.json)|*.json",
            InitialDirectory = PresetsDirectory
        };

        if (dlg.ShowDialog() == true)
        {
            return await ApplyProfileFromFileAsync(dlg.FileName, tweaks, debloatItems, progressCallback);
        }

        return (false, LocalizationManager.Instance["Dialogs.PresetImportCancelled"]);
    }

    public static async Task<(bool Success, string Message)> ApplyProfileFromFileAsync(
        string filePath,
        IEnumerable<TweakModel> tweaks,
        IEnumerable<DebloatItem> debloatItems,
        Action<int, string>? progressCallback = null)
    {
        try
        {
            if (!File.Exists(filePath)) return (false, LocalizationManager.Instance["Dialogs.PresetFileNotFound"]);

            string json = await File.ReadAllTextAsync(filePath);
            var config = JsonSerializer.Deserialize<MasloProfileConfig>(json);

            if (config == null || config.TweakStates == null)
            {
                return (false, LocalizationManager.Instance["Dialogs.PresetCorrupted"]);
            }

            return await ApplyConfigObjectAsync(config, tweaks, debloatItems, progressCallback);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Помилка читання пресету: {ex.Message}", "ERROR");
            return (false, LocalizationManager.Instance.Format("Dialogs.PresetImportError", ex.Message));
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

        // Лічильник реальних операцій: лише ті твіки, де конфігурація відрізняється від поточного стану
        int pendingTweaks = tweakList.Count(t =>
            config.TweakStates.TryGetValue(t.Id, out bool shouldApply) && shouldApply != t.IsApplied);
        int totalOperations = pendingTweaks + (config.DebloatUninstalledIds?.Count ?? 0);
        int currentOp = 0;

        int appliedCount = 0;
        int restoredCount = 0;
        int debloatedCount = 0;

        foreach (var tweak in tweakList)
        {
            if (!config.TweakStates.TryGetValue(tweak.Id, out bool shouldBeApplied)) continue;

            if (shouldBeApplied && !tweak.IsApplied)
            {
                currentOp++;
                int pct = totalOperations > 0 ? (int)((currentOp / (double)totalOperations) * 100) : 100;
                progressCallback?.Invoke(pct, LocalizationManager.Instance.Format("Dialogs.Optimizing", tweak.LocalizedName));
                bool ok = await TweakEngine.Instance.ExecuteTweakAsync(tweak, isApply: true);
                if (ok) appliedCount++;
            }
            else if (!shouldBeApplied && tweak.IsApplied)
            {
                currentOp++;
                int pct = totalOperations > 0 ? (int)((currentOp / (double)totalOperations) * 100) : 100;
                progressCallback?.Invoke(pct, LocalizationManager.Instance.Format("Tweak.Restoring", tweak.LocalizedName));
                bool ok = await TweakEngine.Instance.ExecuteTweakAsync(tweak, isApply: false);
                if (ok) restoredCount++;
            }
        }

        if (config.DebloatUninstalledIds != null)
        {
            foreach (var debloatId in config.DebloatUninstalledIds)
            {
                var targetApp = debloatList.FirstOrDefault(d =>
                    (!string.IsNullOrWhiteSpace(d.Id) && d.Id.Equals(debloatId, StringComparison.OrdinalIgnoreCase)) ||
                    d.Name.Equals(debloatId, StringComparison.OrdinalIgnoreCase));

                if (targetApp != null && targetApp.IsInstalled)
                {
                    currentOp++;
                    int pct = totalOperations > 0 ? (int)((currentOp / (double)totalOperations) * 100) : 100;
                    progressCallback?.Invoke(pct, LocalizationManager.Instance.Format("Dialogs.DebloatingItem", targetApp.Name));
                    bool ok = await DebloatEngine.UninstallPackageAsync(targetApp);
                    if (ok) debloatedCount++;
                }
            }
        }

        progressCallback?.Invoke(100, LocalizationManager.Instance["Dialogs.ConfigDeployed"]);
        string resMsg = LocalizationManager.Instance.Format("Dialogs.ProfileApplied", config.Name, appliedCount, restoredCount, debloatedCount);
        AppLogger.Log(resMsg, "SUCCESS");
        return (true, resMsg);
    }

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
                progressCallback?.Invoke(pct, LocalizationManager.Instance.Format("Dialogs.MasloPackProgress", tweak.LocalizedName));

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
                progressCallback?.Invoke(pct, LocalizationManager.Instance.Format("Dialogs.DebloatingItem", bloat.Name));

                bool ok = await DebloatEngine.UninstallPackageAsync(bloat);
                if (ok) removedApps++;
            }

            progressCallback?.Invoke(100, LocalizationManager.Instance["Dialogs.MasloPackDone"]);
            string msg = LocalizationManager.Instance.Format("Dialogs.MasloPackResult", appliedTweaks, removedApps);
            AppLogger.Log(msg, "SUCCESS");
            return (true, msg);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Помилка Maslo Pack: {ex.Message}", "ERROR");
            return (false, LocalizationManager.Instance.Format("Dialogs.PresetError", ex.Message));
        }
    }
}