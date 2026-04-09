using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using KitX.Core.Contract.Workflow;
using NodifyM.Avalonia.ViewModelBase;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// Replaces NodeViewModel + _nodeIdMap + BlueprintNodeContentViewModel.
/// Input/Output collections inherited from NodeViewModelBase hold BlueprintConnectorVM instances.
/// </summary>
public partial class BlueprintNodeVM : NodeViewModelBase
{
    /// <summary>Original BlueprintNode.Id for round-trip export</summary>
    [ObservableProperty]
    private string _blueprintNodeId = string.Empty;

    /// <summary>Node type for category color rendering</summary>
    [ObservableProperty]
    private BlueprintNodeType _nodeType = BlueprintNodeType.Entry;

    /// <summary>Header color hex (category primary color)</summary>
    [ObservableProperty]
    private string _categoryColor = "#607D8B";

    /// <summary>Body color hex (category lighter color)</summary>
    [ObservableProperty]
    private string _categoryColorLight = "#455A64";

    /// <summary>Display title shown in node header</summary>
    [ObservableProperty]
    private string _displayTitle = string.Empty;

    /// <summary>Node name used for type inference and round-trip export</summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// Selected constant type for Const nodes. Bound to a ComboBox in the node body.
    /// Changing this also updates the output connector's PinType and Metadata.
    /// </summary>
    [ObservableProperty]
    private string _constType = "int";

    /// <summary>
    /// Constant value for Const nodes. Bound to a TextBox in the node body.
    /// Synced bidirectionally with the output connector's DefaultValue.
    /// </summary>
    [ObservableProperty]
    private string _constValue = string.Empty;

    /// <summary>Available constant type options for the dropdown</summary>
    public static List<string> ConstTypeOptions { get; } = ["int", "double", "string", "bool"];

    /// <summary>Whether this node should show the ConstType selector (only Const nodes)</summary>
    public bool ShowConstTypeSelector => NodeType == BlueprintNodeType.Const;

    /// <summary>Whether this node should show the VariableType selector (only Variable nodes)</summary>
    public bool ShowVarTypeSelector => NodeType == BlueprintNodeType.Variable;

    /// <summary>
    /// Variable type for Variable nodes. Bound to a ComboBox in the node body.
    /// Changing this propagates the type to all Get/Set nodes referencing this variable.
    /// </summary>
    [ObservableProperty]
    private string _varType = "int";

    /// <summary>
    /// Variable name for Variable nodes (and Get/Set nodes for type resolution).
    /// </summary>
    [ObservableProperty]
    private string _varName = string.Empty;

    /// <summary>
    /// Callback invoked when VarType changes — used by BlueprintEditorViewModel
    /// to propagate the type to all Get/Set nodes referencing this variable.
    /// </summary>
    internal System.Action<BlueprintNodeVM>? VarTypeChangedCallback { get; set; }

    /// <summary>
    /// Extra metadata for round-trip preservation.
    /// Keyed by property name, e.g. "ConstType" → "int", "ConstValue" → "5".
    /// Used by ConstNode, CallNode, etc. to preserve domain-specific fields
    /// that aren't represented in connectors or display title.
    /// </summary>
    public Dictionary<string, string> Metadata { get; } = [];

    /// <summary>
    /// Callback invoked when ConstType changes — used by BlueprintEditorViewModel
    /// to update the output connector's PinType.
    /// </summary>
    internal System.Action<BlueprintNodeVM>? ConstTypeChangedCallback { get; set; }

    partial void OnConstTypeChanged(string value)
    {
        Metadata["ConstType"] = value;

        // Update output connector PinType to match
        var outputConnector = Output?.GetEnumerator();
        if (outputConnector?.MoveNext() == true && outputConnector.Current is BlueprintConnectorVM conn)
        {
            conn.PinType = ConstTypeToPinType(value);
        }

        ConstTypeChangedCallback?.Invoke(this);
    }

    partial void OnConstValueChanged(string value)
    {
        // Sync to output connector's DefaultValue for round-trip export
        var outputConnector = Output?.GetEnumerator();
        if (outputConnector?.MoveNext() == true && outputConnector.Current is BlueprintConnectorVM conn)
        {
            conn.DefaultValue = string.IsNullOrEmpty(value) ? null : value;
        }
    }

    partial void OnVarTypeChanged(string value)
    {
        Metadata["VarType"] = value;
        VarTypeChangedCallback?.Invoke(this);
    }

    /// <summary>Converts a ConstType string to the corresponding PinType</summary>
    public static PinType ConstTypeToPinType(string constType) => constType?.ToLowerInvariant() switch
    {
        "int" or "integer" => PinType.Integer,
        "bool" or "boolean" => PinType.Boolean,
        "double" or "float" or "number" => PinType.Double,
        "string" => PinType.String,
        _ => PinType.Any
    };

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(NodeType))
        {
            OnPropertyChanged(nameof(ShowConstTypeSelector));
            OnPropertyChanged(nameof(ShowVarTypeSelector));
        }
    }

    /// <summary>Returns (Primary, Light) hex color pair for a node category</summary>
    public static (string Primary, string Light) GetCategoryColors(BlueprintNodeType type) => type switch
    {
        BlueprintNodeType.Entry => ("#4CAF50", "#2E7D32"),          // Green
        BlueprintNodeType.Branch or BlueprintNodeType.Loop
            or BlueprintNodeType.Break => ("#FF9800", "#BF6E00"),   // Orange
        BlueprintNodeType.Const or BlueprintNodeType.Get
            or BlueprintNodeType.Set => ("#2196F3", "#1565C0"),     // Blue
        BlueprintNodeType.Variable => ("#009688", "#00796B"),       // Teal
        BlueprintNodeType.Call or BlueprintNodeType.CallHelper
            or BlueprintNodeType.Print or BlueprintNodeType.Pause => ("#9C27B0", "#7B1FA2"), // Purple
        _ => ("#607D8B", "#455A64")                                 // Gray fallback
    };
}
