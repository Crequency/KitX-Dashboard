using System;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// The words the leave dialog asks with: its title, the line that introduces the findings, and the
/// two answers.
/// </summary>
/// <remarks>
/// <para><b>Why a class and not four bindings.</b> The dialog is a bare <c>Window</c> with no view
/// model, so there is nothing for a <c>Binding</c> to reach. Assembling the sentences here keeps the
/// lookup — and the English fallbacks that stand in when no dictionary is loaded — out of the code
/// behind, and gives the tests a seam that needs no running application.</para>
///
/// <para><b>Same keys, same shapes as the rest of the Bench.</b> Every key lives under
/// <c>Text_Bench_Surface_Leave*</c> in <c>Languages/*.axaml</c>; the dialog carries no literals of its
/// own, which is what <c>SurfaceLeaveLocalizationTests</c> checks by reading the markup.</para>
/// </remarks>
public static class SurfaceLeaveText
{
    private const string KeyPrefix = "Text_Bench_";

    /// <summary>The window title.</summary>
    public static string Title(Func<string, string?>? lookup = null)
        => Ask("Surface_LeaveTitle", "Card layout has findings", lookup);

    /// <summary>The line the findings are listed under.</summary>
    public static string Body(Func<string, string?>? lookup = null)
        => Ask("Surface_LeaveBody",
            "The card is saved. Closing now leaves out what a render would not keep:", lookup);

    /// <summary>The answer that closes the window and keeps the card as it is.</summary>
    public static string LeaveAnyway(Func<string, string?>? lookup = null)
        => Ask("Surface_LeaveAnyway", "Leave anyway", lookup);

    /// <summary>The answer that goes back to the canvas.</summary>
    public static string Stay(Func<string, string?>? lookup = null)
        => Ask("Surface_LeaveStay", "Go back and fix", lookup);

    /// <summary>
    /// The text the dialog shows above its two buttons: the introduction, then the findings when
    /// there are any.
    /// </summary>
    /// <param name="findings">One finding per line, or empty.</param>
    /// <param name="lookup">Resolves a resource key; defaults to the product dictionary.</param>
    public static string Compose(string? findings, Func<string, string?>? lookup = null)
    {
        var body = Body(lookup);

        return string.IsNullOrWhiteSpace(findings)
            ? body
            : body + Environment.NewLine + Environment.NewLine + findings;
    }

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
