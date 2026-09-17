using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using NodifyM.Avalonia.ViewModelBase;

namespace KitX.Dashboard.ViewModels;

// ─────────────────────────────────────────────────────────────────────────────
// ScopeFrameVM — decorative background-frame ViewModel for sub-scope visual grouping.
//
// Per KScript-Blueprint-Correspondence.md §1.3/§3.6, the v6 Blueprint is flat (no
// nesting containers). Sub-scopes (if body / forEach body / switch arms) are rendered
// as *decorative* background frames — visual grouping only, never interactive containers.
//
// Inherits BaseNodeViewModel so it can live inside the NodifyEditor's ItemsSource
// alongside real nodes. It is rendered *first* (bottom ZOrder) via a NodeGroup-based
// DataTemplate that sets IsHitTestVisible=False (pure decoration, no interaction).
// The Location (from BaseNodeViewModel) holds the top-left corner; FrameWidth/
// FrameHeight carry the bounding-box dimensions from IScopeAnalyzer.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// ViewModel for a decorative background frame that visually groups a control-flow
/// sub-scope. Lives in the NodifyEditor's Nodes collection as a non-interactive
/// "ghost" node rendered below real nodes.
/// </summary>
public partial class ScopeFrameVM : BaseNodeViewModel
{
    /// <summary>Frame title (Then / Else / Body / Arm:43 / Default).</summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>Owner control-flow node's function name (Branch/Each/While/Switch).</summary>
    [ObservableProperty]
    private string _ownerFunctionName = string.Empty;

    /// <summary>Nesting depth (0 = direct child of top-level). Drives colour cycling.</summary>
    [ObservableProperty]
    private int _depth;

    /// <summary>Frame width (from ScopeRegion bounding box).</summary>
    [ObservableProperty]
    private double _frameWidth;

    /// <summary>Frame height (from ScopeRegion bounding box).</summary>
    [ObservableProperty]
    private double _frameHeight;

    /// <summary>
    /// True for a CONDITION/SOURCE frame — the data sub-graph feeding a control-flow
    /// node's Condition/List/Selector pin (e.g. the `i, 3 > Compare("BLT")` nodes in
    /// front of a While). Rendered as a dashed selection-style frame so it reads as
    /// background decoration, distinct from the solid sub-scope body frames.
    /// </summary>
    [ObservableProperty]
    private bool _isConditionFrame;

    /// <summary>Background colour hex, cycling by depth (4-colour palette, semi-transparent).</summary>
    public string BackgroundColorHex => DepthPalette(Depth, alpha: "1A");

    /// <summary>
    /// Border colour hex, cycling by depth (solid, more opaque). Condition frames use
    /// a lower-opacity dashed border so they stay background-like.
    /// </summary>
    public string BorderColorHex => DepthPalette(Depth, alpha: IsConditionFrame ? "4D" : "66");

    /// <summary>
    /// Stroke dash array for the frame border: dashed for condition/source frames
    /// (selection-style background), null for solid sub-scope body frames.
    /// </summary>
    public AvaloniaList<double>? BorderDashArray
        => IsConditionFrame ? new AvaloniaList<double> { 4, 3 } : null;

    /// <summary>
    /// 4-colour palette cycling by depth: Blue → Green → Yellow → Red.
    /// </summary>
    private static string DepthPalette(int depth, string alpha) => (depth % 4) switch
    {
        0 => $"#{alpha}2196F3",  // Blue
        1 => $"#{alpha}4CAF50",  // Green
        2 => $"#{alpha}FFC107",  // Yellow
        _ => $"#{alpha}F44336",  // Red
    };

    partial void OnDepthChanged(int value)
    {
        OnPropertyChanged(nameof(BackgroundColorHex));
        OnPropertyChanged(nameof(BorderColorHex));
    }

    partial void OnIsConditionFrameChanged(bool value)
    {
        OnPropertyChanged(nameof(BorderColorHex));
        OnPropertyChanged(nameof(BorderDashArray));
    }
}
