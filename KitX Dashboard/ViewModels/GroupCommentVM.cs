using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using NodifyM.Avalonia.ViewModelBase;

namespace KitX.Dashboard.ViewModels;

// ─────────────────────────────────────────────────────────────────────────────
// GroupCommentVM — decorative leading-comment label anchored to a statement's data
// subgraph (R7).
//
// Maps Contract BlueprintGroupComment (KScript-Blueprint-Correspondence.md §3.5:
// leading `// group comment` lines attach to the statement's primary node; NodeIds
// cover the statement's data-connection subgraph). The comment renders as a sticky
// note that can be dragged, collapsed, and — on hover — reveals a dashed frame
// covering the data subgraph (NodeIds bounding box).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// ViewModel for a draggable group-comment note with a hover-revealed dashed frame
/// covering its data subgraph.
/// </summary>
public partial class GroupCommentVM : BaseNodeViewModel
{
    private readonly Action<GroupCommentVM, string?>? _onCommentEdited;

    /// <summary>The comment text (may contain newlines).</summary>
    [ObservableProperty]
    private string _comment = string.Empty;

    /// <summary>The statement's primary (anchor) node id this comment is attached to.</summary>
    [ObservableProperty]
    private string _anchorNodeId = string.Empty;

    /// <summary>Node ids covered by this comment's data subgraph (drives the dashed frame).</summary>
    public HashSet<string> NodeIds { get; set; } = new();

    /// <summary>Note width.</summary>
    [ObservableProperty]
    private double _width = 180;

    /// <summary>Note height.</summary>
    [ObservableProperty]
    private double _height = 28;

    /// <summary>Collapsed shows only a one-line note; expanded shows the full text.</summary>
    [ObservableProperty]
    private bool _isCollapsed;

    /// <summary>True while the inline editing is off.</summary>
    [ObservableProperty]
    private bool _isEditing;

    /// <summary>Temporary text during inline editing.</summary>
    [ObservableProperty]
    private string _commentEditText = string.Empty;

    /// <summary>True while the mouse hovers the note itself — drives the dashed-frame reveal.</summary>
    [ObservableProperty]
    private bool _isHovered;

    /// <summary>
    /// True while the note is "docked" to its statement leader (follows the subgraph
    /// bounding box). Set false when the user drags it far from any leader (free placement).
    /// </summary>
    [ObservableProperty]
    private bool _isDocked = true;

    public GroupCommentVM()
        : this(null) { }

    public GroupCommentVM(Action<GroupCommentVM, string?>? onCommentEdited)
    {
        _onCommentEdited = onCommentEdited;
    }

    /// <summary>Begins inline editing of the note text.</summary>
    public void BeginEdit()
    {
        CommentEditText = Comment;
        IsEditing = true;
    }

    /// <summary>Commits the edited note text back to the Contract.</summary>
    public void CommitEdit()
    {
        IsEditing = false;
        Comment = CommentEditText;
        _onCommentEdited?.Invoke(this, Comment);
    }

    /// <summary>Discards the inline edit.</summary>
    public void CancelEdit()
    {
        IsEditing = false;
    }

    /// <summary>Bounding box of the comment's data subgraph (NodeIds) — dashed frame on hover.</summary>
    [ObservableProperty]
    private double _highlightX;

    [ObservableProperty]
    private double _highlightY;

    [ObservableProperty]
    private double _highlightWidth;

    [ObservableProperty]
    private double _highlightHeight;

    /// <summary>True when a NodeIds bounding box is available to frame.</summary>
    public bool HasHighlight => HighlightWidth > 0 && HighlightHeight > 0;

    /// <summary>Note text: full comment expanded, one line (ellipsised) when collapsed.</summary>
    public string BubbleText => IsCollapsed
        ? "// " + (Comment.Length > 32 ? Comment[..32] + "…" : Comment)
        : Comment;

    /// <summary>Expanded note height grows with the text.</summary>
    public double EffectiveHeight => IsCollapsed ? 24 : Height;

    partial void OnIsCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(BubbleText));
        OnPropertyChanged(nameof(EffectiveHeight));
    }

    partial void OnCommentChanged(string value)
    {
        OnPropertyChanged(nameof(BubbleText));
    }
}
