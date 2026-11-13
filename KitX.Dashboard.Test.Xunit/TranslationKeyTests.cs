using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using KitX.Dashboard.ViewModels;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// Every key a <c>Translate*</c> call site names must actually resolve, because a miss returns
/// <see langword="null"/> and the neighbouring <c>?? "English text"</c> silently becomes the
/// product's only copy. The dashboard shipped that way for its entire Bench canvas and its card
/// orchestrator: <c>Translate</c> joined prefix, key and suffix unconditionally, so an empty suffix
/// left a trailing separator and <c>Text_Bench_Save</c> was looked up as <c>Text_Bench_Save_</c>.
/// </summary>
/// <remarks>
/// <para><b>Why this reads call sites instead of trusting a list.</b> The defect was not "one wrong
/// key" — it was "the function that builds keys is wrong", so every call site was affected and none
/// of them looked wrong. A test written against a hand-picked key proves one line; enumerating the
/// literals proves the shape.</para>
///
/// <para><b>Two call shapes, and the difference matters.</b> <c>TranslateText("Bench_X")</c> builds
/// <c>Text_Bench_X</c>; <c>TranslateTextWithSuffix("Bench", "X")</c> builds the same thing by a
/// different route. Both must be checked, because a fix that repairs one shape and not the other
/// is exactly what a reader of <c>Translate</c> would write while looking only at the first
/// one.</para>
///
/// <para><b>What it does not prove.</b> That the translated text is <em>good</em>, or that it exists
/// in every language — only that at least one language file, as shipped, answers the key. A key
/// present in <c>zh-cn</c> and missing from <c>en-us</c> passes here. That gap is real and this test
/// does not close it; it closes the one where <em>no</em> language answers and the reader therefore
/// can never see a translation at all.</para>
/// </remarks>
public class TranslationKeyTests
{
    private const string Dashboard = @"KitX Clients\KitX Dashboard\KitX Dashboard";

    /// <summary><c>TranslateText("a")</c> or <c>ViewModelBase.TranslateText("a")</c>.</summary>
    private static readonly Regex BareCall =
        new(@"\bTranslateText\(\s*""(?<key>[^""]+)""", RegexOptions.Compiled);

    /// <summary><c>TranslateTextWithSuffix("a", "b")</c>.</summary>
    private static readonly Regex SuffixCall =
        new(@"\bTranslateTextWithSuffix\(\s*""(?<key>[^""]+)""\s*,\s*""(?<suffix>[^""]+)""",
            RegexOptions.Compiled);

    /// <summary><c>Translate("Text_Key")</c> — a fully written key, prefix included.</summary>
    private static readonly Regex DirectCall =
        new(@"(?<![\w.])Translate\(\s*""(?<key>[^""]+)""", RegexOptions.Compiled);

    private static string RepositoryFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "KitX.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, relativePath);
    }

    /// <summary>Every <c>.cs</c> file under the dashboard project, excluding build output.</summary>
    private static IEnumerable<string> Sources()
    {
        var root = RepositoryFile(Dashboard);
        var bin = Path.Combine(root, "bin");
        var obj = Path.Combine(root, "obj");

        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.StartsWith(bin, StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.StartsWith(obj, StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The resource dictionary shipped as <c>zh-cn</c>, loaded exactly the way the product loads it:
    /// <c>Languages/*.axaml</c> is not an <c>AvaloniaResource</c>, so <c>StyleInclude</c> cannot reach
    /// it and <c>LanguageLoader</c> reads the file off disk through the runtime XAML loader. Loading
    /// it the same way here means the test fails for the same reasons the product would.
    /// </summary>
    private static void LoadLanguage(Application app)
    {
        var path = RepositoryFile(Path.Combine(Dashboard, "Languages", "zh-cn.axaml"));
        Assert.True(File.Exists(path), $"Expected a language file at {path}.");

        var dictionary = AvaloniaRuntimeXamlLoader.Load(File.ReadAllText(path)) as ResourceDictionary;
        Assert.NotNull(dictionary);

        app.Resources.MergedDictionaries.Add(dictionary!);
    }

    /// <summary>
    /// The key each call shape asks for, obtained by asking the product rather than by rebuilding
    /// the key here.
    /// </summary>
    /// <remarks>
    /// <para><b>Why it calls the product.</b> The first version of this file reimplemented the join
    /// in the test — <c>prefix + separator + key + separator + suffix</c>, with the empty segments
    /// dropped. It agreed with the fixed code by construction, so mutating the product back to the
    /// broken formula left the scan green: the test was checking its own copy of the rule. A test
    /// that recomputes the subject's answer is testing the test.</para>
    ///
    /// <para>Now the arguments go through <see cref="ViewModelBase.TranslateText"/>,
    /// <see cref="ViewModelBase.TranslateTextWithSuffix"/> and <see cref="ViewModelBase.Translate"/>,
    /// so the builder is exercised and a wrong builder shows up as missing keys. The empty-suffix
    /// case then fails too, which is what makes this a test of the defect rather than of the
    /// dictionary.</para>
    /// </remarks>
    private static string? Ask(string shape, string key, string suffix, Application app) => shape switch
    {
        "bare" => ViewModelBase.TranslateText(key, app),
        "suffix" => ViewModelBase.TranslateTextWithSuffix(key, suffix, app),
        _ => ViewModelBase.Translate(key, app: app),
    };

    [AvaloniaFact]
    public void Every_key_a_translate_call_names_actually_resolves()
    {
        var app = Application.Current!;
        LoadLanguage(app);

        var asked = new List<(string Site, string Key, string? Text)>();

        foreach (var file in Sources())
        {
            var source = File.ReadAllText(file);
            var name = Path.GetFileName(file);

            foreach (Match match in BareCall.Matches(source))
            {
                var key = match.Groups["key"].Value;
                asked.Add(($"{name}: TranslateText(\"{key}\")", key, Ask("bare", key, string.Empty, app)));
            }

            foreach (Match match in SuffixCall.Matches(source))
            {
                var key = match.Groups["key"].Value;
                var suffix = match.Groups["suffix"].Value;
                asked.Add(($"{name}: TranslateTextWithSuffix(\"{key}\", \"{suffix}\")",
                    $"{key}/{suffix}", Ask("suffix", key, suffix, app)));
            }

            foreach (Match match in DirectCall.Matches(source))
            {
                var key = match.Groups["key"].Value;
                asked.Add(($"{name}: Translate(\"{key}\")", key, Ask("direct", key, string.Empty, app)));
            }
        }

        Assert.NotEmpty(asked);

        var missing = asked
            .Where(pair => pair.Text is null)
            .Select(pair => pair.Site)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"{missing.Count} translation call site(s) resolve to nothing, so the `?? \"...\"` " +
            $"beside each one is the only text anybody sees:\n  " +
            string.Join("\n  ", missing));
    }

    /// <summary>
    /// The scan above is only meaningful if it found call sites and the lookup answers. A parser
    /// that quietly matched nothing would report zero missing keys — the same green as a correct
    /// codebase.
    /// </summary>
    [AvaloniaFact]
    public void The_scan_finds_call_sites_and_the_lookup_answers()
    {
        var app = Application.Current!;
        LoadLanguage(app);

        var sites = Sources()
            .SelectMany(file => BareCall.Matches(File.ReadAllText(file)).Cast<Match>()
                .Concat(SuffixCall.Matches(File.ReadAllText(file)).Cast<Match>())
                .Concat(DirectCall.Matches(File.ReadAllText(file)).Cast<Match>()))
            .Count();

        // Hundreds, not "some": the Bench canvas alone carries about sixty. A threshold near the
        // real number is what makes a broken regex visible; `> 0` would survive a regex that matched
        // one call site and skipped the rest.
        Assert.True(sites > 60, $"expected the scan to find more than 60 call sites, found {sites}");

        // One key of each shape, so a lookup answering nothing shows up here rather than as a green
        // scan that found no offenders.
        Assert.Equal("行号至少为 1。", ViewModelBase.TranslateText("Bench_Surface_RowOutOfRange", app));
        Assert.Equal("已保存", ViewModelBase.TranslateTextWithSuffix("Bench", "Saved", app));
        Assert.NotNull(ViewModelBase.Translate("Text_MainWindow_Title", app: app));

        // And a key known to exist nowhere, so the lookup cannot be answering true blindly.
        Assert.Null(ViewModelBase.TranslateText("Bench_Surface_RowOutOfRange_", app));
    }

    /// <summary>
    /// The shape of the defect itself, pinned: an empty suffix must not leave a separator behind.
    /// </summary>
    /// <remarks>
    /// The scan above would catch a recurrence on today's keys. This one catches it on a key nobody
    /// has written yet, and it names the mechanism rather than one of its symptoms — which is the
    /// difference between "this key is missing" and "the builder is wrong".
    /// </remarks>
    [AvaloniaFact]
    public void An_empty_segment_does_not_leave_a_separator_behind()
    {
        var app = Application.Current!;
        LoadLanguage(app);

        // Both routes to the same resource text. Measured before the fix: the bare one answered
        // null while the suffixed one worked, which is what made the defect look like a missing
        // key rather than a broken builder.
        Assert.Equal(
            ViewModelBase.TranslateText("Bench_Surface_RowOutOfRange", app),
            ViewModelBase.TranslateTextWithSuffix("Bench_Surface", "RowOutOfRange", app));

        Assert.Equal("行号至少为 1。", ViewModelBase.TranslateText("Bench_Surface_RowOutOfRange", app));
    }
}
