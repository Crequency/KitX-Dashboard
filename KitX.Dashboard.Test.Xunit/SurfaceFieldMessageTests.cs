using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using KitX.Dashboard.ViewModels;
using KitX.ToolKit.Contract;
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Contract.Surface;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// A field that refuses what was typed into it says why, in the language the reader chose.
/// </summary>
/// <remarks>
/// <para><b>This is about one sentence, and the sentence is not the point.</b> Every other refusal in
/// this view model goes through <c>TranslateText</c> with an English fallback beside it; the number
/// field was the one that built its own sentence with an interpolated label. It landed in the same
/// column as every translated finding, so a reader who chose Chinese got a Chinese panel with one
/// English line in the middle of it — and nothing in the build looks at a string that is not a
/// resource key, which is why it survived.</para>
///
/// <para><b>Why these assertions compare against the dictionary rather than against each other.</b>
/// "Chinese is different from English" is true of a message that is Chinese and true of a message
/// that is English in both files — no, worse: it is true of any two strings that differ, including an
/// interpolated label and a constant. So the test reads the exact sentence out of
/// <c>zh-cn.axaml</c>, formats the same placeholder the product formats, and requires the product's
/// answer to be that string. A fallback that fired when it should not would be caught; so would a
/// <c>string.Format</c> applied to the resource but not to the fallback.</para>
/// </remarks>
public class SurfaceFieldMessageTests
{
    private const string Dashboard = @"KitX Clients\KitX Dashboard\KitX Dashboard";

    /// <summary>What the number field says when Chinese is the chosen language.</summary>
    /// <remarks>
    /// The resource's own text with its own placeholder, formatted the way the product formats it.
    /// Written as the <c>zh-cn</c> sentence rather than as a substring of it: a test that accepted
    /// "the message mentions 数字" would pass on an implementation that glued a Chinese word onto an
    /// English sentence.
    /// </remarks>
    [AvaloniaFact]
    public void A_number_field_says_why_it_refused_in_the_chosen_language()
    {
        LoadLanguage("zh-cn");

        var editor = Editor(new SurfaceNumberElement { Name = "copies", Row = 1 });

        editor.Select(editor.Elements[0]);
        Field(editor, "min").Value = "many";

        var sentence = Sentence("zh-cn", "Text_Bench_Surface_NumberNotANumber");

        Assert.Equal(string.Format(sentence, "min"), editor.ValidationMessage);

        // And it is not the English fallback wearing a Chinese coat: the two files disagree about
        // this sentence, so an implementation that ignored the resource would fail the line above
        // only if it happened to produce something else. This makes the intent explicit, and it is
        // the assertion a person reads first.
        Assert.NotEqual(
            string.Format(Sentence("en-us", "Text_Bench_Surface_NumberNotANumber"), "min"),
            editor.ValidationMessage);
    }

    /// <summary>The same refusal in English, so the resource and the fallback agree.</summary>
    /// <remarks>
    /// Both directions, because a one-way test cannot tell "the resource is used" from "the resource
    /// happens to be the fallback". If <c>en-us</c> and the C# fallback drift apart, the English
    /// reader sees a sentence that is in no file — which is the same class of defect as the Chinese
    /// reader seeing English.
    /// </remarks>
    [AvaloniaFact]
    public void The_english_sentence_and_the_fallback_are_the_same_sentence()
    {
        LoadLanguage("en-us");

        var editor = Editor(new SurfaceNumberElement { Name = "copies", Row = 1 });

        editor.Select(editor.Elements[0]);
        Field(editor, "min").Value = "many";

        Assert.Equal(string.Format(Sentence("en-us", "Text_Bench_Surface_NumberNotANumber"), "min"),
            editor.ValidationMessage);
    }

    /// <summary>The label the author was editing is named in the sentence, not left as a {0}.</summary>
    /// <remarks>
    /// A resource carrying a placeholder that the caller forgets to format shows up on screen as
    /// <c>'min' must be a number.</c> only if the format ran; if it did not, the reader sees
    /// <c>{0} must be a number.</c>. Both are strings, both are non-empty, and only one is a sentence.
    /// </remarks>
    [AvaloniaFact]
    public void The_refusal_names_the_field_it_refused()
    {
        LoadLanguage("zh-cn");

        var editor = Editor(new SurfaceNumberElement { Name = "copies", Row = 1 });

        editor.Select(editor.Elements[0]);
        Field(editor, "max").Value = "many";

        Assert.Contains("max", editor.ValidationMessage!, StringComparison.Ordinal);
        Assert.DoesNotContain("{0}", editor.ValidationMessage!, StringComparison.Ordinal);
    }

    /// <summary>The key exists in all seven files and carries the placeholder.</summary>
    /// <remarks>
    /// The completeness tests already compare key sets, so this one only has to exist to keep the
    /// failure local: when the key is dropped from one file, this says which file, while the
    /// seven-file comparison says "the sets differ".
    /// </remarks>
    [AvaloniaFact]
    public void Every_language_can_say_that_a_field_needs_a_number()
    {
        var missing = new List<string>();

        foreach (var language in new[] { "en-us", "fr-fr", "ja-jp", "ko-kr", "ru-ru", "zh-cn", "zh-tw" })
        {
            var path = RepositoryFile(Path.Combine(Dashboard, "Languages", language + ".axaml"));

            if (!File.Exists(path))
            {
                missing.Add($"{language}: no file at {path}");
                continue;
            }

            var dictionary = AvaloniaRuntimeXamlLoader.Load(File.ReadAllText(path));

            if (dictionary is not ResourceDictionary resources)
            {
                missing.Add($"{language}: {path} did not load as a resource dictionary");
                continue;
            }

            if (!resources.TryGetResource("Text_Bench_Surface_NumberNotANumber", null, out var found)
                || found is not string sentence
                || string.IsNullOrWhiteSpace(sentence))
            {
                missing.Add($"{language}: Text_Bench_Surface_NumberNotANumber");
                continue;
            }

            if (!sentence.Contains("{0}", StringComparison.Ordinal))
                missing.Add($"{language}: {sentence} — no {{0}}, so the field's name cannot appear");
        }

        Assert.True(missing.Count == 0, "Languages that cannot say a field needs a number:" +
            Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    /// <summary>One entry of one language file, read off disk.</summary>
    /// <remarks>
    /// Off disk, not from the loaded application: the resource dictionary installed by
    /// <see cref="LoadLanguage"/> only ever holds one language, so asking it for a second one
    /// answers with the first. The files are what is committed, and what is committed is the claim.
    /// </remarks>
    private static string Sentence(string language, string key)
    {
        var path = RepositoryFile(Path.Combine(Dashboard, "Languages", language + ".axaml"));
        Assert.True(File.Exists(path), $"Expected a language file at {path}.");

        var dictionary = Assert.IsType<ResourceDictionary>(AvaloniaRuntimeXamlLoader.Load(File.ReadAllText(path)));
        Assert.True(dictionary.TryGetResource(key, null, out var found), $"{language} has no {key}.");
        return Assert.IsType<string>(found);
    }

    /// <summary>Installs one language file the way the product does.</summary>
    private static void LoadLanguage(string language)
    {
        var app = Application.Current;
        Assert.NotNull(app);

        var path = RepositoryFile(Path.Combine(Dashboard, "Languages", language + ".axaml"));
        Assert.True(File.Exists(path), $"Expected a language file at {path}.");

        var dictionary = Assert.IsType<ResourceDictionary>(AvaloniaRuntimeXamlLoader.Load(File.ReadAllText(path)));

        // Replaced, not merged: a merged dictionary keeps the previous language's entries for the
        // same keys, and Avalonia answers from the earlier one — so the second test in this class
        // would silently read the first test's language.
        for (var i = app.Resources.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            if (app.Resources.MergedDictionaries[i] is ResourceDictionary existing
                && existing.ContainsKey("Text_Bench_Surface_NumberNotANumber"))
                app.Resources.MergedDictionaries.RemoveAt(i);
        }

        app.Resources.MergedDictionaries.Add(dictionary);
    }

    private static SurfaceFieldVM Field(SurfaceEditorViewModel editor, string label) =>
        Assert.Single(editor.Fields, field => field.Label == label);

    private static SurfaceEditorViewModel Editor(params SurfaceElement[] elements)
    {
        var document = new SurfaceDocument { Elements = [.. elements] };
        var editor = new SurfaceEditorViewModel(new InMemorySurfaceStore(document), _ => []);
        editor.Load("field-message-test");
        return editor;
    }

    private static string RepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KitX.sln")))
                return Path.Combine(directory.FullName, relativePath);

            directory = directory.Parent;
        }

        throw new InvalidOperationException("KitX.sln was not found above " + AppContext.BaseDirectory);
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

        public event EventHandler<SurfaceChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }
    }
}
