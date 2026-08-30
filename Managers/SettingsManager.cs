using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace MASLOOPTIMIZER;

/// <summary>
/// Зберігання користувацьких налаштувань (масштаб інтерфейсу, автозапуск)
/// у C:\ProgramData\MASLOOPTIMIZER\Config\settings.json та реєстрі Run.
/// </summary>
public static class SettingsManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string WidgetOnlyRunName = "MASLOOPTIMIZER_WidgetOnly";
    private const string SilentRunName = "MASLOOPTIMIZER_Silent";

    // Константи масштабу
    public const double MinScalePercent = 50;
    public const double MaxScalePercent = 200;
    public const double DefaultScalePercent = 100;

    #region UI Scale

    public static double ReadUiScalePercent()
    {
        double percent = DefaultScalePercent;
        try
        {
            if (!File.Exists(AppPaths.SettingsFile)) return percent;

            using var doc = JsonDocument.Parse(File.ReadAllText(AppPaths.SettingsFile));
            if (doc.RootElement.TryGetProperty("UiScale", out var el) &&
                el.TryGetDouble(out var value) &&
                value >= MinScalePercent && value <= MaxScalePercent)
            {
                percent = value;
            }
        }
        catch { }
        return percent;
    }

    public static void SaveUiScalePercent(double percent)
    {
        double clamped = Math.Clamp(percent, MinScalePercent, MaxScalePercent);
        WriteSetting("UiScale", JsonSerializer.SerializeToElement(clamped));
    }

    #endregion

    #region Автозапуск Windows

    public static bool IsWidgetOnlyAutostartEnabled() => RunValueExists(WidgetOnlyRunName);
    public static bool IsSilentAutostartEnabled() => RunValueExists(SilentRunName);

    public static void SetWidgetOnlyAutostart(bool enable)
    {
        if (enable)
        {
            SetRunValue(WidgetOnlyRunName, "--widget-only");
        }
        else
        {
            DeleteRunValue(WidgetOnlyRunName);
        }
    }

    public static void SetSilentAutostart(bool enable)
    {
        if (enable)
        {
            SetRunValue(SilentRunName, "-silent");
        }
        else
        {
            DeleteRunValue(SilentRunName);
        }
    }

    private static bool RunValueExists(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(name) != null;
        }
        catch { return false; }
    }

    private static void SetRunValue(string name, string argument)
    {
        try
        {
            string? exe = CurrentExecutablePath();
            if (string.IsNullOrWhiteSpace(exe)) return;

            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            key?.SetValue(name, $"\"{exe}\" {argument}", RegistryValueKind.String);
        }
        catch { }
    }

    private static void DeleteRunValue(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
        }
        catch { }
    }

    private static string? CurrentExecutablePath()
    {
        try
        {
            return Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName;
        }
        catch { return null; }
    }

    #endregion

    #region Загальний запис у settings.json (з атомарним safe-swap)

    private static void WriteSetting(string key, JsonElement value)
    {
        try
        {
            AppPaths.EnsureDirectories();

            var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(AppPaths.SettingsFile))
            {
                var existing = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(AppPaths.SettingsFile));
                if (existing != null)
                {
                    foreach (var kv in existing) settings[kv.Key] = kv.Value;
                }
            }

            settings[key] = value;

            string tmp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));

            if (new FileInfo(tmp).Length > 0)
            {
                File.Move(tmp, AppPaths.SettingsFile, overwrite: true);
            }
        }
        catch { }
    }

    #endregion

    #region Мова (Language)

    public static string ReadLanguage()
    {
        string? code = ReadLanguageOptional();
        if (string.IsNullOrWhiteSpace(code)) return LocalizationManager.FallbackLanguage;
        return code;
    }

    /// <summary>Читає збережену мову або null, якщо налаштування ще немає (перший запуск).</summary>
    public static string? ReadLanguageOptional()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile)) return null;

            using var doc = JsonDocument.Parse(File.ReadAllText(AppPaths.SettingsFile));
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("Language", out var lang) &&
                lang.ValueKind == JsonValueKind.String)
            {
                string code = NormalizeLanguageCode(lang.GetString());
                if (!string.IsNullOrWhiteSpace(code)) return code;
            }
        }
        catch { }
        return null;
    }

    public static void SaveLanguage(string code)
    {
        string normalized = NormalizeLanguageCode(code);
        if (string.IsNullOrWhiteSpace(normalized)) normalized = LocalizationManager.FallbackLanguage;
        WriteSetting("Language", JsonSerializer.SerializeToElement(normalized));
    }

    public static bool ReadLanguageLocked()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile)) return false;

            using var doc = JsonDocument.Parse(File.ReadAllText(AppPaths.SettingsFile));
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("LanguageLocked", out var el))
            {
                if (el.ValueKind == JsonValueKind.True) return true;
                if (el.ValueKind == JsonValueKind.False) return false;
                if (el.ValueKind == JsonValueKind.String && bool.TryParse(el.GetString(), out var b)) return b;
            }
        }
        catch { }
        return false;
    }

    public static void SaveLanguageLocked(bool locked)
        => WriteSetting("LanguageLocked", JsonSerializer.SerializeToElement(locked));

    /// <summary>Нормалізує код мови (старий UK → UA).</summary>
    private static string NormalizeLanguageCode(string? code)
    {
        string c = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (string.Equals(c, "UK", StringComparison.OrdinalIgnoreCase)) return "UA";
        return c;
    }

    #endregion
}
