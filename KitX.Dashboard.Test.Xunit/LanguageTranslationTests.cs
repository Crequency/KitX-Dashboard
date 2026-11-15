using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// No language file repeats the English sentence — except in the few places where the same word
/// really is the same word, and those places are listed here with a reason instead of being
/// discovered by reading seven files side by side.
/// </summary>
/// <remarks>
/// <para><b>What would go wrong without this file.</b> A value left in English compiles, resolves
/// and renders; it simply reads as English to somebody who chose French. Nothing in the build looks
/// at a value, so the only way this was ever noticed was a person scrolling a 528-entry file and
/// remembering which entries looked wrong. That memory does not survive the next commit, and it did
/// not survive the last one: 58 entries in <c>fr-fr</c> were English before this round.</para>
///
/// <para><b>Why the exceptions have to be written down.</b> A handful of values are identical on
/// purpose — <c>Version</c> is spelled the same in French, <c>Id</c> is the name of a JSON field the
/// author has to type. A test that simply demanded "different from en-us" would be red on a correct
/// translation and would be silenced by editing the translation, which is the wrong direction. So
/// each exception carries the reason it exists and the value it is an exception for; when the value
/// changes the exception goes stale and this file says so rather than hiding the change.</para>
///
/// <para><b>Why the files are read from disk.</b> Same reason as
/// <see cref="LanguageCompletenessTests"/>: <c>Languages/*.axaml</c> are <c>Content</c>, copied next
/// to the executable, so a test that asked the assembly would be asking a copy that may lag the
/// repository. The point of the check is what is committed.</para>
///
/// <para><b>What this does not claim.</b> That a translation is good, or that it says the same thing
/// as the English. A file can be entirely in French and entirely wrong. It closes the case where a
/// file is not in French at all.</para>
/// </remarks>
public class LanguageTranslationTests
{
    private const string Dashboard = @"KitX Clients\KitX Dashboard\KitX Dashboard";

    private const string FallbackLanguage = "en-us";

    private static readonly string[] Languages =
        ["en-us", "fr-fr", "ja-jp", "ko-kr", "ru-ru", "zh-cn", "zh-tw"];

    /// <summary>
    /// One <c>&lt;sys:String x:Key="..."&gt;value&lt;/sys:String&gt;</c>, or the <c>x:String</c> form
    /// the <c>CurrentLanguage</c> entry uses. Non-greedy across newlines because one entry
    /// (<c>Text_ExchangeDeviceKeyWindow_Description</c>) carries an escaped line break.
    /// </summary>
    private static readonly Regex Entry = new(
        @"x:Key=""(?<key>[^""]+)""[^>]*>(?<value>[\s\S]*?)</(?:[A-Za-z]+:)?String>",
        RegexOptions.Compiled);

    private static readonly Regex Placeholder =
        new(@"\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

    /// <summary>
    /// Entries that are identical in every language, with the reason they are allowed to be. These
    /// are field names and fixed tokens: there is no word here to translate.
    /// </summary>
    private static readonly Dictionary<string, (string English, string Reason)> SameEverywhere =
        new(StringComparer.Ordinal)
        {
            ["Text_Bench_MetaMinKitX"] = ("MinKitXVersion",
                "the JSON field name the author writes in toolkit.json"),
            ["Text_Bench_MetaTags"] = ("Tags",
                "the JSON field name the author writes in toolkit.json"),
            ["Text_Bench_Id"] = ("Id",
                "the JSON field name the author writes in toolkit.json"),
            ["Text_Bench_TriggerKindUI"] = ("UI: {0}",
                "a two-letter token followed by a format argument; there is no word to translate"),
        };

    /// <summary>
    /// Entries that a particular language may leave as the English spelling because that language
    /// spells the word the same way.
    /// </summary>
    /// <remarks>
    /// French only, because French is the one language in this set that shares these words. Keeping
    /// the list per language rather than global is what stops it from silently excusing, say, a
    /// Japanese entry that happens to match one of these spellings: an entry listed for <c>fr-fr</c>
    /// is only excused in <c>fr-fr</c>.
    /// </remarks>
    private static readonly Dictionary<string, Dictionary<string, (string English, string Reason)>> SameInOneLanguage =
        new(StringComparer.Ordinal)
        {
            ["fr-fr"] = new(StringComparer.Ordinal)
            {
                ["Text_Public_Minute"] = ("minute", "the French word is spelt the same"),
                ["Text_Bench_CronMinute"] = ("min", "the French abbreviation is the same"),
                ["Text_Settings_Update_Channel_Stable"] = ("Stable", "the French word is spelt the same"),
                ["Text_Settings_Update_Channel_Alpha"] = ("Alpha", "a channel name, not a word to translate"),
                ["Text_Bench_MetaVersion"] = ("Version", "the French word is spelt the same"),
                ["Text_Bench_PluginVersionWatermark"] = ("Version", "the French word is spelt the same"),
                ["Text_Bench_MetaDescription"] = ("Description", "the French word is spelt the same"),
                ["Text_Bench_Type"] = ("Type", "the French word is spelt the same"),
                ["Text_Bench_Plugin"] = ("Plugin", "the word this file uses for a plugin throughout"),
                ["Text_Bench_PaletteGroupWorkflows"] = ("Workflows",
                    "the word this file uses for workflows throughout (Text_Bench_NewWorkflow)"),
                ["Text_Bench_NodeKindWorkflow"] = ("Workflow",
                    "the word this file uses for a workflow throughout"),
            },
        };

    [Fact]
    public void No_language_repeats_an_English_sentence_without_a_reason()
    {
        var english = Read(FallbackLanguage);

        Assert.True(english.Count > 300,
            $"expected more than 300 entries in {FallbackLanguage}.axaml, read {english.Count}");

        var offenders = new List<string>();

        foreach (var language in Languages.Where(code => code != FallbackLanguage))
        {
            var translated = Read(language);

            foreach (var (key, value) in english)
            {
                if (!translated.TryGetValue(key, out var other))
                    continue;

                if (!string.Equals(other.Trim(), value.Trim(), StringComparison.Ordinal))
                    continue;

                if (Excused(language, key))
                    continue;

                // The key, the language and the words: a failure that named only the count would
                // send the reader back to seven files to find the entry it already found.
                offenders.Add($"{language}.axaml  {key}  = {value}");
            }
        }

        Assert.True(offenders.Count == 0,
            $"{offenders.Count} entr{(offenders.Count == 1 ? "y is" : "ies are")} still the English " +
            "sentence, in a language that has a word for it:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// A translation may add a word the English sentence does not use — several legitimately name
    /// the element kind twice, in the file's word and in the reader's — but it must never drop a
    /// value the producer fills in, because the sentence then prints with a hole where the row
    /// number belongs.
    /// </summary>
    /// <remarks>
    /// Measured before writing this: no language omits a placeholder entirely, though several use
    /// one fewer often than the English does. The direction matters — <c>{kind}</c> is filled from
    /// <c>Diagnostic.Data</c> alongside <c>{kindName}</c>, so using both is safe, and requiring the
    /// same set would have been red on text that names the kind twice (114 differences, all
    /// additions).
    /// </remarks>
    [Fact]
    public void A_translation_never_drops_a_placeholder_the_English_supplies()
    {
        var english = Read(FallbackLanguage);
        var offenders = new List<string>();

        foreach (var language in Languages.Where(code => code != FallbackLanguage))
        {
            var translated = Read(language);

            foreach (var (key, value) in english)
            {
                if (!translated.TryGetValue(key, out var other))
                    continue;

                var supplied = Names(value);
                var carried = Names(other);

                // Presence, not count: a sentence can say the row once where the English says it
                // twice ("`row` is {row}, row numbers start at 1" collapses in Chinese to one
                // mention). Requiring the count would have been red on a correct translation and
                // would have taught the reader to edit the translation to satisfy the test.
                foreach (var name in supplied.Keys)
                {
                    if (carried.ContainsKey(name))
                        continue;

                    offenders.Add($"{language}.axaml  {key}  drops {{{name}}}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            $"{offenders.Count} translation(s) drop a value the sentence needs:" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// The scan above is only worth reading if it read something: seven files, the same keys in each,
    /// and a difference it can actually see.
    /// </summary>
    [Fact]
    public void The_seven_files_agree_on_their_keys_and_the_reader_reaches_every_entry()
    {
        var english = Read(FallbackLanguage);

        var reference = string.Join(",", english.Keys.OrderBy(k => k, StringComparer.Ordinal));

        foreach (var language in Languages.Where(code => code != FallbackLanguage))
        {
            var translated = Read(language);

            Assert.Equal(reference, string.Join(",", translated.Keys.OrderBy(k => k, StringComparer.Ordinal)));

            // The second entry of every file names the file, so a reader that stopped at line one
            // would fail here rather than report every value as translated.
            Assert.Equal(language, translated["CurrentLanguage"]);
        }

        // And a difference the reader can see: zh-cn translates this one, and en-us wrote it.
        Assert.Equal("KitX Dashboard", english["Text_MainWindow_Title"]);
        Assert.NotEqual("KitX Dashboard", Read("zh-cn")["Text_MainWindow_Title"]);
        Assert.NotEqual("KitX Dashboard", Read("fr-fr")["Text_MainWindow_Title"]);
    }

    /// <summary>
    /// Every excuse still excuses something, and still names the value it excuses.
    /// </summary>
    /// <remarks>
    /// An exception list rots in one direction only: the entry it excuses gets translated, and the
    /// exception stays behind as a licence for the next person to leave that entry in English. So
    /// each entry has to still match, and still be needed.
    /// </remarks>
    [Fact]
    public void Every_excused_entry_is_still_an_excused_entry()
    {
        var english = Read(FallbackLanguage);

        foreach (var (key, excuse) in SameEverywhere)
        {
            Assert.True(english.TryGetValue(key, out var value), $"{key} is excused but not defined.");
            Assert.Equal(excuse.English, value);

            // One language still needs it, not all of them: French writes "UI : {0}" with the space
            // its typography puts before a colon, so it translates this entry by punctuating it, and
            // demanding the excuse from every language would have turned a correct translation into
            // a failure.
            var stillRepeats = Languages
                .Where(code => code != FallbackLanguage)
                .Count(code => string.Equals(Read(code)[key].Trim(), excuse.English.Trim(), StringComparison.Ordinal));

            Assert.True(stillRepeats > 0,
                $"{key} is excused but every language now translates it; drop the excuse.");
        }

        foreach (var (language, excuses) in SameInOneLanguage)
            foreach (var (key, excuse) in excuses)
            {
                Assert.True(english.TryGetValue(key, out var value), $"{key} is excused but not defined.");
                Assert.Equal(excuse.English, value);

                var translated = Read(language);

                Assert.True(translated.ContainsKey(key), $"{key} is excused in {language} but not defined there.");
                Assert.True(string.Equals(translated[key].Trim(), excuse.English.Trim(), StringComparison.Ordinal),
                    $"{key} is excused in {language} but {language} has translated it; drop the excuse.");
            }
    }

    /// <summary>Whether this language may leave <paramref name="key"/> as the English spelling.</summary>
    private static bool Excused(string language, string key)
        => SameEverywhere.ContainsKey(key)
            || (SameInOneLanguage.TryGetValue(language, out var excuses) && excuses.ContainsKey(key));

    /// <summary>Every named placeholder in <paramref name="text"/>, with how often it appears.</summary>
    private static Dictionary<string, int> Names(string text)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (Match match in Placeholder.Matches(text))
        {
            var name = match.Groups["name"].Value;
            counts[name] = counts.TryGetValue(name, out var count) ? count + 1 : 1;
        }

        return counts;
    }

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
