using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private readonly Action<BlueprintConnectorVMV6, string?>? _onDefaultValueEdited;

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

    /// <summary>
    /// True when this pin belongs to a *definition* node (const/var block declaration).
    /// Such nodes don't participate in wiring — their Value input pin is hidden (P5-A2).
    /// </summary>
    [ObservableProperty]
    private bool _isDefinitionPin;

    /// <summary>True for execution-flow pins (rendered as triangles).</summary>
    public bool IsExecution => PinType == PinType.Execution;

    /// <summary>Hex colour derived from PinType for binding.</summary>
    public string PinTypeColorHex => GetHexColorForPinType(PinType);

    /// <summary>Human-readable pin type name for tooltips.</summary>
    public string PinTypeText => IsExecution ? "Exec" : PinType.ToString();

    /// <summary>Border colour: red when CanConnect is false (hover-preview rejection), else PinType colour.</summary>
    public string EffectiveBorderColorHex => CanConnect ? PinTypeColorHex : "#F44336";

    /// <summary>
    /// True on an unwired, non-Exec input pin — drives the inline default-value editor
    /// (P5-A1). Editing the value is routed back to the Contract via the constructor callback.
    /// </summary>
    public bool ShowDefaultValue => !IsConnected && !IsExecution && Flow == ConnectorFlow.Input;

    /// <summary>
    /// True on an unwired Exec output pin — the exec path dangles here (P5-B1). For a
    /// sub-scope tail this is the *natural* end (back to the control-flow node's End);
    /// the ground icon visualises that instead of implying an error.
    /// </summary>
    public bool IsDanglingExec => IsExecution && !IsConnected && Flow == ConnectorFlow.Output;

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
        : this(null) { }

    public BlueprintConnectorVMV6(Action<BlueprintConnectorVMV6, string?>? onDefaultValueEdited)
    {
        _onDefaultValueEdited = onDefaultValueEdited;
        // CanConnect / IsConnected live on the base class; forward their changes to derived visuals.
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CanConnect))
                OnPropertyChanged(nameof(EffectiveBorderColorHex));
            if (e.PropertyName == nameof(IsConnected))
            {
                OnPropertyChanged(nameof(ShowDefaultValue));
                OnPropertyChanged(nameof(IsDanglingExec));
            }
        };
    }

    partial void OnPinTypeChanged(PinType value)
    {
        OnPropertyChanged(nameof(IsExecution));
        OnPropertyChanged(nameof(PinTypeColorHex));
        OnPropertyChanged(nameof(PinTypeText));
        OnPropertyChanged(nameof(EffectiveBorderColorHex));
        OnPropertyChanged(nameof(ShowDefaultValue));
        OnPropertyChanged(nameof(IsDanglingExec));
    }

    partial void OnDefaultValueChanged(string? value)
        => _onDefaultValueEdited?.Invoke(this, value);
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
    private readonly Action<BlueprintNodeVMV6, string?>? _onCommentCommitted;

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

    /// <summary>Whether inline comment editing is active (P5-B2).</summary>
    [ObservableProperty]
    private bool _isEditingComment;

    /// <summary>Temporary text during inline comment editing (P5-B2).</summary>
    [ObservableProperty]
    private string _commentEditText = string.Empty;

    public BlueprintNodeVMV6()
        : this(null) { }

    public BlueprintNodeVMV6(Action<BlueprintNodeVMV6, string?>? onCommentCommitted)
    {
        _onCommentCommitted = onCommentCommitted;
    }

    /// <summary>Begins inline comment editing.</summary>
    [RelayCommand]
    private void StartEditComment()
    {
        CommentEditText = Comment ?? string.Empty;
        IsEditingComment = true;
    }

    /// <summary>Commits the inline comment text and writes it back to the Contract.</summary>
    [RelayCommand]
    private void CommitComment()
    {
        IsEditingComment = false;
        Comment = CommentEditText;
    }

    /// <summary>Discards the inline comment edit.</summary>
    [RelayCommand]
    private void CancelEditComment()
    {
        IsEditingComment = false;
    }

    /// <summary>
    /// True for *definition* nodes (const/var block declarations). Definition nodes
    /// have no Exec pins and no connections — they live in the initialisation region,
    /// unlike usage nodes which are wired into the exec chain.
    /// </summary>
    [ObservableProperty]
    private bool _isDefinition;

    /// <summary>Header title for definition nodes (e.g. "def guessNum").</summary>
    public string DefinitionTitle => IsDefinition ? $"def {DisplayTitle}" : DisplayTitle;

    /// <summary>
    /// Header background hex — definition nodes render translucent (alpha ~40%)
    /// to visually separate the initialisation region from the exec-wired usage nodes.
    /// </summary>
    public string EffectiveHeaderColorHex =>
        IsDefinition && HeaderColorHex.Length == 7
            ? HeaderColorHex + "66"
            : HeaderColorHex;

    /// <summary>Whether this node is currently executing (debug highlight).</summary>
    [ObservableProperty]
    private bool _isExecuting;

    /// <summary>Whether a breakpoint is set on this node.</summary>
    [ObservableProperty]
    private bool _isBreakpoint;

    /// <summary>True after this node has finished executing during debug.</summary>
    [ObservableProperty]
    private bool _executionCompleted;

    /// <summary>True if this is a control-flow node (Branch/Each/While/Switch).</summary>
    public bool IsControlFlow
        => NodeType == BlueprintNodeType.BuiltinFunction
           && FunctionName is "Branch" or "Each" or "While" or "Switch";

    /// <summary>True if this is a terminator node (break/continue).</summary>
    public bool IsTerminator
        => NodeType == BlueprintNodeType.BuiltinFunction
           && FunctionName is "break" or "continue";

    /// <summary>Terminator glyph (break ⏹ / continue ⏭), shown on the node header (P5-B1).</summary>
    public string? TerminatorGlyph => FunctionName switch
    {
        "break" => "⏹",
        "continue" => "⏭",
        _ => null,
    };

    /// <summary>Terminator glyph colour (break red / continue orange).</summary>
    public string? TerminatorColorHex => FunctionName switch
    {
        "break" => "#F44336",
        "continue" => "#FF9800",
        _ => null,
    };

    /// <summary>Whether the node has a non-empty comment (for UI indicator).</summary>
    public bool HasComment => !string.IsNullOrEmpty(Comment);

    /// <summary>Comment rendered as a leading-`//` bubble under the header (R5).</summary>
    public string CommentBubbleText => Comment != null ? $"// {Comment}" : string.Empty;

    /// <summary>Shows the comment bubble when a comment exists and inline editing is off (R5).</summary>
    public bool ShowCommentBubble => HasComment && !IsEditingComment;

    /// <summary>Toggles the breakpoint flag on this node (right-click menu).</summary>
    [RelayCommand]
    private void ToggleBreakpoint()
    {
        IsBreakpoint = !IsBreakpoint;
    }

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

    /// <summary>Debug highlight border colour hex (null when no debug state active).</summary>
    public string? DebugBorderBrushHex =>
        IsExecuting ? "#FFEB3B" :
        IsBreakpoint ? "#F44336" :
        ExecutionCompleted ? "#4CAF50" :
        null;

    /// <summary>Debug highlight border thickness (0 when no debug state).</summary>
    public double DebugBorderThickness =>
        IsExecuting ? 3.0 : IsBreakpoint ? 2.0 : ExecutionCompleted ? 1.5 : 0.0;

    partial void OnIsExecutingChanged(bool value)
    {
        OnPropertyChanged(nameof(DebugBorderBrushHex));
        OnPropertyChanged(nameof(DebugBorderThickness));
    }

    partial void OnIsBreakpointChanged(bool value)
    {
        OnPropertyChanged(nameof(DebugBorderBrushHex));
        OnPropertyChanged(nameof(DebugBorderThickness));
    }

    partial void OnExecutionCompletedChanged(bool value)
    {
        OnPropertyChanged(nameof(DebugBorderBrushHex));
        OnPropertyChanged(nameof(DebugBorderThickness));
    }

    partial void OnIsDefinitionChanged(bool value)
    {
        OnPropertyChanged(nameof(DefinitionTitle));
        OnPropertyChanged(nameof(EffectiveHeaderColorHex));
    }

    partial void OnHeaderColorHexChanged(string value)
        => OnPropertyChanged(nameof(EffectiveHeaderColorHex));

    partial void OnCommentChanged(string? value)
    {
        OnPropertyChanged(nameof(HasComment));
        OnPropertyChanged(nameof(CommentBubbleText));
        OnPropertyChanged(nameof(ShowCommentBubble));
        // Any comment change (inline edit or programmatic) is written back to the Contract.
        _onCommentCommitted?.Invoke(this, value);
    }

    partial void OnIsEditingCommentChanged(bool value)
        => OnPropertyChanged(nameof(ShowCommentBubble));
}
