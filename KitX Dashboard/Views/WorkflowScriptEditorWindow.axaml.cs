using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using KitX.Dashboard.ViewModels;
using AvaloniaEdit;
using Avalonia.Interactivity;
using System.IO;
using System.Reactive.Linq;
using KitX.Dashboard.Services;
using TextMateSharp.Grammars;
using AvaloniaEdit.TextMate;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Styling;
using Avalonia.Platform.Storage;

namespace KitX.Dashboard.Views;

public partial class WorkflowScriptEditorWindow : Window, IView
{
    private readonly WorkflowScriptEditorWindowViewModel viewModel = new();

    public WorkflowScriptEditorWindow()
    {
        InitializeComponent();

        DataContext = viewModel;

        Initialize();
    }

    private void Initialize()
    {
        InitializeEditor();

        EventService.ThemeConfigChanged += InitializeEditor;

        var codeEditor = this.FindControl<TextEditor>("CodeEditor");
        var outputEditor = this.FindControl<TextEditor>("OutputEditor");

        if (codeEditor != null)
        {
            viewModel.CodeDocument = codeEditor.Document;
            codeEditor.Text = @"// Workflow Script Example
// You can use KitX plugins directly in your workflow scripts

using System;
using System.Threading.Tasks;

public class WorkflowScript
{
    public static async Task Main()
    {
        // Example usage of plugins
        // var result = await SomePlugin.SomeMethod();
        // Console.WriteLine($""Result: {result}"");
        
        Console.WriteLine(""Hello from workflow script!"");
    }
}";
        }

        var runButton = this.FindControl<Button>("RunButton");
        var stopButton = this.FindControl<Button>("StopButton");
        var saveButton = this.FindControl<Button>("SaveButton");
        var statusText = this.FindControl<TextBlock>("StatusText");

        if (runButton != null)
            runButton.Click += RunButton_Click;

        if (stopButton != null)
            stopButton.Click += StopButton_Click;

        if (saveButton != null)
            saveButton.Click += SaveButton_Click;

        // 使用事件监听替代Observable
        viewModel.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(viewModel.IsExecuting))
            {
                if (runButton != null)
                    runButton.IsEnabled = !viewModel.IsExecuting;

                if (stopButton != null)
                    stopButton.IsEnabled = viewModel.IsExecuting;

                if (statusText != null)
                    statusText.Text = viewModel.IsExecuting ? "Running..." : "Ready";
            }
            else if (e.PropertyName == nameof(viewModel.ExecutionResult))
            {
                if (outputEditor != null)
                    outputEditor.Text = viewModel.ExecutionResult;
            }
        };
    }

    private void InitializeEditor()
    {
        var textEditor = this.FindControl<TextEditor>("CodeEditor");
        var outputEditor = this.FindControl<TextEditor>("OutputEditor");

        SetEditor(textEditor, ".cs");
        SetEditor(outputEditor, ".log");
    }

    private void SetEditor(TextEditor? textEditor, string ext)
    {
        if (textEditor is null)
            return;

        var registryOptions = new RegistryOptions(ActualThemeVariant == ThemeVariant.Light ? ThemeName.LightPlus : ThemeName.DarkPlus);
        var textMateInstallation = textEditor.InstallTextMate(registryOptions);
        textMateInstallation.SetGrammar(registryOptions.GetScopeByLanguageId(registryOptions.GetLanguageByExtension(ext).Id));
    }

    private async void RunButton_Click(object? sender, RoutedEventArgs e)
    {
        if (viewModel.CodeDocument != null)
        {
            viewModel.SubmitCodes(viewModel.CodeDocument);
        }
    }

    private void StopButton_Click(object? sender, RoutedEventArgs e)
    {
        viewModel.CancelExecution();
    }

    private async void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        // 使用新的存储提供者API替代过时的SaveFileDialog
        var saveOptions = new FilePickerSaveOptions
        {
            Title = "Save Workflow Script",
            DefaultExtension = "cs",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("C# Files")
                {
                    Patterns = new[] { "*.cs" }
                },
                new FilePickerFileType("All Files")
                {
                    Patterns = new[] { "*" }
                }
            }
        };

        var result = await topLevel.StorageProvider.SaveFilePickerAsync(saveOptions);
        if (result != null && viewModel.CodeDocument != null)
        {
            await using var stream = await result.OpenWriteAsync();
            using var writer = new StreamWriter(stream);
            await writer.WriteAsync(viewModel.CodeDocument.Text);
        }
    }
}