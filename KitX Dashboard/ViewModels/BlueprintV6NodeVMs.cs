using CommunityToolkit.Mvvm.ComponentModel;
using KitX.Core.Contract.Workflow;
using NodifyM.Avalonia.ViewModelBase;

namespace KitX.Dashboard.ViewModels;

// ─────────────────────────────────────────────────────────────────────────────
// V6 Blueprint connector (pin) ViewModel.
//
// Carries the PinType (drives visual colour + Exec/Data layer distinction) and the
// original BlueprintPin.Id for round-trip. Simpler than the v5 connector: no inline
// default-value editor wiring (P1 is read-only BP). Exec pins render as triangles,
// data pins as circles — the distinction is made in the XAML DataTemplate via the
// IsExecution property.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Connector (pin) ViewModel for the v6 blueprint editor. Distinguishes Exec-flow
/// pins from Data-flow pins via <see cref="PinType"/>.
/// </summary>
public partial class BlueprintConnectorVMV6 : ConnectorViewModelBase
{
    /// <summary>Pin data type — drives colour and Exec/Data visual distinction.</summary>
    [ObservableProperty]
    private PinType _pinType = PinType.Any;

    /// <summary>Original BlueprintPin.Id for round-trip export.</summary>
    [ObservableProperty]
    private string? _originalPinId;

    /// <summary>Default value for data input pins (inline literal).</summary>
    [ObservableProperty]
    private string? _defaultValue;

    /// <summary>Runtime value set during debug execution, shown on hover.</summary>
    [ObservableProperty]
    private string? _runtimeValue;

    /// <summary>True for execution-flow pins (rendered as triangles).</summary>
    public bool IsExecution => PinType == PinType.Execution;

    /// <summary>Hex colour derived from PinType for binding.</summary>
    public string PinTypeColorHex => GetHexColorForPinType(PinType);

    /// <summary>Human-readable pin type name for tooltips.</summary>
    public string PinTypeText => IsExecution ? "Exec" : PinType.ToString();

    /// <summary>Border colour: red when CanConnect is false (hover-preview rejection), else PinType colour.</summary>
    public string EffectiveBorderColorHex => CanConnect ? PinTypeColorHex : "#F44336";

    /// <summary>Maps PinType to hex colour for visual rendering.</summary>
    public static string GetHexColorForPinType(PinType pinType) => pinType switch
    {
        PinType.Execution => "#32CD32",     // LimeGreen
        PinType.Boolean => "#00FFFF",       // Cyan
        PinType.Integer => "#FFA500",       // Orange
        PinType.Double => "#9370DB",        // MediumPurple
        PinType.String => "#FFFF00",        // Yellow
        PinType.Json => "#4FC3F7",          // Light Blue
        PinType.Dict => "#A9A9A9",          // DarkGray
        _ => "#FFFFFF"                      // White (Any)
    };

    public BlueprintConnectorVMV6()
    {
        // CanConnect lives on the base class; forward its changes to EffectiveBorderColorHex.
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CanConnect))
                OnPropertyChanged(nameof(EffectiveBorderColorHex));
        };
    }

    partial void OnPinTypeChanged(PinType value)
    {
        OnPropertyChanged(nameof(IsExecution));
        OnPropertyChanged(nameof(PinTypeColorHex));
        OnPropertyChanged(nameof(PinTypeText));
        OnPropertyChanged(nameof(EffectiveBorderColorHex));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// V6 Blueprint connection (edge) ViewModel.
//
// Stroke colour follows the source connector's PinType. Exec connections render as
// solid thick lines; data connections as thinner lines — the IsExecution flag drives
// the DataTemplate choice (stroke dash array + thickness).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Connection ViewModel for the v6 blueprint editor. Stroke colour follows the source
/// connector; IsExecution distinguishes Exec-flow from Data-flow connections.
/// </summary>
public partial class BlueprintConnectionVMV6 : ConnectionViewModelBase
{
    /// <summary>Stroke colour hex derived from the source connector's PinType.</summary>
    [ObservableProperty]
    private string _strokeColorHex = "#FFFFFF";

    /// <summary>Whether this is an execution-flow connection (solid thick line).</summary>
    public bool IsExecution { get; }

    private readonly BlueprintConnectorVMV6? _sourceConnector;

    public BlueprintConnectionVMV6(NodifyEditorViewModelBase editor,
        BlueprintConnectorVMV6 source, BlueprintConnectorVMV6 target)
        : base(editor, source, target)
    {
        _sourceConnector = source;
        StrokeColorHex = source.PinTypeColorHex;
        IsExecution = source.IsExecution;
        source.PropertyChanged += OnSourceConnectorPropertyChanged;
    }

    private void OnSourceConnectorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BlueprintConnectorVMV6.PinTypeColorHex) && _sourceConnector != null)
            StrokeColorHex = _sourceConnector.PinTypeColorHex;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// V6 Blueprint node ViewModel.
//
// Leaner than the v5 BlueprintNodeVM: the v6 flat + structured-graph model eliminates
// Block/Scope/Goto complexity. Carries the node type, function name (for control-flow
// nodes), display title, and header colour. Comment support is included for the
// GroupComment/Comment annotation round-trip.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Node ViewModel for the v6 blueprint editor. Carries type metadata and visual
/// properties; Input/Output collections hold <see cref="BlueprintConnectorVMV6"/>.
/// </summary>
public partial class BlueprintNodeVMV6 : NodeViewModelBase
{
    /// <summary>Original BlueprintNode.Id for round-trip export.</summary>
    [ObservableProperty]
    private string _blueprintNodeId = string.Empty;

    /// <summary>Node type for category-colour rendering.</summary>
    [ObservableProperty]
    private BlueprintNodeType _nodeType;

    /// <summary>BuiltinFunctionNode.FunctionName (e.g. "Branch", "Print", "break").</summary>
    [ObservableProperty]
    private string? _functionName;

    /// <summary>Display title shown in the node header.</summary>
    [ObservableProperty]
    private string _displayTitle = string.Empty;

    /// <summary>Header colour hex (category colour).</summary>
    [ObservableProperty]
    private string _headerColorHex = "#607D8B";

    /// <summary>User comment (from BlueprintNode.Comment or GroupComment).</summary>
    [ObservableProperty]
    private string? _comment;

    /// <summary>Whether this node is currently executing (debug highlight).</summary>
    [ObservableProperty]
    private bool _isExecuting;

    /// <summary>Whether a breakpoint is set on this node.</summary>
    [ObservableProperty]
    private bool _isBreakpoint;

    /// <summary>True if this is a control-flow node (Branch/Each/While/Switch).</summary>
    public bool IsControlFlow
        => NodeType == BlueprintNodeType.BuiltinFunction
           && FunctionName is "Branch" or "Each" or "While" or "Switch";

    /// <summary>True if this is a terminator node (break/continue).</summary>
    public bool IsTerminator
        => NodeType == BlueprintNodeType.BuiltinFunction
           && FunctionName is "break" or "continue";

    /// <summary>Whether the node has a non-empty comment (for UI indicator).</summary>
    public bool HasComment => !string.IsNullOrEmpty(Comment);

    /// <summary>Category colour for a given node type + function name.</summary>
    public static string GetHeaderColor(BlueprintNodeType type, string? functionName) => type switch
    {
        BlueprintNodeType.Entry => "#4CAF50",       // Green
        BlueprintNodeType.PluginTrigger => "#4CAF50",
        BlueprintNodeType.Const => "#2196F3",        // Blue
        BlueprintNodeType.Variable => "#009688",     // Teal
        BlueprintNodeType.Call => "#9C27B0",         // Purple
        BlueprintNodeType.CallHelper => "#9C27B0",
        BlueprintNodeType.BuiltinFunction => functionName switch
        {
            "Branch" or "Each" or "While" or "Switch" => "#FF9800",  // Orange (control flow)
            "break" or "continue" => "#F44336",                        // Red (terminator)
            _ => "#8BC34A",                                             // Light green (ordinary builtin)
        },
        _ => "#607D8B"
    };

    partial void OnCommentChanged(string? value)
        => OnPropertyChanged(nameof(HasComment));
}
