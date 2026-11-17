using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// Every class a button asks for must be a class somebody defined, in a scope that reaches it.
/// </summary>
/// <remarks>
/// <para><b>Why this is a test and not a review habit.</b> A misspelt class, a deleted one and one
/// defined in another window's styles are three different mistakes with one identical result: the
/// button renders with its default template and nobody finds out until somebody clicks the wrong
/// thing. The Surface editor shipped a remove button with <c>Classes="flat"</c> — and no
/// <c>Button.flat</c> has ever existed anywhere in this repository or in either Fluent theme package,
/// so the button took Fluent's default 48×34 and covered the label of every cell. Nothing in the test
/// suite could see it, because nothing in the test suite read a style selector.</para>
///
/// <para><b>Scope is the part that is easy to get wrong.</b> A <c>Window.Styles</c> entry applies to
/// that window and no other; <c>Application.Styles</c> applies to all of them. The editor's palette
/// buttons asked for <c>palette</c>, which was defined in BenchWindow's own styles — so the same word
/// meant "styled" in one window and "unstyled" in the other. This test therefore accepts a definition
/// only from the file that uses it or from <c>App.axaml</c>, never from a sibling window.</para>
///
/// <para><b>What it cannot see.</b> Whether the style looks right, whether a setter has the effect it
/// claims, and whether a class defined in App.axaml is used anywhere. It answers one question: does a
/// definition exist, and can it reach this file.</para>
/// </remarks>
public class SurfaceButtonStyleTests
{
    /// <summary>A button that asks for one or more classes.</summary>
    private static readonly Regex ButtonWithClasses = new(
        @"<Button\b(?<attrs>[\s\S]*?)/?>",
        RegexOptions.Compiled);

    private static readonly Regex ClassesAttribute = new(
        @"\bClasses=""(?<classes>[^""]*)""",
        RegexOptions.Compiled);

    /// <summary>A style selector for a button class, e.g. <c>Button.palette</c>.</summary>
    private static readonly Regex ButtonSelector = new(
        @"<Style\b[^>]*\bSelector=""Button\.(?<name>[A-Za-z_][A-Za-z0-9_]*)(?<rest>[^""]*)""",
        RegexOptions.Compiled);

    /// <summary>A style file this application includes by relative path.</summary>
    private static readonly Regex LocalStyleInclude = new(
        @"<StyleInclude\b[^>]*\bSource=""/(?<path>[^""]+)""",
        RegexOptions.Compiled);

    /// <summary>The styles a view declares for itself, as opposed to everything in the application.</summary>
    private static readonly Regex StylesBlock = new(
        @"<(?<owner>Window|UserControl)\.Styles>(?<body>[\s\S]*?)</\k<owner>\.Styles>",
        RegexOptions.Compiled);

    [Fact]
    public void Every_class_a_button_asks_for_is_defined_where_that_button_can_see_it()
    {
        var views = RepositoryDirectory(@"KitX Clients\KitX Dashboard\KitX Dashboard");
        var appStyles = Path.Combine(views, "App.axaml");

        // What every window can reach: the application's own styles plus the files it includes.
        // A missing App.axaml is itself a failure worth naming, not something to skip past.
        Assert.True(File.Exists(appStyles), $"App.axaml was not found at {appStyles}");

        var application = Read(appStyles);
        var applicationClasses = new HashSet<string>(
            ClassNamesIn(application.Text), StringComparer.Ordinal);

        foreach (Match include in LocalStyleInclude.Matches(application.Text))
        {
            var included = Path.Combine(views, include.Groups["path"].Value.Replace('/', Path.DirectorySeparatorChar));

            // An include that does not resolve is invisible to this test and would silently shrink
            // what counts as "defined". Say so rather than continue with a smaller set.
            Assert.True(File.Exists(included), $"{appStyles} includes '{include.Groups["path"].Value}', which does not exist at {included}");

            applicationClasses.UnionWith(ClassNamesIn(Read(included).Text));
        }

        var lines = new List<string>();

        // Every axaml under the project except the built copies in bin/ and obj/.
        foreach (var path in Directory.EnumerateFiles(views, "*.axaml", SearchOption.AllDirectories)
                     .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                 && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            var file = Read(path);

            // A view can define its own classes; anything else it must inherit. Its own block is
            // read separately from the application's precisely because the scope differs.
            var visible = new HashSet<string>(applicationClasses, StringComparer.Ordinal);
            visible.UnionWith(ClassNamesIn(
                string.Join("\n", StylesBlock.Matches(file.Text).Select(m => m.Groups["body"].Value))));

            foreach (Match button in ButtonWithClasses.Matches(file.Text))
            {
                var attributes = button.Groups["attrs"].Value;
                var classes = ClassesAttribute.Match(attributes);

                if (!classes.Success)
                    continue;

                var line = LineOf(file.Text, button.Index);

                foreach (var name in classes.Groups["classes"].Value
                             .Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    // A dynamic class (`Classes.selected="{Binding ...}"`) is written as a separate
                    // attribute and is not in this string; anything here is a literal the author typed.
                    if (visible.Contains(name))
                        continue;

                    lines.Add(
                        $"{path.Replace(views + Path.DirectorySeparatorChar, string.Empty)}:{line} asks for " +
                        $"Classes=\"{name}\", and no Button.{name} selector is defined in that file's own " +
                        $"styles or in App.axaml. A class with no definition renders as the toolkit's " +
                        $"default button — which is how a 48x34 Fluent default came to cover a cell's label.");
                }
            }
        }

        Assert.True(
            lines.Count == 0,
            "Buttons ask for classes that nothing defines:" + Environment.NewLine + string.Join(Environment.NewLine, lines));
    }

    [Fact]
    public void The_editor_and_the_bench_draw_their_palette_the_same_way()
    {
        // Both windows write Classes="palette". It used to be defined in BenchWindow's own styles, so
        // the word meant "styled" in one window and "unstyled" in the other — the same defect the
        // class-name scan above catches, asserted here by name because this pair is what shipped it.
        var views = RepositoryDirectory(@"KitX Clients\KitX Dashboard\KitX Dashboard");
        var application = Read(Path.Combine(views, "App.axaml"));

        Assert.Contains("palette", ClassNamesIn(application.Text), StringComparer.Ordinal);

        foreach (var window in new[] { "BenchWindow.axaml", "SurfaceEditorWindow.axaml" })
        {
            var file = Read(Path.Combine(views, "Views", window));

            // Still used — otherwise this test would keep passing after the last palette button left.
            Assert.Contains(
                file.Text.Split('\n'),
                line => line.Contains("Classes=\"palette\"", StringComparison.Ordinal));

            // And not redefined locally, because a local copy would shadow the application's and the
            // two would drift.
            Assert.DoesNotContain(
                "Button.palette",
                string.Join("\n", StylesBlock.Matches(file.Text).Select(m => m.Groups["body"].Value)),
                StringComparison.Ordinal);
        }
    }

    private static IEnumerable<string> ClassNamesIn(string source) =>
        ButtonSelector.Matches(source).Select(m => m.Groups["name"].Value);

    private sealed record Source(string Text, int Lines);

    private static Source Read(string path)
    {
        var text = File.ReadAllText(path);

        return new Source(text, text.Count(c => c == '\n') + 1);
    }

    private static int LineOf(string text, int index)
    {
        var line = 1;

        for (var i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
                line++;
        }

        return line;
    }

    /// <summary>Walks up from the test binary until it finds the solution file.</summary>
    private static string RepositoryDirectory(string relativePath)
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
}
