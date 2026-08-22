using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;

// Явні аліаси для діалогів WPF
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace MASLOOPTIMIZER;

public class MasloProfileConfig
{
    public string Version { get; set; } = "0.3.1";
    public string CreatedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    public string Author { get; set; } = "3ibravMasla";
    public string MachineName { get; set; } = Environment.MachineName;
    public Dictionary<string, bool> TweakStates { get; set; } = new();
    public List<string> DebloatUninstalledIds { get; set; } = new();
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

    public static (bool Success, string Message) ExportFullProfile(
        IEnumerable<TweakModel> tweaks, 
        IEnumerable<DebloatItem> debloatItems)
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
                var config = new MasloProfileConfig();

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

                return (true, $"Конфігурацію збережено ({appliedTweaks} твіків, {removedApps} деблоат-пакетів): {Path.GetFileName(dlg.FileName)}");
            }
            catch (Exception ex)
            {
                return (false, $"Помилка експорту: {ex.Message}");
            }
        }

        return (false, "Експорт скасовано.");
    }

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
            try
            {
                string json = await File.ReadAllTextAsync(dlg.FileName);
                var config = JsonSerializer.Deserialize<MasloProfileConfig>(json);

                if (config == null || config.TweakStates == null)
                {
                    return (false, "Файл конфігурації пошкоджений або має невірну структуру.");
                }

                var tweakList = tweaks.ToList();
                var debloatList = debloatItems.ToList();
                int totalOperations = tweakList.Count + config.DebloatUninstalledIds.Count;
                int currentOp = 0;

                int appliedCount = 0;
                int restoredCount = 0;
                int debloatedCount = 0;

                foreach (var tweak in tweakList)
                {
                    currentOp++;
                    int pct = (int)((currentOp / (double)totalOperations) * 100);

                    if (config.TweakStates.TryGetValue(tweak.Id, out bool shouldBeApplied))
                    {
                        if (shouldBeApplied && !tweak.IsApplied)
                        {
                            progressCallback?.Invoke(pct, $"Застосування твіка: {tweak.Name}");
                            bool ok = await TweakEngine.Instance.ExecuteTweakAsync(tweak, isApply: true);
                            if (ok) appliedCount++;
                        }
                        else if (!shouldBeApplied && tweak.IsApplied)
                        {
                            progressCallback?.Invoke(pct, $"Відновлення твіка: {tweak.Name}");
                            bool ok = await TweakEngine.Instance.ExecuteTweakAsync(tweak, isApply: false);
                            if (ok) restoredCount++;
                        }
                    }
                }

                foreach (var debloatId in config.DebloatUninstalledIds)
                {
                    currentOp++;
                    int pct = (int)((currentOp / (double)totalOperations) * 100);

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

                progressCallback?.Invoke(100, "Конфігурацію розгорнуто!");
                return (true, $"Профіль '{Path.GetFileName(dlg.FileName)}' успішно активовано! (+{appliedCount} твіків, -{restoredCount} відновлено, {debloatedCount} UWP видалено).");
            }
            catch (Exception ex)
            {
                return (false, $"Помилка імпорту: {ex.Message}");
            }
        }

        return (false, "Імпорт скасовано.");
    }

    public static async Task<(bool Success, string Message)> ApplyMasloSignaturePackAsync(
        IEnumerable<TweakModel> tweaks,
        IEnumerable<DebloatItem> debloatItems,
        Action<int, string>? progressCallback = null)
    {
        try
        {
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

            progressCallback?.Invoke(100, "Maslo Signature Pack успішно застосовано!");
            return (true, $"Maslo Pack активовано! (Застосовано {appliedTweaks} твіків, очищено {removedApps} програм).");
        }
        catch (Exception ex)
        {
            return (false, $"Помилка: {ex.Message}");
        }
    }
}