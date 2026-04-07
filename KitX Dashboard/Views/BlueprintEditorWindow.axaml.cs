using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using KitX.Core.Contract.Workflow;
using KitX.Dashboard.ViewModels;
using Serilog;

namespace KitX.Dashboard.Views;

public partial class BlueprintEditorWindow : Window, IView
{
    private readonly BlueprintEditorViewModel _viewModel;
    private string? _pendingSourceCode;
    private List<HelperFunction>? _pendingHelperFunctions;

    public BlueprintEditorWindow()
    {
        InitializeComponent();

        // Use DI to get the ViewModel
        _viewModel = App.GetService<BlueprintEditorViewModel>();
        DataContext = _viewModel;

        Loaded += OnLoaded;
    }

    /// <summary>
    /// Sets the BlockScript source code to import when the window loads
    /// </summary>
    public void SetSourceCode(string sourceCode, List<HelperFunction>? helperFunctions = null)
    {
        _pendingSourceCode = sourceCode;
        _pendingHelperFunctions = helperFunctions;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        // If there's pending source code, import it
        if (!string.IsNullOrEmpty(_pendingSourceCode))
        {
            var sourceCode = _pendingSourceCode;
            var helpers = _pendingHelperFunctions;
            _pendingSourceCode = null;
            _pendingHelperFunctions = null;

            Dispatcher.UIThread.Post(async () =>
            {
                await _viewModel.ImportFromBlockScriptCommand.ExecuteAsync((sourceCode, helpers));
            });
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            // Don't intercept Delete when typing in a TextBox
            if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox)
            {
                base.OnKeyDown(e);
                return;
            }

            if (_viewModel.DeleteSelectedNodesCommand.CanExecute(null))
            {
                _viewModel.DeleteSelectedNodesCommand.Execute(null);
                e.Handled = true;
            }
        }
        base.OnKeyDown(e);
    }
}
