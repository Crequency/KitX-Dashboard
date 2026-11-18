using Avalonia.Controls;
using Avalonia.Interactivity;
using KitX.Dashboard.ViewModels;

namespace KitX.Dashboard.Views;

/// <summary>
/// Asks whether to take every element off the card, saying how many there are.
/// </summary>
/// <remarks>
/// <para><b>Why a dialog at all.</b> Emptying the canvas is one click, it takes the whole layout
/// with it, and the button sits next to Save in a toolbar where the two are eight pixels apart. The
/// window exists to make that click reversible up to the moment of confirming — the same argument as
/// <see cref="DeleteToolkitDialog"/>, and the same shape, because a second dialog design would be a
/// second thing to keep true.</para>
///
/// <para><b>Default is cancel.</b> Escape closes with <see langword="false"/> through
/// <see cref="Window"/>'s own behaviour for a modal, and the cancel button is first in the markup so
/// it also wins the first tab stop. A dialog whose Enter key empties the card is a dialog that turns
/// a stray keypress into lost work.</para>
///
/// <para><b>What it does not claim.</b> The sentence says the file keeps what it has until the next
/// save. That is true and it is the whole of the reassurance: there is no undo for a save, and a
/// dialog that implied one would be describing software that does not exist.</para>
/// </remarks>
public partial class ClearSurfaceDialog : Window
{
    private readonly int _count;

    /// <summary>Creates the dialog for a canvas holding <paramref name="count"/> elements.</summary>
    /// <param name="count">How many elements are on the canvas right now.</param>
    public ClearSurfaceDialog(int count)
    {
        InitializeComponent();
        _count = count;

        ApplyLanguage();
        ClearSurfaceMessage.Text = ClearSurfaceText.Compose(_count);
    }

    /// <summary>Parameterless constructor for the XAML loader.</summary>
    public ClearSurfaceDialog()
    {
        InitializeComponent();
        _count = 0;

        ApplyLanguage();
        ClearSurfaceMessage.Text = ClearSurfaceText.Compose(_count);
    }

    /// <summary>
    /// Puts the dictionary's words on the window. Called from both constructors because the XAML
    /// loader builds the parameterless one first, and a window that briefly shows no title is still a
    /// window somebody can see.
    /// </summary>
    private void ApplyLanguage()
    {
        Title = ClearSurfaceText.Title();
        ClearSurfaceConfirm.Content = ClearSurfaceText.Confirm();
        ClearSurfaceCancel.Content = ClearSurfaceText.Cancel();
    }

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
