using CommunityToolkit.Mvvm.ComponentModel;
using NodifyM.Avalonia.ViewModelBase;

namespace KitX.Dashboard.ViewModels;

// ─────────────────────────────────────────────────────────────────────────────
// GroupCommentHighlightVM — the SINGLE shared dashed-frame overlay for group comments.
//
// One instance lives in the NodifyEditor's Nodes collection at the top of the Z-order
// (after real nodes and all notes). It is a pure decoration: IsHitTestVisible=False,
// never interactive. While a group-comment note is hovered, the editor positions and
// sizes it to cover that statement's data subgraph; when nothing is hovered its size
// is zero so it effectively does not exist (no hit-test conflict with anything).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// ViewModel for the shared dashed subgraph frame shown while a group-comment note is
/// hovered. Zero size when hidden (the XAML template renders Width/Height only — a
/// zero-size Rectangle is effectively absent, no hit-test conflict).
/// </summary>
public partial class GroupCommentHighlightVM : BaseNodeViewModel
{
    [ObservableProperty]
    private double _width;

    [ObservableProperty]
    private double _height;
}
