using Avalonia.Controls;
using Avalonia.Interactivity;
using KitX.Dashboard.ViewModels;

namespace KitX.Dashboard.Views;

/// <summary>
/// Asks what to do about a card that is on disk but would not render exactly as laid out.
/// </summary>
/// <remarks>
/// <para><b>Two answers, because there are two.</b> The card has already been written by the time this
/// opens (S10 writes before it validates), so the choice is not "save or discard" — it is "leave with
/// what a render would drop" or "go back and move the cell". A single OK would take that choice away,
/// and a dialog with three buttons would offer a middle answer nobody can act on.</para>
///
/// <para><b>The sentences come from the dictionary.</b> Title, introduction and both answers are
/// read through <see cref="SurfaceLeaveText"/>, so this window carries no English of its own. A
/// language switched while the dialog is open is not followed: the dialog exists to answer one
/// question and is gone before a reader could pick a different language, and re-reading the keys on
/// an event would only add a way for the window to change under the pointer.</para>
/// </remarks>
public partial class SurfaceLeaveDialog : Window
{
    /// <summary>Creates the dialog with the findings it is asking about.</summary>
    /// <param name="findings">What the validator said about the card, one line each.</param>
    public SurfaceLeaveDialog(string findings)
    {
        InitializeComponent();
        ApplyLanguage();

        SurfaceLeaveMessage.Text = SurfaceLeaveText.Compose(findings);
    }

    /// <summary>Parameterless constructor for the XAML loader.</summary>
    public SurfaceLeaveDialog()
    {
        InitializeComponent();
        ApplyLanguage();
    }

    /// <summary>
    /// Puts the dictionary's words on the window. Called from both constructors because the XAML
    /// loader builds the parameterless one first, and a window that briefly shows no title is still
    /// a window somebody can see.
    /// </summary>
    private void ApplyLanguage()
    {
        Title = SurfaceLeaveText.Title();
        SurfaceLeaveFix.Content = SurfaceLeaveText.Stay();
        SurfaceLeaveLeave.Content = SurfaceLeaveText.LeaveAnyway();
    }

    private void OnLeave(object? sender, RoutedEventArgs e) => Close(true);

    private void OnFix(object? sender, RoutedEventArgs e) => Close(false);
}
