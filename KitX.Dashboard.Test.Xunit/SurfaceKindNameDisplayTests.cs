using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KitX.Dashboard.Converters;
using KitX.Dashboard.ViewModels;
using KitX.Dashboard.Views;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Contract.Surface;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// The editor names element kinds in the reader's language, while everything that compares a kind
/// keeps the schema's word.
/// </summary>
/// <remarks>
/// The split is the whole point of this file: two of these tests would pass on a bad implementation
/// that translated the collections, which is why the third one asserts the collections' values. A
/// translated <c>PaletteKinds</c> would still render correctly and would silently stop matching what
/// <c>AddKindCommand</c> and <c>ChangeType</c> expect, so only the pair of assertions together pins
/// the design.
/// </remarks>
public class SurfaceKindNameDisplayTests
{
    /// <summary>
    /// The palette shows translated names and still hands the schema's word to the command.
    /// </summary>
    /// <remarks>
    /// Both halves are read off the same rendered buttons: the visible text proves the display, and
    /// the command parameter proves what a click would carry. Reading only the text would let a
    /// regression that translated the collection pass, because the button would look right and
    /// <c>AddKindCommand</c> would be given "按钮" — a kind the schema does not have.
    /// </remarks>
    [AvaloniaFact]
    public void The_palette_says_the_kind_in_the_readers_language_and_sends_the_schemas_word()
    {
        LoadChinese();

        var editor = Editor();
        var window = new SurfaceEditorWindow(editor, "kind-name-test");
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var palette = window.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("palette"))
            .ToList();

        Assert.Equal(editor.PaletteKinds.Count, palette.Count);

        foreach (var button in palette)
        {
            var shown = Assert.IsType<TextBlock>(button.Content).Text;
            var sent = Assert.IsType<string>(button.CommandParameter);

            Assert.Equal(Named(sent), shown);
            Assert.Contains(sent, editor.PaletteKinds);
        }
    }

    /// <summary>
    /// The cell's corner label names the kind in the reader's language.
    /// </summary>
    /// <remarks>
    /// The corner is 31px wide at ten equal weights and trims rather than clips, so a name that is
    /// translated must also be short enough to survive: this asserts the text the cell actually
    /// carries, not the text it was handed.
    /// </remarks>
    [AvaloniaFact]
    public void A_cell_names_its_kind_in_the_readers_language()
    {
        LoadChinese();

        var editor = Editor(new SurfaceInputElement { Name = "word", Row = 1 });
        var window = new SurfaceEditorWindow(editor, "kind-name-test");
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // The palette button for the same kind says the same word, so the corner is found by size:
        // the cell's label is the only 10pt one in the window.
        var cell = Assert.Single(window.GetVisualDescendants().OfType<TextBlock>()
            .Where(block => block.Text == Named(SurfaceElementKind.Input) && block.FontSize == 10));

        Assert.Equal(Named(SurfaceElementKind.Input), cell.Text);
        Assert.NotEqual(SurfaceElementKind.Input, cell.Text);
    }

    /// <summary>
    /// The type dropdown lists translated names and still stores the schema's word.
    /// </summary>
    /// <remarks>
    /// Same pair as the palette, and the reason the dropdown needed an <c>ItemTemplate</c> rather
    /// than a converted collection: <c>ChangeType</c> switches on the value, so the selection has to
    /// stay a schema name even while the visible item is a translated one.
    /// </remarks>
    [AvaloniaFact]
    public void The_type_dropdown_lists_the_readers_language_and_stores_the_schemas_word()
    {
        LoadChinese();

        var editor = Editor(new SurfaceTextElement { Key = "title", Row = 1 });
        var window = new SurfaceEditorWindow(editor, "kind-name-test");
        window.Show();
        Dispatcher.UIThread.RunJobs();

        editor.Select(editor.Elements[0]);
        Dispatcher.UIThread.RunJobs();

        var picker = window.GetVisualDescendants().OfType<ComboBox>()
            .Single(box => box.ItemsSource is IReadOnlyList<string> choices && choices.Count == 7);

        // The selection is the schema's word: ChangeType compares against it.
        Assert.Equal(SurfaceElementKind.Text, picker.SelectedItem);

        // What the closed dropdown shows is the translated one.
        var shown = window.GetVisualDescendants().OfType<TextBlock>()
            .Any(block => block.Text == Named(SurfaceElementKind.Text));

        Assert.True(shown, $"Expected the dropdown to show '{Named(SurfaceElementKind.Text)}' for {SurfaceElementKind.Text}.");
    }

    /// <summary>The word this client uses for a kind, asked the same way the converter asks.</summary>
    private static string Named(string kind) => ViewModelBase.Translate(SurfaceElementKind.NameKeyOf(kind)) ?? kind;

    /// <summary>
    /// Loads the shipped Chinese dictionary, because English cannot tell the two words apart.
    /// </summary>
    /// <remarks>
    /// In <c>en-us</c> the translated name and the schema's word are the same string — "Input" is
    /// both — so every assertion in this file would pass against an implementation that did nothing
    /// at all. Chinese is the language the defect was reported in and the one where the two words
    /// differ, so it is the only language these tests can be run in.
    /// </remarks>
    private static void LoadChinese()
    {
        var app = Application.Current;
        Assert.NotNull(app);

        if (app!.Resources.TryGetResource(SurfaceElementKind.NameKeyOf(SurfaceElementKind.Input), null, out _))
            return;

        var path = RepositoryFile(Path.Combine(Dashboard, "Languages", "zh-cn.axaml"));
        Assert.True(File.Exists(path), $"Expected a language file at {path}.");

        var dictionary = Assert.IsType<ResourceDictionary>(
            AvaloniaRuntimeXamlLoader.Load(File.ReadAllText(path)));

        app.Resources.MergedDictionaries.Add(dictionary);
    }

    /// <summary>The dashboard project, relative to the repository root.</summary>
    private const string Dashboard = @"KitX Clients\KitX Dashboard\KitX Dashboard";

    private static string RepositoryFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "KitX.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, relativePath);
    }

    private static SurfaceEditorViewModel Editor(params SurfaceElement[] elements) =>
        EditorWithKeys([], elements);

    private static SurfaceEditorViewModel EditorWithKeys(
        IReadOnlyList<KeyDeclaration> keys, params SurfaceElement[] elements)
    {
        var document = new SurfaceDocument { Elements = [.. elements] };
        var editor = new SurfaceEditorViewModel(new InMemorySurfaceStore(document), _ => keys);
        editor.Load("kind-name-test");
        return editor;
    }

    /// <summary>A store that keeps one document in memory and answers for it.</summary>
    private sealed class InMemorySurfaceStore(SurfaceDocument document) : ISurfaceStore
    {
        public SurfaceLoadResult Load(string toolkitId) => new(document, [], []);

        public void Save(string toolkitId, SurfaceDocument saved)
        {
        }

        public void Delete(string toolkitId)
        {
        }

        public IReadOnlyList<Diagnostic> Diagnose(
            SurfaceDocument document, IEnumerable<KeyDeclaration> keys) => [];

        public event System.EventHandler<SurfaceChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }
    }
}
