using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using KitX.Core.Contract.Workflow;
using KitX.Dashboard.ViewModels;

namespace KitX.Dashboard.Services;

/// <summary>
/// Bridge implementation connecting BlueprintEditor to WorkflowEditor.
/// All UI-affecting operations are dispatched to the UI thread.
/// </summary>
internal class WorkflowEditorBridge : IWorkflowEditorBridge
{
    private readonly WorkflowScriptEditorWindowViewModel _viewModel;
    private readonly Action<string> _updateCodeEditor;
    private readonly Action _triggerExecution;

    /// <summary>
    /// Creates a new WorkflowEditorBridge
    /// </summary>
    /// <param name="viewModel">The WorkflowEditor's ViewModel</param>
    /// <param name="updateCodeEditor">Action to update the code editor UI (must set codeEditor.Text)</param>
    /// <param name="triggerExecution">Action to trigger code execution in WorkflowEditor</param>
    public WorkflowEditorBridge(
        WorkflowScriptEditorWindowViewModel viewModel,
        Action<string> updateCodeEditor,
        Action triggerExecution)
    {
        _viewModel = viewModel;
        _updateCodeEditor = updateCodeEditor;
        _triggerExecution = triggerExecution;
    }

    /// <inheritdoc/>
    public string? GetCurrentScript()
    {
        return _viewModel.MainProgramCode;
    }

    /// <inheritdoc/>
    public List<HelperFunction>? GetHelperFunctions()
    {
        return _viewModel.HelperFunctions?.ToList();
    }

    /// <inheritdoc/>
    public void SetScript(string sourceCode, List<HelperFunction>? helpers)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _viewModel.MainProgramCode = sourceCode;
            _updateCodeEditor(sourceCode);

            if (helpers != null)
            {
                _viewModel.HelperFunctions.Clear();
                foreach (var func in helpers)
                {
                    _viewModel.HelperFunctions.Add(func);
                }
            }
        });
    }

    /// <inheritdoc/>
    public void AppendOutput(string output)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _viewModel.ExecutionResult += output + "\n";
        });
    }

    /// <inheritdoc/>
    public void TriggerExecution()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _triggerExecution();
        });
    }
}
