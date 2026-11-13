using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// Every <c>DynamicResource</c> key a window names has to resolve, because an unresolved one is
/// silently <see langword="null"/> rather than an error. The card orchestrator shipped with four
/// theme keys that no theme in this repository defines, so its canvas and its element cards drew
/// with no background and no border — invisible, and nothing reported it.
/// </summary>
/// <remarks>
/// <para><b>Two sources of keys, and the test has to know both.</b> A window names theme keys
/// (<c>LayerFillColorDefaultBrush</c>) and translation keys (<c>Text_Bench_Surface_Save</c>). The
/// translations are not in the application's resources during a test: <c>LanguageLoader</c> loads
/// one <c>Languages/{code}.axaml</c> from disk at startup. So the language dictionaries are read
/// from source here and their keys counted as defined; anything else must come from the theme.</para>
///
/// <para><b>Why scan the file instead of asserting on the control.</b> Asserting that the canvas
/// Border has a background checks the one key somebody remembered to fix. Scanning checks every
/// key the file names, including ones added tomorrow — which is the shape of the defect: not "this
/// brush is wrong" but "this key does not exist".</para>
///
/// <para><b>Why it must run under <see cref="FluentAvalonia.Styling.FluentAvaloniaTheme"/>.</b>
/// FluentAvaloniaTheme and Avalonia's FluentTheme define disjoint key sets: the product's keys
/// resolve to null under FluentTheme, and FluentTheme's keys resolve to null under the product
/// theme. A test app on the wrong theme therefore cannot see this bug, and would also happily
/// bless a key the product does not have. See <c>TestApp.Initialize</c>.</para>
/// </remarks>
public class ThemeResourceKeyTests
{
    private static readonly Regex DynamicResource =
        new(@"\{DynamicResource\s+(?<key>[A-Za-z0-9_.]+)\s*\}", RegexOptions.Compiled);

    private static readonly Regex DefinedKey =
        new(@"x:Key=""(?<key>[A-Za-z0-9_.]+)""", RegexOptions.Compiled);

    private const string Dashboard = @"KitX Clients\KitX Dashboard\KitX Dashboard";

    /// <summary>Walks up from the test binary until it finds the solution, then reads the file.</summary>
    private static string RepositoryFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "KitX.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);

        var path = Path.Combine(dir!.FullName, relativePath);
        Assert.True(File.Exists(path), $"Expected a file at {path}.");
        return path;
    }

    /// <summary>Every key the shipped language dictionaries define, across all languages.</summary>
    private static HashSet<string> TranslationKeys()
    {
        var directory = Path.GetDirectoryName(RepositoryFile(Path.Combine(Dashboard, "Languages", "en-us.axaml")));
        Assert.NotNull(directory);

        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(directory!, "*.axaml"))
            foreach (Match match in DefinedKey.Matches(File.ReadAllText(file)))
                keys.Add(match.Groups["key"].Value);

        return keys;
    }

    public static TheoryData<string> Windows() =>
    [
        Path.Combine(Dashboard, @"Views\SurfaceEditorWindow.axaml"),
    ];

    [AvaloniaTheory]
    [MemberData(nameof(Windows))]
    public void Every_dynamic_resource_key_a_window_names_resolves(string relativePath)
    {
        var source = File.ReadAllText(RepositoryFile(relativePath));

        var keys = DynamicResource.Matches(source)
            .Select(m => m.Groups["key"].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(keys);

        var translations = TranslationKeys();

        var missing = keys
            .Where(key => !translations.Contains(key))
            .Where(key => !Application.Current!.TryFindResource(key, out _))
            .ToList();

        // The message carries every offender, so a failure is a work item rather than a hunt:
        // "which of the seventeen keys is the null one" is otherwise answered by bisecting a
        // screenshot.
        Assert.True(
            missing.Count == 0,
            $"{relativePath} names {missing.Count} resource key(s) that neither the theme nor any " +
            $"language dictionary defines: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// Guards the test above against the failure mode where it passes because it recognised
    /// nothing: if the parser ever stops finding keys, or the theme lookup starts returning
    /// nothing for everything, this fails instead of the scan going quietly green.
    /// </summary>
    [AvaloniaFact]
    public void The_scan_finds_keys_and_the_theme_answers_them()
    {
        Assert.NotEmpty(TranslationKeys());

        // Two keys that must come from the theme — one fill, one stroke — so a theme swap that
        // emptied the lookup would show up here rather than as a green scan above.
        Assert.True(Application.Current!.TryFindResource("LayerFillColorDefaultBrush", out _));
        Assert.True(Application.Current!.TryFindResource("ControlStrokeColorSecondaryBrush", out _));

        // And a key known not to exist anywhere, so the lookup cannot be answering true blindly.
        Assert.False(Application.Current!.TryFindResource("SystemControlBackgroundAltHighBrush", out _));
    }

    /// <summary>
    /// Light and dark must answer the same key with different brushes, or there is only one theme.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this is separate from the scan above.</b> The scan asks whether each key
    /// resolves. It would stay green if both theme variants resolved every key to the same brush —
    /// the keys exist, the lookup works, and there is nothing to see. Two screenshots taken that way
    /// would be identical, and "I checked dark mode" would be a claim about a picture that never
    /// changed.</para>
    ///
    /// <para><b>What it does not prove.</b> That every key differs, only that the variant switch
    /// reaches the resource lookup at all. One key changing is what distinguishes "two themes" from
    /// "one theme asked twice".</para>
    ///
    /// <para><b>If an assertion reads the same value under both variants, suspect the reading before
    /// the subject.</b> The first version of this test pushed <c>RequestedThemeVariant</c> and then
    /// read with the parameterless <c>TryFindResource</c>, which answers from the host's current
    /// variant; the property had been assigned but nothing had re-resolved, so both reads returned
    /// <c>#80ffffff</c> and the test reported "light and dark are identical". The theme was fine.
    /// A wrong reading fails in the shape of "the two are the same", which is the hardest green to
    /// notice: it agrees with a plausible diagnosis and produces no other symptom.</para>
    /// </remarks>
    [AvaloniaFact]
    public void The_two_theme_variants_do_not_answer_with_the_same_brush()
    {
        var app = Application.Current!;
        var original = app.RequestedThemeVariant;

        try
        {
            var light = BrushUnder(app, ThemeVariant.Light, "LayerFillColorDefaultBrush");
            var dark = BrushUnder(app, ThemeVariant.Dark, "LayerFillColorDefaultBrush");

            var strokeLight = BrushUnder(app, ThemeVariant.Light, "ControlStrokeColorSecondaryBrush");
            var strokeDark = BrushUnder(app, ThemeVariant.Dark, "ControlStrokeColorSecondaryBrush");

            Assert.NotNull(light);
            Assert.NotNull(dark);

            // Measured: fill reads #80ffffff on light and #4c3a3a3a on dark; the stroke reads
            // #29000000 and #18ffffff. Two keys rather than one because a single key could differ
            // for a reason that is not the variant — the pair makes "the switch reaches the
            // lookup" a fact about the theme, not about one resource.
            Assert.NotEqual(light, dark);
            Assert.NotEqual(strokeLight, strokeDark);
        }
        finally
        {
            app.RequestedThemeVariant = original;
        }
    }

    /// <summary>The colour a solid brush key resolves to under one variant, as text.</summary>
    private static string? BrushUnder(Application app, ThemeVariant variant, string key)
    {
        // The variant is passed to the lookup rather than pushed into RequestedThemeVariant first:
        // the parameterless overload answers from the host's *current* variant, which does not
        // change until something asks it to re-resolve. Pushing the property and reading straight
        // back returns the previous variant's brush — the two states would read the same.
        if (!app.TryFindResource(key, variant, out var value) || value is not ISolidColorBrush brush)
            return null;

        return brush.Color.ToString();
    }
}
