using System.Collections.Generic;
using System.Threading.Tasks;

namespace KitX.Dashboard.Services;

/// <summary>
/// Abstraction for file dialog operations, enabling MVVM-compatible file picking.
/// </summary>
public interface IFileDialogService
{
    /// <summary>
    /// Shows an open file dialog and returns the selected file path, or null if cancelled.
    /// </summary>
    Task<string?> ShowOpenDialogAsync(string title, List<FileDialogFilter>? filters = null);

    /// <summary>
    /// Shows a save file dialog and returns the selected file path, or null if cancelled.
    /// </summary>
    Task<string?> ShowSaveDialogAsync(string title, string defaultExtension, List<FileDialogFilter>? filters = null, string? suggestedFileName = null);

    /// <summary>
    /// Shows a modal dialog with a text input and returns the entered text, or null if cancelled.
    /// </summary>
    Task<string?> ShowTextInputDialogAsync(string title, string prompt, string? initialText = null);

    /// <summary>
    /// Shows a modal dialog displaying read-only text content.
    /// </summary>
    Task ShowTextOutputDialogAsync(string title, string content);
}

/// <summary>
/// Represents a file dialog filter.
/// </summary>
public class FileDialogFilter
{
    public string Name { get; set; } = string.Empty;
    public List<string> Extensions { get; set; } = [];
}
