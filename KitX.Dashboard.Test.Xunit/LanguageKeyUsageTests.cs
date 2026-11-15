using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// Every key a language file defines has to be built by something in the product — a XAML binding, a
/// Translate* call, a prefix helper, a template, or a code that a formatter turns into a key. A key
/// nothing builds is a sentence nobody will ever read, kept in seven files and translated by nobody
/// after the first day.
/// </summary>
/// <remarks>
/// <para><b>What would go wrong without this file.</b> Nothing. A dead key costs no runtime, throws
/// nothing, and looks exactly like a live one. That is why they accumulate: the panel host was
/// archived and 46 of its keys stayed; the previous workflow editor was replaced and 51 more stayed.
/// Measured before this round: 178 of 528 keys were built nowhere. The count was only knowable by
/// running a scan by hand, and the hand-written scan had been wrong twice.</para>
///
/// <para><b>Why the scan is a test and not a script.</b> Both earlier scans answered "dead" for keys
/// that were live, both times for the same reason: they looked for the key as a string and did not
/// know that TranslateTextWithSuffix("Bench", "Saved") builds Text_Bench_Saved without that string
/// existing anywhere. A script nobody runs keeps that mistake; a test does not, because the mistake
/// makes it red on a key the product displays.</para>
///
/// <para><b>The five shapes, and why each is here.</b> A key is built as (1) a whole literal in XAML
/// (DynamicResource Text_X, ConverterParameter=Text_X); (2) a whole literal in C#; (3)
/// TranslateText("X"), which prefixes Text_; (4) a prefix helper — a method taking string key whose
/// body is Translate(key, prefix: "P") or Const + key — called with a literal;
/// or (5) a pattern with a hole in it: the greeting template Text_Greeting_%Step%_%Index%, the kind
/// names SurfaceElementKind.NameKeyOf, and SurfaceDiagnosticText.KeyFor turning
/// surface.element.no-key into Text_Bench_Diag_element_no_key.</para>
///
/// <para><b>What the scan deliberately cannot see.</b> Two keys are built from a library constant
/// rather than from text: GetThemeDisplayText(FluentAvaloniaTheme.LightModeString) asks for
/// Text_Settings_Personalise_Theme_Light because that constant's value is "Light". No amount of
/// reading this repository shows that, so they are listed by hand in BuiltFromALibraryConstant, each
/// with the call site it depends on — and
/// Every_key_built_from_a_library_constant_is_still_built fails if that call site is edited away, so
/// the list cannot outlive what it excuses.</para>
///
/// <para><b>Comments are stripped first.</b> The localisation converter's documentation mentions
/// Text_PanelHost_RunningCount as an example. A scan that counted that as a use would call a deleted
/// key live because a comment about the old panel host still names it — which is exactly how that key
/// was reported as used during the survey.</para>
/// </remarks>
public class LanguageKeyUsageTests
{
    private const string Dashboard = @"KitX Clients\KitX Dashboard\KitX Dashboard";

    /// <summary>
    /// Where the diagnostics codes are written. The dashboard never spells a diagnostic key out: it
    /// receives a code and mangles it, so the codes have to be found in the assembly that produces
    /// them.
    /// </summary>
    private const string Workflow = @"KitX Clients\KitX Workflow";

    private const string FallbackLanguage = "en-us";

    /// <summary>The keys a library constant builds, and the call site that builds them.</summary>
    private static readonly (string Key, string CallSite)[] BuiltFromALibraryConstant =
    [
        ("Text_Settings_Personalise_Theme_Light", "GetThemeDisplayText(FluentAvaloniaTheme.LightModeString)"),
        ("Text_Settings_Personalise_Theme_Dark", "GetThemeDisplayText(FluentAvaloniaTheme.DarkModeString)"),
    ];

    /// <summary>A whole key, as a token and not as part of a longer key.</summary>
    private static Regex WholeKey(string key) =>
        new(@"(?<![A-Za-z0-9_])" + Regex.Escape(key) + @"(?![A-Za-z0-9_])", RegexOptions.Compiled);

    /// <summary>TranslateText("a") builds Text_a.</summary>
    private static readonly Regex BareCall =
        new(@"\bTranslateText\(\s*""(?<key>[^""]+)""", RegexOptions.Compiled);

    /// <summary>TranslateTextWithSuffix("a", "b") builds Text_a_b.</summary>
    private static readonly Regex SuffixCall =
        new(@"\bTranslateTextWithSuffix\(\s*""(?<key>[^""]+)""\s*,\s*""(?<suffix>[^""]+)""",
            RegexOptions.Compiled);

    /// <summary>A string constant, so a helper's prefix can be read from its declaration.</summary>
    private static readonly Regex StringConstant =
        new(@"\bconst\s+string\s+(?<name>\w+)\s*=\s*""(?<value>[^""]*)""", RegexOptions.Compiled);

    /// <summary>A method that turns a bare name into a key: static string F(string key, ...).</summary>
    private static readonly Regex KeyHelperHead =
        new(@"(?<access>private|internal|public|protected)\s+static\s+string\s+(?<name>\w+)\s*\(\s*string\s+key\b",
            RegexOptions.Compiled);

    /// <summary>A key template with a hole in it, e.g. Text_Greeting_%Step%_%Index%.</summary>
    private static readonly Regex ConstantTemplate =
        new(@"""(?<template>Text_[A-Za-z0-9_]*(?:%[A-Za-z]+%[A-Za-z0-9_]*)+)""", RegexOptions.Compiled);

    /// <summary>A diagnostics code the product can emit, e.g. surface.element.no-key.</summary>
    private static readonly Regex DiagnosticCode =
        new(@"""(?<code>(?:surface|toolkit|workflow)\.[A-Za-z0-9.\-]+)""", RegexOptions.Compiled);

    [Fact]
    public void Every_key_a_language_file_defines_is_built_by_the_product()
    {
        var defined = DefinedKeys();
        var built = BuiltKeys();

        Assert.True(defined.Count > 300,
            "expected more than 300 keys in " + FallbackLanguage + ".axaml, read " + defined.Count);

        // The scan is only worth trusting if it recognised a real share of the file: a broken regex
        // reports every key dead, which reads as "the file is entirely unused" rather than as "the
        // parser is broken".
        Assert.True(built.Count > 250,
            "expected the scan to build more than 250 keys, it built " + built.Count);

        var dead = defined
            .Where(key => !built.ContainsKey(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();

        Assert.True(dead.Count == 0,
            dead.Count + " language key(s) are built nowhere in the product, so nothing will ever " +
            "display them:" + Environment.NewLine +
            string.Join(Environment.NewLine, dead.Select(key => "  " + key)));
    }

    /// <summary>
    /// The two keys a library constant builds are still built, and the call site that builds them is
    /// still there.
    /// </summary>
    /// <remarks>
    /// Without this, the hand-written list above would be a permanent licence: the theme picker could
    /// lose an entry and the exception would keep a key alive that no longer exists.
    /// </remarks>
    [Fact]
    public void Every_key_built_from_a_library_constant_is_still_built()
    {
        var defined = DefinedKeys();
        var sources = SourceFiles()
            .Select(file => StripComments(File.ReadAllText(file)))
            .ToList();

        foreach (var (key, callSite) in BuiltFromALibraryConstant)
        {
            Assert.Contains(key, defined);

            Assert.True(sources.Any(source => source.Contains(callSite, StringComparison.Ordinal)),
                "the list says " + key + " is built by " + callSite +
                ", and no file contains that call any more");
        }
    }

    /// <summary>
    /// The scan answers the shapes it claims to answer. One key per shape, so a parser that quietly
    /// stopped recognising a shape fails here instead of blessing that shape's keys as dead.
    /// </summary>
    [Fact]
    public void The_scan_recognises_every_shape_it_claims_to()
    {
        var built = BuiltKeys();

        // A XAML binding.
        Assert.True(built.ContainsKey("Text_Bench_Surface_Title"), "the XAML shape recognised nothing");

        // TranslateText, and its suffix form: neither key is written whole anywhere.
        Assert.True(built.ContainsKey("Text_Bench_Surface_RowOutOfRange"), "the TranslateText shape recognised nothing");
        Assert.True(built.ContainsKey("Text_Bench_Saved"), "the WithSuffix shape recognised nothing");

        // A prefix helper called with a literal.
        Assert.True(built.ContainsKey("Text_Bench_Surface_LeaveAnyway"), "the helper shape recognised nothing");

        // Templates, and the two formatters that mangle a word into a key.
        Assert.True(built.ContainsKey("Text_Greeting_Night_4"), "the template shape recognised nothing");
        Assert.True(built.ContainsKey("Text_Bench_Surface_Kind_switch"), "the kind shape recognised nothing");
        Assert.True(built.ContainsKey("Text_Bench_Diag_element_no_key"), "the diagnostics shape recognised nothing");

        // And a key that is defined nowhere, so the scan is not answering true blindly.
        Assert.False(built.ContainsKey("Text_Bench_Surface_RowOutOfRange_"));
    }

    /// <summary>The keys en-us.axaml defines.</summary>
    private static HashSet<string> DefinedKeys()
    {
        var path = RepositoryFile(Path.Combine(Dashboard, "Languages", FallbackLanguage + ".axaml"));

        Assert.True(File.Exists(path), "Expected a language file at " + path + ".");

        return new HashSet<string>(
            Regex.Matches(File.ReadAllText(path), @"x:Key=""(?<key>[^""]+)""")
                .Select(match => match.Groups["key"].Value),
            StringComparer.Ordinal);
    }

    /// <summary>Every key the product builds, and the shape that built it.</summary>
    private static Dictionary<string, string> BuiltKeys()
    {
        var defined = DefinedKeys();
        var built = new Dictionary<string, string>(StringComparer.Ordinal);

        void Record(string key, string how)
        {
            if (!built.ContainsKey(key))
                built[key] = how;
        }

        foreach (var file in SourceFiles())
        {
            var name = Path.GetFileName(file);

            if (file.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
            {
                var markup = File.ReadAllText(file);

                foreach (var key in defined.Where(key => WholeKey(key).IsMatch(markup)))
                    Record(key, "bound in " + name);

                continue;
            }

            var source = StripComments(File.ReadAllText(file));

            // (1) and (2): the key written whole, in a literal somewhere.
            foreach (var key in defined.Where(key => WholeKey(key).IsMatch(source)))
                Record(key, "written in " + name);

            foreach (Match match in BareCall.Matches(source))
                Record("Text_" + match.Groups["key"].Value, "TranslateText in " + name);

            foreach (Match match in SuffixCall.Matches(source))
                Record("Text_" + match.Groups["key"].Value + "_" + match.Groups["suffix"].Value,
                    "TranslateTextWithSuffix in " + name);

            // (4): a helper that turns a bare name into a key, and the literals it is called with.
            RecordHelperFamily(source, name, Record);

            foreach (Match match in ConstantTemplate.Matches(source))
                RecordTemplate(match.Groups["template"].Value, defined, name, Record);

            foreach (Match match in DiagnosticCode.Matches(source))
                Record("Text_Bench_Diag_" + Mangled(match.Groups["code"].Value),
                    "diagnostics code in " + name);
        }

        // The kind names come from the contract's own vocabulary, not from a literal in the client.
        foreach (var kind in new[] { "Text", "Button", "Input", "Number", "Select", "Switch", "List", "Element" })
            Record("Text_Bench_Surface_Kind_" + kind.ToLowerInvariant(), "SurfaceElementKind.NameKeyOf");

        foreach (var (key, how) in BuiltFromALibraryConstant)
            Record(key, how);

        return built;
    }

    /// <summary>Records the keys a helper family builds from its literal arguments.</summary>
    /// <remarks>
    /// <para>The family is found by shape rather than by name: any method that takes string key and
    /// either calls Translate(key, prefix: "P") or concatenates a string constant onto key. That
    /// covers GetLogLevelDisplayText, GetThemeDisplayText, GetUpdateTip and Ask without naming any of
    /// them, so a fifth family added later is covered on the day it is written.</para>
    ///
    /// <para><b>An argument the scan cannot resolve does not bless the prefix.</b> Calling a family
    /// with a variable (GetThemeDisplayText(item.ThemeName)) means this scan does not know which key
    /// it asks for, and it says so by leaving that shape's keys to the hand-written list instead of
    /// marking everything under the prefix as used.</para>
    /// </remarks>
    private static void RecordHelperFamily(string source, string name, Action<string, string> record)
    {
        var constants = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match match in StringConstant.Matches(source))
            constants[match.Groups["name"].Value] = match.Groups["value"].Value;

        foreach (Match head in KeyHelperHead.Matches(source))
        {
            var prefix = PrefixOf(BodyOf(source, head.Index), constants);

            if (prefix is null)
                continue;

            var helper = head.Groups["name"].Value;
            var calls = new Regex(@"\b" + Regex.Escape(helper) + @"\(\s*(?<argument>[^,()]*)");

            foreach (Match call in calls.Matches(source))
            {
                var argument = call.Groups["argument"].Value.Trim();

                if (argument == "string key" || argument == "key")
                    continue;

                var literal = Regex.Match(argument, @"^""(?<subject>[^""]*)""$");

                if (literal.Success)
                    record(prefix + literal.Groups["subject"].Value, "helper " + helper + " in " + name);
            }
        }
    }

    /// <summary>The prefix a helper adds to the name it is given, or null when it is not one.</summary>
    private static string? PrefixOf(string body, Dictionary<string, string> constants)
    {
        var named = Regex.Match(body, @"Translate\(\s*key\s*,\s*prefix:\s*""(?<prefix>[^""]+)""");

        if (named.Success)
            return named.Groups["prefix"].Value;

        var concatenated = Regex.Match(body, @"(?<constant>\w+)\s*\+\s*key\b");

        if (concatenated.Success && constants.TryGetValue(concatenated.Groups["constant"].Value, out var prefix))
            return prefix;

        return null;
    }

    /// <summary>The body of the member that starts at start, up to the next member.</summary>
    private static string BodyOf(string source, int start)
    {
        var end = source.IndexOf("\n    }\n", start, StringComparison.Ordinal);

        return source.Substring(start, end < 0 ? Math.Min(source.Length - start, 900) : end - start + 1);
    }

    /// <summary>Records every key the template can produce.</summary>
    private static void RecordTemplate(
        string template, HashSet<string> defined, string name, Action<string, string> record)
    {
        var pattern = Regex.Replace("^" + Regex.Escape(template) + "$", @"%[A-Za-z]+%", "[A-Za-z0-9]+");
        var matcher = new Regex(pattern);

        foreach (var key in defined.Where(key => matcher.IsMatch(key)))
            record(key, "template " + template + " in " + name);
    }

    /// <summary>
    /// The part of a resource key that comes from a diagnostics code. Written out here rather than
    /// asked of SurfaceDiagnosticText.SuffixOf on purpose: this test is about what the shipped files
    /// must contain, and a test that asked the product would agree with the product even if both were
    /// wrong.
    /// </summary>
    private static string Mangled(string code)
        => (code.StartsWith("surface.", StringComparison.Ordinal) ? code.Substring("surface.".Length) : code)
            .Replace('.', '_')
            .Replace('-', '_');

    /// <summary>Every source file the product is built from, with build output left out.</summary>
    private static IEnumerable<string> SourceFiles()
    {
        var separator = Path.DirectorySeparatorChar.ToString();

        var roots = new[] { Dashboard, Workflow }
            .Select(RepositoryFile)
            .Where(Directory.Exists)
            .ToList();

        Assert.NotEmpty(roots);

        var excluded = new[] { separator + "bin" + separator, separator + "obj" + separator,
            separator + "Languages" + separator };

        return roots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            .Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
            .Where(file => !excluded.Any(part => file.Contains(part, StringComparison.OrdinalIgnoreCase)))
            .Where(file => !file.Contains("Test.Xunit", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The source with its comments removed. A key named in a comment is documentation about a key,
    /// not a use of one, and a scan that counted it would keep a deleted key alive for as long as the
    /// comment about its deletion survived.
    /// </summary>
    private static string StripComments(string source)
    {
        var kept = new StringBuilder(source.Length);
        var index = 0;

        while (index < source.Length)
        {
            var current = source[index];

            if (current == '"' && (index == 0 || source[index - 1] != '\\'))
            {
                var end = index + 1;
                kept.Append(current);

                while (end < source.Length && source[end] != '"')
                {
                    if (source[end] == '\\' && end + 1 < source.Length)
                    {
                        kept.Append(source[end]);
                        end++;
                    }

                    kept.Append(source[end]);
                    end++;
                }

                if (end < source.Length)
                    kept.Append(source[end]);

                index = end + 1;
                continue;
            }

            if (current == '/' && index + 1 < source.Length && source[index + 1] == '/')
            {
                while (index < source.Length && source[index] != '\n')
                    index++;

                continue;
            }

            if (current == '/' && index + 1 < source.Length && source[index + 1] == '*')
            {
                index += 2;

                while (index + 1 < source.Length && !(source[index] == '*' && source[index + 1] == '/'))
                    index++;

                index += 2;
                continue;
            }

            kept.Append(current);
            index++;
        }

        return kept.ToString();
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
