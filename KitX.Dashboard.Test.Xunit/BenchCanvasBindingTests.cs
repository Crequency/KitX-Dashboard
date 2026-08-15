using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using NodifyM.Avalonia.Controls;
using NodifyM.Avalonia.ViewModelBase;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(KitX.Dashboard.Test.Xunit.TestAppBuilder))]

namespace KitX.Dashboard.Test.Xunit;

public class TestApp : Application
{
    public override void Initialize()
    {
        // Real control templates so headless tests exercise the same visuals as the app.
        Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://KitX.Dashboard.Test.Xunit"))
        {
            Source = new Uri("avares://NodifyM.Avalonia/Styles/ControlStyles.axaml")
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
    }
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<TestApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// <summary>
/// Empirically pins down how Avalonia resolves a sibling binding on an element whose
/// DataContext is itself set through a local binding (the BenchWindow canvas pattern:
/// DataContext="{Binding Canvas}" + ItemsSource="{Binding Canvas.Nodes}").
/// </summary>
public class BenchCanvasBindingTests
{
    private sealed class FakeCanvas
    {
        public ObservableCollection<string> Nodes { get; } = ["n1", "n2"];
    }

    private sealed class FakeOuter
    {
        public FakeCanvas Canvas { get; } = new();
    }

    [AvaloniaFact]
    public void Local_DataContext_Breaks_Prefixed_Sibling_Binding()
    {
        // The original BenchWindow pattern: DataContext="{Binding Canvas}" makes the
        // sibling ItemsSource="{Binding Canvas.Nodes}" resolve against the Canvas VM
        // itself, where the path "Canvas.Nodes" does not exist → ItemsSource stays null
        // → the NodifyM canvas rendered nothing. Pins this semantics as a regression guard.
        var outer = new FakeOuter();
        var target = new ItemsControl();

        // Attribute order mirrors BenchWindow.axaml: DataContext first, then siblings.
        target.Bind(StyledElement.DataContextProperty, new Binding("Canvas"));
        target.Bind(ItemsControl.ItemsSourceProperty, new Binding("Canvas.Nodes"));

        var window = new Window { Content = target };
        window.DataContext = outer;
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Same(outer.Canvas, target.GetValue(StyledElement.DataContextProperty));
        Assert.Null(target.GetValue(ItemsControl.ItemsSourceProperty));
    }

    [AvaloniaFact]
    public void Prefixed_Sibling_Binding_Resolves_Without_Local_DataContext()
    {
        // The fixed pattern: no local DataContext on the editor — "Canvas.Nodes" resolves
        // against the inherited (window-level) BenchViewModel-like DataContext.
        var outer = new FakeOuter();
        var target = new ItemsControl();

        target.Bind(ItemsControl.ItemsSourceProperty, new Binding("Canvas.Nodes"));

        var window = new Window { Content = target };
        window.DataContext = outer;
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Same(outer.Canvas.Nodes, target.GetValue(ItemsControl.ItemsSourceProperty));
    }

    [AvaloniaFact]
    public void NodifyEditor_SelectedItems_TwoWay_Binding_WritesBackToVm()
    {
        // BenchWindow must bind NodifyEditor.SelectedItems with Mode=TwoWay: the Avalonia
        // property's default binding mode is OneWay, so node clicks would never reach
        // BenchCanvasViewModel.SelectedNodes and the inspector would stay on the toolkit
        // page. This pins the TwoWay contract as a regression guard.
        var editorVm = new NodifyEditorViewModelBase();
        editorVm.Nodes.Add("n1");
        editorVm.Nodes.Add("n2");

        var editor = new NodifyEditor
        {
            Width = 800,
            Height = 600,
            DataContext = editorVm,
            ItemsSource = editorVm.Nodes,
            ItemTemplate = new FuncDataTemplate<object>((_, _) => new Node(), true),
        };
        editor.Bind(NodifyEditor.SelectedItemsProperty, new Binding("SelectedNodes") { Mode = BindingMode.TwoWay });

        var window = new Window { Width = 800, Height = 600, Content = editor };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        editor.UpdateLayout();

        editor.Selection.Select(0);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Contains("n1", editorVm.SelectedNodes);
    }
}
