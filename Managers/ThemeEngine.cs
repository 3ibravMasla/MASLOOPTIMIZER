using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Application = System.Windows.Application;

namespace MASLOOPTIMIZER;

/// <summary>
/// Спеціалізований модуль перемикача тем.
/// Містить 30 тем для головного вікна програми та 30 тем для HUD-віджета.
/// </summary>
public static class ThemeEngine
{
    private static string _currentAppTheme = "CyberStealth";
    private static string _currentWidgetTheme = "Emerald";

    public static string CurrentAppThemeKey => _currentAppTheme;
    public static string CurrentWidgetThemeKey => _currentWidgetTheme;

    public static IReadOnlyList<ThemePalette> AppThemes { get; }
    public static IReadOnlyList<WidgetTheme> WidgetThemes { get; }

    static ThemeEngine()
    {
        AppThemes = BuildAppThemes();
        WidgetThemes = BuildWidgetThemes();
    }

    public static ThemePalette? CurrentAppTheme =>
        AppThemes.FirstOrDefault(t => t.Key == _currentAppTheme) ?? AppThemes.FirstOrDefault();

    public static WidgetTheme? CurrentWidgetTheme =>
        WidgetThemes.FirstOrDefault(t => t.Key == _currentWidgetTheme) ?? WidgetThemes.FirstOrDefault();

    public static ThemePalette? GetAppTheme(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : AppThemes.FirstOrDefault(t => t.Key == key);

    public static WidgetTheme? GetWidgetTheme(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : WidgetThemes.FirstOrDefault(t => t.Key == key);

    /// <summary>Застосовує палітру головного вікна за ключем теми.</summary>
    public static void ApplyAppTheme(string key)
    {
        var theme = GetAppTheme(key) ?? AppThemes[0];
        _currentAppTheme = theme.Key;

        Application.Current?.Dispatcher.Invoke(() =>
        {
            var res = Application.Current.Resources;
            foreach (var kv in theme.Brushes)
            {
                SetBrush(res, kv.Key, kv.Value);
            }
        });

        SaveAppTheme(theme.Key);
    }

    /// <summary>
    /// Подія синхронізації тем: сповіщає відкритий HUD-віджет про зміну теми головної програми.
    /// Параметр — ключ теми віджета (WidgetTheme.Key).
    /// </summary>
    public static event Action<string>? WidgetThemeRequired;

    /// <summary>
    /// Застосовує тему головного вікна та одночасно синхронізує тему HUD-віджета
    /// (обирає тему віджета за співпадінням акцентного кольору).
    /// </summary>
    public static void ApplyAppThemeWithWidget(string key)
    {
        ApplyAppTheme(key);

        string widgetKey = MapAppThemeToWidget(key);
        _currentWidgetTheme = widgetKey;
        SaveWidgetTheme(widgetKey);
        WidgetThemeRequired?.Invoke(widgetKey);
    }

    /// <summary>
    /// Підбирає тему віджета для теми головної програми:
    /// знаходить тему віджета з тим самим акцентним кольором (Accent),
    /// інакше повертає тему за замовчуванням.
    /// </summary>
    public static string MapAppThemeToWidget(string appKey)
    {
        var appTheme = GetAppTheme(appKey);
        if (appTheme == null) return WidgetThemes[0].Key;

        string accent = appTheme.Accent.TrimStart('#').ToUpperInvariant();
        var match = WidgetThemes.FirstOrDefault(w =>
            string.Equals(w.Accent.TrimStart('#').ToUpperInvariant(), accent, StringComparison.Ordinal));
        return match?.Key ?? WidgetThemes[0].Key;
    }

    private static string ReadSavedWidgetTheme()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile)) return WidgetThemes[0].Key;

            using var doc = JsonDocument.Parse(File.ReadAllText(AppPaths.SettingsFile));
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("WidgetTheme", out var t) &&
                t.ValueKind == JsonValueKind.String)
            {
                string key = t.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(key) && WidgetThemes.Any(x => x.Key == key))
                {
                    return key;
                }
            }
        }
        catch { }

        return WidgetThemes[0].Key;
    }

    private static void SaveWidgetTheme(string key)
    {
        try
        {
            AppPaths.EnsureDirectories();

            var options = new JsonSerializerOptions { WriteIndented = true };
            var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

            if (File.Exists(AppPaths.SettingsFile))
            {
                try
                {
                    var existing = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(AppPaths.SettingsFile));
                    if (existing != null)
                    {
                        foreach (var kv in existing) settings[kv.Key] = kv.Value;
                    }
                }
                catch { }
            }

            settings["WidgetTheme"] = JsonSerializer.SerializeToElement(key);
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(settings, options));
        }
        catch { }
    }

    /// <summary>Циклічно перемикає тему головного вікна вперед.</summary>
    public static void ApplyNextAppTheme()
    {
        int index = AppThemes.ToList().FindIndex(t => t.Key == _currentAppTheme);
        int next = index < 0 ? 0 : (index + 1) % AppThemes.Count;
        ApplyAppTheme(AppThemes[next].Key);
    }

    /// <summary>Циклічно перемикає тему головного вікна назад.</summary>
    public static void ApplyPreviousAppTheme()
    {
        int index = AppThemes.ToList().FindIndex(t => t.Key == _currentAppTheme);
        int prev = index <= 0 ? AppThemes.Count - 1 : (index - 1) % AppThemes.Count;
        ApplyAppTheme(AppThemes[prev].Key);
    }

    /// <summary>Застосовує збережені теми головного вікна та віджета при запуску.</summary>
    public static void ApplySavedAppTheme()
    {
        ApplyAppTheme(ReadSavedAppTheme());
        _currentWidgetTheme = ReadSavedWidgetTheme();
    }

    private static void SetBrush(ResourceDictionary res, string key, string hex)
    {
        var bc = new BrushConverter();
        if (bc.ConvertFromString(hex) is SolidColorBrush brush)
        {
            brush.Freeze();
            res[key] = brush;
        }
    }

    private static string ReadSavedAppTheme()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile)) return "CyberStealth";

            using var doc = JsonDocument.Parse(File.ReadAllText(AppPaths.SettingsFile));
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("AppTheme", out var t) &&
                t.ValueKind == JsonValueKind.String)
            {
                string key = t.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(key) && AppThemes.Any(x => x.Key == key))
                {
                    return key;
                }
            }
        }
        catch { }

        return "CyberStealth";
    }

    private static void SaveAppTheme(string key)
    {
        try
        {
            AppPaths.EnsureDirectories();

            var options = new JsonSerializerOptions { WriteIndented = true };
            var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

            if (File.Exists(AppPaths.SettingsFile))
            {
                try
                {
                    var existing = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(AppPaths.SettingsFile));
                    if (existing != null)
                    {
                        foreach (var kv in existing) settings[kv.Key] = kv.Value;
                    }
                }
                catch { }
            }

            settings["AppTheme"] = JsonSerializer.SerializeToElement(key);
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(settings, options));
        }
        catch { }
    }

    // ------------------------------------------------------------------
    // Базові палітри
    // ------------------------------------------------------------------

    private static Dictionary<string, string> BaseDark() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["WindowBg"] = "#0B0C10",
        ["HeaderBg"] = "#12141C",
        ["HeaderBorder"] = "#1E2230",
        ["SidebarBg"] = "#10121A",
        ["SidebarBorder"] = "#1A1E2B",
        ["CardBg"] = "#151722",
        ["CardBorder"] = "#212536",
        ["ActionBtnBg"] = "#181B26",
        ["ActionBtnBorder"] = "#262C3E",
        ["NavBtnBg"] = "Transparent",
        ["NavBtnHover"] = "#181B28",
        ["NavBtnActive"] = "#1E2333",
        ["NavBtnBorderActive"] = "#00FF9D",
        ["TextPrimary"] = "#F1F5F9",
        ["TextSecondary"] = "#94A3B8",
        ["TextMuted"] = "#64748B",
        ["StatusBg"] = "#12141C",
        ["StatusBorder"] = "#1E2230",
        ["AccentGreen"] = "#00FF9D",
        ["AccentBrush"] = "#00FF9D",
        ["BorderMuted"] = "#334155",
        ["BadgeBg"] = "#161924",
        ["BadgeText"] = "#94A3B8",
        ["ProgressTrack"] = "#181B26",
        ["ProgressFill"] = "#00FF9D",
        ["ChipBg"] = "#1E2235",
        ["ChipBorder"] = "#334155",
        ["ChipText"] = "#CBD5E1",
        ["ChipHoverBg"] = "#2A344A",
        ["ChipActiveBg"] = "#0078D4",
        ["ChipActiveBorder"] = "#38BDF8",
        ["ChipActiveText"] = "#FFFFFF",
        ["StatusStdBg"] = "#2A2D3D",
        ["StatusStdText"] = "#94A3B8",
    };

    private static Dictionary<string, string> BaseLight() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["WindowBg"] = "#E2E8F0",
        ["HeaderBg"] = "#ECEFF4",
        ["HeaderBorder"] = "#CBD5E1",
        ["SidebarBg"] = "#E8ECF2",
        ["SidebarBorder"] = "#CBD5E1",
        ["CardBg"] = "#F8FAFC",
        ["CardBorder"] = "#CBD5E1",
        ["ActionBtnBg"] = "#E2E8F0",
        ["ActionBtnBorder"] = "#CBD5E1",
        ["NavBtnBg"] = "Transparent",
        ["NavBtnHover"] = "#CBD5E1",
        ["NavBtnActive"] = "#F1F5F9",
        ["NavBtnBorderActive"] = "#0284C7",
        ["TextPrimary"] = "#0F172A",
        ["TextSecondary"] = "#334155",
        ["TextMuted"] = "#64748B",
        ["StatusBg"] = "#ECEFF4",
        ["StatusBorder"] = "#CBD5E1",
        ["AccentGreen"] = "#0284C7",
        ["AccentBrush"] = "#0284C7",
        ["BorderMuted"] = "#CBD5E1",
        ["BadgeBg"] = "#E2E8F0",
        ["BadgeText"] = "#1E293B",
        ["ProgressTrack"] = "#CBD5E1",
        ["ProgressFill"] = "#0284C7",
        ["ChipBg"] = "#E2E8F0",
        ["ChipBorder"] = "#CBD5E1",
        ["ChipText"] = "#1E293B",
        ["ChipHoverBg"] = "#D8E1E8",
        ["ChipActiveBg"] = "#0284C7",
        ["ChipActiveBorder"] = "#0369A1",
        ["ChipActiveText"] = "#FFFFFF",
        ["StatusStdBg"] = "#E2E8F0",
        ["StatusStdText"] = "#475569",
    };

    private static Dictionary<string, string> ApplyOverrides(Dictionary<string, string> source, params (string Key, string Value)[] overrides)
    {
        var result = new Dictionary<string, string>(source, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in overrides)
        {
            result[key] = value;
        }
        return result;
    }

    private static List<ThemePalette> BuildAppThemes()
    {
        var dark = BaseDark();
        var light = BaseLight();
        var themes = new List<ThemePalette>
        {
            new ThemePalette("WindowsXP", "Windows XP Luna", "🪟 Classic", false, ApplyOverrides(light,
                ("WindowBg", "#ECE9D8"), ("HeaderBg", "#3B7CF5"), ("SidebarBg", "#F0EEE3"), ("CardBg", "#FFFFFF"),
                ("ActionBtnBg", "#F0EEE3"), ("TextPrimary", "#1A1A1A"), ("AccentGreen", "#3B7CF5"), ("ProgressFill", "#3B7CF5"),
                ("NavBtnBorderActive", "#3B7CF5"), ("ChipActiveBg", "#3B7CF5"), ("ChipActiveBorder", "#1F52B3"), ("ChipActiveText", "#FFFFFF"))),
            new ThemePalette("WindowsVista", "Windows Vista Aero", "🪟 Classic", false, ApplyOverrides(light,
                ("WindowBg", "#DFF3FA"), ("HeaderBg", "#A8D8F0"), ("SidebarBg", "#E7F5FC"), ("CardBg", "#F4FBFE"),
                ("ActionBtnBg", "#E1F0FA"), ("TextPrimary", "#0B2E3F"), ("AccentGreen", "#2E7DB8"), ("ProgressFill", "#2E7DB8"),
                ("NavBtnBorderActive", "#2E7DB8"), ("ChipActiveBg", "#2E7DB8"), ("ChipActiveBorder", "#1E5F96"), ("ChipActiveText", "#FFFFFF"))),
            new ThemePalette("Windows7", "Windows 7 Aero", "🪟 Classic", false, ApplyOverrides(light,
                ("WindowBg", "#D8F0FB"), ("HeaderBg", "#8FCBE8"), ("SidebarBg", "#E0F3FC"), ("CardBg", "#F2FAFE"),
                ("ActionBtnBg", "#DDF0FA"), ("TextPrimary", "#0A2B3D"), ("AccentGreen", "#1E90C4"), ("ProgressFill", "#1E90C4"),
                ("NavBtnBorderActive", "#1E90C4"), ("ChipActiveBg", "#1E90C4"), ("ChipActiveBorder", "#1577A8"), ("ChipActiveText", "#FFFFFF"))),
            new ThemePalette("Windows98", "Windows 98 Classic", "🪟 Classic", false, ApplyOverrides(light,
                ("WindowBg", "#C0C0C0"), ("HeaderBg", "#008080"), ("SidebarBg", "#C0C0C0"), ("CardBg", "#D4D0C8"),
                ("ActionBtnBg", "#C0C0C0"), ("TextPrimary", "#000000"), ("AccentGreen", "#008080"), ("ProgressFill", "#008080"),
                ("NavBtnBorderActive", "#008080"), ("ChipActiveBg", "#008080"), ("ChipActiveBorder", "#005A5A"), ("ChipActiveText", "#FFFFFF"))),
            new ThemePalette("Windows2000", "Windows 2000", "🪟 Classic", false, ApplyOverrides(light,
                ("WindowBg", "#D4D0C8"), ("HeaderBg", "#000080"), ("SidebarBg", "#D4D0C8"), ("CardBg", "#E0DCD4"),
                ("ActionBtnBg", "#D4D0C8"), ("TextPrimary", "#000000"), ("AccentGreen", "#000080"), ("ProgressFill", "#000080"),
                ("NavBtnBorderActive", "#000080"), ("ChipActiveBg", "#000080"), ("ChipActiveBorder", "#00005A"), ("ChipActiveText", "#FFFFFF"))),
            new ThemePalette("CyberStealth", "Cyber Stealth", "🌆 Cyberpunk & Neon", true, dark),
            new ThemePalette("Cyberpunk2077", "Cyberpunk 2077", "🌆 Cyberpunk & Neon", true, ApplyOverrides(dark,
                ("WindowBg", "#0D0A0E"), ("HeaderBg", "#161018"), ("SidebarBg", "#120C16"), ("CardBg", "#1A1220"),
                ("ActionBtnBg", "#1C1424"), ("TextPrimary", "#FFF5D6"), ("AccentGreen", "#FFEE00"), ("ProgressFill", "#FFEE00"),
                ("NavBtnBorderActive", "#FFEE00"), ("ChipActiveBg", "#FFEE00"), ("ChipActiveBorder", "#FF00FF"), ("ChipActiveText", "#0A0A0A"))),
            new ThemePalette("Jarvis", "Jarvis", "🌆 Cyberpunk & Neon", true, ApplyOverrides(dark,
                ("WindowBg", "#0A0F14"), ("HeaderBg", "#101822"), ("SidebarBg", "#0C1119"), ("CardBg", "#131B26"),
                ("ActionBtnBg", "#16202C"), ("TextPrimary", "#EAF6FF"), ("AccentGreen", "#FFC107"), ("ProgressFill", "#FFC107"),
                ("NavBtnBorderActive", "#FFC107"), ("ChipActiveBg", "#FFC107"), ("ChipActiveBorder", "#38BDF8"), ("ChipActiveText", "#101010"))),
            new ThemePalette("Tron", "Tron", "🌆 Cyberpunk & Neon", true, ApplyOverrides(dark,
                ("WindowBg", "#050B12"), ("HeaderBg", "#0A1420"), ("SidebarBg", "#07101B"), ("CardBg", "#0D1826"),
                ("ActionBtnBg", "#10202E"), ("TextPrimary", "#D9FBFF"), ("AccentGreen", "#00F0FF"), ("ProgressFill", "#00F0FF"),
                ("NavBtnBorderActive", "#00F0FF"), ("ChipActiveBg", "#00F0FF"), ("ChipActiveBorder", "#FF6D00"), ("ChipActiveText", "#001B1E"))),
            new ThemePalette("Matrix", "Matrix", "🌆 Cyberpunk & Neon", true, ApplyOverrides(dark,
                ("WindowBg", "#020A03"), ("HeaderBg", "#041208"), ("SidebarBg", "#030D05"), ("CardBg", "#07160A"),
                ("ActionBtnBg", "#081A0C"), ("TextPrimary", "#D8FFDD"), ("AccentGreen", "#00FF41"), ("ProgressFill", "#00FF41"),
                ("NavBtnBorderActive", "#00FF41"), ("ChipActiveBg", "#00FF41"), ("ChipActiveBorder", "#00C334"), ("ChipActiveText", "#001000"))),
            new ThemePalette("Synthwave", "Synthwave", "🌆 Cyberpunk & Neon", true, ApplyOverrides(dark,
                ("WindowBg", "#12040E"), ("HeaderBg", "#1A0714"), ("SidebarBg", "#150512"), ("CardBg", "#1F0A1A"),
                ("ActionBtnBg", "#240D1F"), ("TextPrimary", "#FFE3F2"), ("AccentGreen", "#FF2D95"), ("ProgressFill", "#FF2D95"),
                ("NavBtnBorderActive", "#FF2D95"), ("ChipActiveBg", "#FF2D95"), ("ChipActiveBorder", "#B026FF"), ("ChipActiveText", "#FFFFFF"))),
            new ThemePalette("Hologram", "Hologram", "🌆 Cyberpunk & Neon", true, ApplyOverrides(dark,
                ("WindowBg", "#041014"), ("HeaderBg", "#071A20"), ("SidebarBg", "#06141A"), ("CardBg", "#0A1F26"),
                ("ActionBtnBg", "#0C2430"), ("TextPrimary", "#DBFFF9"), ("AccentGreen", "#4FF2D0"), ("ProgressFill", "#4FF2D0"),
                ("NavBtnBorderActive", "#4FF2D0"), ("ChipActiveBg", "#4FF2D0"), ("ChipActiveBorder", "#2A9B8A"), ("ChipActiveText", "#04201B"))),
            new ThemePalette("Dracula", "Dracula", "🖥️ Dark", true, ApplyOverrides(dark,
                ("WindowBg", "#282A36"), ("HeaderBg", "#2E3040"), ("SidebarBg", "#21222C"), ("CardBg", "#343646"),
                ("ActionBtnBg", "#343746"), ("TextPrimary", "#F8F8F2"), ("AccentGreen", "#BD93F9"), ("ProgressFill", "#BD93F9"),
                ("NavBtnBorderActive", "#BD93F9"), ("ChipActiveBg", "#BD93F9"), ("ChipActiveBorder", "#FF79C6"), ("ChipActiveText", "#1A1029"))),
            new ThemePalette("Nord", "Nord", "🖥️ Dark", true, ApplyOverrides(dark,
                ("WindowBg", "#2E3440"), ("HeaderBg", "#343B4A"), ("SidebarBg", "#2A303B"), ("CardBg", "#3B4252"),
                ("ActionBtnBg", "#3B4252"), ("TextPrimary", "#ECEFF4"), ("AccentGreen", "#88C0D0"), ("ProgressFill", "#88C0D0"),
                ("NavBtnBorderActive", "#88C0D0"), ("ChipActiveBg", "#88C0D0"), ("ChipActiveBorder", "#5E81AC"), ("ChipActiveText", "#0B141A"))),
            new ThemePalette("SolarizedDark", "Solarized Dark", "🖥️ Dark", true, ApplyOverrides(dark,
                ("WindowBg", "#002B36"), ("HeaderBg", "#05323D"), ("SidebarBg", "#00212B"), ("CardBg", "#073642"),
                ("ActionBtnBg", "#0A3A46"), ("TextPrimary", "#FDF6E3"), ("AccentGreen", "#2AA198"), ("ProgressFill", "#2AA198"),
                ("NavBtnBorderActive", "#2AA198"), ("ChipActiveBg", "#2AA198"), ("ChipActiveBorder", "#268BD2"), ("ChipActiveText", "#04201B"))),
            new ThemePalette("GruvboxDark", "Gruvbox Dark", "🖥️ Dark", true, ApplyOverrides(dark,
                ("WindowBg", "#282828"), ("HeaderBg", "#32302F"), ("SidebarBg", "#1D2021"), ("CardBg", "#3C3836"),
                ("ActionBtnBg", "#3C3836"), ("TextPrimary", "#FBF1C7"), ("AccentGreen", "#FE8019"), ("ProgressFill", "#FE8019"),
                ("NavBtnBorderActive", "#FE8019"), ("ChipActiveBg", "#FE8019"), ("ChipActiveBorder", "#FABD2F"), ("ChipActiveText", "#1D1003"))),
            new ThemePalette("Monokai", "Monokai", "🖥️ Dark", true, ApplyOverrides(dark,
                ("WindowBg", "#272822"), ("HeaderBg", "#2E2F27"), ("SidebarBg", "#1F201B"), ("CardBg", "#343530"),
                ("ActionBtnBg", "#343530"), ("TextPrimary", "#F8F8F2"), ("AccentGreen", "#F92672"), ("ProgressFill", "#F92672"),
                ("NavBtnBorderActive", "#F92672"), ("ChipActiveBg", "#F92672"), ("ChipActiveBorder", "#A6E22E"), ("ChipActiveText", "#1A0410"))),
            new ThemePalette("OneDark", "One Dark", "🖥️ Dark", true, ApplyOverrides(dark,
                ("WindowBg", "#282C34"), ("HeaderBg", "#2E333D"), ("SidebarBg", "#21252B"), ("CardBg", "#333842"),
                ("ActionBtnBg", "#333842"), ("TextPrimary", "#ABB2BF"), ("AccentGreen", "#61AFEF"), ("ProgressFill", "#61AFEF"),
                ("NavBtnBorderActive", "#61AFEF"), ("ChipActiveBg", "#61AFEF"), ("ChipActiveBorder", "#C678DD"), ("ChipActiveText", "#0D151D"))),
            new ThemePalette("TokyoNight", "Tokyo Night", "🖥️ Dark", true, ApplyOverrides(dark,
                ("WindowBg", "#1A1B26"), ("HeaderBg", "#20202E"), ("SidebarBg", "#16161E"), ("CardBg", "#24283B"),
                ("ActionBtnBg", "#24283B"), ("TextPrimary", "#C0CAF5"), ("AccentGreen", "#7AA2F7"), ("ProgressFill", "#7AA2F7"),
                ("NavBtnBorderActive", "#7AA2F7"), ("ChipActiveBg", "#7AA2F7"), ("ChipActiveBorder", "#BB9AF7"), ("ChipActiveText", "#0E1520"))),
            new ThemePalette("CatppuccinMocha", "Catppuccin Mocha", "🖥️ Dark", true, ApplyOverrides(dark,
                ("WindowBg", "#1E1E2E"), ("HeaderBg", "#262636"), ("SidebarBg", "#181825"), ("CardBg", "#313244"),
                ("ActionBtnBg", "#313244"), ("TextPrimary", "#CDD6F4"), ("AccentGreen", "#CBA6F7"), ("ProgressFill", "#CBA6F7"),
                ("NavBtnBorderActive", "#CBA6F7"), ("ChipActiveBg", "#CBA6F7"), ("ChipActiveBorder", "#F5C2E7"), ("ChipActiveText", "#171320"))),
            new ThemePalette("GitHubDark", "GitHub Dark", "🖥️ Dark", true, ApplyOverrides(dark,
                ("WindowBg", "#0D1117"), ("HeaderBg", "#131820"), ("SidebarBg", "#0A0E13"), ("CardBg", "#161B22"),
                ("ActionBtnBg", "#161B22"), ("TextPrimary", "#E6EDF3"), ("AccentGreen", "#58A6FF"), ("ProgressFill", "#58A6FF"),
                ("NavBtnBorderActive", "#58A6FF"), ("ChipActiveBg", "#58A6FF"), ("ChipActiveBorder", "#1F6FEB"), ("ChipActiveText", "#0D1117"))),
            new ThemePalette("HighContrastBlack", "High Contrast Black", "🖥️ Dark", true, ApplyOverrides(dark,
                ("WindowBg", "#000000"), ("HeaderBg", "#0A0A0A"), ("HeaderBorder", "#FFFFFF"), ("SidebarBg", "#000000"),
                ("SidebarBorder", "#FFFFFF"), ("CardBg", "#0A0A0A"), ("CardBorder", "#FFFFFF"), ("ActionBtnBg", "#0A0A0A"),
                ("ActionBtnBorder", "#FFFFFF"), ("TextPrimary", "#FFFFFF"), ("TextSecondary", "#FFFFFF"), ("TextMuted", "#FFFFFF"),
                ("AccentGreen", "#FFFF00"), ("ProgressFill", "#FFFF00"), ("NavBtnBorderActive", "#FFFF00"), ("ChipBg", "#0A0A0A"),
                ("ChipBorder", "#FFFFFF"), ("ChipText", "#FFFFFF"), ("ChipHoverBg", "#1A1A1A"), ("ChipActiveBg", "#FFFF00"),
                ("ChipActiveBorder", "#FFFFFF"), ("ChipActiveText", "#000000"), ("StatusStdBg", "#0A0A0A"), ("StatusStdText", "#FFFFFF"),
                ("ProgressTrack", "#1A1A1A"))),
            new ThemePalette("NordicLight", "Nordic Light", "🌞 Light", false, light),
            new ThemePalette("SolarizedLight", "Solarized Light", "🌞 Light", false, ApplyOverrides(light,
                ("WindowBg", "#FDF6E3"), ("HeaderBg", "#F5EED0"), ("SidebarBg", "#F3EBD0"), ("CardBg", "#FFFDF5"),
                ("ActionBtnBg", "#F5EED0"), ("TextPrimary", "#073642"), ("AccentGreen", "#2AA198"), ("ProgressFill", "#2AA198"),
                ("NavBtnBorderActive", "#2AA198"), ("ChipActiveBg", "#2AA198"), ("ChipActiveBorder", "#268BD2"), ("ChipActiveText", "#FFFFFF"))),
            new ThemePalette("GruvboxLight", "Gruvbox Light", "🌞 Light", false, ApplyOverrides(light,
                ("WindowBg", "#FBF1C7"), ("HeaderBg", "#F2E5BC"), ("SidebarBg", "#F5ECC6"), ("CardBg", "#FFF9E5"),
                ("ActionBtnBg", "#F2E5BC"), ("TextPrimary", "#3C3836"), ("AccentGreen", "#D65D0E"), ("ProgressFill", "#D65D0E"),
                ("NavBtnBorderActive", "#D65D0E"), ("ChipActiveBg", "#D65D0E"), ("ChipActiveBorder", "#B57614"), ("ChipActiveText", "#FFFFFF"))),
            new ThemePalette("OneLight", "One Light", "🌞 Light", false, ApplyOverrides(light,
                ("WindowBg", "#FAFAFA"), ("HeaderBg", "#F0F0F0"), ("SidebarBg", "#F5F5F5"), ("CardBg", "#FFFFFF"),
                ("ActionBtnBg", "#F0F0F0"), ("TextPrimary", "#383A42"), ("AccentGreen", "#4078F2"), ("ProgressFill", "#4078F2"),
                ("NavBtnBorderActive", "#4078F2"), ("ChipActiveBg", "#4078F2"), ("ChipActiveBorder", "#C678DD"), ("ChipActiveText", "#FFFFFF"))),
            new ThemePalette("GitHubLight", "GitHub Light", "🌞 Light", false, ApplyOverrides(light,
                ("WindowBg", "#FFFFFF"), ("HeaderBg", "#F6F8FA"), ("SidebarBg", "#F6F8FA"), ("CardBg", "#FFFFFF"),
                ("ActionBtnBg", "#F6F8FA"), ("TextPrimary", "#1F2328"), ("AccentGreen", "#0969DA"), ("ProgressFill", "#0969DA"),
                ("NavBtnBorderActive", "#0969DA"), ("ChipActiveBg", "#0969DA"), ("ChipActiveBorder", "#54AEFF"), ("ChipActiveText", "#FFFFFF"))),
            new ThemePalette("Windows11Light", "Windows 11 Light", "🌞 Light", false, ApplyOverrides(light,
                ("WindowBg", "#F3F3F3"), ("HeaderBg", "#F9F9F9"), ("SidebarBg", "#F5F5F5"), ("CardBg", "#FFFFFF"),
                ("ActionBtnBg", "#F5F5F5"), ("TextPrimary", "#1B1B1B"), ("AccentGreen", "#0067C0"), ("ProgressFill", "#0067C0"),
                ("NavBtnBorderActive", "#0067C0"), ("ChipActiveBg", "#0067C0"), ("ChipActiveBorder", "#005EA6"), ("ChipActiveText", "#FFFFFF"))),
            new ThemePalette("HighContrastWhite", "High Contrast White", "🌞 Light", false, ApplyOverrides(light,
                ("WindowBg", "#FFFFFF"), ("HeaderBg", "#F2F2F2"), ("HeaderBorder", "#000000"), ("SidebarBg", "#FFFFFF"),
                ("SidebarBorder", "#000000"), ("CardBg", "#FFFFFF"), ("CardBorder", "#000000"), ("ActionBtnBg", "#F2F2F2"),
                ("ActionBtnBorder", "#000000"), ("TextPrimary", "#000000"), ("TextSecondary", "#000000"), ("TextMuted", "#000000"),
                ("AccentGreen", "#0000FF"), ("ProgressFill", "#0000FF"), ("NavBtnBorderActive", "#0000FF"), ("ChipBg", "#F2F2F2"),
                ("ChipBorder", "#000000"), ("ChipText", "#000000"), ("ChipHoverBg", "#E6E6E6"), ("ChipActiveBg", "#0000FF"),
                ("ChipActiveBorder", "#000000"), ("ChipActiveText", "#FFFFFF"), ("StatusStdBg", "#F2F2F2"), ("StatusStdText", "#000000"),
                ("ProgressTrack", "#E6E6E6"))),
            new ThemePalette("ArcticLight", "Arctic Light", "🌞 Light", false, ApplyOverrides(light,
                ("WindowBg", "#EAF6FF"), ("HeaderBg", "#DBEFFE"), ("SidebarBg", "#E3F2FD"), ("CardBg", "#F6FBFF"),
                ("ActionBtnBg", "#DBEFFE"), ("TextPrimary", "#082032"), ("AccentGreen", "#0EA5E9"), ("ProgressFill", "#0EA5E9"),
                ("NavBtnBorderActive", "#0EA5E9"), ("ChipActiveBg", "#0EA5E9"), ("ChipActiveBorder", "#0284C7"), ("ChipActiveText", "#FFFFFF"))),




        };
        return themes;
    }

    private static List<WidgetTheme> BuildWidgetThemes()
    {
        return new List<WidgetTheme>
        {
            new WidgetTheme("Emerald", "🌟 Emerald", "Neon", "#00FF9D"),
            new WidgetTheme("Rainbow", "🌈 RGB Rainbow", "Animated", "#00FF9D", animation: "Rainbow"),
            new WidgetTheme("Pulse", "💓 Pulse", "Animated", "#00FF9D", animation: "Pulse"),
            new WidgetTheme("CyberCyan", "💎 Cyber Cyan", "Neon", "#00F0FF"),
            new WidgetTheme("LavaOrange", "🔥 Lava Orange", "Neon", "#FF6D00"),
            new WidgetTheme("Crimson", "🩸 Crimson", "Neon", "#FF1744"),
            new WidgetTheme("Violet", "🔮 Violet", "Neon", "#B026FF"),
            new WidgetTheme("AmberGold", "👑 Amber Gold", "Neon", "#FFD700"),
            new WidgetTheme("ToxicLime", "⚡ Toxic Lime", "Neon", "#76FF03"),
            new WidgetTheme("Stealth", "🌑 Stealth Matte", "Matte", "#94A3B8", background: "#F4080B10", border: "#2A344A", glowOpacity: 0.0, logoGlowOpacity: 0.0, brandForeground: "#94A3B8", progressForeground: "#38BDF8"),
            new WidgetTheme("GhostGlass", "👻 Ghost Glass", "Glass", "#FFFFFF", background: "#220A0D14", border: "#60FFFFFF", glowOpacity: 0.0, logoGlowOpacity: 0.0, brandForeground: "#FFFFFF", progressForeground: "#FFFFFF"),
            new WidgetTheme("WindowsXP", "🪟 XP Luna", "Classic", "#3B7CF5"),
            new WidgetTheme("WindowsVista", "🪟 Vista Aero", "Classic", "#4FA8E0"),
            new WidgetTheme("Windows7", "🪟 Win7 Aero", "Classic", "#6FC3FF"),
            new WidgetTheme("Windows98", "🪟 Win98 Grey", "Classic", "#C0C0C0"),
            new WidgetTheme("Windows2000", "🪟 Win2000 Navy", "Classic", "#000080"),
            new WidgetTheme("Windows11", "🪟 Win11 Blue", "Classic", "#60CDFF"),
            new WidgetTheme("Jarvis", "🤖 Jarvis Gold", "AI", "#FFC107"),
            new WidgetTheme("Cyberpunk2077", "🌆 Cyberpunk 2077", "Cyberpunk", "#FFEE00"),
            new WidgetTheme("CyberpunkMagenta", "🟪 Cyberpunk Magenta", "Cyberpunk", "#FF00FF"),
            new WidgetTheme("Matrix", "🟩 Matrix Green", "Cyberpunk", "#00FF41"),
            new WidgetTheme("Tron", "🔷 Tron Cyan", "Cyberpunk", "#00BCD4"),
            new WidgetTheme("SynthwavePink", "💗 Synthwave Pink", "Retro", "#FF2D95"),
            new WidgetTheme("Dracula", "🧛 Dracula Purple", "Dark", "#BD93F9"),
            new WidgetTheme("Nord", "🧊 Nord Ice", "Dark", "#88C0D0"),
            new WidgetTheme("Gruvbox", "🍊 Gruvbox Orange", "Dark", "#FE8019"),
            new WidgetTheme("Monokai", "🎀 Monokai Pink", "Dark", "#F92672"),
            new WidgetTheme("TokyoNight", "🌃 Tokyo Night", "Dark", "#7AA2F7"),
            new WidgetTheme("HighContrastWhite", "⚪ Contrast White", "Contrast", "#FFFFFF"),
            new WidgetTheme("HighContrastBlack", "⚫ Contrast Black", "Contrast", "#000000"),

        };
    }
}

/// <summary>Палітра кольорів для головного вікна програми.</summary>
public sealed class ThemePalette
{
    public string Key { get; }
    public string DisplayName { get; }
    public string Category { get; }
    public bool IsDark { get; }
    public IReadOnlyDictionary<string, string> Brushes { get; }

    public string Accent => Brushes.TryGetValue("AccentGreen", out var v) ? v : "#00FF9D";

    public ThemePalette(string key, string displayName, string category, bool isDark, IReadOnlyDictionary<string, string> brushes)
    {
        Key = key;
        DisplayName = displayName;
        Category = category;
        IsDark = isDark;
        Brushes = brushes;
    }
}

/// <summary>Стиль неонової підсвітки HUD-віджета.</summary>
public sealed class WidgetTheme
{
    public string Key { get; }
    public string DisplayName { get; }
    public string Category { get; }
    public string Accent { get; }
    public string? Background { get; }
    public string? Border { get; }
    public double GlowOpacity { get; }
    public double LogoGlowOpacity { get; }
    public string Animation { get; }
    public string? BrandForeground { get; }
    public string? ProgressForeground { get; }

    public WidgetTheme(
        string key,
        string displayName,
        string category,
        string accent,
        string? background = null,
        string? border = null,
        double glowOpacity = 0.40,
        double logoGlowOpacity = 0.70,
        string animation = "None",
        string? brandForeground = null,
        string? progressForeground = null)
    {
        Key = key;
        DisplayName = displayName;
        Category = category;
        Accent = accent;
        Background = background;
        Border = border;
        GlowOpacity = glowOpacity;
        LogoGlowOpacity = logoGlowOpacity;
        Animation = animation;
        BrandForeground = brandForeground;
        ProgressForeground = progressForeground;
    }
}
