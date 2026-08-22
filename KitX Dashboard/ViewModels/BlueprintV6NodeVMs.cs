using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KitX.Core.Contract.Workflow;
using KitX.Dashboard.Names;
using NodifyM.Avalonia.ViewModelBase;
using Serilog;

namespace KitX.Dashboard.ViewModels;

// ─────────────────────────────────────────────────────────────────────────────
// DictNew key/value pair row ViewModel (T8).
//
// One row in a DictNew definition node's pair editor. KeyText/ValueText map 1:1 to
// the Key{i}/Value{i} input pins' DefaultValue (the key/value literal text), written
// back to the Contract through the editor callback. ApplyFromContract loads a pair
// without invoking callbacks — a load must never write VM state over the Contract.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>A single Key/Value row of a DictNew definition node's pair editor.</summary>
public partial class DictPairRowVM : ObservableObject
{
    private readonly Action<string?, string?>? _onTextEdited;

    /// <summary>Contract pin id of the Key{i} input pin backing this row.</summary>
    public string? KeyPinId { get; internal set; }

    /// <summary>Contract pin id of the Value{i} input pin backing this row.</summary>
    public string? ValuePinId { get; internal set; }

    /// <summary>Key literal text (written to the Key pin's DefaultValue).</summary>
    [ObservableProperty]
    private string? _keyText;

    /// <summary>Value literal text (written to the Value pin's DefaultValue).</summary>
    [ObservableProperty]
    private string? _valueText;

    public DictPairRowVM()
        : this(null) { }

    /// <param name="onTextEdited">Invoked with (pinId, text) on either field edit.</param>
    public DictPairRowVM(Action<string?, string?>? onTextEdited)
    {
        _onTextEdited = onTextEdited;
    }

    partial void OnKeyTextChanged(string? value) => _onTextEdited?.Invoke(KeyPinId, value);

    partial void OnValueTextChanged(string? value) => _onTextEdited?.Invoke(ValuePinId, value);

    /// <summary>Loads a pair straight from the Contract pins — no edit callbacks.</summary>
    public void ApplyFromContract(string? keyPinId, string? valuePinId, string? keyText, string? valueText)
    {
        KeyPinId = keyPinId;
        ValuePinId = valuePinId;
        // Direct field writes are INTENTIONAL: using the generated properties would fire
        // OnKeyTextChanged/OnValueTextChanged and write the loaded values back into the
        // contract (a load must never trigger edit callbacks).
#pragma warning disable MVVMTK0034
        _keyText = keyText;
        _valueText = valueText;
#pragma warning restore MVVMTK0034
        OnPropertyChanged(nameof(KeyText));
        OnPropertyChanged(nameof(ValueText));
    }
}

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

    /// <summary>
    /// True when this dangling Exec output sits INSIDE a While/Each loop body — the
    /// dangling tail means "back to the loop head, condition re-evaluated" (loop-back),
    /// rendered as ↺ instead of the natural-end ground icon ⎍. Set by the Blueprint VM
    /// from the scope analysis (RefreshLoopBodyMarkers).
    /// </summary>
    [ObservableProperty]
    private bool _isLoopBodyDangling;

    /// <summary>Dangling Exec output that is NOT a loop body tail → natural end (⎍).</summary>
    public bool IsDanglingExecNatural => IsDanglingExec && !IsLoopBodyDangling;

    /// <summary>Maps PinType to hex colour for visual rendering. Any (white) is theme-aware:
    /// dark grey on Light themes so lines/pins stay visible, white on Dark.</summary>
    public static string GetHexColorForPinType(PinType pinType) => pinType switch
    {
        PinType.Execution => "#32CD32",     // LimeGreen
        PinType.Boolean => "#00FFFF",       // Cyan
        PinType.Integer => "#FFA500",       // Orange
        PinType.Double => "#9370DB",        // MediumPurple
        PinType.String => "#FFFF00",        // Yellow
        PinType.Json => "#4FC3F7",          // Light Blue
        PinType.Dict => "#A9A9A9",          // DarkGray
        _ => Application.Current?.RequestedThemeVariant == ThemeVariant.Dark ? "#FFFFFF" : "#3A3A3A",  // Any
    };

    /// <summary>Re-evaluates theme-dependent colours after a theme switch (host calls this).</summary>
    public void RefreshThemeColor()
    {
        OnPropertyChanged(nameof(PinTypeColorHex));
        OnPropertyChanged(nameof(EffectiveBorderColorHex));
        OnPropertyChanged(nameof(ShowDefaultValue));
    }

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
                OnPropertyChanged(nameof(IsDanglingExecNatural));
            }
            if (e.PropertyName == nameof(IsLoopBodyDangling))
                OnPropertyChanged(nameof(IsDanglingExecNatural));
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

    /// <summary>Re-evaluates the stroke colour after a theme switch (the source connector's Any colour is theme-aware).</summary>
    public void RefreshStrokeColor()
    {
        if (_sourceConnector != null)
            StrokeColorHex = _sourceConnector.PinTypeColorHex;
    }

    /// <summary>
    /// Releases the subscription on the source connector (D11). Must be called on every
    /// deletion path (DisconnectConnector / DeleteNodes / RemoveConnection); the editor
    /// owns the lifecycle of its connection VMs.
    /// </summary>
    public void Detach()
    {
        if (_sourceConnector != null)
            _sourceConnector.PropertyChanged -= OnSourceConnectorPropertyChanged;
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
    private readonly Action<BlueprintNodeVMV6, string? /*oldName*/, string? /*newName*/, string? /*value*/>? _onDefinitionEdited;
    private readonly Action<BlueprintNodeVMV6, string?>? _onUsageNameEdited;
    private readonly Action<BlueprintNodeVMV6, string?>? _onUsageValueEdited;
    private readonly Action<BlueprintNodeVMV6>? _onAddDictPair;
    private readonly Action<BlueprintNodeVMV6, DictPairRowVM>? _onRemoveDictPair;

    /// <summary>Original BlueprintNode.Id for round-trip export.</summary>
    [ObservableProperty]
    private string _blueprintNodeId = string.Empty;

    /// <summary>Node type for category-colour rendering.</summary>
    [ObservableProperty]
    private BlueprintNodeType _nodeType;

    /// <summary>BuiltinFunctionNode.FunctionName (e.g. BpFunctionNames.Branch, "Print", "break").</summary>
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

    public BlueprintNodeVMV6(Action<BlueprintNodeVMV6, string?>? onCommentCommitted,
        Action<BlueprintNodeVMV6, string?, string?, string?>? onDefinitionEdited)
    {
        _onCommentCommitted = onCommentCommitted;
        _onDefinitionEdited = onDefinitionEdited;
    }

    public BlueprintNodeVMV6(Action<BlueprintNodeVMV6, string?>? onCommentCommitted,
        Action<BlueprintNodeVMV6, string?, string?, string?>? onDefinitionEdited,
        Action<BlueprintNodeVMV6, string?>? onUsageNameEdited,
        Action<BlueprintNodeVMV6, string?>? onUsageValueEdited,
        Action<BlueprintNodeVMV6>? onAddDictPair,
        Action<BlueprintNodeVMV6, DictPairRowVM>? onRemoveDictPair)
    {
        _onCommentCommitted = onCommentCommitted;
        _onDefinitionEdited = onDefinitionEdited;
        _onUsageNameEdited = onUsageNameEdited;
        _onUsageValueEdited = onUsageValueEdited;
        _onAddDictPair = onAddDictPair;
        _onRemoveDictPair = onRemoveDictPair;
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

    // ── Inline usage-name editing (2026-08-03: click-to-edit, same pattern as comment) ──

    /// <summary>Whether the usage name selector is in editing mode (AutoCompleteBox visible).</summary>
    [ObservableProperty]
    private bool _isEditingUsageName;

    /// <summary>Temporary text during usage-name editing (committed via CommitUsageName).</summary>
    [ObservableProperty]
    private string _usageEditText = string.Empty;

    /// <summary>Idle-state display of the referenced name (click to edit).</summary>
    public bool ShowUsageNameText
        => !IsEditingUsageName && !string.IsNullOrEmpty(UsageName);

    /// <summary>Idle-state placeholder shown while no name is referenced yet.</summary>
    public bool ShowUsageNamePlaceholder
        => !IsEditingUsageName && string.IsNullOrEmpty(UsageName);

    partial void OnIsEditingUsageNameChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowUsageNameText));
        OnPropertyChanged(nameof(ShowUsageNamePlaceholder));
    }

    /// <summary>
    /// Begins usage-name editing, seeding the temp text from the current reference.
    /// Entered ONLY via the explicit Edit icon button (no click-on-text shortcut — the
    /// editor must not fight the canvas for pointer/focus).
    /// </summary>
    [RelayCommand]
    private void StartEditUsageName()
    {
        UsageEditText = UsageName ?? string.Empty;
        IsEditingUsageName = true;
    }

    /// <summary>Commits the edited usage name (flows through the UsageName setter → editor callback).
    /// Guarded: once the editing state has already ended (Esc / explicit cancel), a late
    /// LostFocus from the disappearing editor must NOT re-commit the discarded text.</summary>
    [RelayCommand]
    private void CommitUsageName()
    {
        if (!IsEditingUsageName) return;
        IsEditingUsageName = false;
        UsageName = UsageEditText;
    }

    /// <summary>Discards the usage-name edit.</summary>
    [RelayCommand]
    private void CancelEditUsageName()
    {
        IsEditingUsageName = false;
    }

    /// <summary>
    /// True for *definition* nodes (const/var block declarations). Definition nodes
    /// have no Exec pins and no connections — they live in the initialisation region,
    /// unlike usage nodes which are wired into the exec chain.
    /// </summary>
    [ObservableProperty]
    private bool _isDefinition;

    /// <summary>Header title for definition nodes: "const guessNum" / "var counter" (R8).</summary>
    public string DefinitionTitle =>
        IsDefinition
            ? $"{DefinitionKindText} {DefinitionName ?? DisplayTitle}"
            : DisplayTitle;

    /// <summary>
    /// DeclKind of a DictNew definition node ("const"/"var"), loaded from the Contract's
    /// Properties["DeclKind"] (2026-08-03: const dict declarations are legal KS and the
    /// backend renders them; the header must reflect the declared kind, not hardcode "var").
    /// </summary>
    private string? _dictDeclKind;
    public string? DictDeclKind
    {
        get => _dictDeclKind;
        set
        {
            if (_dictDeclKind == value) return;
            _dictDeclKind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DefinitionKindText));
        }
    }

    /// <summary>Definition keyword ("const" / "var") for the header title (R8).</summary>
    public string DefinitionKindText => IsDictNewNode
        ? DictDeclKind ?? "var"
        : NodeType switch
        {
            BlueprintNodeType.Const => "const",
            BlueprintNodeType.Variable => "var",
            _ => "def",
        };

    // ── DictNew definition nodes (T8) ──

    /// <summary>True for DictNew definition nodes (renders the key/value pair editor).</summary>
    public bool IsDictNewNode => FunctionName == BpFunctionNames.DictNew;

    /// <summary>True for const/var definition nodes — shows the Type/Name/Value declaration editor.</summary>
    public bool ShowDefinitionEditor => IsDefinition && !IsDictNewNode;

    /// <summary>Key/value rows of a DictNew definition node (edited on the card).</summary>
    public ObservableCollection<DictPairRowVM> DictPairs { get; } = new();

    /// <summary>Appends a new key/value pair to a DictNew node (Contract + VM double-write).</summary>
    [RelayCommand]
    private void AddDictPair() => _onAddDictPair?.Invoke(this);

    /// <summary>Removes the given key/value row (and its pins) from a DictNew node.</summary>
    [RelayCommand]
    private void RemoveDictPair(DictPairRowVM? row)
    {
        if (row != null)
            _onRemoveDictPair?.Invoke(this, row);
    }

    /// <summary>
    /// Editable declaration name for definition nodes (ConstNode.ConstName / VariableNode.VarName).
    /// Renaming a definition node synchronises all same-named usage nodes in the working blueprint.
    /// </summary>
    private string? _definitionName;
    public string? DefinitionName
    {
        get => _definitionName;
        set
        {
            if (_definitionName == value) return;
            var oldName = _definitionName;
            _definitionName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DefinitionTitle));
            // Pass the RAW user value (not the getter's fallback) so a rename never
            // accidentally writes the KS default into the user-value slot.
            _onDefinitionEdited?.Invoke(this, oldName, value, _definitionValue);
        }
    }

    /// <summary>Declaration type text for definition nodes ("int", "string", ...).</summary>
    [ObservableProperty]
    private string? _definitionType;

    /// <summary>
    /// KS-script default value for definition nodes (ConstNode.DefaultValue /
    /// VariableNode.DefaultValue) — read-only on the BP side. The node's value box
    /// displays the USER value when set, otherwise falls back to this default
    /// (<see cref="DefinitionValue"/> getter). Kept in sync with the KS editor's
    /// Variable Constants panel DefaultValue.
    /// </summary>
    private string? _defaultValue;
    public string? DefaultValue
    {
        get => _defaultValue;
        set
        {
            if (_defaultValue == value) return;
            _defaultValue = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DefinitionValue));
            OnPropertyChanged(nameof(DefinitionTitle));
            Log.Debug("[BPNodeVM] DefaultValue set: id={Id} value={Value}", BlueprintNodeId, value);
        }
    }

    /// <summary>
    /// Applies the full definition state straight from the Contract node during
    /// blueprint loading. Assigns backing fields directly and raises change
    /// notifications WITHOUT invoking the edit callbacks — a load must never
    /// write the (still uninitialised) VM state back over the Contract values.
    /// </summary>
    public void ApplyDefinitionFromContract(string? name, string? type, string? defaultValue,
        string? userValue, string? dictDeclKind = null)
    {
        _definitionName = name;
        // Direct field write is INTENTIONAL: using the generated property would raise
        // PropertyChanged before the VM is attached to the canvas (load path).
#pragma warning disable MVVMTK0034
        _definitionType = type;
#pragma warning restore MVVMTK0034
        _defaultValue = defaultValue;
        _definitionValue = userValue;
        _dictDeclKind = dictDeclKind;
        OnPropertyChanged(nameof(DefinitionName));
        OnPropertyChanged(nameof(DefinitionType));
        OnPropertyChanged(nameof(DefaultValue));
        OnPropertyChanged(nameof(DefinitionValue));
        OnPropertyChanged(nameof(DefinitionTitle));
        OnPropertyChanged(nameof(DictDeclKind));
        OnPropertyChanged(nameof(DefinitionKindText));
    }

    /// <summary>
    /// Editable USER value for definition nodes (ConstValue / VarInitialValue) — the
    /// override entered on the BP node (maps to the KS editor's Variable Constants
    /// panel UserValue). Empty → null (fall back to <see cref="DefaultValue"/>).
    /// The getter falls back to the KS-script default while the user value is empty,
    /// so the box always shows what the variable would effectively initialise to.
    /// </summary>
    private string? _definitionValue;
    public string? DefinitionValue
    {
        get => _definitionValue ?? _defaultValue;
        set
        {
            // Normalise empty input to null (use the default).
            var userValue = string.IsNullOrWhiteSpace(value) ? null : value;
            if (_definitionValue == userValue) return;
            _definitionValue = userValue;
            OnPropertyChanged();
            Log.Debug("[BPNodeVM] DefinitionValue set: id={Id} user={User} default={Def}",
                BlueprintNodeId, userValue, _defaultValue);
            // oldName == newName == current name: value edits must NOT trigger the
            // rename path in UpdateDefinitionNode.
            _onDefinitionEdited?.Invoke(this, DefinitionName, DefinitionName, userValue);
        }
    }

    /// <summary>
    /// Header background hex — definition nodes render translucent (alpha ~40%)
    /// to visually separate the initialisation region from the exec-wired usage nodes.
    /// </summary>
    public string EffectiveHeaderColorHex =>
        IsDefinition && HeaderColorHex.Length == 7
            ? HeaderColorHex + "66"
            : HeaderColorHex;

    // ── Usage-node editing (2026-08-03) ──

    /// <summary>True for a usage VariableNode (a var reference — const references are
    /// VariableNodes in the BP model too, since BpRenderer renders every identifier
    /// the same way).</summary>
    public bool IsUsageVariableNode
        => !IsDefinition && NodeType == BlueprintNodeType.Variable;

    /// <summary>True for a usage ConstNode (a pipeline literal source).</summary>
    public bool IsUsageConstNode
        => !IsDefinition && NodeType == BlueprintNodeType.Const;

    /// <summary>
    /// Editable referenced name for usage VariableNodes (VariableNode.VarName).
    /// Backed by an AutoCompleteBox of declared const/var names; edits write back to
    /// the Contract so Reverse re-emits the reference under the new name. The header
    /// title mirrors the bare referenced name (same as the renderer's GetDisplayTitle —
    /// no "PubVar:" prefix). Invalid edits are rejected by the editor callback, which
    /// rolls the VM back via <see cref="ApplyUsageFromContract"/>.
    /// </summary>
    private string? _usageName;
    public string? UsageName
    {
        get => _usageName;
        set
        {
            if (_usageName == value) return;
            _usageName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowUsageNameText));
            OnPropertyChanged(nameof(ShowUsageNamePlaceholder));
            if (!string.IsNullOrWhiteSpace(value))
            {
                DisplayTitle = value;
                Title = value;
            }
            _onUsageNameEdited?.Invoke(this, value);
        }
    }

    /// <summary>Read-only storage-tier kind of this usage VariableNode (const = read-only
    /// reference, var = read/write reference). FIXED at creation by the palette node type
    /// ("常量（使用）" vs "变量（使用）") — there is no runtime kind switch; the backend
    /// renders the same shape for the corresponding KS reference.</summary>
    private VariableKind _usageVarKind = VariableKind.PubVar;
    public VariableKind UsageVarKind
    {
        get => _usageVarKind;
        set
        {
            if (_usageVarKind == value) return;
            _usageVarKind = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsConstUsageRefNode));
            OnPropertyChanged(nameof(IsVarUsageRefNode));
        }
    }

    /// <summary>True for a const usage reference node (VarKind=Const, read-only).</summary>
    public bool IsConstUsageRefNode => !IsDefinition && NodeType == BlueprintNodeType.Variable
        && _usageVarKind == VariableKind.Const;

    /// <summary>True for a var usage reference node (VarKind=PubVar, read/write).</summary>
    public bool IsVarUsageRefNode => !IsDefinition && NodeType == BlueprintNodeType.Variable
        && _usageVarKind == VariableKind.PubVar;

    /// <summary>
    /// Editable literal value for usage ConstNodes (ConstNode.ConstValue). Reverse's
    /// NodeToKsNode reads ConstValue ?? ConstName, so the literal round-trips.
    /// </summary>
    private string? _usageValue;
    public string? UsageValue
    {
        get => _usageValue;
        set
        {
            if (_usageValue == value) return;
            _usageValue = value;
            OnPropertyChanged();
            _onUsageValueEdited?.Invoke(this, value);
        }
    }

    /// <summary>
    /// Applies usage-node state straight from the Contract during load (no edit callbacks)
    /// and is the rollback path for rejected name edits — restores the VM's name, kind
    /// and header title from the Contract's current values.
    /// </summary>
    public void ApplyUsageFromContract(string? varName, string? literalValue, VariableKind? varKind = null)
    {
        _usageName = varName;
        _usageValue = literalValue;
        if (varKind.HasValue)
        {
            _usageVarKind = varKind.Value;
            OnPropertyChanged(nameof(UsageVarKind));
            OnPropertyChanged(nameof(IsConstUsageRefNode));
            OnPropertyChanged(nameof(IsVarUsageRefNode));
        }
        if (!string.IsNullOrEmpty(varName))
        {
            // Usage VariableNode headers mirror the bare referenced name (matches the
            // contract's GetDisplayTitle — no "PubVar:" prefix).
            DisplayTitle = varName;
            Title = varName;
        }
        OnPropertyChanged(nameof(UsageName));
        OnPropertyChanged(nameof(UsageValue));
        OnPropertyChanged(nameof(ShowUsageNameText));
        OnPropertyChanged(nameof(ShowUsageNamePlaceholder));
    }

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
           && FunctionName is BpFunctionNames.Branch or BpFunctionNames.Each or BpFunctionNames.While or BpFunctionNames.Switch;

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
            BpFunctionNames.Branch or BpFunctionNames.Each or BpFunctionNames.While or BpFunctionNames.Switch => "#FF9800",  // Orange (control flow)
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
        OnPropertyChanged(nameof(ShowDefinitionEditor));
    }

    partial void OnFunctionNameChanged(string? value)
    {
        OnPropertyChanged(nameof(IsDictNewNode));
        OnPropertyChanged(nameof(DefinitionKindText));
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
