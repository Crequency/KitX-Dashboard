using CommunityToolkit.Mvvm.ComponentModel;
using KitX.Core.Contract.Workflow;
using NodifyM.Avalonia.ViewModelBase;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// Replaces PinViewModel + _pinTypes dictionary.
/// Each connector carries its own metadata as bindable properties.
/// </summary>
public partial class BlueprintConnectorVM : ConnectorViewModelBase
{
    /// <summary>Pin type for visual rendering and connection validation</summary>
    [ObservableProperty]
    private PinType _pinType = PinType.Any;

    /// <summary>Original BlueprintPin.Id for round-trip export</summary>
    [ObservableProperty]
    private string? _originalPinId;

    /// <summary>Default value for data pins</summary>
    [ObservableProperty]
    private string? _defaultValue;

    /// <summary>Runtime value set during debug execution, shown on hover</summary>
    [ObservableProperty]
    private string? _runtimeValue;

    /// <summary>Whether this is an execution flow pin (triangle shape)</summary>
    public bool IsExecution => PinType == PinType.Execution;

    /// <summary>Hex color derived from PinType for binding</summary>
    public string PinTypeColorHex => GetHexColorForPinType(PinType);

    /// <summary>Human-readable pin type name, for tooltips.</summary>
    public string PinTypeText => PinType.ToString();

    /// <summary>Show default value editor: only for disconnected non-execution data pins</summary>
    public bool ShowDefaultValue => !IsConnected && !IsExecution;

    /// <summary>Maps PinType to hex color for visual rendering</summary>
    public static string GetHexColorForPinType(PinType pinType) => pinType switch
    {
        PinType.Execution => "#32CD32",     // LimeGreen
        PinType.Boolean => "#00FFFF",       // Cyan
        PinType.Integer => "#FFA500",       // Orange
        PinType.Double => "#9370DB",        // MediumPurple
        PinType.String => "#FFFF00",        // Yellow
        PinType.Json => "#4FC3F7",          // Light Blue (List-Port design §2.1)
        _ => "#FFFFFF"                      // White (Any)
    };

    partial void OnPinTypeChanged(PinType value)
    {
        OnPropertyChanged(nameof(IsExecution));
        OnPropertyChanged(nameof(PinTypeColorHex));
        OnPropertyChanged(nameof(ShowDefaultValue));
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(IsConnected))
            OnPropertyChanged(nameof(ShowDefaultValue));
    }
}
