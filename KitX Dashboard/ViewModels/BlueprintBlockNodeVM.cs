using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NodifyM.Avalonia.Controls;
using NodifyM.Avalonia.ViewModelBase;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// Block node variant of <see cref="BlueprintNodeVM"/>. Exists as a distinct
/// runtime type so that <c>NodeTemplateSelector</c> can dispatch to a dedicated
/// <c>NestedNode</c> DataTemplate via type-based <c>DataTemplate.Match</c>
/// (instead of the previous <c>Panel</c>-wrapped two-variant template, which
/// broke <c>ContentPresenter.Child is BaseNode</c> and prevented selection/drag).
/// <para>
/// Carries the nested child-node collection (<see cref="ChildNodes"/>) whose
/// <c>Location</c> is relative to the owning block (Phase 1, Approach B).
/// </para>
/// <para>
/// Per <c>NodifyM-Embedded-Node-Migration-Guide.md</c>: exposes an
/// <see cref="InnerEditor"/> property (a self-contained
/// <see cref="NodifyEditorViewModelBase"/> subclass) that the NestedNode and
/// SubEditorOverlay templates bind to via
/// <c>{ReflectionBinding InnerEditor}</c>. Inner-layer connections live in
/// <c>InnerEditor.Connections</c> and are isolated from the outer editor —
/// no outer Connect() routing logic is required for inner connections.
/// </para>
/// </summary>
public partial class BlueprintBlockNodeVM : BlueprintNodeVM
{
    /// <summary>
    /// Inner editor context. The NestedNode template binds its embedded
    /// <c>NodifyEditor</c> via <c>{ReflectionBinding InnerEditor}</c>; the
    /// <c>SubEditorOverlay</c> likewise binds
    /// <c>{ReflectionBinding InnerEditor.Nodes}</c> /
    /// <c>{ReflectionBinding InnerEditor.Connections}</c> /
    /// <c>{ReflectionBinding InnerEditor.PendingConnection}</c>.
    /// <para>
    /// Uses <see cref="BlueprintInnerEditorVM"/> (subclass) so inner
    /// connections are created as <see cref="BlueprintConnectionVM"/> and
    /// keep StrokeColorHex + PinType-following behaviour.
    /// </para>
    /// </summary>
    public BlueprintInnerEditorVM InnerEditor { get; } = new();

    /// <summary>
    /// Child nodes rendered inside this block. Forwards directly to
    /// <see cref="InnerEditor"/>'s <c>Nodes</c> collection — the inner
    /// NodifyEditor is the actual renderer. <c>Location</c> is relative to
    /// this block's <c>Location</c> (canvas-absolute origin of a child =
    /// <c>child.Location + this.Location</c>). Populated by
    /// <c>BlueprintEditorViewModel.InitializeBlockScopes</c>.
    /// </summary>
    public ObservableCollection<object?> ChildNodes => InnerEditor.Nodes;

    /// <summary>
    /// Inline = children render inside the NestedNode's own template on the
    /// outer canvas (Phase 1). SubEditor is reserved for Phase 2.
    /// </summary>
    [ObservableProperty]
    private NestedNodeExpandMode _expandMode = NestedNodeExpandMode.Embedded;
}