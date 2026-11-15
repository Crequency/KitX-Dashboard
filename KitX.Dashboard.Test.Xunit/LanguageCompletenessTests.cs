using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// The one thing that can still be said about a language file without anyone reading it: whether it
/// still admits, in the file itself, that part of it has not been translated yet.
/// </summary>
/// <remarks>
/// <para><b>What would go wrong without this file.</b> Nothing in the build reads the words a
/// language file contains. A sentence left in English with a <c>&lt;!-- TODO translate --&gt;</c>
/// beside it compiles, resolves, renders — and reads as English to a reader who chose French. The
/// count of unfinished sentences was, until now, a number somebody wrote down in a task and nobody
/// could re-derive: the marker is a comment, so no test, no compiler and no XAML loader ever looks
/// at it. This file looks at it.</para>
///
/// <para><b>Why the files are read from disk.</b> Same reason as
/// <see cref="SurfaceLeaveLocalizationTests"/>: <c>Languages/*.axaml</c> are <c>Content</c>, copied
/// next to the executable at run time, not <c>AvaloniaResource</c> embedded in it, so a test that
/// asked the assembly would be asking a copy that may lag behind the one in the repository. Reading
/// the repository path is what makes the check a check on what is committed.</para>
///
/// <para><b>What this does not claim.</b> A file with no marker is not a finished translation: a
/// sentence can be left in English without the marker, and several were. This test says nothing
/// about those — it only closes the hole where a sentence was known to be unfinished and nothing
/// could fail because of it.</para>
/// </remarks>
public class LanguageCompletenessTests
{
    private const string Dashboard = @"KitX Clients\KitX Dashboard\KitX Dashboard";

    private static readonly string[] Languages =
        ["en-us", "fr-fr", "ja-jp", "ko-kr", "ru-ru", "zh-cn", "zh-tw"];

    private const string Marker = "TODO";

    [Fact]
    public void No_language_file_says_a_sentence_is_still_waiting_to_be_translated()
    {
        var unfinished = new List<string>();

        foreach (var language in Languages)
        {
            var path = RepositoryFile(Path.Combine(Dashboard, "Languages", language + ".axaml"));

            Assert.True(File.Exists(path), $"Expected a language file at {path}.");

            var lines = File.ReadAllLines(path);

            for (var index = 0; index < lines.Length; index++)
            {
                if (!lines[index].Contains(Marker, StringComparison.Ordinal))
                    continue;

                // The line number and the key, because a marker with neither names nothing to go
                // and translate: 528 keys, and the reader of this failure has to find the one.
                unfinished.Add($"{language}.axaml:{index + 1}  {KeyOf(lines[index])}");
            }
        }

        Assert.True(unfinished.Count == 0,
            "Language entries still marked as waiting to be translated:" +
            Environment.NewLine + string.Join(Environment.NewLine, unfinished));
    }

    /// <summary>The key a language entry defines, so a failure names the entry and not the line.</summary>
    private static string KeyOf(string line)
    {
        const string opening = "x:Key=\"";

        var start = line.IndexOf(opening, StringComparison.Ordinal);

        if (start < 0)
            return line.Trim();

        start += opening.Length;

        var end = line.IndexOf('"', start);

        return end < 0 ? line.Trim() : line[start..end];
    }

    private static string RepositoryFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "KitX.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, relativePath);
    }
}
