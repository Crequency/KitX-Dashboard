using System.Collections.Generic;
using System.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using Avalonia.Controls;
using KitX.Dashboard.Services;
using KitX.Dashboard.ViewModels;
using KitX.Dashboard.Views;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Contract.Events;
using KitX.ToolKit.Contract.Surface;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// Emptying the card asks first, and cancelling really means nothing happened.
/// </summary>
/// <remarks>
/// <para><b>Why the assertion is a call count rather than an empty canvas.</b> A test that only
/// checks "the canvas is still full after I said no" passes for a command that never ran, for one
/// that ran and threw, and for one whose confirmation always answers no so nobody ever noticed the
/// button does nothing. Recording the question and the emptying separately is what lets the two
/// claims be made independently.</para>
///
/// <para><b>Why this is red without the dialog.</b> The command used to be
/// <c>ReactiveCommand.Create(Clear)</c>, which empties the canvas on the first click with nobody
/// asked. Against that build the "not confirmed" case finds the canvas empty and
/// <see cref="AnsweringWindows.QuestionsAsked"/> zero, so the first test below fails on both
/// halves.</para>
/// </remarks>
public sealed class ClearSurfaceConfirmationTests
{
    /// <summary>A canvas with three elements, and a window service that answers whatever it is told.</summary>
    private static (SurfaceEditorViewModel Editor, AnsweringWindows Windows) Editor(bool answer)
    {
        var windows = new AnsweringWindows(answer);

        var editor = new SurfaceEditorViewModel(
            new QuietStore(
            [
                new SurfaceTextElement { Row = 1, Key = "a" },
                new SurfaceTextElement { Row = 2, Key = "b" },
                new SurfaceTextElement { Row = 3, Key = "c" },
            ]),
            _ => [new KeyDeclaration("a", "string", KeyRole.Out)],
            nameOf: null,
            windows: windows);

        editor.Load("clear-test");

        return (editor, windows);
    }

    [Fact]
    public async Task A_clear_that_is_cancelled_never_reaches_the_canvas()
    {
        var (editor, windows) = Editor(answer: false);
        editor.Load("clear-test");

        var before = editor.Elements.Count;

        await editor.ClearCommand.Execute().ToTask();

        Assert.Equal(1, windows.QuestionsAsked);
        Assert.Equal(before, editor.Elements.Count);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public async Task A_confirmed_clear_reaches_the_canvas_exactly_once()
    {
        var (editor, windows) = Editor(answer: true);
        editor.Load("clear-test");

        await editor.ClearCommand.Execute().ToTask();

        Assert.Equal(1, windows.QuestionsAsked);
        Assert.Empty(editor.Elements);
        Assert.True(editor.IsDirty);
    }

    [Fact]
    public async Task The_question_says_how_many_elements_are_about_to_go()
    {
        var (editor, windows) = Editor(answer: false);
        editor.Load("clear-test");

        var onCanvas = editor.Elements.Count;

        await editor.ClearCommand.Execute().ToTask();

        Assert.Equal(onCanvas, windows.LastCount);
    }

    /// <summary>
    /// Calls <see cref="SurfaceEditorViewModel.Clear"/> directly still empties the canvas.
    /// </summary>
    /// <remarks>
    /// The split between asking and emptying is only worth having if the emptying half is still
    /// callable on its own; a guard added inside <c>Clear</c> would make the command's tests pass
    /// while leaving no way to empty the canvas from code that already knows the answer.
    /// </remarks>
    [Fact]
    public void Clearing_without_asking_still_empties_the_canvas()
    {
        var (editor, _) = Editor(answer: false);
        editor.Load("clear-test");

        editor.Clear();

        Assert.Empty(editor.Elements);
    }

    /// <summary>
    /// A view model built without a window service empties the canvas without asking anybody.
    /// </summary>
    /// <remarks>
    /// Every existing test in this project builds the editor with three arguments. If an absent
    /// service meant "no", all of them would find a button that does nothing — a behaviour change
    /// disguised as a safety improvement. It means yes instead, because the elements are a draft and
    /// the file is untouched until saved.
    /// </remarks>
    [Fact]
    public async Task Without_a_window_service_the_command_still_empties_the_canvas()
    {
        var editor = new SurfaceEditorViewModel(
            new QuietStore([new SurfaceTextElement { Row = 1, Key = "a" }]),
            _ => [new KeyDeclaration("a", "string", KeyRole.Out)]);

        editor.Load("clear-test");

        await editor.ClearCommand.Execute().ToTask();

        Assert.Empty(editor.Elements);
    }

    /// <summary>A window service that records what it was asked and answers a fixed way.</summary>
    private sealed class AnsweringWindows(bool answer) : IWindowService
    {
        public int QuestionsAsked { get; private set; }

        public int LastCount { get; private set; }

        public MainWindow? MainWindow { get; set; }

        public PluginsLaunchWindow? PluginsLaunchWindow { get; set; }

        public Toolkit? BenchToolkit { get; set; }

        public List<Window> Windows { get; } = [];

        public Dictionary<string, Window> WorkflowEditorWindows { get; } = [];

        public Dictionary<string, Window> BenchWindows { get; } = [];

        public void OpenBenchWindow(Toolkit toolkit) => throw new NotSupportedException();

        public void ShowWorkflowEditorWindow(string workflowId, string filePath) => throw new NotSupportedException();

        public void ShowMermaidExportWindow(string text, string? ownerToolkitId) => throw new NotSupportedException();

        public void OpenSurfaceEditorWindow(string toolkitId) => throw new NotSupportedException();

        public void ShowWindow<T>(T window, Window? owner = null, bool showDialog = false, bool onlyOneInSameTime = false)
            where T : Window => throw new NotSupportedException();

        public void ShowDialog<T>(T window, Window? owner = null) where T : Window => throw new NotSupportedException();

        public Task<bool> ConfirmDeleteToolkitAsync(string toolkitName, Window? owner = null)
            => throw new NotSupportedException();

        public Task<bool> ConfirmClearSurfaceAsync(int elementCount, Window? owner = null)
        {
            QuestionsAsked++;
            LastCount = elementCount;
            return Task.FromResult(answer);
        }
    }

    /// <summary>A store that reads one document and says nothing about it.</summary>
    private sealed class QuietStore(IReadOnlyList<SurfaceElement> elements) : ISurfaceStore
    {
        public SurfaceLoadResult Load(string toolkitId) =>
            new(new SurfaceDocument { Elements = elements }, [], []);

        public IReadOnlyList<Diagnostic> Diagnose(SurfaceDocument document, IEnumerable<KeyDeclaration> keys) => [];

        public void Save(string toolkitId, SurfaceDocument document)
        {
        }

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
