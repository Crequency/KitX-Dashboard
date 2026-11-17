using System;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// The words the delete confirmation asks with: its title, the sentence naming the ToolKit, the
/// warning that the deletion takes work with it, and the two answers.
/// </summary>
/// <remarks>
/// <para><b>Why a class and not four bindings.</b> Same reason as
/// <see cref="SurfaceLeaveText"/>: the dialog is a bare <c>Window</c> with no view model, so there is
/// nothing for a <c>Binding</c> to reach. Assembling the sentences here keeps the lookup — and the
/// English fallbacks that stand in when no dictionary is loaded — out of the code behind, and gives
/// the tests a seam that needs no running application.</para>
///
/// <para><b>Why the name is formatted in rather than interpolated at the call site.</b> A language
/// may put the name anywhere in the sentence, and <c>Text_ToolKit_DeleteBody</c> carries
/// <c>{0}</c> for that. Formatting it here means the code behind never sees a literal, which is what
/// the localization tests check.</para>
/// </remarks>
public static class DeleteToolkitText
{
    private const string KeyPrefix = "Text_ToolKit_";

    /// <summary>The window title.</summary>
    public static string Title(Func<string, string?>? lookup = null)
        => Ask("DeleteTitle", "Delete this ToolKit?", lookup);

    /// <summary>
    /// The sentence naming what is about to go. <paramref name="name"/> is the ToolKit's display
    /// name, which may be empty — a draft can be saved without one.
    /// </summary>
    public static string Body(string? name, Func<string, string?>? lookup = null)
    {
        var format = Ask("DeleteBody",
            "\u201c{0}\u201d and everything in it will be removed from the repository.", lookup);

        var shown = string.IsNullOrWhiteSpace(name) ? Untitled(lookup) : name;

        // Format, never concatenate: a translation is free to move the name to the end of the
        // sentence, and a concatenated prefix would then read backwards in every language that
        // does. This is the same rule the surface findings follow.
        return string.Format(format, shown);
    }

    /// <summary>
    /// The line that says the deletion cannot be undone, so the reader knows what they are trading
    /// for a click.
    /// </summary>
    /// <remarks>
    /// It goes into the bin rather than out of the world (<c>ToolkitStore.Delete</c> moves the
    /// directory), but the sentence does not promise a way back. There is no command that restores
    /// from the bin yet, and a dialog that advertised one would be describing software that does
    /// not exist.
    /// </remarks>
    public static string Warning(Func<string, string?>? lookup = null)
        => Ask("DeleteWarning", "This cannot be undone from the application.", lookup);

    /// <summary>The answer that goes through with it.</summary>
    public static string Confirm(Func<string, string?>? lookup = null)
        => Ask("DeleteConfirm", "Delete", lookup);

    /// <summary>The answer that leaves everything alone.</summary>
    public static string Cancel(Func<string, string?>? lookup = null)
        => Ask("DeleteCancel", "Cancel", lookup);

    /// <summary>The text the dialog shows above its two buttons.</summary>
    /// <param name="name">The ToolKit's display name, or empty.</param>
    /// <param name="lookup">Resolves a resource key; defaults to the product dictionary.</param>
    public static string Compose(string? name, Func<string, string?>? lookup = null)
        => Body(name, lookup) + Environment.NewLine + Environment.NewLine + Warning(lookup);

    private static string Untitled(Func<string, string?>? lookup)
        => Ask("DeleteUntitled", "this ToolKit", lookup);

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
