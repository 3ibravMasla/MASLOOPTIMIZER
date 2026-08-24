using System.Windows;
using System.Windows.Media;
using Application = System.Windows.Application;

namespace MASLOOPTIMIZER;

public static class ThemeManager
{
    public static bool IsDarkTheme { get; private set; } = true;

    public static void ApplyTheme(bool isDark)
    {
        IsDarkTheme = isDark;

        Application.Current?.Dispatcher.Invoke(() =>
        {
            var res = Application.Current.Resources;

            if (isDark)
            {
                // Cyber Stealth Dark
                SetBrush(res, "WindowBg", "#0B0C10");
                SetBrush(res, "HeaderBg", "#12141C");
                SetBrush(res, "HeaderBorder", "#1E2230");
                SetBrush(res, "SidebarBg", "#10121A");
                SetBrush(res, "SidebarBorder", "#1A1E2B");
                SetBrush(res, "CardBg", "#151722");
                SetBrush(res, "CardBorder", "#212536");
                SetBrush(res, "ActionBtnBg", "#181B26");
                SetBrush(res, "ActionBtnBorder", "#262C3E");
                SetBrush(res, "NavBtnBg", "Transparent");
                SetBrush(res, "NavBtnHover", "#181B28");
                SetBrush(res, "NavBtnActive", "#1E2333");
                SetBrush(res, "NavBtnBorderActive", "#00FF9D");
                SetBrush(res, "TextPrimary", "#F1F5F9");
                SetBrush(res, "TextSecondary", "#94A3B8");
                SetBrush(res, "TextMuted", "#64748B");
                SetBrush(res, "StatusBg", "#12141C");
                SetBrush(res, "StatusBorder", "#1E2230");
                SetBrush(res, "AccentGreen", "#00FF9D");
                SetBrush(res, "BadgeBg", "#161924");
                SetBrush(res, "BadgeText", "#94A3B8");
                SetBrush(res, "ProgressTrack", "#181B26");
                SetBrush(res, "ProgressFill", "#00FF9D");
                SetBrush(res, "ChipBg", "#1E2235");
                SetBrush(res, "ChipBorder", "#334155");
                SetBrush(res, "ChipText", "#CBD5E1");
                SetBrush(res, "ChipHoverBg", "#2A344A");
                SetBrush(res, "ChipActiveBg", "#0078D4");
                SetBrush(res, "ChipActiveBorder", "#38BDF8");
                SetBrush(res, "ChipActiveText", "#FFFFFF");
                SetBrush(res, "StatusStdBg", "#2A2D3D");
                SetBrush(res, "StatusStdText", "#94A3B8");
            }
            else
            {
                // Nordic Soft Slate
                SetBrush(res, "WindowBg", "#E2E8F0");
                SetBrush(res, "HeaderBg", "#ECEFF4");
                SetBrush(res, "HeaderBorder", "#CBD5E1");
                SetBrush(res, "SidebarBg", "#E8ECF2");
                SetBrush(res, "SidebarBorder", "#CBD5E1");
                SetBrush(res, "CardBg", "#F8FAFC");
                SetBrush(res, "CardBorder", "#CBD5E1");
                SetBrush(res, "ActionBtnBg", "#E2E8F0");
                SetBrush(res, "ActionBtnBorder", "#CBD5E1");
                SetBrush(res, "NavBtnBg", "Transparent");
                SetBrush(res, "NavBtnHover", "#CBD5E1");
                SetBrush(res, "NavBtnActive", "#F1F5F9");
                SetBrush(res, "NavBtnBorderActive", "#0284C7");
                SetBrush(res, "TextPrimary", "#0F172A");
                SetBrush(res, "TextSecondary", "#334155");
                SetBrush(res, "TextMuted", "#64748B");
                SetBrush(res, "StatusBg", "#ECEFF4");
                SetBrush(res, "StatusBorder", "#CBD5E1");
                SetBrush(res, "AccentGreen", "#0284C7");
                SetBrush(res, "BadgeBg", "#E2E8F0");
                SetBrush(res, "BadgeText", "#1E293B");
                SetBrush(res, "ProgressTrack", "#CBD5E1");
                SetBrush(res, "ProgressFill", "#0284C7");
                SetBrush(res, "ChipBg", "#E2E8F0");
                SetBrush(res, "ChipBorder", "#CBD5E1");
                SetBrush(res, "ChipText", "#1E293B");
                SetBrush(res, "ChipHoverBg", "#D8E1E8");
                SetBrush(res, "ChipActiveBg", "#0284C7");
                SetBrush(res, "ChipActiveBorder", "#0369A1");
                SetBrush(res, "ChipActiveText", "#FFFFFF");
                SetBrush(res, "StatusStdBg", "#E2E8F0");
                SetBrush(res, "StatusStdText", "#475569");
            }
        });
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
}