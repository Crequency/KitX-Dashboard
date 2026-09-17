using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using KitX.Dashboard.Services;

namespace KitX.Dashboard.Views;

/// <summary>Read-only mermaid preview with copy-to-clipboard (Bench UX v2 C18).</summary>
public partial class MermaidExportWindow : Window
{
    public MermaidExportWindow(string mermaidText)
    {
        InitializeComponent();
        MermaidText = mermaidText;
        DataContext = this;
    }

    public string MermaidText { get; }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is not null)
            await Clipboard.SetTextAsync(MermaidText);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
