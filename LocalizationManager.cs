using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MASLOOPTIMIZER;

public class LocalizationManager : INotifyPropertyChanged
{
    private static readonly Lazy<LocalizationManager> _instance = new(() => new LocalizationManager());
    public static LocalizationManager Instance => _instance.Value;

    public string CurrentLanguage { get; private set; } = "UK";

    // Словник швидкого доступу
    private readonly Dictionary<string, Dictionary<string, string>> _translations = new()
    {
        ["UK"] = new()
        {
            ["AppTitle"] = "MASLOOPTIMIZER",
            ["DarkTheme"] = "🌙 Темна тема",
            ["LightTheme"] = "☀️ Світла тема",
            ["Ready"] = "Система готова до налаштування.",
            ["Apply"] = "Оптимізувати",
            ["Applied"] = "Застосовано ✓",
            ["Restore"] = "Відновити",
            ["SafePack"] = "⚡ 1-Click Safe Pack",
            ["Search"] = "🔍 Пошук...",
            ["DnsTitle"] = "🌐 ОПТИМІЗАТОР МЕРЕЖЕВИХ ЗАТРИМОК DNS",
            ["CleanerTitle"] = "🧹 ГЛИБОКЕ ОЧИЩЕННЯ ТА ЗВІЛЬНЕННЯ SSD"
        },
        ["EN"] = new()
        {
            ["AppTitle"] = "MASLOOPTIMIZER",
            ["DarkTheme"] = "🌙 Dark Theme",
            ["LightTheme"] = "☀️ Light Theme",
            ["Ready"] = "System is ready for optimization.",
            ["Apply"] = "Optimize",
            ["Applied"] = "Applied ✓",
            ["Restore"] = "Restore",
            ["SafePack"] = "⚡ 1-Click Safe Pack",
            ["Search"] = "🔍 Search...",
            ["DnsTitle"] = "🌐 DNS LATENCY OPTIMIZER",
            ["CleanerTitle"] = "🧹 DEEP SSD DISK CLEANER"
        },
        ["DE"] = new()
        {
            ["AppTitle"] = "MASLOOPTIMIZER",
            ["DarkTheme"] = "🌙 Dunkles Design",
            ["LightTheme"] = "☀️ Helles Design",
            ["Ready"] = "System ist bereit für Optimierung.",
            ["Apply"] = "Optimieren",
            ["Applied"] = "Aktiviert ✓",
            ["Restore"] = "Wiederherstellen",
            ["SafePack"] = "⚡ 1-Klick Safe Paket",
            ["Search"] = "🔍 Suchen...",
            ["DnsTitle"] = "🌐 DNS LATENZ OPTIMIERER",
            ["CleanerTitle"] = "🧹 TIEFE SSD-BEREINIGUNG"
        }
    };

    public string this[string key]
    {
        get
        {
            if (_translations.TryGetValue(CurrentLanguage, out var dict) && dict.TryGetValue(key, out var val))
                return val;
            return key;
        }
    }

    public void SetLanguage(string langCode)
    {
        if (_translations.ContainsKey(langCode))
        {
            CurrentLanguage = langCode;
            OnPropertyChanged("Item[]");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}