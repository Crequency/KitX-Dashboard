using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using KitX.Core.Contract.Workflow;
using KitX.Dashboard.ViewModels;
using KitX.WorkflowV6.Lens.BpGraphLens;
using KitX.WorkflowV6.Lens.KsTextLens;
using KitX.WorkflowV6.Serialization;
using Serilog;
using TextMateSharp.Grammars;
using V6Workflow = KitX.WorkflowV6.Ir.Workflow;

namespace KitX.Dashboard.Views;

// ─────────────────────────────────────────────────────────────────────────────
// WorkflowEditorWindowV6 — code-behind for the v6 editor window.
//
// Wires up the full KS-side feature set (mirrors v5.1 WorkflowEditorWindow):
//   • AvaloniaEdit (KS text editor with syntax highlighting).
//   • Helper Function list (add/remove/select/back-to-main).
//   • Variable Constants panel (reset/reset-all, debounced parse).
//   • Helper Function settings (return type, parameters add/remove).
//   • Mode-switch synchronization (refresh TextEditor on BP→KS).
//   • Save (Ctrl+S) and auto-save on window closing.
//   • LoadWorkflowAsync (from .kcs by workflow ID).
// ─────────────────────────────────────────────────────────────────────────────

public partial class WorkflowEditorWindowV6 : Window
{
    private readonly WorkflowEditorViewModelV6 _viewModel;
    private bool _isEditingHelperFunction = false;
    private CancellationTokenSource? _debounceCts;
    private CancellationTokenSource? _autoSaveCts;

    public WorkflowEditorWindowV6()
    {
        InitializeComponent();

        var ksTextLens = App.GetService<KsTextLens>();
        var bpGraphLens = App.GetService<BpGraphLens>();

        _viewModel = new WorkflowEditorViewModelV6(ksTextLens, bpGraphLens);
        DataContext = _viewModel;

        InitializeEditor();
        WireUpCodeEditor();
        WireUpHelperFunctions();
        WireUpConstants();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ActualThemeVariantChanged += (_, _) => InitializeEditor();

        Closing += OnWindowClosing;
    }

    // ── Load workflow ──

    public async Task LoadWorkflowAsync(string workflowId)
    {
        var storage = App.GetService<KitX.Core.Contract.Workflow.IWorkflowStorageService>();
        var kcs = await storage.LoadWorkflowDataAsync(workflowId);
        if (kcs == null)
        {
            _viewModel.StatusText = $"Workflow not found: {workflowId}";
            return;
        }
        if (kcs.IrVersion != "v6")
        {
            _viewModel.StatusText = $"Not a v6 workflow (IrVersion={kcs.IrVersion ?? "null"})";
            return;
        }

        try
        {
            var ir = WorkflowSerializer.Deserialize(kcs.IrData);
            _viewModel.SetWorkflowId(workflowId);
            _viewModel.LoadFromIr(ir, kcs.Name, kcs);

            var codeEditor = this.FindControl<TextEditor>("CodeEditor");
            if (codeEditor != null)
            {
                Log.Information("[WorkflowEditorWindowV6] LoadWorkflowAsync: setting codeEditor.Text to {Length} chars", _viewModel.KsSource.Length);
                codeEditor.Text = _viewModel.KsSource;
            }

            var constantsItemsControl = this.FindControl<ItemsControl>("ConstantsItemsControl");
            if (constantsItemsControl != null)
                constantsItemsControl.ItemsSource = _viewModel.VariableConstants;
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"Failed to load v6 IR: {ex.Message}";
        }
    }

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

        Log.Information("[WorkflowEditorWindowV6] InitializeEditor: setting Text to {Length} chars", _viewModel.KsSource.Length);
        textEditor.Text = _viewModel.KsSource;

        // KS grammar requires 4-space indents and forbids Tab (KS001).
        // Configure the editor so pressing Tab inserts 4 spaces instead of a Tab char.
        try
        {
            textEditor.Options = new AvaloniaEdit.TextEditorOptions
            {
                ConvertTabsToSpaces = true,
                IndentationSize = 4,
            };
        }
        catch
        {
            // Older AvaloniaEdit API surface — non-fatal.
        }

        // P5-C2: load the dedicated KS TextMate grammar (Assets/TextMate/ks) when present,
        // falling back to the C# grammar approximation. The resources are copied to the
        // output directory via the csproj Assets/** CopyToOutputDirectory rule.
        var registryOptions = new RegistryOptions(
            ActualThemeVariant == ThemeVariant.Light ? ThemeName.LightPlus : ThemeName.DarkPlus
        );
        var installation = textEditor.InstallTextMate(registryOptions);
        try
        {
            var ksPackage = new System.IO.FileInfo(
                System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "TextMate", "ks", "package.json"));
            if (ksPackage.Exists)
            {
                registryOptions.LoadFromLocalFile("ks", ksPackage, overwrite: true);
                installation.SetGrammar(registryOptions.GetScopeByLanguageId("ks"));
                Log.Information("[WorkflowEditorWindowV6] KS grammar loaded from {Path}", ksPackage.FullName);
                return;
            }
            Log.Warning("[WorkflowEditorWindowV6] KS grammar package not found at {Path}, falling back to C#", ksPackage.FullName);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[WorkflowEditorWindowV6] Failed to load KS grammar, falling back to C#");
        }
        installation.SetGrammar(
            registryOptions.GetScopeByLanguageId(registryOptions.GetLanguageByExtension(".cs").Id)
        );

        textEditor.TextChanged += (_, _) =>
        {
            if (_isEditingHelperFunction) return;
            _viewModel.KsSource = textEditor.Document?.Text ?? string.Empty;
        };
    }

    // ── Code editor wiring (debounced constant parsing + auto-save) ──

    private void WireUpCodeEditor()
    {
        var codeEditor = this.FindControl<TextEditor>("CodeEditor");
        if (codeEditor == null) return;

        codeEditor.TextChanged += (_, _) =>
        {
            if (codeEditor.Document == null) return;

            if (_isEditingHelperFunction)
            {
                if (_viewModel.SelectedHelperFunction != null)
                    _viewModel.SelectedHelperFunction.Code = codeEditor.Document.Text;
                return;
            }

            // Debounced constant parsing (500ms)
            _debounceCts?.Cancel();
            _debounceCts = new CancellationTokenSource();
            var token = _debounceCts.Token;
            _ = Task.Delay(500, token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (codeEditor.Document == null) return;
                    _viewModel.ParseConstantsFromCode(codeEditor.Document.Text);
                    var constantsItemsControl = this.FindControl<ItemsControl>("ConstantsItemsControl");
                    if (constantsItemsControl != null)
                        constantsItemsControl.ItemsSource = _viewModel.VariableConstants;
                });
            }, token);

            ScheduleAutoSave();
        };
    }

    // ── Helper Functions wiring ──

    private void WireUpHelperFunctions()
    {
        var helperFunctionsListBox = this.FindControl<ListBox>("HelperFunctionsListBox");
        if (helperFunctionsListBox != null)
        {
            helperFunctionsListBox.ItemsSource = _viewModel.HelperFunctions;
            helperFunctionsListBox.SelectionChanged += OnHelperFunctionSelected;
        }

        var addBtn = this.FindControl<Button>("AddHelperFunctionListButton");
        if (addBtn != null)
            addBtn.Click += OnAddHelperFunction;

        var backBtn = this.FindControl<Button>("BackToMainProgramButton");
        if (backBtn != null)
            backBtn.Click += OnBackToMainProgram;

        AddHandler(Button.ClickEvent, OnHelperFunctionButtonClick);
    }

    private void OnAddHelperFunction(object? sender, RoutedEventArgs e)
    {
        _viewModel.AddHelperFunctionCommand.Execute(null);

        var listBox = this.FindControl<ListBox>("HelperFunctionsListBox");
        if (listBox != null)
        {
            listBox.ItemsSource = null;
            listBox.ItemsSource = _viewModel.HelperFunctions;
            if (_viewModel.SelectedHelperFunction != null)
                listBox.SelectedItem = _viewModel.SelectedHelperFunction;
        }
    }

    private void OnHelperFunctionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.SelectedItem is HelperFunction selectedFunction)
        {
            _viewModel.SelectedHelperFunction = selectedFunction;
            _isEditingHelperFunction = true;

            var codeEditor = this.FindControl<TextEditor>("CodeEditor");
            var codeEditorTitle = this.FindControl<TextBlock>("CodeEditorTitle");

            if (codeEditor != null)
                codeEditor.Text = selectedFunction.Code;
            if (codeEditorTitle != null)
                codeEditorTitle.Text = $"Helper Function: {selectedFunction.Name}";
        }
    }

    private void OnBackToMainProgram(object? sender, RoutedEventArgs e)
    {
        _isEditingHelperFunction = false;
        _viewModel.SelectedHelperFunction = null;

        var codeEditor = this.FindControl<TextEditor>("CodeEditor");
        var codeEditorTitle = this.FindControl<TextBlock>("CodeEditorTitle");

        if (codeEditor != null)
            codeEditor.Text = _viewModel.KsSource;
        if (codeEditorTitle != null)
            codeEditorTitle.Text = "Main Program";

        var listBox = this.FindControl<ListBox>("HelperFunctionsListBox");
        if (listBox != null)
            listBox.SelectedItem = null;
    }

    private void OnHelperFunctionButtonClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button button) return;

        if (button.Tag is HelperFunction function && button.Content?.ToString() == "X")
        {
            var parent = button.Parent;
            while (parent != null)
            {
                if (parent is ListBox) break;
                parent = parent.Parent;
            }
            if (parent == null) return;

            _viewModel.RemoveHelperFunctionCommand.Execute(function);

            var listBox = this.FindControl<ListBox>("HelperFunctionsListBox");
            if (listBox != null)
            {
                listBox.ItemsSource = null;
                listBox.ItemsSource = _viewModel.HelperFunctions;
            }

            if (_viewModel.SelectedHelperFunction == null)
            {
                _isEditingHelperFunction = false;
                var codeEditor = this.FindControl<TextEditor>("CodeEditor");
                var codeEditorTitle = this.FindControl<TextBlock>("CodeEditorTitle");
                if (codeEditor != null)
                    codeEditor.Text = _viewModel.KsSource;
                if (codeEditorTitle != null)
                    codeEditorTitle.Text = "Main Program";
            }
        }
    }

    // ── Constants panel wiring ──

    private void WireUpConstants()
    {
        var constantsItemsControl = this.FindControl<ItemsControl>("ConstantsItemsControl");
        if (constantsItemsControl != null)
            constantsItemsControl.ItemsSource = _viewModel.VariableConstants;

        var resetAllBtn = this.FindControl<Button>("ResetAllConstantsButton");
        if (resetAllBtn != null)
            resetAllBtn.Click += OnResetAllConstants;

        var addParamBtn = this.FindControl<Button>("AddParameterButton");
        if (addParamBtn != null)
            addParamBtn.Click += OnAddParameter;

        AddHandler(Button.ClickEvent, OnConstantsButtonClick);
    }

    private void OnResetAllConstants(object? sender, RoutedEventArgs e)
    {
        _viewModel.ResetAllConstantsCommand.Execute(null);
        var constantsItemsControl = this.FindControl<ItemsControl>("ConstantsItemsControl");
        if (constantsItemsControl != null)
        {
            constantsItemsControl.ItemsSource = null;
            constantsItemsControl.ItemsSource = _viewModel.VariableConstants;
        }
    }

    private void OnAddParameter(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedHelperFunction != null)
        {
            var newParam = new HelperFunctionParameter
            {
                Name = $"param{_viewModel.SelectedHelperFunction.Parameters.Count + 1}",
                Type = "object"
            };
            _viewModel.Parameters.Add(newParam);
            _viewModel.SelectedHelperFunction.Parameters.Add(newParam);
        }
    }

    private void OnConstantsButtonClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Button button) return;

        // Reset constant button
        if (button.Tag is VariableConstant constant && button.Content?.ToString() == "R")
        {
            _viewModel.ResetConstantCommand.Execute(constant);
            var constantsItemsControl = this.FindControl<ItemsControl>("ConstantsItemsControl");
            if (constantsItemsControl != null)
            {
                constantsItemsControl.ItemsSource = null;
                constantsItemsControl.ItemsSource = _viewModel.VariableConstants;
            }
        }
        // Remove parameter button
        else if (button.Tag is HelperFunctionParameter param && button.Content?.ToString() == "X")
        {
            if (_viewModel.SelectedHelperFunction != null)
            {
                _viewModel.Parameters.Remove(param);
                _viewModel.SelectedHelperFunction.Parameters.Remove(param);
            }
        }
    }

    // ── Mode switch synchronization ──

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkflowEditorViewModelV6.Mode) && _viewModel.IsBlockScriptMode)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var codeEditor = this.FindControl<TextEditor>("CodeEditor");
                var codeEditorTitle = this.FindControl<TextBlock>("CodeEditorTitle");
                var constantsItemsControl = this.FindControl<ItemsControl>("ConstantsItemsControl");

                _isEditingHelperFunction = false;
                if (codeEditor != null)
                    codeEditor.Text = _viewModel.KsSource;
                if (codeEditorTitle != null)
                    codeEditorTitle.Text = "Main Program";

                if (codeEditor?.Document != null)
                {
                    _viewModel.ParseConstantsFromCode(codeEditor.Document.Text);
                    if (constantsItemsControl != null)
                        constantsItemsControl.ItemsSource = _viewModel.VariableConstants;
                }
            });
        }

        // Auto-scroll output to bottom
        if (e.PropertyName == nameof(WorkflowEditorViewModelV6.ExecutionOutput))
        {
            Dispatcher.UIThread.Post(() =>
            {
                var scrollViewer = this.FindControl<ScrollViewer>("OutputScrollViewer");
                if (scrollViewer != null)
                    scrollViewer.ScrollToEnd();
            });
        }
    }

    // ── Auto-save ──

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
        _debounceCts?.Cancel();
        if (_viewModel.IsDirty)
            await _viewModel.SaveAsync();
    }

    // ── Keyboard shortcuts ──

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.S && e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
        {
            _ = _viewModel.SaveAsync();
            e.Handled = true;
        }
        else if (e.Key == Avalonia.Input.Key.Delete && _viewModel.IsBlueprintMode)
        {
            _viewModel.BlueprintVM.DeleteSelectedNodesCommand.Execute(null);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    // ── Inline node comment editing (P5-B2) ──

    private void OnCommentIndicatorPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (sender is Avalonia.Controls.Control { DataContext: BlueprintNodeVMV6 node })
            node.StartEditCommentCommand.Execute(null);
    }

    private void OnCommentEditKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (sender is not Avalonia.Controls.Control { DataContext: BlueprintNodeVMV6 node }) return;
        if (e.Key == Avalonia.Input.Key.Enter)
        {
            node.CommitCommentCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Avalonia.Input.Key.Escape)
        {
            node.CancelEditCommentCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnCommentEditLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Avalonia.Controls.Control { DataContext: BlueprintNodeVMV6 node })
            node.CommitCommentCommand.Execute(null);
    }
}