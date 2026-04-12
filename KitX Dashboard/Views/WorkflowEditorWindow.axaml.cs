using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using KitX.Core.Contract.Event;
using KitX.Core.Contract.Tasks;
using KitX.Core.Contract.Workflow;
using KitX.Core.Event;
using KitX.Dashboard.Controls;
using KitX.Dashboard.Services;
using KitX.Dashboard.ViewModels;
using TextMateSharp.Grammars;
using AvaloniaEdit.TextMate;
using Avalonia.Styling;

namespace KitX.Dashboard.Views;

public partial class WorkflowEditorWindow : Window, IView
{
    private readonly WorkflowEditorViewModel _viewModel;
    private bool _isEditingHelperFunction = false;
    private CancellationTokenSource? _debounceCts;
    private CancellationTokenSource? _autoSaveCts;
    private Popup? _contextPopup;
    private string _workflowId = string.Empty;

    private static string? GetResource(string key) =>
        Application.Current?.TryFindResource(key, out var v) == true ? v as string : null;

    public WorkflowEditorWindow()
    {
        InitializeComponent();

        var scriptVM = App.GetService<WorkflowScriptEditorWindowViewModel>();
        var blueprintVM = App.GetService<BlueprintEditorViewModel>();

        _viewModel = new WorkflowEditorViewModel(
            App.GetService<IWorkflowStorageService>(),
            App.GetService<IBlueprintService>(),
            App.GetService<ITasksService>(),
            scriptVM,
            blueprintVM
        );

        DataContext = _viewModel;

        Initialize();
    }

    /// <summary>
    /// Loads a workflow by ID after construction.
    /// Call before Show().
    /// </summary>
    public async Task LoadWorkflowAsync(string workflowId)
    {
        _workflowId = workflowId;
        await _viewModel.LoadWorkflowAsync(workflowId);

        // Update the code editor with loaded content
        var codeEditor = this.FindControl<TextEditor>("CodeEditor");
        if (codeEditor != null)
        {
            codeEditor.Text = _viewModel.ScriptVM.MainProgramCode ?? string.Empty;
        }
    }

    private void Initialize()
    {
        InitializeEditor();

        var eventService = App.GetService<IEventService>();
        eventService.Subscribe(EventNames.ThemeConfigChanged, (s, e) => InitializeEditor());

        WireUpCodeEditor();
        WireUpHelperFunctions();
        WireUpConstants();
        WireUpRunStop();
        WireUpOutput();
        WireUpBPContextMenu();
        WireUpModeSwitch();

        // Auto-save on window closing
        Closing += OnWindowClosing;
    }

    #region AvaloniaEdit Initialization

    private void InitializeEditor()
    {
        var textEditor = this.FindControl<TextEditor>("CodeEditor");
        SetEditorSyntax(textEditor, ".cs");
    }

    private void SetEditorSyntax(TextEditor? textEditor, string ext)
    {
        if (textEditor is null) return;

        var registryOptions = new RegistryOptions(
            ActualThemeVariant == ThemeVariant.Light ? ThemeName.LightPlus : ThemeName.DarkPlus
        );
        var textMateInstallation = textEditor.InstallTextMate(registryOptions);
        textMateInstallation.SetGrammar(
            registryOptions.GetScopeByLanguageId(registryOptions.GetLanguageByExtension(ext).Id)
        );
    }

    #endregion

    #region Code Editor Wiring

    private void WireUpCodeEditor()
    {
        var codeEditor = this.FindControl<TextEditor>("CodeEditor");
        var constantsItemsControl = this.FindControl<ItemsControl>("ConstantsItemsControl");
        if (codeEditor == null) return;

        _viewModel.ScriptVM.CodeDocument = codeEditor.Document;

        codeEditor.TextChanged += (s, e) =>
        {
            if (codeEditor.Document == null) return;

            // Helper function editing: sync immediately
            if (_isEditingHelperFunction)
            {
                if (_viewModel.ScriptVM.SelectedHelperFunction != null)
                {
                    _viewModel.ScriptVM.SelectedHelperFunction.Code = codeEditor.Document.Text;
                }
                return;
            }

            // Main program editing: debounce parse + auto-save
            _viewModel.ScriptVM.MainProgramCode = codeEditor.Document.Text;
            _viewModel.IsDirty = true;

            // Debounced constant parsing
            _debounceCts?.Cancel();
            _debounceCts = new CancellationTokenSource();
            var token = _debounceCts.Token;

            _ = Task.Delay(500, token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (codeEditor.Document == null) return;
                    _viewModel.ScriptVM.ParseConstantsFromCode(codeEditor.Document.Text);
                    if (constantsItemsControl != null)
                        constantsItemsControl.ItemsSource = _viewModel.ScriptVM.VariableConstants;
                });
            }, token);

            // Auto-save debounce (3 seconds)
            ScheduleAutoSave();
        };
    }

    #endregion

    #region Auto-Save

    private void ScheduleAutoSave()
    {
        _autoSaveCts?.Cancel();
        _autoSaveCts = new CancellationTokenSource();
        var token = _autoSaveCts.Token;

        _ = Task.Delay(3000, token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (_viewModel.IsDirty)
                    await _viewModel.SaveAsync();
            });
        }, token);
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        _autoSaveCts?.Cancel();

        if (_viewModel.IsDirty)
        {
            await _viewModel.SaveAsync();
        }
    }

    #endregion

    #region Helper Functions Wiring

    private void WireUpHelperFunctions()
    {
        var helperFunctionsListBox = this.FindControl<ListBox>("HelperFunctionsListBox");
        if (helperFunctionsListBox != null)
        {
            helperFunctionsListBox.ItemsSource = _viewModel.ScriptVM.HelperFunctions;
            helperFunctionsListBox.SelectionChanged += OnHelperFunctionSelected;
        }

        var addBtn = this.FindControl<Button>("AddHelperFunctionListButton");
        if (addBtn != null)
            addBtn.Click += OnAddHelperFunction;

        var backBtn = this.FindControl<Button>("BackToMainProgramButton");
        if (backBtn != null)
            backBtn.Click += OnBackToMainProgram;

        // Wire remove buttons via event bubbling (buttons in DataTemplate use Tag)
        AddHandler(Button.ClickEvent, OnHelperFunctionButtonClick);
    }

    private void OnAddHelperFunction(object? sender, RoutedEventArgs e)
    {
        _viewModel.ScriptVM.AddHelperFunctionCommand?.Execute().Subscribe();

        var helperFunctionsListBox = this.FindControl<ListBox>("HelperFunctionsListBox");
        if (helperFunctionsListBox != null)
        {
            helperFunctionsListBox.ItemsSource = null;
            helperFunctionsListBox.ItemsSource = _viewModel.ScriptVM.HelperFunctions;

            if (_viewModel.ScriptVM.SelectedHelperFunction != null)
                helperFunctionsListBox.SelectedItem = _viewModel.ScriptVM.SelectedHelperFunction;
        }
    }

    private void OnHelperFunctionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.SelectedItem is HelperFunction selectedFunction)
        {
            _viewModel.ScriptVM.SelectedHelperFunction = selectedFunction;
            _isEditingHelperFunction = true;

            var codeEditor = this.FindControl<TextEditor>("CodeEditor");
            var codeEditorTitle = this.FindControl<TextBlock>("CodeEditorTitle");

            if (codeEditor != null)
                codeEditor.Text = selectedFunction.Code;

            if (codeEditorTitle != null)
                codeEditorTitle.Text = (GetResource("Text_WorkflowEditor_HelperFunctionTitle") ?? "Helper Function: {0}")
                    .Replace("$name", selectedFunction.Name)
                    .Replace("{0}", selectedFunction.Name);
        }
    }

    private void OnBackToMainProgram(object? sender, RoutedEventArgs e)
    {
        _isEditingHelperFunction = false;
        _viewModel.ScriptVM.SelectedHelperFunction = null;

        var codeEditor = this.FindControl<TextEditor>("CodeEditor");
        var codeEditorTitle = this.FindControl<TextBlock>("CodeEditorTitle");

        if (codeEditor != null)
            codeEditor.Text = _viewModel.ScriptVM.MainProgramCode ?? string.Empty;

        if (codeEditorTitle != null)
            codeEditorTitle.Text = GetResource("Text_WorkflowEditor_MainProgram") ?? "Main Program";

        var helperFunctionsListBox = this.FindControl<ListBox>("HelperFunctionsListBox");
        if (helperFunctionsListBox != null)
            helperFunctionsListBox.SelectedItem = null;
    }

    private void OnHelperFunctionButtonClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button button) return;

        // Remove helper function button (Tag holds the HelperFunction)
        if (button.Tag is HelperFunction function)
        {
            // Only handle if this button's parent is the helper function list
            var parent = button.GetVisualParent();
            while (parent != null)
            {
                if (parent is ListBox) break;
                parent = parent.GetVisualParent();
            }
            if (parent == null) return;

            // Check if this is within the helper function list by checking the button's content
            if (button.Content?.ToString() == "X")
            {
                _viewModel.ScriptVM.RemoveHelperFunctionCommand?.Execute(function).Subscribe();

                var helperFunctionsListBox = this.FindControl<ListBox>("HelperFunctionsListBox");
                if (helperFunctionsListBox != null)
                {
                    helperFunctionsListBox.ItemsSource = null;
                    helperFunctionsListBox.ItemsSource = _viewModel.ScriptVM.HelperFunctions;
                }

                if (_viewModel.ScriptVM.SelectedHelperFunction == null)
                {
                    _isEditingHelperFunction = false;
                    var codeEditor = this.FindControl<TextEditor>("CodeEditor");
                    var codeEditorTitle = this.FindControl<TextBlock>("CodeEditorTitle");
                    if (codeEditor != null)
                        codeEditor.Text = _viewModel.ScriptVM.MainProgramCode ?? string.Empty;
                    if (codeEditorTitle != null)
                        codeEditorTitle.Text = GetResource("Text_WorkflowEditor_MainProgram") ?? "Main Program";
                }
            }
        }
    }

    #endregion

    #region Constants Panel Wiring

    private void WireUpConstants()
    {
        var constantsItemsControl = this.FindControl<ItemsControl>("ConstantsItemsControl");
        if (constantsItemsControl != null)
            constantsItemsControl.ItemsSource = _viewModel.ScriptVM.VariableConstants;

        var resetAllBtn = this.FindControl<Button>("ResetAllConstantsButton");
        if (resetAllBtn != null)
            resetAllBtn.Click += OnResetAllConstants;

        var addParamBtn = this.FindControl<Button>("AddParameterButton");
        if (addParamBtn != null)
            addParamBtn.Click += OnAddParameter;

        // Wire reset/remove via event bubbling
        AddHandler(Button.ClickEvent, OnConstantsButtonClick);
    }

    private void OnResetAllConstants(object? sender, RoutedEventArgs e)
    {
        _viewModel.ScriptVM.ResetAllConstantsCommand?.Execute().Subscribe();
        var constantsItemsControl = this.FindControl<ItemsControl>("ConstantsItemsControl");
        if (constantsItemsControl != null)
        {
            constantsItemsControl.ItemsSource = null;
            constantsItemsControl.ItemsSource = _viewModel.ScriptVM.VariableConstants;
        }
    }

    private void OnAddParameter(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.ScriptVM.SelectedHelperFunction != null)
        {
            var newParam = new HelperFunctionParameter
            {
                Name = $"param{_viewModel.ScriptVM.SelectedHelperFunction.Parameters.Count + 1}",
                Type = "object"
            };
            _viewModel.ScriptVM.Parameters.Add(newParam);
            _viewModel.ScriptVM.SelectedHelperFunction.Parameters.Add(newParam);
        }
    }

    private void OnConstantsButtonClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button button) return;

        // Reset constant button (in constants panel)
        if (button.Tag is VariableConstant constant && button.Content?.ToString() == "R")
        {
            _viewModel.ScriptVM.ResetConstantCommand?.Execute(constant).Subscribe();
            var constantsItemsControl = this.FindControl<ItemsControl>("ConstantsItemsControl");
            if (constantsItemsControl != null)
            {
                constantsItemsControl.ItemsSource = null;
                constantsItemsControl.ItemsSource = _viewModel.ScriptVM.VariableConstants;
            }
        }
        // Remove parameter button (in helper function settings)
        else if (button.Tag is HelperFunctionParameter param && button.Content?.ToString() == "X")
        {
            if (_viewModel.ScriptVM.SelectedHelperFunction != null)
            {
                _viewModel.ScriptVM.Parameters.Remove(param);
                _viewModel.ScriptVM.SelectedHelperFunction.Parameters.Remove(param);
            }
        }
    }

    #endregion

    #region Run/Stop Wiring

    private void WireUpRunStop()
    {
        var runButton = this.FindControl<Button>("RunButton");
        var stopButton = this.FindControl<Button>("StopButton");

        if (runButton != null)
            runButton.Click += OnRun;

        if (stopButton != null)
            stopButton.Click += OnStop;
    }

    private async void OnRun(object? sender, RoutedEventArgs e)
    {
        var codeEditor = this.FindControl<TextEditor>("CodeEditor");

        // Save helper function code if currently editing one
        if (_isEditingHelperFunction && _viewModel.ScriptVM.SelectedHelperFunction != null)
            _viewModel.ScriptVM.SelectedHelperFunction.Code = _viewModel.ScriptVM.CodeDocument?.Text
                ?? _viewModel.ScriptVM.SelectedHelperFunction.Code;

        // Switch back to main program
        _isEditingHelperFunction = false;
        _viewModel.ScriptVM.SelectedHelperFunction = null;

        if (codeEditor != null)
            codeEditor.Text = _viewModel.ScriptVM.MainProgramCode ?? string.Empty;

        var codeEditorTitle = this.FindControl<TextBlock>("CodeEditorTitle");
        if (codeEditorTitle != null)
            codeEditorTitle.Text = GetResource("Text_WorkflowEditor_MainProgram") ?? "Main Program";

        await Task.Delay(50);

        if (_viewModel.ScriptVM.CodeDocument != null)
            _viewModel.ScriptVM.SubmitCodes(_viewModel.ScriptVM.CodeDocument);
    }

    private void OnStop(object? sender, RoutedEventArgs e)
    {
        _viewModel.ScriptVM.CancelExecution();
    }

    #endregion

    #region Output + Status Wiring

    private void WireUpOutput()
    {
        _viewModel.ScriptVM.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(_viewModel.ScriptVM.IsExecuting))
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var statusText = this.FindControl<TextBlock>("StatusText");
                    if (statusText != null)
                        statusText.Text = _viewModel.ScriptVM.IsExecuting
                            ? (GetResource("Text_WorkflowEditor_Running") ?? "Running...")
                            : (GetResource("Text_WorkflowEditor_Ready") ?? "Ready");
                });
            }
        };

        _viewModel.BlueprintVM.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(_viewModel.BlueprintVM.NodeCount)
                || e.PropertyName == nameof(_viewModel.BlueprintVM.ConnectionCount))
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var nodeCountText = this.FindControl<TextBlock>("NodeCountText");
                    if (nodeCountText != null)
                    {
                        nodeCountText.Text = (GetResource("Text_WorkflowEditor_NodesConnectionsFormat")
                            ?? "Nodes: $nodes  Connections: $connections")
                            .Replace("$nodes", _viewModel.BlueprintVM.NodeCount.ToString())
                            .Replace("$connections", _viewModel.BlueprintVM.ConnectionCount.ToString());
                    }
                });
            }
        };
    }

    #endregion

    #region Blueprint Context Menu

    private void WireUpBPContextMenu()
    {
        Loaded += (s, e) =>
        {
            var editor = this.FindControl<NodifyM.Avalonia.Controls.NodifyEditor>("EditorControl");
            if (editor != null)
                editor.ContextRequested += OnEditorContextRequested;
        };
    }

    #endregion

    #region Mode Switch Wiring

    /// <summary>
    /// When mode switches from BP→BS, the converted code is set on ScriptVM.MainProgramCode
    /// but the AvaloniaEdit TextEditor needs to be refreshed explicitly (no reverse binding).
    /// </summary>
    private void WireUpModeSwitch()
    {
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(_viewModel.Mode) && _viewModel.IsBlockScriptMode)
            {
                var codeEditor = this.FindControl<TextEditor>("CodeEditor");
                var codeEditorTitle = this.FindControl<TextBlock>("CodeEditorTitle");
                var constantsItemsControl = this.FindControl<ItemsControl>("ConstantsItemsControl");

                if (codeEditor != null)
                {
                    _isEditingHelperFunction = false;
                    codeEditor.Text = _viewModel.ScriptVM.MainProgramCode ?? string.Empty;
                }

                if (codeEditorTitle != null)
                    codeEditorTitle.Text = GetResource("Text_WorkflowEditor_MainProgram") ?? "Main Program";

                // Refresh constants display from the newly converted code
                if (codeEditor?.Document != null)
                {
                    _viewModel.ScriptVM.ParseConstantsFromCode(codeEditor.Document.Text);
                    if (constantsItemsControl != null)
                        constantsItemsControl.ItemsSource = _viewModel.ScriptVM.VariableConstants;
                }
            }
        };
    }

    #endregion

    #region Blueprint Context Menu

    private void OnEditorContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not NodifyM.Avalonia.Controls.NodifyEditor editor) return;
        if (e.Source is not Visual visual) return;

        var baseNode = FindAncestor<NodifyM.Avalonia.Controls.BaseNode>(visual);
        var scopeBlock = FindAncestor<ScopeBlockControl>(visual);

        if (baseNode == null && scopeBlock == null) return;

        e.Handled = true;
        CloseContextPopup();

        var bpVM = _viewModel.BlueprintVM;

        // Scope block context menu
        if (scopeBlock != null && scopeBlock.DataContext is BlueprintScopeBlockVM scopeVm)
        {
            editor.SelectItem(scopeBlock, false);
            var panel = CreateMenuPanel();
            panel.Children.Add(CreateMenuButton(
                GetResource("Text_WorkflowEditor_Rename") ?? "Rename", bpVM.RenameScopeBlockCommand, scopeVm));
            panel.Children.Add(CreateSeparator());
            panel.Children.Add(CreateMenuButton(
                GetResource("Text_WorkflowEditor_Delete") ?? "Delete", bpVM.DeleteSelectedNodesCommand, null));
            ShowContextPopup(panel);
            return;
        }

        // Regular node context menu
        if (baseNode == null) return;
        if (baseNode.DataContext is not BlueprintNodeVM nodeVm) return;

        editor.SelectItem(baseNode, false);
        var nodePanel = CreateMenuPanel();
        nodePanel.Children.Add(CreateMenuButton(
            GetResource("Text_WorkflowEditor_Delete") ?? "Delete", bpVM.DeleteSelectedNodesCommand, null));
        nodePanel.Children.Add(CreateSeparator());

        if (bpVM.ScopeBlocks.Count > 0)
        {
            nodePanel.Children.Add(new TextBlock
            {
                Text = GetResource("Text_WorkflowEditor_MoveToScope") ?? "Move to Scope:",
                Foreground = Avalonia.Media.Brush.Parse("#999999"),
                FontSize = 11,
                Margin = new Thickness(8, 4, 8, 2),
            });

            foreach (var scope in bpVM.ScopeBlocks)
            {
                nodePanel.Children.Add(CreateMenuButton(
                    $"  {scope.DisplayName} ({scope.ContainedNodeIds.Count} nodes)",
                    bpVM.MoveSelectedNodesToScopeCommand,
                    scope.ScopeId
                ));
            }
        }

        nodePanel.Children.Add(CreateSeparator());
        nodePanel.Children.Add(CreateMenuButton(
            GetResource("Text_WorkflowEditor_RemoveFromScope") ?? "Remove from Scope", bpVM.RemoveSelectedNodesFromScopeCommand, null));

        if (nodeVm.NodeType is BlueprintNodeType.Const
            or BlueprintNodeType.Variable
            or BlueprintNodeType.Get
            or BlueprintNodeType.Set)
        {
            nodePanel.Children.Add(CreateSeparator());
            nodePanel.Children.Add(CreateMenuButton(
                GetResource("Text_WorkflowEditor_Rename") ?? "Rename", bpVM.RenameSelectedNodeCommand, nodeVm));
        }

        ShowContextPopup(nodePanel);
    }

    private static StackPanel CreateMenuPanel() => new()
    {
        Background = Avalonia.Media.Brush.Parse("#2D2D2D"),
        MinWidth = 180,
    };

    private static Border CreateSeparator() => new()
    {
        Height = 1,
        Background = Avalonia.Media.Brush.Parse("#444444"),
        Margin = new Thickness(4, 2),
    };

    private Button CreateMenuButton(string text, System.Windows.Input.ICommand command, object? parameter)
    {
        return new Button
        {
            Content = text,
            Command = command,
            CommandParameter = parameter,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            Padding = new Thickness(8, 4),
            Background = Avalonia.Media.Brushes.Transparent,
            Foreground = Avalonia.Media.Brushes.White,
            FontSize = 13,
        };
    }

    private void ShowContextPopup(StackPanel panel)
    {
        var border = new Border
        {
            Background = Avalonia.Media.Brush.Parse("#2D2D2D"),
            BorderBrush = Avalonia.Media.Brush.Parse("#555555"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(2),
            Child = panel,
            BoxShadow = new Avalonia.Media.BoxShadows(
                new Avalonia.Media.BoxShadow
                {
                    Color = Avalonia.Media.Color.FromArgb(80, 0, 0, 0),
                    OffsetX = 2, OffsetY = 2, Blur = 8
                }
            ),
        };

        _contextPopup = new Popup
        {
            Placement = PlacementMode.Pointer,
            PlacementTarget = this,
            IsLightDismissEnabled = true,
            Child = border,
        };

        ((ISetLogicalParent)_contextPopup).SetParent(this);
        _contextPopup.Open();
    }

    private void CloseContextPopup()
    {
        if (_contextPopup != null)
        {
            _contextPopup.Close();
            ((ISetLogicalParent)_contextPopup).SetParent(null);
            _contextPopup = null;
        }
    }

    private static T? FindAncestor<T>(Visual visual) where T : Visual
    {
        var current = visual.GetVisualParent();
        while (current != null)
        {
            if (current is T result) return result;
            current = current.GetVisualParent();
        }
        return null;
    }

    #endregion

    #region Keyboard Shortcuts

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

            // Only handle in BP mode
            if (_viewModel.IsBlueprintMode)
            {
                var bpVM = _viewModel.BlueprintVM;
                if (bpVM.DeleteSelectedNodesCommand.CanExecute(null))
                {
                    bpVM.DeleteSelectedNodesCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }

        // Ctrl+S to save
        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _ = _viewModel.SaveAsync();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    #endregion

    protected override void OnClosed(EventArgs e)
    {
        _autoSaveCts?.Cancel();
        _viewModel.BlueprintVM.Cleanup();
        base.OnClosed(e);
    }
}
