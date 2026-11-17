using Avalonia.Controls;
using Avalonia.Interactivity;
using KitX.Dashboard.ViewModels;

namespace KitX.Dashboard.Views;

/// <summary>
/// Asks whether to delete a ToolKit, naming which one.
/// </summary>
/// <remarks>
/// <para><b>Why a dialog at all.</b> The repository page had a delete button that ran the moment
/// it was clicked. The card sits in a grid where the buttons are next to each other, and a
/// ToolKit's directory is not something the application can put back — so the button needs a
/// second party: the window exists to make the click reversible up to the moment of confirming.</para>
///
/// <para><b>Default is cancel.</b> Escape closes with <see langword="false"/> by
/// <see cref="Window"/>'s own behaviour for a modal, and the cancel button is the first one in the
/// markup so it also wins the first tab stop. A dialog whose Enter key deletes is a dialog that
/// turns a double-click into a deletion.</para>
///
/// <para><b>The sentences come from the dictionary.</b> Title, body, warning and both answers are
/// read through <see cref="DeleteToolkitText"/>, so this window carries no English of its own.</para>
/// </remarks>
public partial class DeleteToolkitDialog : Window
{
    private readonly string _toolkitName;

    /// <summary>Creates the dialog for the ToolKit with the given display name.</summary>
    /// <param name="toolkitName">What the reader will see in the sentence, or empty.</param>
    public DeleteToolkitDialog(string? toolkitName)
    {
        InitializeComponent();
        _toolkitName = toolkitName ?? string.Empty;

        ApplyLanguage();
        DeleteToolkitMessage.Text = DeleteToolkitText.Compose(_toolkitName);
    }

    /// <summary>Parameterless constructor for the XAML loader.</summary>
    public DeleteToolkitDialog()
    {
        InitializeComponent();
        _toolkitName = string.Empty;

        ApplyLanguage();
        DeleteToolkitMessage.Text = DeleteToolkitText.Compose(_toolkitName);
    }

    /// <summary>
    /// Puts the dictionary's words on the window. Called from both constructors because the XAML
    /// loader builds the parameterless one first, and a window that briefly shows no title is still
    /// a window somebody can see.
    /// </summary>
    private void ApplyLanguage()
    {
        Title = DeleteToolkitText.Title();
        DeleteToolkitConfirm.Content = DeleteToolkitText.Confirm();
        DeleteToolkitCancel.Content = DeleteToolkitText.Cancel();
    }

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
