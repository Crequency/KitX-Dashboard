using System;
using Avalonia.Controls;
using AvaloniaEdit;
using KitX.Dashboard.ViewModels;

namespace KitX.Dashboard.Views;

// ─────────────────────────────────────────────────────────────────────────────
// WorkflowEditorWindowV6 — code-behind for the v6-grammar editor window scaffolding.
//
// The code-behind is intentionally minimal: it constructs the placeholder ViewModel,
// seeds the BS code editor with the placeholder source, and wires the ClearOutput
// button. No BS execution, no BP wiring, no helper-function management, no constants
// panel, no debug highlight — all of that arrives with the v6 implementation plan.
//
// To open this window today (it is intentionally not wired into any menu):
//
//     var window = new WorkflowEditorWindowV6();
//     window.Show();
//
// Future: when the v6 implementation lands, WorkflowPageViewModel will gain a
// parallel "Open in v6 Editor" entry (or an ExperimentalFlags toggle) that
// constructs this window with a real workflow session.
// ─────────────────────────────────────────────────────────────────────────────

public partial class WorkflowEditorWindowV6 : Window
{
    private readonly WorkflowEditorViewModelV6 _viewModel;

    public WorkflowEditorWindowV6()
    {
        InitializeComponent();

        _viewModel = new WorkflowEditorViewModelV6();
        DataContext = _viewModel;

        // Seed the placeholder BS source so the editor area is non-empty. The real
        // loader (LoadWorkflowAsync) arrives with the v6 implementation plan.
        var codeEditor = this.FindControl<TextEditor>("CodeEditor");
        if (codeEditor != null)
        {
            codeEditor.Text = _viewModel.PlaceholderSource;
        }
    }
}
