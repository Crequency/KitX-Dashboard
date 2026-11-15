using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using KitX.Dashboard.ViewModels;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// The workflow editor (v6) shows no sentence of its own making in one fixed language: every word a
/// reader can see comes from <c>Text_WorkflowEditor_*</c>, and changing the language changes it.
/// </summary>
/// <remarks>
/// <para><b>What went wrong before this file.</b> The window was written in both languages at once —
/// Chinese labels beside English ones, a status line in English while the error above it was Chinese —
/// and none of it moved when the reader picked French. Nothing failed: a literal renders whatever it
/// says. The only way anybody noticed was a person reading the window and remembering which half of it
/// they had not translated.</para>
///
/// <para><b>Why a scan and not a list of strings.</b> A list would be a photograph of today's window.
/// The scan asserts the property the window has to keep: every visible text is a resource reference or
/// a token that is not a word, and every reference resolves in all seven files. A new label added as a
/// literal fails it on the day it is written.</para>
///
/// <para><b>What it cannot see.</b> A sentence assembled at run time from fragments, and a sentence
/// that is faithful to the English and wrong. The first would need a type system and the second needs a
/// French reader. This closes the case where the window is in a fixed language at all.</para>
/// </remarks>
public class WorkflowEditorV6LocalizationTests
{
    private const string Dashboard = @"KitX Clients\KitX Dashboard\KitX Dashboard";

    private const string FallbackLanguage = "en-us";

    private static readonly string[] Languages =
        ["en-us", "fr-fr", "ja-jp", "ko-kr", "ru-ru", "zh-cn", "zh-tw"];

    /// <summary>The four files that make up the window.</summary>
    private static readonly string[] Window =
    [
        Path.Combine("Views", "WorkflowEditorWindowV6.axaml"),
        Path.Combine("Views", "WorkflowEditorWindowV6.axaml.cs"),
        Path.Combine("ViewModels", "WorkflowEditorViewModelV6.cs"),
        Path.Combine("ViewModels", "BlueprintEditorViewModelV6.cs"),
    ];

    private static readonly Regex Entry = new(
        @"x:Key=""(?<key>[^""]+)""[^>]*>(?<value>[\s\S]*?)</(?:[A-Za-z]+:)?String>",
        RegexOptions.Compiled);

    /// <summary>
    /// The attributes that put words in front of the reader, and the element forms of the same thing.
    /// </summary>
    /// <remarks>
    /// <para><b>Why the list is attributes and not "anything that looks like text".</b> A scan for
    /// alphabetic characters would flag colours, bindings, enum values and event names. These are the
    /// properties whose value is drawn.</para>
    ///
    /// <para><b><c>ToolTip.Tip</c> is here because it is the one this window forgot.</b> Twenty-nine
    /// tooltips were the whole reason a reader could hover a button in a French session and read
    /// Chinese; a scan that covered <c>Text</c> and <c>Content</c> alone would have called the window
    /// fully localised.</para>
    /// </remarks>
    private static readonly string[] VisibleAttributes =
        ["Text", "Content", "Title", "Watermark", "ToolTip.Tip", "Header"];

    /// <summary>
    /// The literals this window is allowed to keep, each with the reason it is not a word.
    /// </summary>
    /// <remarks>
    /// Kept short on purpose: an excuse list long enough to be convenient is a list that hides the next
    /// untranslated label. <see cref="Every_excused_literal_is_still_in_the_window"/> fails when one of
    /// these stops appearing, so the list cannot outlive the markup it describes.
    /// </remarks>
    private static readonly Dictionary<string, string> NotWords = new(StringComparer.Ordinal)
    {
        ["\u238d"] = "a glyph the window draws beside a step-over button; no word to translate",
        ["\u21ba"] = "a rotation arrow drawn as the undo button",
        ["KScript"] = "the name of the language the editor is editing, not a description of it",
        ["X"] = "the button that closes or removes a row; the glyph is the whole label",
        ["R"] = "the single letter on the reset button, beside its tooltip which is translated",
        ["\U0001F4AC"] = "a speech-balloon emoji standing in for a group comment",
        ["const"] = "the KS keyword itself: a translation would change the language, not describe it",
        ["var"] = "the KS keyword itself",
        ["\u00d7"] = "the multiplication sign on the close button of a key-value row",
    };

    /// <summary>
    /// No visible text in the window is a literal: each one is a resource reference or a token that is
    /// not a word.
    /// </summary>
    [Fact]
    public void No_visible_text_in_the_window_is_a_literal()
    {
        var markup = File.ReadAllText(RepositoryFile(Path.Combine(Dashboard, Window[0])));

        var offenders = VisibleLiterals(markup)
            .Where(literal => !NotWords.ContainsKey(literal.Value))
            .Select(literal => $"line {literal.Line}: {literal.Attribute}=\"{literal.Value}\"")
            .ToList();

        Assert.True(offenders.Count == 0,
            $"{offenders.Count} visible text(s) in the workflow editor are literals, so they read the " +
            "same in every language:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// Every reference the window and its view models make resolves in every language file.
    /// </summary>
    /// <remarks>
    /// <para><b>Why the product's own lookup is used.</b> An earlier scan in this repository rebuilt the
    /// key in the test — <c>prefix + separator + name</c> — and agreed with the broken builder by
    /// construction, so the defect it was written for went unnoticed. Here every reference is resolved
    /// by <see cref="ViewModelBase.Translate"/>, which is what the window itself calls.</para>
    ///
    /// <para><b>Why all seven files and not one.</b> A key missing from one file is invisible to a scan
    /// that reads another: the reader who picked that language gets the English fallback in the middle of
    /// a translated window, which is exactly the symptom this file exists to catch.</para>
    /// </remarks>
    [AvaloniaFact]
    public void Every_workflow_editor_reference_resolves_in_every_language()
    {
        var app = Application.Current!;

        var referenced = ReferencedKeys();

        // Hundreds of references to a few dozen keys; a scan that found none would be green and useless.
        Assert.True(referenced.Count > 60,
            $"expected the window to reference more than 60 keys, found {referenced.Count}");

        var missing = new List<string>();

        foreach (var language in Languages)
        {
            LoadLanguage(app, language);

            foreach (var (key, site) in referenced)
                if (ViewModelBase.Translate(key, app: app) is null)
                    missing.Add($"{language}: {key} (from {site})");
        }

        Assert.True(missing.Count == 0,
            $"{missing.Count} reference(s) resolve to nothing, so the English fallback beside each one is " +
            "the only text that language shows:" + Environment.NewLine +
            string.Join(Environment.NewLine, missing));
    }

    /// <summary>
    /// The sentences the view models compose are rebuilt when the language changes.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this needs its own check.</b> The XAML side is handled by the framework: a
    /// <c>DynamicResource</c> re-resolves when the dictionary changes, so a label follows the reader for
    /// free. A sentence the view model has already put in a property does not — it was resolved once, and
    /// nothing re-asks. That is not a hypothetical: the status line, the output frame and the blueprint
    /// error all showed their first language until the window was reopened.</para>
    ///
    /// <para><b>What is asserted.</b> A recipe is filed for each sentence and re-resolved through the
    /// product's lookup, so a change of language reaches it. Asked of the classes that hold the recipes
    /// rather than by rebuilding a dictionary lookup here.</para>
    /// </remarks>
    [AvaloniaFact]
    public void A_composed_sentence_is_asked_for_again_after_the_language_changes()
    {
        var app = Application.Current!;

        // The English first, while the English file is loaded: asking for it after the switch would
        // compare Chinese to Chinese and pass for the wrong reason.
        LoadLanguage(app, FallbackLanguage);
        var english = WorkflowEditorText.Get("StatusSaved", "Saved (v6)");

        LoadLanguage(app, "zh-cn");
        var chinese = ViewModelBase.Translate("Text_WorkflowEditor_StatusSaved", app: app);

        Assert.Equal("Saved (v6)", english);
        Assert.NotNull(chinese);
        Assert.NotEqual(english, chinese);

        // The recipe carries the key, so asking again after the dictionary changes answers the new
        // language. A finished sentence could not: this is the whole reason the recipe exists.
        var recipe = WorkflowEditorText.Make("StatusSaved", "Saved (v6)");

        var before = recipe.Text;

        LoadLanguage(app, "fr-fr");
        var after = recipe.Text;

        Assert.NotEqual(before, after);
        Assert.Equal(ViewModelBase.Translate("Text_WorkflowEditor_StatusSaved", app: app), after);
    }

    /// <summary>
    /// Every key the window references is defined in all seven files.
    /// </summary>
    /// <remarks>
    /// Deliberately a separate assertion from the resolution check: this one names the file that is
    /// missing the entry, which is the difference between "a key is undefined somewhere" and "here is the
    /// file to open".
    /// </remarks>
    [Fact]
    public void Every_workflow_editor_key_exists_in_all_seven_files()
    {
        var referenced = ReferencedKeys().Select(pair => pair.Key).Distinct(StringComparer.Ordinal).ToList();

        var missing = new List<string>();

        foreach (var language in Languages)
        {
            var defined = ReadLanguage(language);

            foreach (var key in referenced)
                if (!defined.ContainsKey(key))
                    missing.Add($"{language}.axaml is missing {key}");
        }

        Assert.True(missing.Count == 0,
            $"{missing.Count} entr{(missing.Count == 1 ? "y is" : "ies are")} missing from a language file:" +
            Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    /// <summary>
    /// The excuse list still excuses something that is still there.
    /// </summary>
    /// <remarks>
    /// An exception list rots in one direction: the literal gets translated, the entry stays behind, and
    /// the next literal that happens to match it is silently allowed. Asking the markup rather than the
    /// list is what stops that.
    /// </remarks>
    [Fact]
    public void Every_excused_literal_is_still_in_the_window()
    {
        var markup = File.ReadAllText(RepositoryFile(Path.Combine(Dashboard, Window[0])));

        var literals = VisibleLiterals(markup).ToList();

        foreach (var (value, reason) in NotWords)
        {
            Assert.True(literals.Any(literal => literal.Value == value),
                $"\"{value}\" is excused as {reason}, and the window no longer shows it; drop the excuse.");
        }
    }

    /// <summary>
    /// A key defined for this window is referenced by this window.
    /// </summary>
    /// <remarks>
    /// The other direction of the same question, and the one that catches a rename: a key nobody
    /// references is a sentence kept in seven files and translated forever for nothing. Asked only of the
    /// <c>Text_WorkflowEditor_</c> family, because that family belongs to this window alone.
    /// </remarks>
    [Fact]
    public void No_workflow_editor_key_is_defined_and_never_used()
    {
        var referenced = ReferencedKeys()
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal);

        const string Prefix = "Text_WorkflowEditor_";

        var unused = ReadLanguage(FallbackLanguage)
            .Where(entry => entry.Value is not null && entry.Key.StartsWith(Prefix, StringComparison.Ordinal))
            .Select(entry => entry.Key)
            .Where(key => !referenced.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        Assert.True(unused.Count == 0,
            $"{unused.Count} key(s) exist for this window and nothing asks for them:" +
            Environment.NewLine + string.Join(Environment.NewLine, unused));
    }

    /// <summary>The visible text literals in the markup, with the line each sits on.</summary>
    private static IEnumerable<(int Line, string Attribute, string Value)> VisibleLiterals(string markup)
    {
        var attribute = new Regex(
            @"\b(?<attribute>" + string.Join("|", VisibleAttributes.Select(Regex.Escape)) + @")=""(?<value>[^""]*)""");

        foreach (Match match in attribute.Matches(markup))
        {
            var value = match.Groups["value"].Value;

            // A binding, a resource reference, a converter call: markup, not words.
            if (value.StartsWith('{'))
                continue;

            if (string.IsNullOrWhiteSpace(value))
                continue;

            var line = markup.Take(match.Index).Count(character => character == '\n') + 1;

            yield return (line, match.Groups["attribute"].Value, Decode(value));
        }
    }

    /// <summary>An XML entity turned back into the character it stands for.</summary>
    /// <remarks>
    /// The markup writes the emoji as <c>&amp;#x1F4AC;</c>; comparing the entity to the character would
    /// make the excuse list and the window disagree about a literal they both spell the same way.
    /// </remarks>
    private static string Decode(string value)
    {
        var decoded = value;

        foreach (Match entity in Regex.Matches(value, @"&#(?:x(?<hex>[0-9A-Fa-f]+)|(?<dec>[0-9]+));"))
        {
            var code = entity.Groups["hex"].Success
                ? Convert.ToInt32(entity.Groups["hex"].Value, 16)
                : int.Parse(entity.Groups["dec"].Value);

            decoded = decoded.Replace(entity.Value, char.ConvertFromUtf32(code));
        }

        return decoded;
    }

    /// <summary>Every <c>Text_WorkflowEditor_*</c> key the four files ask for, and where.</summary>
    /// <remarks>
    /// <para><b>A name counts when a language file defines it under this window's prefix.</b> The first
    /// version of this scan took the first quoted argument of each call, which is right for
    /// <c>Get("StatusSaved", …)</c> and wrong twice over: <c>Reject(KsConstraintErrorCodes.KS102, "E3",
    /// "BpErrExecIncoming", …)</c> carries its key third with an unquoted enum first, and a fix
    /// suggestion is the ninth argument of a <c>Message</c>. Counting positions meant a scan that
    /// reported 17 defined keys as dead while they sat in the file in plain sight — the failure mode
    /// this test exists to prevent, produced by the test.</para>
    ///
    /// <para><b>Why no positions at all.</b> A quoted name is a key if, and only if, the shipped files
    /// define <c>Text_WorkflowEditor_</c> + that name. The dictionary is the authority; the call shape
    /// does not have to be. That also survives the next carrier: a tenth field on the record, a ternary
    /// picking between two keys, a key passed to a helper not written yet — all of them are just another
    /// quoted name in the file.</para>
    ///
    /// <para><b>What it can over-count.</b> A literal that happens to spell a key nothing else uses
    /// would be counted as a use. The word list this has to coincide with is 145 long and the literals
    /// are English sentences and punctuation, so the coincidence is not realistic; the cost of it, if it
    /// ever happened, is one dead key going unnoticed, which
    /// <see cref="LanguageKeyUsageTests"/> also looks for from the other side.</para>
    /// </remarks>
    private static List<(string Key, string Site)> ReferencedKeys()
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        var defined = ReadLanguage(FallbackLanguage);

        var markup = File.ReadAllText(RepositoryFile(Path.Combine(Dashboard, Window[0])));

        foreach (Match match in Regex.Matches(markup, @"\{DynamicResource\s+(?<key>Text_WorkflowEditor_[A-Za-z0-9_]+)\}"))
            found.TryAdd(match.Groups["key"].Value, "the markup");

        foreach (var file in Window.Skip(1))
        {
            var source = File.ReadAllText(RepositoryFile(Path.Combine(Dashboard, file)));
            var name = Path.GetFileName(file);

            foreach (Match match in Regex.Matches(source, @"""(?<value>[A-Za-z][A-Za-z0-9_]*)"""))
            {
                var candidate = WorkflowEditorText.KeyPrefix + match.Groups["value"].Value;

                if (defined.ContainsKey(candidate))
                    found.TryAdd(candidate, name);
            }
        }

        return found.Select(pair => (pair.Key, pair.Value)).ToList();
    }

    /// <summary>One language file as key to value.</summary>
    private static Dictionary<string, string?> ReadLanguage(string language)
    {
        var path = RepositoryFile(Path.Combine(Dashboard, "Languages", language + ".axaml"));

        Assert.True(File.Exists(path), $"Expected a language file at {path}.");

        var entries = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (Match match in Entry.Matches(File.ReadAllText(path)))
            entries[match.Groups["key"].Value] = match.Groups["value"].Value;

        Assert.True(entries.Count > 300,
            $"expected more than 300 entries in {language}.axaml, read {entries.Count}");

        return entries;
    }

    /// <summary>
    /// Loads one language file the way the product does: off disk, through the runtime XAML loader,
    /// because <c>Languages/*.axaml</c> is <c>Content</c> and cannot be reached by a <c>StyleInclude</c>.
    /// </summary>
    private static void LoadLanguage(Application app, string language)
    {
        app.Resources.MergedDictionaries.Clear();

        var path = RepositoryFile(Path.Combine(Dashboard, "Languages", language + ".axaml"));
        Assert.True(File.Exists(path), $"Expected a language file at {path}.");

        var dictionary = AvaloniaRuntimeXamlLoader.Load(File.ReadAllText(path)) as ResourceDictionary;
        Assert.NotNull(dictionary);

        app.Resources.MergedDictionaries.Add(dictionary!);
    }

    /// <summary>Walks up from the test binary until it finds the solution, then reads the file.</summary>
    private static string RepositoryFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "KitX.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, relativePath);
    }
}
