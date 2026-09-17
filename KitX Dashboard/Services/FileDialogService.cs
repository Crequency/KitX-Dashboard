using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace KitX.Dashboard.Services;

/// <summary>
/// Avalonia implementation of file dialog service using modern IStorageProvider API.
/// For text input/output dialogs, uses simple inline window construction.
/// </summary>
public class FileDialogService : IFileDialogService
{
    public async Task<string?> ShowOpenDialogAsync(string title, List<FileDialogFilter>? filters = null)
    {
        var window = GetMainWindow();
        if (window == null) return null;

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        };

        if (filters != null)
        {
            options.FileTypeFilter = filters.Select(f => new FilePickerFileType(f.Name)
            {
                Patterns = f.Extensions.Select(ext => $"*.{ext}").ToList()
            }).ToList();
        }

        var result = await window.StorageProvider.OpenFilePickerAsync(options);
        return result.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> ShowSaveDialogAsync(string title, string defaultExtension, List<FileDialogFilter>? filters = null, string? suggestedFileName = null)
    {
        var window = GetMainWindow();
        if (window == null) return null;

        var options = new FilePickerSaveOptions
        {
            Title = title,
            DefaultExtension = defaultExtension,
            SuggestedFileName = suggestedFileName
        };

        if (filters != null)
        {
            options.FileTypeChoices = filters.Select(f => new FilePickerFileType(f.Name)
            {
                Patterns = f.Extensions.Select(ext => $"*.{ext}").ToList()
            }).ToList();
        }

        var result = await window.StorageProvider.SaveFilePickerAsync(options);
        return result?.TryGetLocalPath();
    }

    public async Task<string?> ShowTextInputDialogAsync(string title, string prompt, string? initialText = null)
    {
        var owner = GetMainWindow();
        if (owner == null) return null;

        var tcs = new TaskCompletionSource<string?>();

        var dialog = new Window
        {
            Title = title,
            Width = 500,
            Height = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var textBox = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 300,
            Margin = new Thickness(10),
            Text = initialText ?? string.Empty
        };

        var cancelButton = new Button { Content = "Cancel", Width = 80, Margin = new Thickness(0, 0, 10, 0) };
        var okButton = new Button { Content = "Import", Width = 80 };

        cancelButton.Click += (_, _) =>
        {
            tcs.TrySetResult(null);
            dialog.Close();
        };
        okButton.Click += (_, _) =>
        {
            tcs.TrySetResult(textBox.Text);
            dialog.Close();
        };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(10, 10, 10, 5) });
        panel.Children.Add(textBox);

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(10, 0, 10, 10),
            Children = { cancelButton, okButton }
        };
        panel.Children.Add(buttonPanel);

        dialog.Content = panel;

        // D3: the title-bar X close button bypasses both buttons — without this the
        // caller would await tcs.Task forever. Resolve with null (same as Cancel).
        dialog.Closed += (_, _) => tcs.TrySetResult(null);

        await dialog.ShowDialog(owner);
        return await tcs.Task;
    }

    public async Task ShowTextOutputDialogAsync(string title, string content)
    {
        var owner = GetMainWindow();
        if (owner == null) return;

        var dialog = new Window
        {
            Title = title,
            Width = 600,
            Height = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var textBox = new TextBox
        {
            Text = content,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas, Courier New"),
            Margin = new Thickness(10)
        };

        var closeButton = new Button
        {
            Content = "Close",
            Width = 80,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(10)
        };
        closeButton.Click += (_, _) => dialog.Close();

        var panel = new StackPanel();
        panel.Children.Add(textBox);
        panel.Children.Add(closeButton);

        dialog.Content = panel;
        await dialog.ShowDialog(owner);
    }

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }
}
