using Avalonia;
using NodifyM.Avalonia.ViewModelBase;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// A node on the Bench canvas. <c>Title</c>/<c>Location</c>/<c>Input</c>/<c>Output</c> are
/// inherited from the NodifyM base VM. <see cref="NodeType"/> is <c>Source</c> (a Spawn
/// trigger) or <c>Workflow</c>.
/// </summary>
public sealed partial class BenchNodeVM : NodeViewModelBase
{
    public BenchNodeVM(string title, string nodeType, string kindLabel, Point location)
    {
        Title = title;
        NodeType = nodeType;
        KindLabel = kindLabel;
        Location = location;
    }

    /// <summary>Discriminant: <c>Source</c> (Spawn trigger) or <c>Workflow</c>.</summary>
    public string NodeType { get; }

    /// <summary>Human-readable kind label (trigger type / 工作流).</summary>
    public string KindLabel { get; }
}

/// <summary>A connector (pin) on a Bench node.</summary>
public sealed class BenchConnectorVM : ConnectorViewModelBase
{
    public BenchConnectorVM(string title, ConnectorViewModelBase.ConnectorFlow flow)
    {
        Title = title;
        Flow = flow;
    }
}

/// <summary>A connection (edge) on the Bench canvas: source connector → target connector.</summary>
public sealed class BenchConnectionVM : ConnectionViewModelBase
{
    public BenchConnectionVM(NodifyEditorViewModelBase editor, BenchConnectorVM source, BenchConnectorVM target)
        : base(editor, source, target)
    {
    }
}
