using System;
using System.IO;

namespace MASLOOPTIMIZER;

public static class AppPaths
{
    // Головна папка: C:\ProgramData\MASLOOPTIMIZER
    public static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "MASLOOPTIMIZER"
    );

    public static readonly string Logs = Path.Combine(Root, "Logs");
    public static readonly string Backups = Path.Combine(Root, "Backups");
    public static readonly string Security = Path.Combine(Root, "Security");
    public static readonly string Presets = Path.Combine(Root, "Presets");
    public static readonly string Config = Path.Combine(Root, "Config");

    // Файли
    public static readonly string HistoryLogFile = Path.Combine(Logs, "history.json");
    public static readonly string AppLogFile = Path.Combine(Logs, "app.log");
    public static readonly string SettingsFile = Path.Combine(Config, "settings.json");

    static AppPaths()
    {
        EnsureDirectories();
    }

    public static void EnsureDirectories()
    {
        try
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Logs);
            Directory.CreateDirectory(Backups);
            Directory.CreateDirectory(Security);
            Directory.CreateDirectory(Presets);
            Directory.CreateDirectory(Config);
        }
        catch { }
    }
}