using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NodifyM.Avalonia.ViewModelBase;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// Replaces ConnectorViewModel + _connectorPinTypes dictionary.
/// StrokeColorHex is bound directly to Connection.Stroke via DataTemplate.
/// Subscribes to source connector's PinType changes for dynamic color updates.
/// </summary>
public partial class BlueprintConnectionVM : ConnectionViewModelBase
{
    /// <summary>Stroke color hex derived from source connector's PinType</summary>
    [ObservableProperty]
    private string _strokeColorHex = "#FFFFFF";

    private readonly BlueprintConnectorVM? _sourceConnector;

    public BlueprintConnectionVM(NodifyEditorViewModelBase editor,
        BlueprintConnectorVM source, BlueprintConnectorVM target)
        : base(editor, source, target)
    {
        _sourceConnector = source;
        StrokeColorHex = source.PinTypeColorHex;
        source.PropertyChanged += OnSourceConnectorPropertyChanged;
    }

    public BlueprintConnectionVM(NodifyEditorViewModelBase editor,
        BlueprintConnectorVM source, BlueprintConnectorVM target, string text)
        : base(editor, source, target, text)
    {
        _sourceConnector = source;
        StrokeColorHex = source.PinTypeColorHex;
        source.PropertyChanged += OnSourceConnectorPropertyChanged;
    }

    private void OnSourceConnectorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BlueprintConnectorVM.PinTypeColorHex) && _sourceConnector != null)
        {
            StrokeColorHex = _sourceConnector.PinTypeColorHex;
        }
    }
}
