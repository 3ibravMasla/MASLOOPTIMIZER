using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace MASLOOPTIMIZER;

public class TweakBundle
{
    [JsonPropertyName("Version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("TotalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("Tweaks")]
    public List<TweakModel> Tweaks { get; set; } = new();
}

public class TweakModel : INotifyPropertyChanged
{
    [JsonPropertyName("Id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("Name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("Category")]
    public string Category { get; set; } = "Загальні твіки";

    [JsonPropertyName("Risk")]
    public string Risk { get; set; } = "Safe"; // UI, Safe, Medium, High

    [JsonPropertyName("Description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("Benefits")]
    public string Benefits { get; set; } = string.Empty;

    [JsonPropertyName("SideEffects")]
    public string SideEffects { get; set; } = string.Empty;

    [JsonPropertyName("CheckScript")]
    public string CheckScript { get; set; } = string.Empty;

    [JsonPropertyName("ApplyScript")]
    public string ApplyScript { get; set; } = string.Empty;

    [JsonPropertyName("RestoreScript")]
    public string RestoreScript { get; set; } = string.Empty;

    private bool _isApplied;
    [JsonIgnore]
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
    [JsonIgnore]
    public bool IsBusy
    {
        get => _isBusy;
        set 
        { 
            _isBusy = value; 
            OnPropertyChanged(); 
        }
    }

    [JsonIgnore]
    public string StatusText => IsApplied ? "🟢 АКТИВНО" : "⚪ СТАНДАРТ";

    [JsonIgnore]
    public string StatusColor => IsApplied ? "#107C41" : "#2A2D3D";

    [JsonIgnore]
    public string ActionButtonText => IsApplied 
        ? "Застосовано ✓" 
        : (Risk == "UI" ? "Застосувати" : "Оптимізувати");

    [JsonIgnore]
    public string RiskColor => Risk switch
    {
        "UI" => "#8A2BE2",
        "Safe" => "#107C41",
        "Medium" => "#D87A00",
        "High" => "#C42B1C",
        _ => "#555555"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}