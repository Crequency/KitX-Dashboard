using CommunityToolkit.Mvvm.ComponentModel;
using NodifyM.Avalonia.ViewModelBase;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// Replaces ConnectorViewModel + _connectorPinTypes dictionary.
/// StrokeColorHex is bound directly to Connection.Stroke via DataTemplate.
/// </summary>
public partial class BlueprintConnectionVM : ConnectionViewModelBase
{
    /// <summary>Stroke color hex derived from source connector's PinType</summary>
    [ObservableProperty]
    private string _strokeColorHex = "#FFFFFF";

    public BlueprintConnectionVM(NodifyEditorViewModelBase editor,
        BlueprintConnectorVM source, BlueprintConnectorVM target)
        : base(editor, source, target)
    {
        StrokeColorHex = source.PinTypeColorHex;
    }

    public BlueprintConnectionVM(NodifyEditorViewModelBase editor,
        BlueprintConnectorVM source, BlueprintConnectorVM target, string text)
        : base(editor, source, target, text)
    {
        StrokeColorHex = source.PinTypeColorHex;
    }
}
