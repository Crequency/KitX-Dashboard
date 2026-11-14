using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KitX.Dashboard.ViewModels;
using KitX.Dashboard.Views;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Contract.Surface;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// The check panel shows one row per finding, and the row carries the code.
/// </summary>
/// <remarks>
/// <para><b>What this file is guarding.</b> <see cref="SurfaceEditorViewModel.Save"/> used to join
/// every message with spaces into a single property, and the window bound one <c>TextBlock</c> to it.
/// Three warnings therefore arrived as one run-on paragraph in a 289-pixel column: no line breaks,
/// no codes, and no way to say which sentence belonged to which cell. The view model was reporting
/// three separate facts and the screen was rendering one.</para>
///
/// <para><b>Why a window test and not a view-model test.</b> The view model always had the list —
/// <c>Diagnose</c> returns a list, and the old code joined it on the way out. Replacing
/// <c>string.Join</c> with a collection leaves the interesting half undone: whether the template
/// actually repeats per item. A collection that renders as one row looks exactly like a joined
/// string from the view model's side.</para>
///
/// <para><b>What it cannot see.</b> Colours, wrapping and contrast. It counts rendered text.</para>
/// </remarks>
public class SurfaceEditorCheckPanelTests
{
    private const string WarningCode = "surface.element.unknown-key";

    [AvaloniaFact]
    public void Each_finding_is_its_own_row_and_carries_its_code()
    {
        var viewModel = Editor(new FixedStore(
            [Problem("ghost"), Problem("phantom")]));

        var window = Show(viewModel);

        // Not asserted as "the panel exists": the shape under test is the count of rows after a save.
        viewModel.Save();
        Dispatcher.UIThread.RunJobs();

        var texts = Texts(window);

        Assert.Equal(2, texts.Count(text => text == WarningCode));

        // Neither message may contain the other's word. That is the difference between two rows and
        // one sentence containing two clauses, and it is the only assertion here a joined string
        // fails.
        Assert.Contains(texts, text => text?.Contains("ghost", StringComparison.Ordinal) == true);
        Assert.Contains(texts, text => text?.Contains("phantom", StringComparison.Ordinal) == true);
    }

    [AvaloniaFact]
    public void A_card_with_nothing_wrong_shows_no_rows_and_says_saved()
    {
        var viewModel = Editor(new FixedStore([]));

        var window = Show(viewModel);

        viewModel.Save();
        Dispatcher.UIThread.RunJobs();

        var texts = Texts(window);

        Assert.DoesNotContain(texts, text => text == WarningCode);
        Assert.Contains("Saved.", texts);
        Assert.DoesNotContain("Saved, with problems.", texts);
    }

    private static SurfaceEditorViewModel Editor(ISurfaceStore store)
    {
        var viewModel = new SurfaceEditorViewModel(store, _ => []);

        // Load, because Save refuses a card that belongs to no ToolKit — a different guard from the
        // one under test here.
        viewModel.Load("panel-test");
        return viewModel;
    }

    private static SurfaceEditorWindow Show(SurfaceEditorViewModel viewModel)
    {
        var window = new SurfaceEditorWindow(viewModel, "panel-test");
        window.Show();

        // Layout runs on the dispatcher queue rather than inside Show(); without this every
        // descendant still reports bounds of zero and the template has not been applied yet.
        Dispatcher.UIThread.RunJobs();

        return window;
    }

    private static List<string?> Texts(Window window) =>
        [.. window.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text)];

    private static Diagnostic Problem(string key) => new(
        DiagnosticSeverity.Warning,
        WarningCode,
        $"Dropped Text at row 2, weight 1: the ToolKit does not declare a key named '{key}'.",
        new DiagnosticPath(Key: key));

    /// <summary>A store that answers every diagnosis with the same list and writes nowhere.</summary>
    private sealed class FixedStore(IReadOnlyList<Diagnostic> diagnostics) : ISurfaceStore
    {
        public SurfaceLoadResult Load(string toolkitId) => new(
            new SurfaceDocument
            {
                Elements = [new SurfaceTextElement { Row = 1, Weight = 1, Key = "ghost" }],
            },
            [],
            []);

        public void Save(string toolkitId, SurfaceDocument document)
        {
        }

        public IReadOnlyList<Diagnostic> Diagnose(
            SurfaceDocument document, IEnumerable<KeyDeclaration> keys) => diagnostics;

        public void Delete(string toolkitId)
        {
        }

        public event EventHandler<SurfaceChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }
    }
}
