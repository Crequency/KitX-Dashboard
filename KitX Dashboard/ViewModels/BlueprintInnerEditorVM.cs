using NodifyM.Avalonia.ViewModelBase;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// Inner editor ViewModel for a <see cref="BlueprintBlockNodeVM"/>.
/// <para>
/// Subclasses <see cref="NodifyEditorViewModelBase"/> so that connections
/// created inside an Embedded/SubEditor nested node are
/// <see cref="BlueprintConnectionVM"/> instances (preserving StrokeColorHex
/// and PinType change-following) rather than the base
/// <see cref="ConnectionViewModelBase"/> the default Connect() would create.
/// </para>
/// <para>
/// This editor is fully self-contained: its own
/// <see cref="NodifyEditorViewModelBase.PendingConnection"/> (created in the
/// base constructor) routes inner drag-connect operations back to
/// <see cref="Connect(ConnectorViewModelBase, ConnectorViewModelBase)"/>,
/// so no outer-editor routing logic is required.
/// </para>
/// </summary>
public partial class BlueprintInnerEditorVM : NodifyEditorViewModelBase
{
    public override void Connect(ConnectorViewModelBase source, ConnectorViewModelBase target)
    {
        if (source is not BlueprintConnectorVM src || target is not BlueprintConnectorVM tgt)
        {
            base.Connect(source, target);
            return;
        }

        var alreadyConnected = false;
        foreach (var c in Connections)
        {
            if (c is BlueprintConnectionVM bp && bp.Source == src && bp.Target == tgt)
            {
                alreadyConnected = true;
                break;
            }
            if (c is BlueprintConnectionVM bp2 && bp2.Source == tgt && bp2.Target == src)
            {
                alreadyConnected = true;
                break;
            }
        }
        if (alreadyConnected) return;

        Connections.Add(new BlueprintConnectionVM(this, src, tgt));
        src.IsConnected = true;
        tgt.IsConnected = true;
    }
}