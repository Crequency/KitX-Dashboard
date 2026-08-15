using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Media;
using KitX.ToolKit.Models;
using Material.Icons;
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
        IsComment = kind == BenchCanvasViewModel.BenchNodeKind.Comment;
        (NodeIcon, HeaderColor, HeaderForegroundColor, AffinityLabel) = kind switch
        {
            BenchCanvasViewModel.BenchNodeKind.Source => (MaterialIconKind.Hand, "#4CAF50", "White", "Spawn"),
            BenchCanvasViewModel.BenchNodeKind.Workflow => (MaterialIconKind.Sitemap, "#9C27B0", "White", null),
            BenchCanvasViewModel.BenchNodeKind.Panel => (MaterialIconKind.ViewDashboard, "#3873D9", "White", null),
            _ => (MaterialIconKind.CommentOutline, "#8A8A8A", "White", null),
        };
    }

    /// <summary>Discriminant: <c>Source</c> (Spawn trigger), <c>Workflow</c>, <c>Panel</c>, or <c>Comment</c>.</summary>
    public BenchCanvasViewModel.BenchNodeKind Kind { get; }

    /// <summary>True for comment nodes (no pins, text-only body).</summary>
    public bool IsComment { get; }

    private string _kindLabel = string.Empty;

    /// <summary>Human-readable kind label (trigger type / 工作流 / 面板). Mutable so the
    /// inspector can refresh it live when the underlying trigger config changes.</summary>
    public string KindLabel
    {
        get => _kindLabel;
        set => SetProperty(ref _kindLabel, value);
    }

    private string _configId = string.Empty;

    /// <summary>Key of the underlying config object (trigger id / workflow id / "panel" /
    /// comment id). Mutable so a workflow-id cascade can re-key the node.</summary>
    public string ConfigId
    {
        get => _configId;
        set => SetProperty(ref _configId, value);
    }

    /// <summary>Panel controls (only set for Panel nodes) — previewed on the node body.</summary>
    public IReadOnlyList<UiControl>? Controls { get; set; }

    /// <summary>True for the GUI panel node (renders the control-list body).</summary>
    public bool IsPanel => Kind == BenchCanvasViewModel.BenchNodeKind.Panel;

    /// <summary>Material icon name for the node header (category visual system).</summary>
    public MaterialIconKind NodeIcon { get; set; }

    public string HeaderColor { get; }

    public string HeaderForegroundColor { get; }

    private IBrush? _headerBrush;

    private IBrush? _headerForeground;

    /// <summary>Lazily-created brush (Avalonia brushes must be created on the UI thread).</summary>
    public IBrush HeaderBrush => _headerBrush ??= new SolidColorBrush(Color.Parse(HeaderColor));

    public IBrush HeaderForeground => _headerForeground ??= new SolidColorBrush(Color.Parse(HeaderForegroundColor));

    /// <summary>Spawn / 实例内 affinity badge; null for non-trigger nodes.</summary>
    public string? AffinityLabel { get; set; }

    public bool HasAffinity => !string.IsNullOrWhiteSpace(AffinityLabel);

    private bool _hasDiagnostic;

    /// <summary>Drives the inline red border + warning badge (Bench UX v2 §4.6).</summary>
    public bool HasDiagnostic
    {
        get => _hasDiagnostic;
        set => SetProperty(ref _hasDiagnostic, value);
    }

    private string _inDegreeText = string.Empty;

    /// <summary>fan-out/AND-join degree badge text (only non-empty when >1).</summary>
    public string InDegreeText
    {
        get => _inDegreeText;
        set
        {
            if (SetProperty(ref _inDegreeText, value))
                OnPropertyChanged(nameof(HasInDegreeBadge));
        }
    }

    public bool HasInDegreeBadge => !string.IsNullOrWhiteSpace(InDegreeText);

    private string _outDegreeText = string.Empty;

    public string OutDegreeText
    {
        get => _outDegreeText;
        set
        {
            if (SetProperty(ref _outDegreeText, value))
                OnPropertyChanged(nameof(HasOutDegreeBadge));
        }
    }

    public bool HasOutDegreeBadge => !string.IsNullOrWhiteSpace(OutDegreeText);

    private string _commentText = string.Empty;

    /// <summary>Comment node text (write-through to <see cref="Toolkit.Comments"/>).</summary>
    public string CommentText
    {
        get => _commentText;
        set => SetProperty(ref _commentText, value);
    }
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

    private bool _isSelected;

    /// <summary>Selected state for the edge inspector (Bench UX v2 C11).</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public double EdgeThickness => Kind == BenchEdgeKind.Completion ? 3.2 : 2.0;

    public AvaloniaList<double> EdgeDash => Kind == BenchEdgeKind.Completion
        ? new AvaloniaList<double> { 6, 3 }
        : new AvaloniaList<double>();

    public string EdgeToolTip
    {
        get
        {
            var src = Source is BenchConnectorVM s ? s.Title : Source.Title;
            var tgt = Target is BenchConnectorVM t ? t.Title : Target.Title;
            return Kind == BenchEdgeKind.Completion
                ? $"完成边（线束）：{src} → {tgt}"
                : $"绑定：{src} → {tgt}";
        }
    }
}
