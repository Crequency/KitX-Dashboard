using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// A value is written in a script its language writes, and in no script that language does not.
/// </summary>
/// <remarks>
/// <para><b>What would go wrong without this file.</b> A translation pasted into the wrong column
/// compiles, resolves, renders — and reads as a foreign language to whoever chose that file. This
/// happened: <c>Text_WorkflowEditor_ConnectionDisconnectTip</c> shipped with five of its seven
/// values rotated one language along (fr-fr held Chinese, ja-jp held French, ko-kr held Japanese,
/// ru-ru held Korean, zh-cn held Russian). Every other check in this project passed on it:</para>
/// <list type="bullet">
/// <item>the value is not equal to the English one, so <see cref="LanguageTranslationTests"/> was green;</item>
/// <item>the key is defined in all seven files and something builds it, so the usage checks were green;</item>
/// <item>the placeholders match, because a sentence moves language without losing its <c>{row}</c>.</item>
/// </list>
/// <para>Nothing in the toolchain looks at what script the letters are made of, and the person who
/// reviewed it saw seven rows of a table that all looked like text.</para>
///
/// <para><b>Why two rules and not one.</b> They fail in opposite directions and neither alone is
/// enough. A value rotated from a language that shares this file's script (French into English, or
/// Russian into another Cyrillic language) is invisible to the "no foreign script" rule; a value
/// that is simply empty of words — a code, an identifier — is invisible to the "own script" rule.
/// The rotation that shipped was caught by the pair, not by either: three of the five were wrong
/// because they contained a script that file never writes, and two because they contained nothing
/// of their own.</para>
///
/// <para><b>Where the two rules deliberately stop.</b> "Own script" is applied to prose only, and
/// "no foreign script" only to scripts that are unambiguous: no English, French or Russian sentence
/// contains a Han character, no text outside Japanese contains kana, no text outside Korean contains
/// Hangul, no text outside Russian contains Cyrillic. Korean may legitimately carry Hanja and
/// Japanese may legitimately carry Han characters, so neither is asserted against. Asserting a rule
/// that a correct translation can violate would make the test red on good input, and the way out of
/// a red test is to edit the translation — the wrong direction, and the one that teaches the next
/// person to satisfy the check rather than the language.</para>
///
/// <para><b>Why the exceptions are written down with the value they excuse.</b> Same reason as
/// <see cref="LanguageTranslationTests"/>: an exception that outlives the thing it excused becomes a
/// licence. <see cref="Every_script_exception_is_still_needed"/> asks each entry to still match the
/// value it names, so a translation that fixes the entry takes the exception down with it.</para>
///
/// <para><b>What this does not claim.</b> That a translation says the right thing, or that it is in
/// the right language — only that it is written in an alphabet that language uses. French text
/// inside the French file can still be wrong in every other way.</para>
/// </remarks>
public class LanguageScriptTests
{
    private const string Dashboard = @"KitX Clients\KitX Dashboard\KitX Dashboard";

    private static readonly string[] Languages =
        ["en-us", "fr-fr", "ja-jp", "ko-kr", "ru-ru", "zh-cn", "zh-tw"];

    /// <summary>One entry. Same reader as <see cref="LanguageTranslationTests"/>, same reason.</summary>
    private static readonly Regex Entry = new(
        @"x:Key=""(?<key>[^""]+)""[^>]*>(?<value>[\s\S]*?)</(?:[A-Za-z]+:)?String>",
        RegexOptions.Compiled);

    /// <summary>Han characters. Written by Chinese, Japanese and (as Hanja) Korean.</summary>
    private static readonly Regex Han = new(@"[\u3400-\u4DBF\u4E00-\u9FFF\uF900-\uFAFF]", RegexOptions.Compiled);

    /// <summary>Hiragana, katakana, and the half-width katakana block. Written by Japanese only.</summary>
    private static readonly Regex Kana = new(@"[\u3040-\u309F\u30A0-\u30FF\uFF66-\uFF9D]", RegexOptions.Compiled);

    /// <summary>Hangul syllables, jamo and compatibility jamo. Written by Korean only.</summary>
    private static readonly Regex Hangul = new(@"[\uAC00-\uD7AF\u1100-\u11FF\u3130-\u318F]", RegexOptions.Compiled);

    /// <summary>Cyrillic. Written by Russian, and by no other language in this set.</summary>
    private static readonly Regex Cyrillic = new(@"[\u0400-\u04FF]", RegexOptions.Compiled);

    /// <summary>Latin letters, with the accents French needs.</summary>
    private static readonly Regex Latin = new(@"[A-Za-z\u00C0-\u024F]", RegexOptions.Compiled);

    /// <summary>
    /// The characters a language writes in. Latin is not listed as anybody's own: it is the carrier
    /// every file uses for identifiers, product names and keyboard hints, so its presence says
    /// nothing about which language a sentence is in.
    /// </summary>
    private static readonly Dictionary<string, Regex[]> WrittenIn = new(StringComparer.Ordinal)
    {
        ["ja-jp"] = [Kana, Han],
        ["ko-kr"] = [Hangul],
        ["ru-ru"] = [Cyrillic],
        ["zh-cn"] = [Han],
        ["zh-tw"] = [Han],
    };

    /// <summary>
    /// Scripts that a language never writes, whatever the sentence says. Korean and Japanese are
    /// absent from this table on purpose: see the remarks on the class.
    /// </summary>
    private static readonly Dictionary<string, (string Script, Regex Pattern)[]> NeverWrites =
        new(StringComparer.Ordinal)
        {
            ["en-us"] = [("Han", Han), ("kana", Kana), ("Hangul", Hangul), ("Cyrillic", Cyrillic)],
            ["fr-fr"] = [("Han", Han), ("kana", Kana), ("Hangul", Hangul), ("Cyrillic", Cyrillic)],
            ["ja-jp"] = [("Hangul", Hangul), ("Cyrillic", Cyrillic)],
            ["ko-kr"] = [("kana", Kana), ("Cyrillic", Cyrillic)],
            ["ru-ru"] = [("Han", Han), ("kana", Kana), ("Hangul", Hangul)],
            ["zh-cn"] = [("kana", Kana), ("Hangul", Hangul), ("Cyrillic", Cyrillic)],
            ["zh-tw"] = [("kana", Kana), ("Hangul", Hangul), ("Cyrillic", Cyrillic)],
        };

    /// <summary>
    /// Entries whose value is not a sentence in any language: a language code, or the name of a
    /// JSON field the author has to type. The value, not the key, is what the rule cannot help with.
    /// </summary>
    private static readonly Dictionary<string, (string Value, string Reason)> NotProse =
        new(StringComparer.Ordinal)
        {
            ["CurrentLanguage"] = ("<the language code>",
                "the value is the file's own code — ja-jp holds 'ja-jp', which is the point of the entry"),
            ["Text_Bench_MetaMinKitX"] = ("MinKitXVersion",
                "the JSON field name the author writes in toolkit.json"),
            ["Text_Bench_MetaTags"] = ("Tags",
                "the JSON field name the author writes in toolkit.json"),
        };

    /// <summary>
    /// Entries that carry a face rather than a word. The kaomoji are the same characters in all
    /// seven files — that is what makes it the same face — so every script rule reads them as
    /// contamination.
    /// </summary>
    private static readonly Dictionary<string, (string Reason, string Marker)> Kaomoji =
        new(StringComparer.Ordinal)
        {
            ["Text_Greeting_AfterNoon_1"] = ("the kaomoji ヾ(≧▽≦*)o is the same face in every file", "ヾ"),
            ["Text_Greeting_Evening_1"] = ("the kaomoji o(*￣▽￣*)ブ is the same face in every file", "ブ"),
            ["Text_Greeting_Night_1"] = ("the kaomoji o(*￣▽￣*)ブ is the same face in every file", "ブ"),
        };

    /// <summary>
    /// A sentence in this language contains at least one character of the script that language is
    /// written in.
    /// </summary>
    /// <remarks>
    /// Prose only. A value with fewer than four letters is an abbreviation or an identifier and says
    /// nothing either way: <c>Id</c>, <c>OK</c>, <c>X</c> are the same in every file and correct in
    /// every file. Four was measured, not chosen: every value below it on the day this was written
    /// was one of those, and every value above it that failed was a real rotation or a real stray.
    /// </remarks>
    [Fact]
    public void A_value_is_written_in_a_script_its_language_is_written_in()
    {
        var read = Languages.ToDictionary(code => code, Read, StringComparer.Ordinal);

        // The reader has to reach every entry before its silence means anything.
        Assert.All(Languages, code => Assert.True(read[code].Count > 400,
            $"{code}.axaml has {read[code].Count} entries this reader can see; the file has more."));

        var offenders = new List<string>();

        foreach (var language in Languages)
        {
            if (!WrittenIn.TryGetValue(language, out var scripts))
                continue;

            foreach (var (key, value) in read[language])
            {
                var text = value.Trim();

                if (!IsProse(text) || NotProse.ContainsKey(key) || Kaomoji.ContainsKey(key))
                    continue;

                if (scripts.Any(script => script.IsMatch(text)))
                    continue;

                offenders.Add($"{language}.axaml  {key}  = {Show(text)}  " +
                    $"(no {Describe(language)} in it)");
            }
        }

        Assert.True(offenders.Count == 0,
            $"{offenders.Count} entr{(offenders.Count == 1 ? "y carries" : "ies carry")} " +
            "nothing of the language it is filed under:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// No value carries a script its language never writes.
    /// </summary>
    /// <remarks>
    /// Measured over all seven files on the day this was written: zero violations of the four hard
    /// scripts. The one set of characters that trips every rule is the kaomoji in
    /// <see cref="Text_Greeting_Night_1"/> and its neighbours, which are excused by name with the
    /// marker they are excused for.
    /// </remarks>
    [Fact]
    public void A_value_carries_no_script_its_language_never_writes()
    {
        var offenders = new List<string>();

        foreach (var language in Languages)
        {
            var read = Read(language);

            foreach (var (key, value) in read)
            {
                var text = value.Trim();

                if (text.Length == 0 || Kaomoji.ContainsKey(key))
                    continue;

                foreach (var (script, pattern) in NeverWrites[language])
                {
                    var hit = pattern.Match(text);

                    if (!hit.Success)
                        continue;

                    // A stray character is the tell: a rotated sentence is the whole value, and a
                    // single 数 in the middle of a Korean word is somebody editing the wrong file.
                    // Both are wrong, so both are reported, with the characters shown.
                    offenders.Add($"{language}.axaml  {key}  = {Show(text)}  (holds {script}: {hit.Value})");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            $"{offenders.Count} value(s) carry a script their file does not write:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>Every exception still names a value that is still there, and is still needed.</summary>
    [Fact]
    public void Every_script_exception_is_still_needed()
    {
        foreach (var (key, excuse) in NotProse)
        {
            var stillFailing = Languages
                .Where(code => WrittenIn.ContainsKey(code))
                .Count(code => Read(code).TryGetValue(key, out var value)
                    && IsProse(value)
                    && !WrittenIn[code].Any(script => script.IsMatch(value)));

            Assert.True(stillFailing > 0,
                $"{key} is excused from the script rule ('{excuse.Reason}') but no language needs the excuse.");
        }

        foreach (var (key, excuse) in Kaomoji)
        {
            var holders = Languages
                .Where(code => Read(code).TryGetValue(key, out var value) && value.Contains(excuse.Marker, StringComparison.Ordinal))
                .ToList();

            Assert.True(holders.Count > 0,
                $"{key} is excused for its kaomoji ('{excuse.Reason}') but no file still carries '{excuse.Marker}'.");

            // The marker is what makes the excuse specific: if the greeting is rewritten without a
            // face, the excuse has nothing left to excuse and would otherwise sit there forever.
            Assert.True(holders.Count == Languages.Length,
                $"{key} carries '{excuse.Marker}' in {holders.Count} of {Languages.Length} files; " +
                "the kaomoji is supposed to be the same face in every one of them.");
        }
    }

    /// <summary>
    /// Whether a value is long enough to be a sentence, and so to be held to a script.
    /// </summary>
    private static bool IsProse(string text)
        => (Latin.Matches(text).Count + Han.Matches(text).Count + Kana.Matches(text).Count
            + Hangul.Matches(text).Count + Cyrillic.Matches(text).Count) >= 4;

    /// <summary>How to say which script a language is written in, in a failure message.</summary>
    private static string Describe(string language) => language switch
    {
        "ja-jp" => "kana or Han character",
        "ko-kr" => "Hangul syllable",
        "ru-ru" => "Cyrillic letter",
        "zh-cn" or "zh-tw" => "Han character",
        _ => "letter",
    };

    /// <summary>A value a terminal can show, cut so one bad line does not bury the rest.</summary>
    private static string Show(string text)
        => "\"" + (text.Length <= 60 ? text : text[..57] + "...") + "\"";

    /// <summary>One language file, as key to value.</summary>
    private static Dictionary<string, string> Read(string language)
    {
        var path = RepositoryFile(Path.Combine(Dashboard, "Languages", language + ".axaml"));

        Assert.True(File.Exists(path), $"Expected a language file at {path}.");

        var entries = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match match in Entry.Matches(File.ReadAllText(path)))
            entries[match.Groups["key"].Value] = match.Groups["value"].Value;

        Assert.True(entries.Count > 0, $"{language}.axaml carries no entries this reader can see.");

        return entries;
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
