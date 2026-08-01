using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using NodifyM.Avalonia.ViewModelBase;

namespace KitX.Dashboard.ViewModels;

// ─────────────────────────────────────────────────────────────────────────────
// GroupCommentVM — the *note* of a group comment (the only interactive part).
//
// Per the user-driven design (2026-08-02): every group comment contributes exactly
// ONE interactive control — this note. It is a plain ghost item placed at the TOP of
// the Z-order (after real nodes) so it is always hit-testable, even inside scope
// frames with dense nodes. The note is positioned above its statement's primary
// (leader) node while docked; dragging it with a proximity threshold either re-docks
// it to a nearby leader or leaves it at a free position.
//
// The dashed frame covering the statement's data subgraph is a SINGLE shared overlay
// (GroupCommentHighlightVM) driven by this note's IsHovered — there is no per-note
// frame control, so notes can never steal each other's hit-test area.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// ViewModel for a group-comment note: draggable, click-to-collapse, double-click to
/// edit. The single shared dashed-frame overlay shows its data subgraph while hovered.
/// </summary>
public partial class GroupCommentVM : BaseNodeViewModel
{
    private readonly Action<GroupCommentVM, string?>? _onCommentEdited;

    /// <summary>The comment text (may contain newlines).</summary>
    [ObservableProperty]
    private string _comment = string.Empty;

    /// <summary>The statement's primary (anchor/leader) node id this comment is attached to.</summary>
    [ObservableProperty]
    private string _anchorNodeId = string.Empty;

    /// <summary>Node ids of the statement's data subgraph — drives the shared dashed frame.</summary>
    public HashSet<string> NodeIds { get; set; } = new();

    /// <summary>Note width.</summary>
    [ObservableProperty]
    private double _width = 180;

    /// <summary>Note height (expanded).</summary>
    [ObservableProperty]
    private double _height = 28;

    /// <summary>Collapsed shows only a one-line note; expanded shows the full text.</summary>
    [ObservableProperty]
    private bool _isCollapsed;

    /// <summary>True while the note text is being edited inline.</summary>
    [ObservableProperty]
    private bool _isEditing;

    /// <summary>Temporary text during inline editing.</summary>
    [ObservableProperty]
    private string _commentEditText = string.Empty;

    /// <summary>True while the mouse hovers the note — reveals the shared dashed frame.</summary>
    [ObservableProperty]
    private bool _isHovered;

    /// <summary>
    /// True while the note is docked to its statement leader (follows the leader's
    /// position). Set false when the user drags it far from any leader (free placement —
    /// node moves no longer pull it).
    /// </summary>
    [ObservableProperty]
    private bool _isDocked = true;

    public GroupCommentVM()
        : this(null) { }

    public GroupCommentVM(Action<GroupCommentVM, string?>? onCommentEdited)
    {
        _onCommentEdited = onCommentEdited;
    }

    /// <summary>Note text: full comment expanded, one line (ellipsised) when collapsed.</summary>
    public string BubbleText => IsCollapsed
        ? "// " + (Comment.Length > 32 ? Comment[..32] + "…" : Comment)
        : Comment;

    /// <summary>Expanded note height grows with the text.</summary>
    public double EffectiveHeight => IsCollapsed ? 24 : Height;

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
