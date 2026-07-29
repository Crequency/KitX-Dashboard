using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using KitX.Dashboard.ViewModels;
using KitX.WorkflowV6.Lens.BpGraphLens;
using KitX.WorkflowV6.Lens.KsTextLens;
using TextMateSharp.Grammars;

namespace KitX.Dashboard.Views;

// ─────────────────────────────────────────────────────────────────────────────
// WorkflowEditorWindowV6 — code-behind for the v6 editor window.
//
// Wires up:
//   • The ViewModel (constructed from DI-resolved KsTextLens + BpGraphLens).
//   • AvaloniaEdit (KS text editor with syntax highlighting).
//   • Mode-switch synchronization (refresh TextEditor text when switching back to KS).
//
// P1 scope: KS editing + read-only BP rendering. No execution, no debug, no save yet.
// ─────────────────────────────────────────────────────────────────────────────

public partial class WorkflowEditorWindowV6 : Window
{
    private readonly WorkflowEditorViewModelV6 _viewModel;

    public WorkflowEditorWindowV6()
    {
        InitializeComponent();

        // Resolve v6 services from DI. These are registered via AddKitXWorkflowV6()
        // in App.InitializeServiceProvider().
        var ksTextLens = App.GetService<KsTextLens>();
        var bpGraphLens = App.GetService<BpGraphLens>();

        _viewModel = new WorkflowEditorViewModelV6(ksTextLens, bpGraphLens);
        DataContext = _viewModel;

        InitializeEditor();

        // Wire mode switch: when switching back to KS, refresh the TextEditor text.
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Wire theme changes to re-apply syntax highlighting.
        ActualThemeVariantChanged += (_, _) => InitializeEditor();
    }

    /// <summary>
    /// Loads KS source text into the editor after construction.
    /// Call before Show().
    /// </summary>
    public void LoadSource(string ksSource)
    {
        _viewModel.KsSource = ksSource;
        var codeEditor = this.FindControl<TextEditor>("CodeEditor");
        if (codeEditor != null)
            codeEditor.Text = ksSource;
    }

    // ── AvaloniaEdit initialization ──

    private void InitializeEditor()
    {
        var textEditor = this.FindControl<TextEditor>("CodeEditor");
        if (textEditor == null) return;

        // Seed the editor with the default/source text.
        textEditor.Text = _viewModel.KsSource;

        // Apply syntax highlighting. P1 uses C# TextMate grammar as a close
        // approximation (KS shares keywords, string, comment, and number tokens).
        // A dedicated KS grammar will be added in a later phase.
        var registryOptions = new RegistryOptions(
            ActualThemeVariant == ThemeVariant.Light ? ThemeName.LightPlus : ThemeName.DarkPlus
        );
        var installation = textEditor.InstallTextMate(registryOptions);
        installation.SetGrammar(
            registryOptions.GetScopeByLanguageId(registryOptions.GetLanguageByExtension(".cs").Id)
        );

        // Sync text changes back to the ViewModel.
        textEditor.TextChanged += (_, _) =>
        {
            _viewModel.KsSource = textEditor.Document?.Text ?? string.Empty;
        };
    }

    // ── Mode switch synchronization ──

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WorkflowEditorViewModelV6.Mode)) return;
        if (_viewModel.IsBlockScriptMode)
        {
            // Refresh the TextEditor text (the ViewModel may have updated KsSource
            // via BP→KS reverse translation).
            Dispatcher.UIThread.Post(() =>
            {
                var codeEditor = this.FindControl<TextEditor>("CodeEditor");
                if (codeEditor != null && codeEditor.Document?.Text != _viewModel.KsSource)
                {
                    codeEditor.Text = _viewModel.KsSource;
                }
            });
        }
    }
}
