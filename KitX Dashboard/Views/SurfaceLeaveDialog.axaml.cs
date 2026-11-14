using Avalonia.Controls;
using Avalonia.Interactivity;

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
/// <para><b>The sentences are literals.</b> Every other string in this window comes from
/// <c>Languages/*.axaml</c>; a new key here would have to be added to seven files that another change
/// is editing right now, and this is the one screen where the words are not part of the product's
/// vocabulary. They move into the language files with the diagnostics panel's localization.</para>
/// </remarks>
public partial class SurfaceLeaveDialog : Window
{
    /// <summary>Creates the dialog with the findings it is asking about.</summary>
    /// <param name="findings">What the validator said about the card, one line each.</param>
    public SurfaceLeaveDialog(string findings)
    {
        InitializeComponent();

        SurfaceLeaveMessage.Text = findings;
    }

    /// <summary>Parameterless constructor for the XAML loader.</summary>
    public SurfaceLeaveDialog()
    {
        InitializeComponent();
    }

    private void OnLeave(object? sender, RoutedEventArgs e) => Close(true);

    private void OnFix(object? sender, RoutedEventArgs e) => Close(false);
}
