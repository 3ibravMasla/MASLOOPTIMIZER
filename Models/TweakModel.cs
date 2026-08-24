using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace MASLOOPTIMIZER;

public enum TweakActionType
{
    Registry,
    Service,
    Command
}

public class RegistryAction
{
    [JsonPropertyName("Hive")]
    public string Hive { get; set; } = "HKCU"; // HKCU або HKLM

    [JsonPropertyName("KeyPath")]
    public string KeyPath { get; set; } = string.Empty;

    [JsonPropertyName("ValueName")]
    public string ValueName { get; set; } = string.Empty;

    [JsonPropertyName("ValueKind")]
    public string ValueKind { get; set; } = "DWord"; // DWord, String, MultiString

    [JsonPropertyName("ApplyValue")]
    public object? ApplyValue { get; set; }

    [JsonPropertyName("RestoreValue")]
    public object? RestoreValue { get; set; }

    [JsonPropertyName("DeleteOnRestore")]
    public bool DeleteOnRestore { get; set; } = false;
}

public class ServiceAction
{
    [JsonPropertyName("ServiceName")]
    public string ServiceName { get; set; } = string.Empty;

    [JsonPropertyName("ApplyStartup")]
    public string ApplyStartup { get; set; } = "Disabled"; // Disabled, Manual, Automatic

    [JsonPropertyName("RestoreStartup")]
    public string RestoreStartup { get; set; } = "Automatic";

    [JsonPropertyName("StopOnApply")]
    public bool StopOnApply { get; set; } = true;

    [JsonPropertyName("StartOnRestore")]
    public bool StartOnRestore { get; set; } = false;
}

public class CommandAction
{
    [JsonPropertyName("CheckCmd")]
    public string CheckCmd { get; set; } = string.Empty;

    [JsonPropertyName("CheckExpected")]
    public string CheckExpected { get; set; } = string.Empty;

    [JsonPropertyName("ApplyCmd")]
    public string ApplyCmd { get; set; } = string.Empty;

    [JsonPropertyName("RestoreCmd")]
    public string RestoreCmd { get; set; } = string.Empty;
}

public class TweakModel : INotifyPropertyChanged
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("Name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("Category")]
    public string Category { get; set; } = "Загальні";

    [JsonPropertyName("Risk")]
    public string Risk { get; set; } = "Safe"; // UI, Safe, Medium, High

    [JsonPropertyName("Description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("Benefits")]
    public string Benefits { get; set; } = string.Empty;

    [JsonPropertyName("SideEffects")]
    public string SideEffects { get; set; } = string.Empty;

    [JsonPropertyName("Type")]
    public string Type { get; set; } = "Registry"; // Registry, Service, Command

    [JsonPropertyName("RegistryActions")]
    public List<RegistryAction> RegistryActions { get; set; } = new();

    [JsonPropertyName("ServiceActions")]
    public List<ServiceAction> ServiceActions { get; set; } = new();

    [JsonPropertyName("CommandAction")]
    public CommandAction? CommandAction { get; set; }

    // Резервні поля для зворотної сумісності зі старими JSON
    [JsonPropertyName("CheckScript")]
    public string? CheckScript { get; set; }

    [JsonPropertyName("ApplyScript")]
    public string? ApplyScript { get; set; }

    [JsonPropertyName("RestoreScript")]
    public string? RestoreScript { get; set; }

    private bool _isApplied;
    public bool IsApplied
    {
        get => _isApplied;
        set
        {
            if (_isApplied != value)
            {
                _isApplied = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(ActionButtonText));
            }
        }
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy != value)
            {
                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActionButtonText));
            }
        }
    }

    public string StatusText => IsApplied ? "🟢 ОПТИМІЗОВАНО" : "⚪ СТАНДАРТ";
    public string StatusColor => IsApplied ? "#107C41" : "#2A2D3D";

    public string ActionButtonText
    {
        get
        {
            if (IsBusy) return "⏳ Обробка...";
            return IsApplied ? "↩️ Відновити" : "⚡ Застосувати";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class TweakBundle
{
    [JsonPropertyName("Version")]
    public string Version { get; set; } = "2.0";

    [JsonPropertyName("GeneratedAt")]
    public string GeneratedAt { get; set; } = string.Empty;

    [JsonPropertyName("TotalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("Tweaks")]
    public List<TweakModel> Tweaks { get; set; } = new();
}