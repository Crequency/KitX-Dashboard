using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using NodifyM.Avalonia.Controls;
using NodifyM.Avalonia.ViewModelBase;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// Pins NodifyM's node positioning semantics: the editor's item CONTAINER is positioned
/// via a style binding Canvas.Left/Top → VM Location, and dragging writes the NODE
/// CONTROL's Location — which only reaches the VM (and thus the container) when the
/// node's Location binding is TwoWay. That is why the working editors (V6 blueprint,
/// upstream example) bind Location with Mode=TwoWay.
/// </summary>
public class NodifyNodePositionTests
{
    private sealed class FakeNodeVm : NodeViewModelBase
    {
        public FakeNodeVm(Point p) => Location = p;
    }

    private sealed class FakeEditorVm : NodifyEditorViewModelBase
    {
    }

    private static (NodifyEditor Editor, ContentPresenter Container) BuildEditor(BindingMode locationMode)
    {
        var editorVm = new FakeEditorVm();
        editorVm.Nodes.Add(new FakeNodeVm(new Point(100, 50)));

        var editor = new NodifyEditor
        {
            Width = 800,
            Height = 600,
            DataContext = editorVm,
            ItemsSource = editorVm.Nodes,
            ItemTemplate = new FuncDataTemplate<object>((_, _) =>
            {
                var n = new Node();
                n.Bind(BaseNode.LocationProperty, new Binding("Location") { Mode = locationMode });
                return n;
            }, true),
        };

        var window = new Window { Width = 800, Height = 600, Content = editor };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        editor.UpdateLayout();

        var container = editor.GetVisualDescendants().OfType<ContentPresenter>()
            .First(c => c.Content is FakeNodeVm);
        return (editor, container);
    }

    [AvaloniaFact]
    public void Container_Positions_From_Vm_Location()
    {
        var (_, container) = BuildEditor(BindingMode.OneWay);
        Assert.Equal(100, container.Bounds.X);
        Assert.Equal(50, container.Bounds.Y);
    }

    [AvaloniaFact]
    public void Drag_Reaches_Vm_And_Container_Only_With_TwoWay()
    {
        // TwoWay: control write-back updates the VM, the VM raises change,
        // the container's Canvas.Left binding re-evaluates → node moves.
        var (editor, container) = BuildEditor(BindingMode.TwoWay);
        var node = container.GetVisualDescendants().OfType<Node>().First();
        var vm = (FakeNodeVm)container.Content!;

        node.Location = new Point(210, 90); // what NodifyEditor.NodeDragging does
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        editor.UpdateLayout();

        Assert.Equal(new Point(210, 90), vm.Location);
        Assert.Equal(210, container.Bounds.X);
    }

    [AvaloniaFact]
    public void Drag_Is_Lost_With_OneWay_Location_Binding()
    {
        // OneWay (the Bench regression): the control's Location changes but the VM —
        // and therefore the positioned container — never hear about it.
        var (editor, container) = BuildEditor(BindingMode.OneWay);
        var node = container.GetVisualDescendants().OfType<Node>().First();
        var vm = (FakeNodeVm)container.Content!;

        node.Location = new Point(210, 90);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        editor.UpdateLayout();

        Assert.Equal(new Point(100, 50), vm.Location);
        Assert.Equal(100, container.Bounds.X);
    }
}
