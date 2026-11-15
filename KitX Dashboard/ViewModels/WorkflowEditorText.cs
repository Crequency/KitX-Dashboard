using System;
using System.Text.RegularExpressions;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// Turns a name and the values a sentence is built from into the words this window shows.
/// </summary>
/// <remarks>
/// <para><b>Why the window cannot call the dictionary itself.</b> It does, in the end — this is the
/// one place that knows the prefix. A window that spelled <c>Text_WorkflowEditor_</c> at sixty call
/// sites would have sixty chances to mistype it and no way to notice: a miss returns
/// <see langword="null"/> and the <c>?? "English text"</c> beside the call site quietly becomes the
/// product's only copy, in every language at once. That is not a hypothetical: it is how the Bench
/// canvas shipped for its whole life.</para>
///
/// <para><b>Why the placeholders are named.</b> <c>{name}</c> rather than <c>{0}</c>: a translation
/// may put the node somewhere else in the sentence, and a positional format would then print the row
/// where the name belongs. <see cref="SurfaceDiagnosticText"/> was written the same way for the same
/// reason, and the two now share one rule rather than two.</para>
///
/// <para><b>What a miss does.</b> The English sentence the call site carries is what the reader sees.
/// That sentence is also what the log records, so a report from the screen can be matched against the
/// log without a translation in between — and a language file that has not been brought up to date
/// leaves a window that reads, not a window that is blank.</para>
/// </remarks>
public static class WorkflowEditorText
{
    /// <summary>The resource-key prefix every string this window shows carries.</summary>
    public const string KeyPrefix = "Text_WorkflowEditor_";

    /// <summary>
    /// A named placeholder. Digits are excluded on purpose: <c>{0}</c> is a format argument, not a
    /// value this window substitutes, and rewriting one would corrupt a sentence somebody wrote.
    /// </summary>
    private static readonly Regex Placeholder =
        new(@"\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

    /// <summary>
    /// One sentence this window can show again: the name it is filed under, the English it falls
    /// back to, and the values the sentence names.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a recipe and not the finished sentence.</b> The dictionary is asked at the moment
    /// the line is written, so a sentence written before a language switch stays in the old language
    /// for as long as the window is open. The key is the only part that survives a translation; asking
    /// again needs the key, and nothing else.</para>
    ///
    /// <para><b>Why it lives here.</b> Keeping the recipe beside the lookup is what makes every
    /// sentence in this window traceable to the key it came from. The status line, the output frame
    /// and the blueprint's rejections all carry one, so a language change has one thing to rebuild
    /// rather than three private copies of "key, fallback, values" that drift apart.</para>
    /// </remarks>
    /// <param name="Key">The bare name, e.g. <c>StatusSaved</c>.</param>
    /// <param name="Fallback">The English sentence, used when the dictionary has no entry.</param>
    /// <param name="Values">The values to substitute, by the names the sentence uses.</param>
    public readonly record struct Recipe(string Key, string Fallback, (string Name, object? Value)[] Values)
    {
        /// <summary>This sentence in the reader's language, as of now.</summary>
        public string Text => Get(Key, Fallback, Values);
    }

    /// <summary>Files a sentence under a name so it can be asked for again.</summary>
    /// <param name="key">A bare name, e.g. <c>StatusSaved</c>.</param>
    /// <param name="fallback">The English sentence the call site reads well enough on its own.</param>
    /// <param name="values">The values the sentence names.</param>
    public static Recipe Make(string key, string fallback, params (string Name, object? Value)[] values)
        => new(key, fallback, values);

    /// <summary>The resource key that holds the words for <paramref name="key"/>.</summary>
    /// <param name="key">A bare name, e.g. <c>Run</c>.</param>
    /// <remarks>
    /// Used where the key has to be carried rather than resolved — a palette entry holds one until
    /// the entry is drawn, because drawing happens after the reader may have changed the language.
    /// </remarks>
    public static string Key(string key) => KeyPrefix + key;

    /// <summary>
    /// The sentence for <paramref name="key"/>, with each named placeholder replaced by its value.
    /// </summary>
    /// <param name="key">A bare name, e.g. <c>StatusSaved</c>.</param>
    /// <param name="fallback">
    /// The English sentence, used when the dictionary has no entry or no language is loaded. It
    /// carries the same placeholders, so it reads correctly on its own.
    /// </param>
    /// <param name="values">The values to substitute, by the names the sentence uses.</param>
    public static string Get(string key, string fallback, params (string Name, object? Value)[] values)
    {
        ArgumentNullException.ThrowIfNull(fallback);

        return Fill(Translate(key) ?? fallback, values);
    }

    /// <summary>The dictionary's answer for <paramref name="key"/>, or null when it has none.</summary>
    /// <remarks>
    /// A separate member rather than an inline call so the scan that reads these files sees one shape
    /// for every lookup, and so a caller that already holds a full key — the palette converter — can
    /// ask without adding the prefix a second time.
    /// </remarks>
    public static string? Translate(string key)
        => ViewModelBase.Translate(KeyPrefix + key);

    /// <summary>Replaces every named placeholder in <paramref name="format"/> with its value.</summary>
    /// <remarks>
    /// A placeholder with no value is left alone rather than blanked: <c>{name}</c> on screen says
    /// "this call site is missing a value", while an empty string says nothing and looks like a
    /// sentence that never had one.
    /// </remarks>
    public static string Fill(string format, params (string Name, object? Value)[] values)
    {
        ArgumentNullException.ThrowIfNull(format);

        if (values.Length == 0)
            return format;

        return Placeholder.Replace(format, match =>
        {
            foreach (var (name, value) in values)
                if (string.Equals(name, match.Groups[1].Value, StringComparison.Ordinal))
                    return value?.ToString() ?? string.Empty;

            return match.Value;
        });
    }
}
