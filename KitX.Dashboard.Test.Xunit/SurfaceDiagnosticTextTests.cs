using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using KitX.Dashboard.ViewModels;
using KitX.ToolKit.Contract;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// The check panel's sentence: the code chooses the text, the recorded values fill it, and a code
/// the dictionary does not know shows the producer's English sentence instead.
/// </summary>
/// <remarks>
/// <para><b>What would go wrong without this file.</b> The panel used to bind the producer's
/// <c>Message</c> directly, so the client showed whatever language the service happened to write in
/// — English — beside a code that named the very resource key which would have translated it. The
/// formatter closes that gap; these tests pin the two halves that a future edit can quietly break:
/// the code-to-key rule, and the promise that a missing value is visible rather than fatal.</para>
///
/// <para><b>Why the lookup is injected.</b> <see cref="ViewModelBase.Translate"/> answers
/// <see langword="null"/> off the UI thread by design, so a plain <c>[Fact]</c> that went through
/// the real dictionary would be testing the thread, not the formatter. The product's own lookup is
/// exercised in <see cref="Every_sentence_the_client_can_ask_for_exists_in_all_seven_languages"/>
/// instead, against the shipped files.</para>
/// </remarks>
public class SurfaceDiagnosticTextTests
{
    private const string Dashboard = @"KitX Clients\KitX Dashboard\KitX Dashboard";

    /// <summary>The diagnostic codes the ToolKit can report, as strings, so a renamed key is caught here.</summary>
    /// <remarks>
    /// Written out rather than reflected out of <c>SurfaceDiagnosticCodes</c>: that class is
    /// <c>internal</c> to the ToolKit assembly, and a list built from it by reflection would agree
    /// with it by construction — the same defect this test exists to catch, one level up.
    /// </remarks>
    private static readonly string[] Codes =
    [
        "surface.file.unreadable",
        "surface.file.invalid-json",
        "surface.file.root-not-object",
        "surface.file.missing-version",
        "surface.file.unsupported-schema",
        "surface.file.unsupported-layout-version",
        "surface.file.elements-missing",
        "surface.file.unknown-field",
        "surface.element.unsupported-type",
        "surface.element.empty",
        "surface.element.events-not-allowed",
        "surface.element.unknown-field",
        "surface.element.no-key",
        "surface.element.unknown-key",
        "surface.element.key-ignored",
        "surface.element.inert",
        "surface.element.empty-label",
        "surface.element.button.no-events",
        "surface.card.values-have-no-way-out",
        "surface.element.button.unknown-emphasis",
        "surface.element.button.repeated-primary",
        "surface.element.select.no-options",
        "surface.element.select.ambiguous-options",
        "surface.element.list.key-not-array",
        "surface.element.position-out-of-grid",
        "surface.element.weight-repaired",
        "surface.element.text.unknown-format",
        "surface.element.number.range-reversed",
        "surface.element.number.step-not-positive",
        "surface.element.number.decimals-negative",
    ];

    /// <summary>Every name a sentence in the dictionary is allowed to use.</summary>
    private static readonly HashSet<string> KnownPlaceholders = new(StringComparer.Ordinal)
    {
        "count", "decimals", "expected", "field", "file", "key", "kind", "known",
        "max", "min", "name", "reason", "row", "step", "type", "value", "version", "weight",
    };

    private static readonly Regex Placeholder =
        new(@"\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

    private static Diagnostic Record(string code, string message,
        IReadOnlyDictionary<string, string>? data = null)
        => Diagnostic.Warning(code, message, null, data);

    private static Dictionary<string, string> Values(params (string Name, string Value)[] pairs)
        => pairs.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);

    [Fact]
    public void A_code_names_its_sentence_by_dropping_the_prefix_and_flattening_the_separators()
    {
        Assert.Equal("Text_Bench_Diag_element_position_out_of_grid",
            SurfaceDiagnosticText.KeyFor("surface.element.position-out-of-grid"));

        Assert.Equal("Text_Bench_Diag_file_invalid_json",
            SurfaceDiagnosticText.KeyFor("surface.file.invalid-json"));

        Assert.Equal("Text_Bench_Diag_element_no_key",
            SurfaceDiagnosticText.KeyFor("surface.element.no-key"));
    }

    [Fact]
    public void A_sentence_from_the_dictionary_is_filled_from_the_values_the_producer_recorded()
    {
        var said = SurfaceDiagnosticText.Describe(
            Record("surface.element.unknown-key", "Kept Text at row 1, weight 2: unknown key 'nope'.",
                Values(("kind", "Text"), ("row", "1"), ("weight", "2"), ("key", "nope"))),
            _ => "保留第 {row} 行、权重 {weight} 的 {kind}：未知键 '{key}'。");

        Assert.Equal("保留第 1 行、权重 2 的 Text：未知键 'nope'。", said);
    }

    [Fact]
    public void A_code_with_no_sentence_falls_back_to_the_message_the_producer_wrote()
    {
        const string message = "Dropped Text at row 0, weight 1: 'row' is 0, row numbers start at 1.";

        var said = SurfaceDiagnosticText.Describe(
            Record("surface.position.out-of-grid", message, Values(("row", "0"))),
            _ => null);

        Assert.Equal(message, said);
    }

    [Fact]
    public void An_empty_sentence_is_a_miss_as_well()
    {
        const string message = "Kept Text at row 1, weight 1: no name.";

        Assert.Equal(message, SurfaceDiagnosticText.Describe(
            Record("surface.element.inert", message), _ => string.Empty));
    }

    [Fact]
    public void A_placeholder_with_no_value_stays_visible_instead_of_being_blanked()
    {
        var said = SurfaceDiagnosticText.Describe(
            Record("surface.element.unknown-key", "unused",
                Values(("kind", "Text"), ("weight", "2"), ("key", "nope"))),
            _ => "保留第 {row} 行、权重 {weight} 的 {kind}：未知键 '{key}'。");

        Assert.Equal("保留第 {row} 行、权重 2 的 Text：未知键 'nope'。", said);
    }

    [Fact]
    public void A_numbered_placeholder_is_not_a_name_and_is_left_alone()
    {
        var said = SurfaceDiagnosticText.Describe(
            Record("surface.element.unknown-key", "unused", Values(("kind", "Text"))),
            _ => "{kind} is not {0} and {1} is not {kind}.");

        Assert.Equal("Text is not {0} and {1} is not Text.", said);
    }

    [Fact]
    public void A_value_that_looks_like_a_placeholder_is_not_substituted_again()
    {
        var said = SurfaceDiagnosticText.Describe(
            Record("surface.file.unknown-field", "unused", Values(("file", "{reason}"), ("field", "colour"))),
            _ => "{file} has an unrecognised field '{field}'.");

        Assert.Equal("{reason} has an unrecognised field 'colour'.", said);
    }

    [Fact]
    public void A_sentence_with_no_values_is_returned_whole()
    {
        var said = SurfaceDiagnosticText.Describe(
            Record("surface.element.empty", "unused"), _ => "已丢弃一个空元素。");

        Assert.Equal("已丢弃一个空元素。", said);
    }

    [AvaloniaFact]
    public void Every_sentence_the_client_can_ask_for_exists_in_all_seven_languages()
    {
        var missing = new List<string>();

        foreach (var language in new[] { "en-us", "fr-fr", "ja-jp", "ko-kr", "ru-ru", "zh-cn", "zh-tw" })
        {
            var path = RepositoryFile(Path.Combine(Dashboard, "Languages", language + ".axaml"));
            Assert.True(File.Exists(path), $"Expected a language file at {path}.");

            var dictionary = Assert.IsType<ResourceDictionary>(
                AvaloniaRuntimeXamlLoader.Load(File.ReadAllText(path)));

            foreach (var code in Codes)
            {
                var key = SurfaceDiagnosticText.KeyFor(code);

                if (!dictionary.TryGetResource(key, null, out var found) || found is not string sentence
                    || string.IsNullOrWhiteSpace(sentence))
                {
                    missing.Add($"{language}: {key}");
                    continue;
                }

                foreach (Match placeholder in Placeholder.Matches(sentence))
                {
                    if (!KnownPlaceholders.Contains(placeholder.Groups[1].Value))
                        missing.Add($"{language}: {key} uses {{{placeholder.Groups[1].Value}}}, which no producer records");
                }
            }
        }

        Assert.True(missing.Count == 0, "Sentences the check panel would fall back on:" + Environment.NewLine +
            string.Join(Environment.NewLine, missing));
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
