using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KitX.ToolKit.Models;
using KitX.ToolKit.Validation;
using NodifyM.Avalonia.ViewModelBase;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// The Bench canvas <b>editor</b> — config is the single source of truth and the canvas is
/// a live editor over an in-memory <see cref="Toolkit"/> config (GUI RFC §8.3 closed loop).
/// Edit ops (connect / disconnect / delete / palette-add / inspector) mutate the in-memory
/// config directly; saving runs a hard validation and rejects on error.
/// </summary>
public sealed partial class BenchCanvasViewModel : NodifyEditorViewModelBase
{
    /// <summary>Node discriminant.</summary>
    public enum BenchNodeKind { Source, Workflow, Panel }

    private readonly Toolkit _toolkit;
    private readonly Dictionary<BenchNodeVM, object> _nodeConfig = new();
    private readonly Dictionary<BenchConnectorVM, (BenchNodeVM node, string pin)> _connectorInfo = new();
    private int _sourceCount;
    private int _workflowCount;
    private bool _hasPanel;

    public BenchCanvasViewModel(Toolkit toolkit)
    {
        _toolkit = toolkit ?? throw new ArgumentNullException(nameof(toolkit));
        BuildGraph();
        SelectedNodes.CollectionChanged += (_, _) =>
            SelectedNode = SelectedNodes.OfType<BenchNodeVM>().FirstOrDefault();
        RefreshValidation();
    }

    /// <summary>The in-memory config the canvas edits (shared with the host for saving).</summary>
    public Toolkit Config => _toolkit;

    /// <summary>True when the config had any nodes.</summary>
    public bool HasContent => Nodes.Count > 0;

    // ── Inspector state ──

    [ObservableProperty]
    private BenchNodeVM? _selectedNode;

    [ObservableProperty]
    private Trigger? _selectedTrigger;

    [ObservableProperty]
    private ToolkitWorkflow? _selectedWorkflow;

    [ObservableProperty]
    private UiPanel? _selectedPanel;

    [ObservableProperty]
    private UiControl? _selectedControl;

    [ObservableProperty]
    private ObservableCollection<BenchBindingRowVM> _selectedBindings = new();

    [ObservableProperty]
    private ObservableCollection<UiControl> _selectedControls = new();

    [ObservableProperty]
    private IReadOnlyList<string> _validationErrors = [];

    [ObservableProperty]
    private string? _lastError;

    [ObservableProperty]
    private IReadOnlyList<string> _inspectorWorkflowOptions = [];

    [ObservableProperty]
    private string? _addWorkflowId;

    partial void OnSelectedNodeChanged(BenchNodeVM? value)
    {
        UpdateInspector();
        OnPropertyChanged(nameof(IsSourceSelected));
        OnPropertyChanged(nameof(IsWorkflowSelected));
        OnPropertyChanged(nameof(IsPanelSelected));
        OnPropertyChanged(nameof(IsNothingSelected));
        OnPropertyChanged(nameof(IsTimerSelected));
        OnPropertyChanged(nameof(IsPluginEventSelected));
        OnPropertyChanged(nameof(IsUIEventSelected));
    }

    /// <summary>Inspector panels visibility (driven by the selected node kind).</summary>
    public bool IsSourceSelected => SelectedNode is { Kind: BenchNodeKind.Source };
    public bool IsWorkflowSelected => SelectedNode is { Kind: BenchNodeKind.Workflow };
    public bool IsPanelSelected => SelectedNode is { Kind: BenchNodeKind.Panel };
    public bool IsNothingSelected => SelectedNode is null;
    public bool IsTimerSelected => SelectedTrigger is { Type: TriggerType.Timer };
    public bool IsPluginEventSelected => SelectedTrigger is { Type: TriggerType.PluginEvent };
    public bool IsUIEventSelected => SelectedTrigger is { Type: TriggerType.UIEvent };

    // ── Graph construction ──

    private void BuildGraph()
    {
        Nodes.Clear();
        Connections.Clear();
        _sourceCount = 0;
        _workflowCount = 0;
        _hasPanel = false;

        foreach (var t in _toolkit.Triggers.Where(x => x.Type != TriggerType.WorkflowCompletion))
            AddSourceNode(t, _sourceCount++);
        foreach (var wf in _toolkit.Workflows)
            AddWorkflowNode(wf, _workflowCount++);
        if (_toolkit.UiPanel is not null)
            AddPanelNode(_toolkit.UiPanel);

        foreach (var t in _toolkit.Triggers)
        {
            if (t.Type == TriggerType.WorkflowCompletion)
            {
                if (string.IsNullOrWhiteSpace(t.Config?.From))
                    continue;
                foreach (var b in t.Bindings)
                    ConnectExisting("out:" + t.Config.From, "in:" + b.Workflow, BenchEdgeKind.Completion);
            }
            else
            {
                foreach (var b in t.Bindings)
                    ConnectExisting("src:" + t.Id, "in:" + b.Workflow, BenchEdgeKind.Binding);
            }
        }
    }

    private void AddSourceNode(Trigger trigger, int index)
    {
        var node = new BenchNodeVM(trigger.Id, BenchNodeKind.Source, SourceKindLabel(trigger), trigger.Id,
            new Point(60, 40 + index * 100));
        var outPin = new BenchConnectorVM("触发", ConnectorViewModelBase.ConnectorFlow.Output, "src:" + trigger.Id);
        node.Output.Add(outPin);
        RegisterNode(node, outPin, trigger);
    }

    private void AddWorkflowNode(ToolkitWorkflow wf, int index)
    {
        var node = new BenchNodeVM(wf.Name, BenchNodeKind.Workflow, "工作流", wf.Id,
            new Point(460, 40 + index * 100));
        var inPin = new BenchConnectorVM("输入", ConnectorViewModelBase.ConnectorFlow.Input, "in:" + wf.Id);
        var outPin = new BenchConnectorVM("输出", ConnectorViewModelBase.ConnectorFlow.Output, "out:" + wf.Id);
        node.Input.Add(inPin);
        node.Output.Add(outPin);
        _nodeConfig[node] = wf;
        _connectorInfo[inPin] = (node, "in");
        _connectorInfo[outPin] = (node, "out");
        Nodes.Add(node);
    }

    private void AddPanelNode(UiPanel panel)
    {
        var node = new BenchNodeVM("GUI 面板", BenchNodeKind.Panel, $"控件 {panel.Controls.Count}", "panel",
            new Point(460, 40 + _workflowCount * 100 + 60))
        {
            Controls = panel.Controls,
        };
        _nodeConfig[node] = panel;
        Nodes.Add(node);
    }

    private void RegisterNode(BenchNodeVM node, BenchConnectorVM outPin, Trigger trigger)
    {
        _nodeConfig[node] = trigger;
        _connectorInfo[outPin] = (node, "out");
        Nodes.Add(node);
    }

    private void ConnectExisting(string srcKey, string tgtKey, BenchEdgeKind kind)
    {
        var src = AllConnectors().FirstOrDefault(c => c.Key == srcKey);
        var tgt = AllConnectors().FirstOrDefault(c => c.Key == tgtKey);
        if (src is not null && tgt is not null)
            Connections.Add(new BenchConnectionVM(this, src, tgt, kind));
    }

    private IEnumerable<BenchConnectorVM> AllConnectors()
        => Nodes.OfType<BenchNodeVM>().SelectMany(n => n.Input.Cast<BenchConnectorVM>().Concat(n.Output.Cast<BenchConnectorVM>()));

    // ── Connect (rewritten for validation + config double-write) ──

    /// <inheritdoc />
    public override void Connect(ConnectorViewModelBase source, ConnectorViewModelBase target)
    {
        if (source is not BenchConnectorVM src || target is not BenchConnectorVM tgt)
            return;
        if (!_connectorInfo.TryGetValue(src, out var si) || !_connectorInfo.TryGetValue(tgt, out var ti))
            return;
        var (srcNode, _) = si;
        var (tgtNode, _) = ti;

        // Normalise direction: output → input.
        var (outCon, inCon, outNode, inNode) = src.Flow == ConnectorViewModelBase.ConnectorFlow.Output
            ? (src, tgt, srcNode, tgtNode)
            : (tgt, src, tgtNode, srcNode);

        if (outNode.Kind == BenchNodeKind.Source)
        {
            if (inNode.Kind != BenchNodeKind.Workflow)
            {
                SetError("绑定连线只能连接到工作流节点");
                return;
            }

            var trigger = (Trigger)_nodeConfig[outNode];
            if (trigger.Bindings.Any(b => b.Workflow == inNode.ConfigId))
            {
                SetError("该触发器已绑定此工作流");
                return;
            }

            trigger.Bindings.Add(new TriggerBinding { Workflow = inNode.ConfigId });
            Connections.Add(new BenchConnectionVM(this, outCon, inCon, BenchEdgeKind.Binding));
        }
        else if (outNode.Kind == BenchNodeKind.Workflow)
        {
            if (inNode.Kind != BenchNodeKind.Workflow)
            {
                SetError("完成边只能连接到工作流节点");
                return;
            }

            if (!AddCompletionEdge(outNode.ConfigId, inNode.ConfigId, outCon, inCon))
                return;
        }
        else
        {
            SetError("面板节点不能作为连线源");
            return;
        }

        outCon.IsConnected = true;
        inCon.IsConnected = true;
        LastError = null;
        RefreshValidation();
    }

    /// <summary>Adds (or reuses) a workflow→workflow completion edge; rejects on cycle.</summary>
    private bool AddCompletionEdge(string fromWf, string toWf, BenchConnectorVM outCon, BenchConnectorVM inCon)
    {
        var existing = _toolkit.Triggers.FirstOrDefault(t => t.Type == TriggerType.WorkflowCompletion && t.Config?.From == fromWf);
        if (existing is not null)
        {
            if (existing.Bindings.Any(b => b.Workflow == toWf))
            {
                SetError("该完成边已存在");
                return false;
            }

            existing.Bindings.Add(new TriggerBinding { Workflow = toWf });
        }
        else
        {
            existing = new Trigger
            {
                Id = NewTriggerId(),
                Type = TriggerType.WorkflowCompletion,
                Config = new TriggerConfig { From = fromWf },
            };
            existing.Bindings.Add(new TriggerBinding { Workflow = toWf });
            _toolkit.Triggers.Add(existing);
        }

        Connections.Add(new BenchConnectionVM(this, outCon, inCon, BenchEdgeKind.Completion));

        var result = new ConfigValidator().Validate(_toolkit);
        if (result.Errors.Any(e => e.Contains("cycle", StringComparison.OrdinalIgnoreCase)))
        {
            // Roll back the just-added completion edge so the graph stays a strict DAG.
            var t = _toolkit.Triggers.FirstOrDefault(x => x.Type == TriggerType.WorkflowCompletion && x.Config?.From == fromWf);
            if (t is not null)
            {
                t.Bindings.RemoveAll(b => b.Workflow == toWf);
                if (t.Bindings.Count == 0)
                    _toolkit.Triggers.Remove(t);
            }

            Connections.Remove(Connections.OfType<BenchConnectionVM>().LastOrDefault());
            SetError("检测到环：完成边不得形成循环");
            return false;
        }

        return true;
    }

    // ── Disconnect / delete (config double-write) ──

    /// <inheritdoc />
    public override void DisconnectConnector(ConnectorViewModelBase connector)
    {
        var related = Connections.OfType<BenchConnectionVM>()
            .Where(c => c.Source == connector || c.Target == connector).ToList();
        foreach (var c in related)
            RemoveConnection(c);
    }

    [RelayCommand]
    private void RemoveConnection(BenchConnectionVM? connection)
    {
        if (connection is null)
            return;
        RemoveConfigEdge(connection);
        Connections.Remove(connection);
        RefreshIsConnected();
        RefreshValidation();
    }

    private void RemoveConfigEdge(BenchConnectionVM conn)
    {
        if (conn.Source is not BenchConnectorVM srcCon || conn.Target is not BenchConnectorVM tgtCon)
            return;

        if (conn.Kind == BenchEdgeKind.Binding)
        {
            if (!_connectorInfo.TryGetValue(srcCon, out var si))
                return;
            var srcTrigger = (Trigger)_nodeConfig[si.node];
            var tgtWorkflow = _connectorInfo[tgtCon].node.ConfigId;
            srcTrigger.Bindings.RemoveAll(b => b.Workflow == tgtWorkflow);
        }
        else
        {
            var fromWf = _connectorInfo[srcCon].node.ConfigId;
            var toWf = _connectorInfo[tgtCon].node.ConfigId;
            var t = _toolkit.Triggers.FirstOrDefault(x => x.Type == TriggerType.WorkflowCompletion && x.Config?.From == fromWf);
            if (t is not null)
            {
                t.Bindings.RemoveAll(b => b.Workflow == toWf);
                if (t.Bindings.Count == 0)
                    _toolkit.Triggers.Remove(t);
            }
        }
    }

    [RelayCommand]
    private void DeleteNode(BenchNodeVM? node)
    {
        if (node is null)
            return;
        var related = Connections.OfType<BenchConnectionVM>()
            .Where(c => ConnectsNode(c, node)).ToList();
        foreach (var c in related)
            RemoveConfigEdge(c);
        foreach (var c in related)
            Connections.Remove(c);

        switch (node.Kind)
        {
            case BenchNodeKind.Source:
                _toolkit.Triggers.RemoveAll(t => t.Id == node.ConfigId);
                break;
            case BenchNodeKind.Workflow:
                _toolkit.Workflows.RemoveAll(w => w.Id == node.ConfigId);
                foreach (var t in _toolkit.Triggers)
                    t.Bindings.RemoveAll(b => b.Workflow == node.ConfigId);
                _toolkit.Triggers.RemoveAll(t => t.Type == TriggerType.WorkflowCompletion && t.Config?.From == node.ConfigId);
                break;
            case BenchNodeKind.Panel:
                _toolkit.UiPanel = null;
                _hasPanel = false;
                break;
        }

        CleanupNodeMaps(node);
        Nodes.Remove(node);
        if (SelectedNode == node)
            SelectedNode = null;
        RefreshIsConnected();
        RefreshValidation();
    }

    [RelayCommand]
    private void DeleteSelectedNodes()
    {
        var toRemove = SelectedNodes.OfType<BenchNodeVM>().ToList();
        foreach (var n in toRemove)
            DeleteNode(n);
    }

    private bool ConnectsNode(BenchConnectionVM c, BenchNodeVM n)
        => (c.Source is BenchConnectorVM s && _connectorInfo.TryGetValue(s, out var si) && si.node == n)
        || (c.Target is BenchConnectorVM t && _connectorInfo.TryGetValue(t, out var ti) && ti.node == n);

    private void CleanupNodeMaps(BenchNodeVM node)
    {
        _nodeConfig.Remove(node);
        var stale = _connectorInfo.Where(kvp => kvp.Value.node == node).Select(kvp => kvp.Key).ToList();
        foreach (var c in stale)
            _connectorInfo.Remove(c);
    }

    // ── Palette ──

    [RelayCommand]
    private void AddTrigger(string? typeStr)
    {
        var type = Enum.TryParse<TriggerType>(typeStr, out var parsed) ? parsed : TriggerType.Manual;
        var trigger = new Trigger
        {
            Id = NewTriggerId(),
            Type = type,
            Config = new TriggerConfig { Surface = "auto" },
        };
        _toolkit.Triggers.Add(trigger);
        AddSourceNode(trigger, _sourceCount++);
        LastError = null;
        RefreshValidation();
    }

    [RelayCommand]
    private void AddPanel()
    {
        if (_toolkit.UiPanel is not null)
        {
            SetError("该工具箱已有一个 GUI 面板");
            return;
        }

        _toolkit.UiPanel = new UiPanel { Layout = "stack" };
        AddPanelNode(_toolkit.UiPanel);
        _hasPanel = true;
        LastError = null;
        RefreshValidation();
    }

    [RelayCommand]
    private void AddControl(string? type)
    {
        if (SelectedPanel is null || string.IsNullOrWhiteSpace(type))
            return;
        var control = new UiControl { Type = type, Id = NewControlId(type) };
        SelectedPanel.Controls.Add(control);
        RefreshSelectedControls();
        SelectedControl = control;
        RefreshValidation();
    }

    [RelayCommand]
    private void RemoveControl(UiControl? control)
    {
        if (SelectedPanel is null || control is null)
            return;
        SelectedPanel.Controls.Remove(control);
        RefreshSelectedControls();
        if (SelectedControl == control)
            SelectedControl = null;
        RefreshValidation();
    }

    [RelayCommand]
    private void AddBinding(string? workflowId)
    {
        if (SelectedTrigger is null || string.IsNullOrWhiteSpace(workflowId))
            return;
        if (SelectedTrigger.Bindings.Any(b => b.Workflow == workflowId))
        {
            SetError("该工作流已被此触发器绑定");
            return;
        }

        SelectedTrigger.Bindings.Add(new TriggerBinding { Workflow = workflowId });
        RefreshSelectedBindings();
        RefreshValidation();
    }

    [RelayCommand]
    private void RemoveBinding(BenchBindingRowVM? row)
    {
        if (SelectedTrigger is null || row is null)
            return;
        SelectedTrigger.Bindings.Remove(row.Model);
        RefreshSelectedBindings();
        RefreshValidation();
    }

    /// <summary>The fixed ten-control palette offered by the panel designer.</summary>
    public static IReadOnlyList<string> PanelControlTypes { get; } =
        ["Text", "Icon", "Button", "Input", "Number", "Select", "Switch", "Log", "Progress", "Dialog"];

    /// <summary>Spawn presentation modes (D3).</summary>
    public static IReadOnlyList<string> SurfaceOptions { get; } = ["auto", "silent"];

    /// <summary>UIEvent event names.</summary>
    public static IReadOnlyList<string> EventOptions { get; } = ["Click", "Submit", "Confirm", "Change"];

    /// <summary>Panel layout strategies.</summary>
    public static IReadOnlyList<string> LayoutOptions { get; } = ["stack", "grid"];

    // ── Inspector helpers ──

    private void UpdateInspector()
    {
        SelectedTrigger = null;
        SelectedWorkflow = null;
        SelectedPanel = null;
        SelectedControl = null;
        SelectedBindings.Clear();
        SelectedControls.Clear();

        if (SelectedNode is null || !_nodeConfig.TryGetValue(SelectedNode, out var cfg))
            return;

        InspectorWorkflowOptions = _toolkit.Workflows.Select(w => w.Id).ToList();

        switch (SelectedNode.Kind)
        {
            case BenchNodeKind.Source:
                SelectedTrigger = cfg as Trigger;
                RefreshSelectedBindings();
                break;
            case BenchNodeKind.Workflow:
                SelectedWorkflow = cfg as ToolkitWorkflow;
                break;
            case BenchNodeKind.Panel:
                SelectedPanel = cfg as UiPanel;
                RefreshSelectedControls();
                break;
        }
    }

    private void RefreshSelectedBindings()
    {
        var options = _toolkit.Workflows.Select(w => w.Id).ToList();
        SelectedBindings = new ObservableCollection<BenchBindingRowVM>(
            (SelectedTrigger?.Bindings ?? [])
                .Select(b => new BenchBindingRowVM(b, options, RefreshValidation)));
    }

    private void RefreshSelectedControls()
    {
        SelectedControls.Clear();
        if (SelectedPanel is not null)
            foreach (var c in SelectedPanel.Controls)
                SelectedControls.Add(c);
    }

    private void RefreshIsConnected()
    {
        foreach (var conn in Connections.OfType<BenchConnectionVM>())
        {
            conn.Source.IsConnected = Connections.Any(c => c.Source == conn.Source || c.Target == conn.Source);
            conn.Target.IsConnected = Connections.Any(c => c.Source == conn.Target || c.Target == conn.Target);
        }
    }

    private void RefreshValidation()
        => ValidationErrors = new ConfigValidator().Validate(_toolkit).Errors;

    private void SetError(string message)
        => LastError = message;

    // ── Misc ──

    private static string SourceKindLabel(Trigger trigger) => trigger.Type switch
    {
        TriggerType.Manual => "手动",
        TriggerType.PluginEvent => $"插件: {trigger.Config?.PluginName}",
        TriggerType.UIEvent => $"UI: {trigger.Config?.Control}",
        TriggerType.Timer => "定时",
        _ => trigger.Type.ToString(),
    };

    private static string NewTriggerId()
        => "trg_" + Guid.NewGuid().ToString("N")[..8];

    private static string NewControlId(string type)
        => type.ToLowerInvariant() + "_" + Guid.NewGuid().ToString("N")[..6];
}
