using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using KitX.ToolKit.Models;
using NodifyM.Avalonia.ViewModelBase;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// Discriminates a Bench edge: a trigger→workflow <c>Binding</c> vs. a workflow→workflow
/// <c>Completion</c> ("canvas edge"). Drives how an edge maps back onto the config.
/// </summary>
public enum BenchEdgeKind
{
    Binding,
    Completion,
}

/// <summary>A node on the Bench canvas.</summary>
public sealed class BenchNodeVM : NodeViewModelBase
{
    public BenchNodeVM(string title, BenchCanvasViewModel.BenchNodeKind kind, string kindLabel, string configId, Point location)
    {
        Title = title;
        Kind = kind;
        KindLabel = kindLabel;
        ConfigId = configId;
        Location = location;
    }

    /// <summary>Discriminant: <c>Source</c> (Spawn trigger), <c>Workflow</c>, or <c>Panel</c>.</summary>
    public BenchCanvasViewModel.BenchNodeKind Kind { get; }

    /// <summary>Human-readable kind label (trigger type / 工作流 / 面板).</summary>
    public string KindLabel { get; }

    /// <summary>Key of the underlying config object (trigger id / workflow id / "panel").</summary>
    public string ConfigId { get; }
}

/// <summary>A connector (pin) on a Bench node. <see cref="Key"/> identifies the pin
/// (<c>src:{id}</c>, <c>in:{id}</c>, <c>out:{id}</c>) for config edge resolution.</summary>
public sealed class BenchConnectorVM : ConnectorViewModelBase
{
    public BenchConnectorVM(string title, ConnectorViewModelBase.ConnectorFlow flow, string key = "")
    {
        Title = title;
        Flow = flow;
        Key = key;
    }

    /// <summary>Stable pin key used to (re)build config edges.</summary>
    public string Key { get; }
}

/// <summary>A connection (edge) on the Bench canvas, tagged with its config role.</summary>
public sealed class BenchConnectionVM : ConnectionViewModelBase
{
    public BenchConnectionVM(NodifyEditorViewModelBase editor, BenchConnectorVM source, BenchConnectorVM target, BenchEdgeKind kind)
        : base(editor, source, target)
    {
        Kind = kind;
    }

    /// <summary>Whether this edge is a trigger binding or a workflow-completion edge.</summary>
    public BenchEdgeKind Kind { get; }
}

/// <summary>
/// A single trigger→workflow binding row shown in the inspector. Wraps a
/// <see cref="TriggerBinding"/> in an observable façade so the inspector can edit the
/// target workflow and the parameter-injection map (one <c>name=source</c> line each) and
/// notify the editor to re-validate on change.
/// </summary>
public sealed class BenchBindingRowVM : ReactiveObject
{
    private readonly Action _onEdit;
    private string _paramsText;

    public BenchBindingRowVM(TriggerBinding model, IEnumerable<string> workflowOptions, Action onEdit)
    {
        Model = model;
        WorkflowOptions = workflowOptions.ToList();
        _onEdit = onEdit;
        _paramsText = FormatParams(model.Params);
    }

    /// <summary>The underlying config binding.</summary>
    public TriggerBinding Model { get; }

    /// <summary>Workflow ids the binding may target.</summary>
    public List<string> WorkflowOptions { get; }

    /// <summary>Target workflow id (edits <see cref="Model.Workflow"/>).</summary>
    public string SelectedWorkflow
    {
        get => Model.Workflow;
        set
        {
            if (Model.Workflow != value)
            {
                Model.Workflow = value;
                this.RaisePropertyChanged();
                _onEdit();
            }
        }
    }

    /// <summary>Parameter-injection map as <c>name=source</c> lines (edits <see cref="Model.Params"/>).</summary>
    public string ParamsText
    {
        get => _paramsText;
        set
        {
            if (_paramsText != value)
            {
                _paramsText = value;
                Model.Params = ParseParams(value);
                this.RaisePropertyChanged();
                _onEdit();
            }
        }
    }

    private static string FormatParams(Dictionary<string, string?> p)
        => string.Join(Environment.NewLine, p.Select(kv => $"{kv.Key}={kv.Value}"));

    private static Dictionary<string, string?> ParseParams(string text)
        => text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && l.Contains('='))
            .GroupBy(l => l[..l.IndexOf('=')].Trim())
            .ToDictionary(g => g.Key, g => (string?)g.First().Substring(g.First().IndexOf('=') + 1).Trim());
}
