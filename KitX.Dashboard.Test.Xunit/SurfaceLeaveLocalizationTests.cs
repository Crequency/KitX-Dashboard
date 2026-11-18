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
using KitX.ToolKit.Contract.Declaration;
using KitX.ToolKit.Contract.Surface;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// The two strings task-4 left in English: the leave dialog, and the finding an automatic write
/// failure puts in the check panel.
/// </summary>
/// <remarks>
/// <para><b>What would go wrong without this file.</b> Both are user-visible and both were written
/// before the language files were available: the dialog carried four literals in its markup, and the
/// save failure was a code with no key behind it, so the panel would have fallen back to the
/// producer's English sentence the moment the panel started looking sentences up. A code without a
/// key is silent — it renders, in the wrong language, and nobody sees an error.</para>
///
/// <para><b>Why the lookup is injected here too.</b> Same reason as
/// <see cref="SurfaceDiagnosticTextTests"/>: <see cref="ViewModelBase.Translate"/> answers
/// <see langword="null"/> off the UI thread, so the shape of the sentence is tested against a fake
/// and the shipped files are tested against themselves.</para>
/// </remarks>
public class SurfaceLeaveLocalizationTests
{
    private const string Dashboard = @"KitX Clients\KitX Dashboard\KitX Dashboard";

    private static readonly string[] Languages =
        ["en-us", "fr-fr", "ja-jp", "ko-kr", "ru-ru", "zh-cn", "zh-tw"];

    /// <summary>Every key this change is responsible for, and what it is allowed to contain.</summary>
    /// <remarks>
    /// <para><b>This table covers the keys listed in it and nothing else.</b> It is not a registry of
    /// every diagnostic sentence, and it does not promise that the placeholders of the other
    /// <c>Text_Bench_Diag_*</c> lines are filled by anything. Those sentences are written by
    /// <c>SurfaceValidator</c> and <c>SurfaceStore</c> in <c>KitX.ToolKit</c>, which this table
    /// cannot see; asserting their placeholders here would mean either reading that producer
    /// code (unreliable — it changes without this file) or keeping a second list as long as the
    /// producer's (a second source of truth, which this codebase already pays for elsewhere).</para>
    /// <para>To widen it, answer first: <em>who declares which placeholders each diagnostic
    /// sentence is allowed to have?</em> Until there is an answer, a longer table here would be
    /// a guess wearing a test's clothes.</para>
    /// </remarks>
    private static readonly (string Key, string[] Placeholders)[] Keys =
    [
        ("Text_Bench_Surface_LeaveTitle", []),
        ("Text_Bench_Surface_LeaveBody", []),
        ("Text_Bench_Surface_LeaveAnyway", []),
        ("Text_Bench_Surface_LeaveStay", []),
        ("Text_Bench_Diag_save_failed", ["file", "reason"]),

        // The empty-the-card confirmation. `ClearBody` carries a positional `{0}` for the count,
        // which the placeholder regex above does not match at all — it only knows named
        // placeholders. Listing it as allowed would claim a check that never runs, so this table
        // says what it actually knows: no *named* placeholder is allowed in any of the four.
        ("Text_Bench_Surface_ClearTitle", []),
        ("Text_Bench_Surface_ClearBody", []),
        ("Text_Bench_Surface_ClearConfirm", []),
        ("Text_Bench_Surface_ClearCancel", []),
    ];

    private static readonly Regex Placeholder =
        new(@"\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

    [Fact]
    public void The_dialog_says_what_the_dictionary_says()
    {
        var said = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Text_Bench_Surface_LeaveTitle"] = "卡片排版有问题",
            ["Text_Bench_Surface_LeaveBody"] = "卡片已保存。现在关闭，会落下渲染时不保留的部分：",
            ["Text_Bench_Surface_LeaveAnyway"] = "仍然关闭",
            ["Text_Bench_Surface_LeaveStay"] = "回去修改",
        };
        Func<string, string?> lookup = key => said.GetValueOrDefault(key);

        Assert.Equal("卡片排版有问题", SurfaceLeaveText.Title(lookup));
        Assert.Equal("卡片已保存。现在关闭，会落下渲染时不保留的部分：", SurfaceLeaveText.Body(lookup));
        Assert.Equal("仍然关闭", SurfaceLeaveText.LeaveAnyway(lookup));
        Assert.Equal("回去修改", SurfaceLeaveText.Stay(lookup));
    }

    [Fact]
    public void A_missing_or_empty_key_answers_in_English()
    {
        Func<string, string?> nothing = _ => null;
        Func<string, string?> blank = _ => string.Empty;

        foreach (var lookup in new[] { nothing, blank })
        {
            Assert.Equal("Card layout has findings", SurfaceLeaveText.Title(lookup));
            Assert.Equal("Leave anyway", SurfaceLeaveText.LeaveAnyway(lookup));
            Assert.Equal("Go back and fix", SurfaceLeaveText.Stay(lookup));
        }
    }

    [Fact]
    public void The_findings_are_listed_under_the_introduction()
    {
        Func<string, string?> lookup = key => key switch
        {
            "Text_Bench_Surface_LeaveBody" => "保存了，但这些会落下：",
            _ => null,
        };

        Assert.Equal(
            "保存了，但这些会落下：" + Environment.NewLine + Environment.NewLine + "第 2 行太宽",
            SurfaceLeaveText.Compose("第 2 行太宽", lookup));

        Assert.Equal("保存了，但这些会落下：", SurfaceLeaveText.Compose("   ", lookup));
        Assert.Equal("保存了，但这些会落下：", SurfaceLeaveText.Compose(null, lookup));
    }

    [Fact]
    public void A_failed_write_is_a_finding_the_panel_can_translate()
    {
        var failure = Diagnostic.Error(
            "surface.save.failed",
            "The card could not be written: disk full",
            null,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["reason"] = "disk full" });

        Assert.Equal("Text_Bench_Diag_save_failed", SurfaceDiagnosticText.KeyFor(failure.Code));
        Assert.Equal("卡片写不进磁盘：disk full", SurfaceDiagnosticText.Describe(
            failure, _ => "卡片写不进磁盘：{reason}"));

        // No key anywhere: the log's English sentence is what a reader sees, reason included.
        Assert.Equal("The card could not be written: disk full", SurfaceDiagnosticText.Describe(
            failure, _ => null));
    }

    [AvaloniaFact]
    public void Every_key_this_change_owns_exists_in_all_seven_languages()
    {
        var missing = new List<string>();

        foreach (var language in Languages)
        {
            var path = RepositoryFile(Path.Combine(Dashboard, "Languages", language + ".axaml"));
            Assert.True(File.Exists(path), $"Expected a language file at {path}.");

            var dictionary = Assert.IsType<ResourceDictionary>(
                AvaloniaRuntimeXamlLoader.Load(File.ReadAllText(path)));

            foreach (var (key, allowed) in Keys)
            {
                if (!dictionary.TryGetResource(key, null, out var found) || found is not string sentence
                    || string.IsNullOrWhiteSpace(sentence))
                {
                    missing.Add($"{language}: {key}");
                    continue;
                }

                foreach (Match placeholder in Placeholder.Matches(sentence))
                {
                    var name = placeholder.Groups[1].Value;

                    if (!allowed.Contains(name, StringComparer.Ordinal))
                        missing.Add($"{language}: {key} uses {{{name}}}, which nothing fills");
                }
            }
        }

        Assert.True(missing.Count == 0, "Keys the dialog or the panel would fall back on:" +
            Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void The_dialog_markup_carries_no_English_of_its_own()
    {
        var markup = File.ReadAllText(RepositoryFile(
            Path.Combine(Dashboard, "Views", "SurfaceLeaveDialog.axaml")));

        foreach (var literal in new[] { "Card layout has findings", "Go back and fix", "Leave anyway" })
            Assert.DoesNotContain(literal, markup, StringComparison.Ordinal);
    }

    /// <summary>The panel's sentence names the place and repeats the reason.</summary>
    /// <remarks>
    /// Asserted through the same <see cref="Diagnostic"/> the product builds, so the named values
    /// are checked against what the writer records rather than against a dictionary made up here.
    /// An earlier version hard-coded a reason and no place; the place was added because the store
    /// knows the real file name while the platform's message names the temporary one, and a finding
    /// that cannot say where is a finding the author cannot act on.
    /// </remarks>
    [Fact]
    public void A_write_that_fails_is_reported_as_a_finding_that_carries_the_place_and_the_reason()
    {
        var viewModel = new SurfaceEditorViewModel(new ThrowingStore(), _ => []);
        viewModel.Load("save-failure-test");
        viewModel.Clear();

        Assert.True(viewModel.IsDirty);

        var verdict = viewModel.PrepareToClose();

        Assert.Equal(SurfaceEditorViewModel.SurfaceCloseVerdict.WriteFailed, verdict);

        var failure = Assert.Single(viewModel.Diagnostics);

        Assert.Equal(SurfaceEditorViewModel.SaveFailedCode, failure.Code);

        // This store is an ordinary one, so there is no SurfaceWriteException to carry a path and
        // the toolkit id stands in. What matters is that the sentence and the finding agree on it,
        // and that the platform's own words come through.
        Assert.Contains("save-failure-test", failure.Message, StringComparison.Ordinal);

        Assert.NotNull(failure.Data);
        Assert.True(failure.Data!.TryGetValue("file", out var file));
        Assert.Equal("save-failure-test", file);
        Assert.True(failure.Data!.TryGetValue("reason", out var reason));
        Assert.Equal("disk full", reason);

        // The same Record, read the way the panel reads it.
        Assert.Equal("卡片写不进磁盘：disk full", SurfaceDiagnosticText.Describe(
            failure, _ => "卡片写不进磁盘：{reason}"));
    }

    /// <summary>A store whose writes fail, so the failure path is what runs.</summary>
    private sealed class ThrowingStore : ISurfaceStore
    {
        public SurfaceLoadResult Load(string toolkitId) => new(
            new SurfaceDocument
            {
                Elements = [new SurfaceTextElement { Row = 1, Weight = 1, Key = "title" }],
            },
            [],
            []);

        public void Save(string toolkitId, SurfaceDocument document)
            => throw new IOException("disk full");

        public IReadOnlyList<Diagnostic> Diagnose(
            SurfaceDocument document, IEnumerable<KeyDeclaration> keys) => [];

        public void Delete(string toolkitId)
        {
        }

        public event EventHandler<SurfaceChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }
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
