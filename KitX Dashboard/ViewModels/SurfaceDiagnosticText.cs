using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using KitX.ToolKit.Contract;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// Turns a <see cref="Diagnostic"/> the ToolKit produced into the sentence this client shows.
/// </summary>
/// <remarks>
/// <para><b>Why the producer cannot do this.</b> A <see cref="Diagnostic"/> is built inside
/// <c>KitX.ToolKit</c>, a service-side assembly that has no language: it is constructed by a
/// compiler, run by a host, and its sentences end up in logs and in scripts. So the record carries
/// the facts (<see cref="Diagnostic.Data"/>) and an English sentence
/// (<see cref="Diagnostic.Message"/>), and the choosing of words happens here, where the resource
/// dictionary is.</para>
///
/// <para><b>Why the code decides, and not the sentence.</b> A code is
/// <c>surface.element.position-out-of-grid</c>: stable, greppable, and already displayed under the
/// sentence. Its resource key is that code with the <c>surface.</c> prefix dropped and every
/// <c>.</c> and <c>-</c> turned into <c>_</c>, under <c>Text_Bench_Diag_</c>. The mapping is
/// mechanical on purpose — a translator can read a key and know which finding it belongs to
/// without a lookup table in between.</para>
///
/// <para><b>Why the placeholders are named.</b> <c>{row}</c> rather than <c>{0}</c>: a translation
/// may put the row somewhere else in the sentence, and a positional format would then print the
/// weight where the row belongs. A name survives reordering; an index does not.</para>
///
/// <para><b>What a miss does.</b> No key, or no language loaded yet, and the English sentence is
/// what the reader sees. That is the same sentence the log has, so a report from the screen can be
/// matched against the log without translation in between. A placeholder whose value the producer
/// forgot stays on screen as <c>{row}</c> — visible, searchable, and never a crash: a diagnostic
/// about a broken file must not itself need a working file to render.</para>
/// </remarks>
public static class SurfaceDiagnosticText
{
    /// <summary>The resource-key prefix every localised diagnostic sentence carries.</summary>
    public const string KeyPrefix = "Text_Bench_Diag_";

    /// <summary>The prefix a ToolKit diagnostics code carries, and the one the key leaves behind.</summary>
    private const string CodePrefix = "surface.";

    /// <summary>
    /// A named placeholder. Digits are excluded on purpose: <c>{0}</c> is a format argument, not a
    /// value this client substitutes, and rewriting one would corrupt a sentence somebody wrote.
    /// </summary>
    private static readonly Regex Placeholder =
        new(@"\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

    /// <summary>The resource key that holds the sentence for <paramref name="code"/>.</summary>
    /// <param name="code">A diagnostics code, e.g. <c>surface.element.unknown-key</c>.</param>
    public static string KeyFor(string code) => KeyPrefix + SuffixOf(code);

    /// <summary>
    /// The part of the key that comes from the code: the prefix dropped, <c>.</c> and <c>-</c>
    /// turned into <c>_</c>.
    /// </summary>
    /// <param name="code">A diagnostics code.</param>
    public static string SuffixOf(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        var body = code.StartsWith(CodePrefix, StringComparison.Ordinal)
            ? code[CodePrefix.Length..]
            : code;

        return body.Replace('.', '_').Replace('-', '_');
    }

    /// <summary>
    /// The sentence to show for <paramref name="diagnostic"/>: the localised one when its code has
    /// a key, the producer's English sentence otherwise.
    /// </summary>
    /// <param name="diagnostic">What the save reported.</param>
    /// <param name="lookup">
    /// Resolves a resource key to text, or <see langword="null"/> when the key is not there. Defaults
    /// to <see cref="ViewModelBase.Translate"/>; a test passes its own so it does not need a running
    /// application, and so the test does not have to agree with this class about the dictionary.
    /// </param>
    public static string Describe(Diagnostic diagnostic, Func<string, string?>? lookup = null)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        var format = (lookup ?? DefaultLookup)(KeyFor(diagnostic.Code));

        return string.IsNullOrEmpty(format)
            ? diagnostic.Message
            : Fill(format!, diagnostic.Data);
    }

    /// <summary>
    /// Replaces every named placeholder in <paramref name="format"/> with the value the producer
    /// recorded under that name.
    /// </summary>
    /// <param name="format">A sentence from the resource dictionary.</param>
    /// <param name="data">The values the producer recorded, or <see langword="null"/>.</param>
    /// <remarks>
    /// A placeholder with no value is left alone rather than blanked: <c>{row}</c> on screen says
    /// "this client is missing a value the producer has", while an empty string says nothing and
    /// looks like a sentence that never had one.
    /// </remarks>
    public static string Fill(string format, IReadOnlyDictionary<string, string>? data)
    {
        ArgumentNullException.ThrowIfNull(format);

        if (data is null || data.Count == 0)
            return format;

        return Placeholder.Replace(format, match =>
            data.TryGetValue(match.Groups[1].Value, out var value) && value is not null
                ? value
                : match.Value);
    }

    /// <summary>
    /// The product's dictionary lookup. It answers <see langword="null"/> off the UI thread by
    /// design (see <see cref="ViewModelBase.Translate"/>), which is a miss like any other and falls
    /// back to the English sentence.
    /// </summary>
    private static string? DefaultLookup(string key) => ViewModelBase.Translate(key);
}
