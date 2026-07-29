using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    /// <summary>
    /// Storage tier for Variable nodes (v5.0). Bound to a ComboBox in the node body.
    /// Changing this updates Metadata["VarKind"].
    /// </summary>
    [ObservableProperty]
    private string _varKind = "PubVar";

    /// <summary>Available VarKind options for the dropdown</summary>
    public static List<string> VarKindOptions { get; } = ["PubVar", "Const", "BlockVar", "LoopIndex"];

    /// <summary>Whether this node should show the ConstType selector (only Const nodes)</summary>
    public bool ShowConstTypeSelector => NodeType == BlueprintNodeType.Const;

    /// <summary>Whether this node should show the VariableType selector (only Variable nodes)</summary>
    public bool ShowVarTypeSelector => NodeType == BlueprintNodeType.Variable;

    /// <summary>Whether this node should show the VarKind selector (only Variable nodes)</summary>
    public bool ShowVarKindSelector => NodeType == BlueprintNodeType.Variable;

    /// <summary>Whether this node has a non-empty comment (for UI indicator visibility)</summary>
    public bool HasComment => !string.IsNullOrEmpty(Comment);

    /// <summary>Whether this node is a BlockNode (for DataTemplate routing)</summary>
    public bool IsBlockNode => NodeType == BlueprintNodeType.Block;

    /// <summary>
    /// Block scope ViewModel for BlockNode sub-graph management (v5.0).
    /// Null for non-BlockNode types.
    /// </summary>
    public BlockNodeScopeVM? BlockScope { get; set; }

    /// <summary>
    /// Whether this node is visible on the canvas.
    /// Used to hide EntryPoint/ExitPoint nodes when their parent BlockNode is collapsed.
    /// Defaults to true.
    /// </summary>
    [ObservableProperty]
    private bool _isVisible = true;

    /// <summary>
    /// Builtin function name, if this node is a BuiltinFunction node.
    /// Used for logic that previously checked specific BlueprintNodeType values.
    /// </summary>
    public string? BuiltinFunctionName { get; set; }

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

    /// <summary>True while this node is the current execution point during debug</summary>
    [ObservableProperty]
    private bool _isExecuting;

    /// <summary>True after this node has been executed during debug</summary>
    [ObservableProperty]
    private bool _executionCompleted;

    /// <summary>True if a debug breakpoint is set on this node</summary>
    [ObservableProperty]
    private bool _isBreakpoint;

    /// <summary>
    /// User-facing comment attached to this node (v5.0 bidirectional comment retention).
    /// Sourced from BS <c>//</c> comments. Null or empty means no comment.
    /// </summary>
    [ObservableProperty]
    private string? _comment;

    /// <summary>Whether inline comment editing is active</summary>
    [ObservableProperty]
    private bool _isEditingComment;

    /// <summary>Temporary text during inline comment editing</summary>
    [ObservableProperty]
    private string _commentEditText = string.Empty;

    /// <summary>Border brush override for debug highlighting</summary>
    public Avalonia.Media.IBrush? BorderBrushOverride =>
        IsExecuting ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.LimeGreen, 0.9) :
        IsBreakpoint ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.Red, 0.7) :
        ExecutionCompleted ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.Gray, 0.4) :
        null;

    /// <summary>Border thickness override for debug highlighting</summary>
    public double BorderThicknessOverride =>
        IsExecuting ? 3.0 :
        IsBreakpoint ? 2.0 :
        0.0;

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
    public Dictionary<string, string> Metadata { get; } = new();

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

    partial void OnVarKindChanged(string value)
    {
        Metadata["VarKind"] = value;
    }

    partial void OnCommentChanged(string? value)
    {
        OnPropertyChanged(nameof(HasComment));
    }

    /// <summary>Begins inline editing of the node comment</summary>
    [RelayCommand]
    private void StartEditComment()
    {
        CommentEditText = Comment ?? string.Empty;
        IsEditingComment = true;
    }

    /// <summary>Commits the inline comment edit</summary>
    [RelayCommand]
    private void CommitComment()
    {
        var trimmed = CommentEditText?.Trim();
        Comment = string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
        IsEditingComment = false;
    }

    /// <summary>Cancels inline comment editing without saving</summary>
    [RelayCommand]
    private void CancelEditComment()
    {
        IsEditingComment = false;
    }

    partial void OnIsExecutingChanged(bool value)
    {
        OnPropertyChanged(nameof(BorderBrushOverride));
        OnPropertyChanged(nameof(BorderThicknessOverride));
    }

    partial void OnExecutionCompletedChanged(bool value)
    {
        OnPropertyChanged(nameof(BorderBrushOverride));
    }

    partial void OnIsBreakpointChanged(bool value)
    {
        OnPropertyChanged(nameof(BorderBrushOverride));
        OnPropertyChanged(nameof(BorderThicknessOverride));
    }

    /// <summary>Converts a ConstType string to the corresponding PinType</summary>
    public static PinType ConstTypeToPinType(string constType) => constType?.ToLowerInvariant() switch
    {
        "int" or "integer" => PinType.Integer,
        "bool" or "boolean" => PinType.Boolean,
        "double" or "float" or "number" => PinType.Double,
        "string" => PinType.String,
        "json" or "jsonelement" => PinType.Json,  // List-Port design §3.3
        "dict" => PinType.Dict,                   // Dict-Type design §2.2
        _ => PinType.Any
    };

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(NodeType))
        {
            OnPropertyChanged(nameof(ShowConstTypeSelector));
            OnPropertyChanged(nameof(ShowVarTypeSelector));
            OnPropertyChanged(nameof(ShowVarKindSelector));
        }
    }

    /// <summary>Returns (Primary, Light) hex color pair for a node category</summary>
    public static (string Primary, string Light) GetCategoryColors(BlueprintNodeType type) => type switch
    {
        BlueprintNodeType.Entry or BlueprintNodeType.PluginTrigger => ("#4CAF50", "#2E7D32"), // Green
        BlueprintNodeType.Const => ("#2196F3", "#1565C0"),                                     // Blue
        BlueprintNodeType.Variable => ("#009688", "#00796B"),                                   // Teal
        BlueprintNodeType.Call or BlueprintNodeType.CallHelper => ("#9C27B0", "#7B1FA2"),      // Purple
        BlueprintNodeType.BuiltinFunction => ("#FF9800", "#BF6E00"),                            // Orange
        BlueprintNodeType.Block => ("#FF5722", "#BF360C"),                                      // Deep Orange
        BlueprintNodeType.EntryPoint => ("#8BC34A", "#558B2F"),                                 // Light Green
        BlueprintNodeType.ExitPoint => ("#FF7043", "#D84315"),                                  // Orange-red
        _ => ("#607D8B", "#455A64")                                                             // Gray fallback
    };

    /// <summary>Returns (Primary, Light) hex color pair based on builtin function name</summary>
    public static (string Primary, string Light) GetBuiltinFunctionColors(string functionName) => functionName switch
    {
        "Print" or "Pause" => ("#9C27B0", "#7B1FA2"), // Purple - I/O
        _ => ("#FF9800", "#BF6E00")                    // Orange - control flow / generic
    };
}
