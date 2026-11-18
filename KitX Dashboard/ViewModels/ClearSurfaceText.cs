using System;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// The words the empty-the-card confirmation asks with: its title, the sentence saying how much it
/// takes away, and the two answers.
/// </summary>
/// <remarks>
/// <para><b>Why a class and not four bindings.</b> The same reason as
/// <see cref="DeleteToolkitText"/> and <see cref="SurfaceLeaveText"/>: the dialog is a bare
/// <c>Window</c> with no view model, so there is nothing for a <c>Binding</c> to reach. Keeping the
/// lookup, and the English fallbacks that stand in when no dictionary is loaded, in one class also
/// gives the tests a seam that needs no running application.</para>
///
/// <para><b>Why the count is formatted in rather than interpolated at the call site.</b> A language
/// may put the number anywhere in the sentence, and the resource carries <c>{0}</c> for that.
/// Formatting here means the code behind never sees a literal, which is what the localization tests
/// check.</para>
///
/// <para><b>Why it says "<c>{0}</c> elements" and not "are you sure".</b> A dialog that asks
/// "empty the card?" is a dialog whose reader has to reconstruct what is about to go. The number is
/// already known at the call site, and it is the only fact here the reader could get wrong: twelve
/// elements and one element are the same click, and only one of them is worth a second thought.</para>
/// </remarks>
public static class ClearSurfaceText
{
    private const string KeyPrefix = "Text_Bench_Surface_";

    /// <summary>The window title.</summary>
    public static string Title(Func<string, string?>? lookup = null)
        => Ask("ClearTitle", "Empty the card?", lookup);

    /// <summary>The sentence saying how much is about to go.</summary>
    /// <param name="count">How many elements are on the canvas right now.</param>
    /// <param name="lookup">Resolves a resource key; defaults to the product dictionary.</param>
    public static string Body(int count, Func<string, string?>? lookup = null)
    {
        var format = Ask("ClearBody",
            "All {0} elements on this card will be taken off it. The file on disk keeps what it has until you save.", lookup);

        // Invariant digits, not a culture-formatted number: at four digits and above some cultures
        // insert a group separator and the sentence was not written for one. A language that wants a
        // different numeral form says so in its own string.
        return string.Format(format, count.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>The answer that goes through with it.</summary>
    public static string Confirm(Func<string, string?>? lookup = null)
        => Ask("ClearConfirm", "Empty it", lookup);

    /// <summary>The answer that leaves everything alone.</summary>
    public static string Cancel(Func<string, string?>? lookup = null)
        => Ask("ClearCancel", "Cancel", lookup);

    /// <summary>The sentence the dialog shows above its two buttons.</summary>
    /// <param name="count">How many elements are on the canvas right now.</param>
    /// <param name="lookup">Resolves a resource key; defaults to the product dictionary.</param>
    public static string Compose(int count, Func<string, string?>? lookup = null)
        => Body(count, lookup);

    private static string Ask(string key, string fallback, Func<string, string?>? lookup)
    {
        var text = (lookup ?? DefaultLookup)(KeyPrefix + key);

        // A key that is present but empty is a translation somebody left unwritten, which reads the
        // same as a missing key: the English sentence is still truer than a blank dialog.
        return string.IsNullOrEmpty(text) ? fallback : text;
    }

    /// <summary>
    /// The product's dictionary lookup. It answers <see langword="null"/> off the UI thread by
    /// design (see <see cref="ViewModelBase.Translate"/>) and the fallback takes over.
    /// </summary>
    private static string? DefaultLookup(string key) => ViewModelBase.Translate(key);
}
