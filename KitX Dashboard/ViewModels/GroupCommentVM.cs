using CommunityToolkit.Mvvm.ComponentModel;
using NodifyM.Avalonia.ViewModelBase;

namespace KitX.Dashboard.ViewModels;

// ─────────────────────────────────────────────────────────────────────────────
// GroupCommentVM — decorative leading-comment label anchored above a statement's
// primary node (P5-B2).
//
// Maps Contract BlueprintGroupComment (KScript-Blueprint-Correspondence.md §3.5:
// leading `// group comment` lines attach to the statement's primary node). Like
// ScopeFrameVM it lives in the NodifyEditor's Nodes collection as a non-interactive
// ghost node — read-only for now, positioned just above its anchor node.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// ViewModel for a decorative group-comment label anchored above a node.
/// </summary>
public partial class GroupCommentVM : BaseNodeViewModel
{
    /// <summary>The comment text (may contain newlines).</summary>
    [ObservableProperty]
    private string _comment = string.Empty;

    /// <summary>Label width (fits the comment text).</summary>
    [ObservableProperty]
    private double _width = 160;

    /// <summary>Label height.</summary>
    [ObservableProperty]
    private double _height = 24;
}
