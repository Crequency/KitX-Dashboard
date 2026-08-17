using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using KitX.Core.Contract.Workflow;
using KitX.Dashboard.ViewModels;
using KitX.ToolKit.Contracts;
using KitX.WorkflowV6.Lens.BpGraphLens;
using KitX.WorkflowV6.Lens.KsTextLens;
using KitX.WorkflowV6.Serialization;
using NodifyM.Avalonia;
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
    /// <summary>Which document the AvaloniaEdit currently shows — drives TextChanged routing (R1).</summary>
    private enum EditorContext { MainProgram, HelperFunction }

    private readonly WorkflowEditorViewModelV6 _viewModel;
    private EditorContext _context = EditorContext.MainProgram;
    private bool _textChangedWired;
    private CancellationTokenSource? _debounceCts;
    private CancellationTokenSource? _autoSaveCts;

    public WorkflowEditorWindowV6()
    {
        InitializeComponent();

        _viewModel = App.GetService<WorkflowEditorViewModelV6>();
        DataContext = _viewModel;

        // R1: the view exposes the live editor text to the VM for explicit snapshots
        // before Save/Run. Returns null while a Helper Function is shown (not the main program).
        _viewModel.EditorTextProvider = () =>
        {
            if (_context != EditorContext.MainProgram) return null;
            return this.FindControl<TextEditor>("CodeEditor")?.Document?.Text;
        };

        InitializeEditor();
        WireUpHelperFunctions();
        WireUpConstants();

        var editor = this.FindControl<NodifyM.Avalonia.Controls.NodifyEditor>("EditorControl");
        if (editor != null)
        {
            // Reliable right-click channel (NodifyM RightClick event — independent of Avalonia's
            // fragile ContextRequested which TextBox etc. suppress).
            editor.RightClick += OnEditorRightClick;

            // Drag-to-blank → in-place node selector: the PendingConnection control marks
            // the completion event handled (then drops the connection), so the window must
            // listen with handledEventsToo: true to observe blank releases.
            editor.AddHandler(
                NodifyM.Avalonia.Controls.Connector.PendingConnectionCompletedEvent,
                new EventHandler<NodifyM.Avalonia.Events.PendingConnectionEventArgs>(OnPendingConnectionCompleted),
                RoutingStrategies.Bubble,
                handledEventsToo: true);
        }

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ActualThemeVariantChanged += (_, _) =>
        {
            // C2: never overwrite the document on theme changes — the editor may be showing
            // a helper function's code, and a programmatic Text assignment would route the
            // main program text into the helper's Code via OnEditorTextChanged. Only
            // re-install the grammar/theme; the document keeps its current context.
            InitializeEditor(refreshDocument: false);
            _viewModel.BlueprintVM.RefreshThemeColors();
        };

        Closing += OnWindowClosing;
    }

    protected override void OnClosed(EventArgs e)
    {
        // Symmetric teardown for the ctor subscription (symmetric-unsubscribe rule):
        // the VM is transient per window, so release the PropertyChanged hook on close
        // to avoid retaining the window via the VM.
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        base.OnClosed(e);
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

            SetEditorContext(EditorContext.MainProgram, _viewModel.KsSource, "Main Program");

            var constantsItemsControl = this.FindControl<ItemsControl>("ConstantsItemsControl");
            if (constantsItemsControl != null)
                constantsItemsControl.ItemsSource = _viewModel.VariableConstants;
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = $"Failed to load v6 IR: {ex.Message}";
        }
    }

    /// <summary>Loads a ToolKit-bundled workflow by explicit file path (Bench UX v2 C5).</summary>
    public async Task LoadWorkflowFileAsync(string filePath)
    {
        // TODO(B1): move into VM
        var kcs = await App.GetService<IToolkitWorkflowFileStore>().LoadAsync(filePath);
        if (kcs == null)
        {
            _viewModel.StatusText = $"Workflow not found: {filePath}";
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
            _viewModel.SetWorkflowId(kcs.Id);
            _viewModel.SetWorkflowFilePath(filePath);
            _viewModel.LoadFromIr(ir, kcs.Name, kcs);

            SetEditorContext(EditorContext.MainProgram, _viewModel.KsSource, "Main Program");

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
        SetEditorContext(EditorContext.MainProgram, ksSource, "Main Program");
    }

    // ── AvaloniaEdit initialization ──

    private void InitializeEditor(bool refreshDocument = true)
    {
        var textEditor = this.FindControl<TextEditor>("CodeEditor");
        if (textEditor == null) return;

        if (refreshDocument)
        {
            Log.Information("[WorkflowEditorWindowV6] InitializeEditor: setting Text to {Length} chars", _viewModel.KsSource.Length);
            textEditor.Text = _viewModel.KsSource;
        }

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
        var ksGrammarLoaded = false;
        try
        {
            var ksPackage = new System.IO.FileInfo(
                System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "TextMate", "ks", "package.json"));
            if (ksPackage.Exists)
            {
                registryOptions.LoadFromLocalFile("ks", ksPackage, overwrite: true);
                installation.SetGrammar(registryOptions.GetScopeByLanguageId("ks"));
                Log.Information("[WorkflowEditorWindowV6] KS grammar loaded from {Path}", ksPackage.FullName);
                ksGrammarLoaded = true;
            }
            else
            {
                Log.Warning("[WorkflowEditorWindowV6] KS grammar package not found at {Path}, falling back to C#", ksPackage.FullName);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[WorkflowEditorWindowV6] Failed to load KS grammar, falling back to C#");
        }
        if (!ksGrammarLoaded)
        {
            installation.SetGrammar(
                registryOptions.GetScopeByLanguageId(registryOptions.GetLanguageByExtension(".cs").Id));
        }

        // Apply the KS-aware theme: the built-in LightPlus/DarkPlus themes carry no
        // rules for source.ks scopes, so without this every pipeline element would
        // render in the default colour. The wrapper appends VS-palette rules for the
        // KS scopes on top of the base theme.
        try
        {
            var isDark = ActualThemeVariant != ThemeVariant.Light;
            installation.SetTheme(new KitX.Dashboard.Services.KScriptTheme(
                registryOptions.GetDefaultTheme(), isDark));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[WorkflowEditorWindowV6] Failed to apply KS theme, using base theme");
        }

        // Single TextChanged routing point (R1): wire once, not per theme-change call.
        if (!_textChangedWired)
        {
            textEditor.TextChanged += OnEditorTextChanged;
            _textChangedWired = true;
        }
    }

    // ── Editor context routing (R1) ──

    /// <summary>
    /// Atomically switches the editor to show <paramref name="docText"/> under the given
    /// context, keeping the context flag, document, and title in sync. The TextChanged
    /// handler routes typing to the main program (KsSource) or the selected helper's Code.
    /// </summary>
    private void SetEditorContext(EditorContext context, string docText, string title)
    {
        _context = context;
        var codeEditor = this.FindControl<TextEditor>("CodeEditor");
        var codeEditorTitle = this.FindControl<TextBlock>("CodeEditorTitle");
        if (codeEditor != null)
            codeEditor.Text = docText;
        if (codeEditorTitle != null)
            codeEditorTitle.Text = title;
    }

    /// <summary>
    /// The single editor TextChanged handler. Routes by current context, and — for the
    /// main program — refreshes KsSource, debounced constant parsing, and auto-save.
    /// </summary>
    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (sender is not TextEditor codeEditor || codeEditor.Document == null) return;
        var doc = codeEditor.Document.Text;

        if (_context == EditorContext.HelperFunction)
        {
            if (_viewModel.SelectedHelperFunction != null)
                _viewModel.SelectedHelperFunction.Code = doc;
            return;
        }

        _viewModel.KsSource = doc;

        // Debounced constant parsing (500ms).
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
            SetEditorContext(EditorContext.HelperFunction,
                selectedFunction.Code ?? string.Empty,
                $"Helper Function: {selectedFunction.Name}");
        }
    }

    private void OnBackToMainProgram(object? sender, RoutedEventArgs e)
    {
        _viewModel.SelectedHelperFunction = null;
        SetEditorContext(EditorContext.MainProgram, _viewModel.KsSource, "Main Program");

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

            // R1: the VM auto-selects the next helper (or null) after removal — refresh the
            // editor context to match the new selection so the code-behind flag, document,
            // and title never drift out of sync.
            if (_viewModel.SelectedHelperFunction is { } remaining)
            {
                SetEditorContext(EditorContext.HelperFunction,
                    remaining.Code ?? string.Empty,
                    $"Helper Function: {remaining.Name}");
                if (listBox != null)
                    listBox.SelectedItem = remaining;
            }
            else
            {
                SetEditorContext(EditorContext.MainProgram, _viewModel.KsSource, "Main Program");
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

    // ── Editor-level right-click menus (F2) ──

    private Popup? _contextPopup;

    /// <summary>
    /// Shows the right-click menu via NodifyM's reliable <c>RightClick</c> channel (v5.1
    /// custom-Popup pattern — Avalonia's ContextRequested/ContextMenu is unreliable, e.g.
    /// suppressed when the pressed element is a TextBox). A press landing on a node opens
    /// the node menu; anywhere else opens the canvas menu.
    /// </summary>
    private void OnEditorRightClick(object? sender, NodifyM.Avalonia.Events.RightClickEventArgs e)
    {
        Log.Information("[WorkflowEditorWindowV6] RightClick: source={Source}, position={Pos}", e.PressedSource?.GetType().Name, e.Position);
        var source = e.PressedSource as Avalonia.Controls.Control;
        var nodeVm = source?.GetSelfAndVisualAncestors()
            .OfType<NodifyM.Avalonia.Controls.Node>()
            .Select(n => n.DataContext as BlueprintNodeVMV6)
            .FirstOrDefault(dc => dc != null);
        var connectionVm = source?.GetSelfAndVisualAncestors()
            .OfType<NodifyM.Avalonia.Controls.BaseConnection>()
            .Select(c => c.DataContext as BlueprintConnectionVMV6)
            .FirstOrDefault(dc => dc != null);

        var panel = new StackPanel { Spacing = 2 };
        if (nodeVm != null)
        {
            panel.Children.Add(CreateMenuButton("Toggle Breakpoint", _viewModel.ToggleBreakpointCommand, nodeVm));
            panel.Children.Add(CreateMenuButton("添加组注释", _viewModel.BlueprintVM.AddGroupCommentCommand, nodeVm));
            panel.Children.Add(CreateMenuButton("删除节点", _viewModel.BlueprintVM.DeleteNodeCommand, nodeVm));
        }
        else if (connectionVm != null)
        {
            panel.Children.Add(CreateMenuButton("断开连线", _viewModel.BlueprintVM.RemoveConnectionCommand, connectionVm));
        }
        else
        {
            // D13.6: placeholder entry for the blank-canvas context menu (under
            // development — no actions yet). Kept visible so the menu doesn't
            // feel broken; remove once canvas-level actions land.
            panel.Children.Add(CreateMenuButton("画布菜单（开发中）", null, null));
        }
        ShowContextPopup(panel);
    }

    private static Button CreateMenuButton(string text, System.Windows.Input.ICommand? command, object? parameter) => new()
    {
        Content = text,
        Command = command,
        CommandParameter = parameter,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Left,
        Padding = new Thickness(10, 5),
        Margin = new Thickness(1),
        MinWidth = 130,
        FontSize = 12,
    };

    private void ShowContextPopup(StackPanel panel)
    {
        CloseContextPopup();
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(242, 36, 38, 43)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(180, 120, 120, 120)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(2),
            Child = panel,
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

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkflowEditorViewModelV6.Mode) && _viewModel.IsBlockScriptMode)
        {
            Dispatcher.UIThread.Post(() =>
            {
                // R1: returning to KS mode always restores the main-program editing context
                // (in case a helper session was active when the mode switched).
                SetEditorContext(EditorContext.MainProgram, _viewModel.KsSource, "Main Program");

                var constantsItemsControl = this.FindControl<ItemsControl>("ConstantsItemsControl");
                var codeEditor = this.FindControl<TextEditor>("CodeEditor");
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

    /// <summary>
    /// Periodic dirty check (3 s interval, self-rescheduling). Runs for the whole
    /// window lifetime — a single fire at construction (the old behaviour) would only
    /// ever auto-save once, leaving every later edit to the window-close path.
    /// Cancelled on window closing (OnWindowClosing) so the loop stops cleanly.
    /// </summary>
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
                // Self-reschedule: keep watching for edits for the window's lifetime.
                if (!token.IsCancellationRequested)
                    ScheduleAutoSave();
            });
        }, token);
    }

    /// <summary>
    /// True while the close path is inside the save-then-close sequence; lets the
    /// re-entrant Closing event (raised by the explicit Close() below) through
    /// instead of cancelling and re-saving forever (D4).
    /// </summary>
    private bool _closingAfterSave;

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closingAfterSave)
            return;

        _autoSaveCts?.Cancel();
        _debounceCts?.Cancel();

        // D4: an async void handler cannot extend the close — the window is already
        // gone when SaveAsync completes, so a quick reopen may read the stale file.
        // Cancel the close, save synchronously from the user's perspective, then
        // close again (guarded against re-entry). SaveAsync swallows its own errors,
        // so no exception can escape and wedge the close path.
        if (_viewModel.IsDirty)
        {
            e.Cancel = true;
            _closingAfterSave = true;
            await _viewModel.SaveAsync();
            Close();
        }
    }

    // ── Keyboard shortcuts ──

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.S && e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
        {
            _ = _viewModel.SaveAsync();
            e.Handled = true;
        }
        else if (e.Key == Avalonia.Input.Key.Delete && _viewModel.IsBlueprintMode && !IsTextInputFocused())
        {
            _viewModel.BlueprintVM.DeleteSelectedNodesCommand.Execute(null);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    /// <summary>True when keyboard focus is inside a text input (Delete must not remove nodes then).</summary>
    private bool IsTextInputFocused()
    {
        var focus = FocusManager?.GetFocusedElement();
        return focus is TextBox or TextEditor;
    }

    // ── In-place node selector (drag from output port onto blank canvas) ──

    /// <summary>
    /// Observed with handledEventsToo: true — fires after the PendingConnection control
    /// has already handled the completion. A release on blank canvas (null target, not
    /// cancelled) of an OUTPUT port opens the selector at the drop point; input-port
    /// drags are a design no-op and valid targets flow through the normal Connect path.
    /// </summary>
    private void OnPendingConnectionCompleted(object? sender, NodifyM.Avalonia.Events.PendingConnectionEventArgs e)
    {
        if (e.Canceled || e.TargetConnector != null) return;
        if (e.SourceConnector is not BlueprintConnectorVMV6 src) return;
        if (src.Flow != NodifyM.Avalonia.ViewModelBase.ConnectorViewModelBase.ConnectorFlow.Output) return;

        // Drop point in canvas coordinates (matches the PendingConnection control's own
        // TargetAnchor computation — anchor + drag offset).
        var drop = new Avalonia.Point(e.Anchor.X + e.OffsetX, e.Anchor.Y + e.OffsetY);
        _viewModel.BlueprintVM.OpenNodeSelector(src, drop);
    }

    private void OnNodeSelectorPopupOpened(object? sender, EventArgs e)
    {
        this.FindControl<TextBox>("NodeSelectorSearchBox")?.Focus();
    }

    private void OnNodeSelectorSearchKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Enter)
        {
            var first = _viewModel.BlueprintVM.NodeSelectorItems.FirstOrDefault();
            if (first != null)
                _viewModel.BlueprintVM.SelectNodeSelectorItemCommand.Execute(first);
            e.Handled = true;
        }
        else if (e.Key == Avalonia.Input.Key.Escape)
        {
            _viewModel.BlueprintVM.CloseNodeSelector();
            e.Handled = true;
        }
    }

    // ── Inline node comment editing (P5-B2) ──

    private void OnCommentIndicatorPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (sender is not Avalonia.Controls.Control { DataContext: BlueprintNodeVMV6 node }) return;
        // R5: toggle — clicking again while editing commits; otherwise begin editing.
        if (node.IsEditingComment)
            node.CommitCommentCommand.Execute(null);
        else
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

    // ── Inline usage-name editing (2026-08-03, REWRITTEN): pointer-driven only.
    //
    // No focus events drive any state. The Pencil button is the ONLY entry and the
    // ONLY toggle: idle → Pencil = start editing (box focused for keyboard, dropdown
    // opens when the box has focus and empty text — pure convenience, never a state
    // driver); editing → Pencil / Enter = commit; Esc = cancel. The row is a fixed
    // three-column Grid, so the button never moves between states.

    private void OnUsageNameEditButtonPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (sender is not Avalonia.Controls.Control { DataContext: BlueprintNodeVMV6 node }) return;
        if (node.IsEditingUsageName)
        {
            node.CommitUsageNameCommand.Execute(null);
        }
        else
        {
            node.StartEditUsageNameCommand.Execute(null);
            // Focus is deferred (IsEditingUsageName flips IsVisible on the next layout
            // pass; an invisible box cannot take focus synchronously). Posting focuses
            // the now-visible box so the user can type immediately; its KeyDown then
            // handles Enter/Esc. Focus is a convenience only — no logic depends on it.
            if (sender is Avalonia.Controls.Control c && c.Parent is Avalonia.Controls.Panel panel)
            {
                var box = panel.GetVisualDescendants().OfType<AutoCompleteBox>().FirstOrDefault();
                if (box is not null)
                    Dispatcher.UIThread.Post(() =>
                    {
                        box.Focus();
                        if (string.IsNullOrEmpty(box.Text))
                            box.IsDropDownOpen = true;
                    });
            }
        }
        // Keep the press on the button — never propagate to the canvas select/drag.
        e.Handled = true;
    }

    private void OnUsageNameEditKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (sender is not Avalonia.Controls.Control { DataContext: BlueprintNodeVMV6 node }) return;
        if (e.Key == Avalonia.Input.Key.Enter)
        {
            node.CommitUsageNameCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Avalonia.Input.Key.Escape)
        {
            node.CancelEditUsageNameCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ── GroupComment note: drag / collapse / edit / hover (2026-08-02) ──

    private bool _gcDragging;
    private bool _gcClickCandidate;
    private Avalonia.Point _gcDragStartPointer;
    private Avalonia.Point _gcDragStartLocation;

    private void OnGroupCommentPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (sender is not Avalonia.Controls.Control c || c.DataContext is not GroupCommentVM vm) return;

        // Double-click begins inline text editing (and does not start a drag).
        if (e.ClickCount >= 2)
        {
            vm.BeginEdit();
            e.Handled = true;
            return;
        }
        StartGroupCommentDrag(c, vm, e);
    }

    private void StartGroupCommentDrag(Avalonia.Controls.Control c, GroupCommentVM vm, Avalonia.Input.PointerPressedEventArgs e)
    {
        var editor = this.FindControl<NodifyM.Avalonia.Controls.NodifyEditor>("EditorControl");
        if (editor == null) return;
        _gcDragStartPointer = e.GetPosition(editor);
        _gcDragStartLocation = vm.Location;
        _gcDragging = true;
        _gcClickCandidate = true;
        _viewModel.BlueprintVM.SetGroupCommentDragging(vm);
        e.Pointer.Capture(c);
        e.Handled = true;  // prevent NodifyEditor node selection/drag
    }

    private void OnGroupCommentPointerMoved(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (!_gcDragging || sender is not Avalonia.Controls.Control c || c.DataContext is not GroupCommentVM vm) return;
        var editor = this.FindControl<NodifyM.Avalonia.Controls.NodifyEditor>("EditorControl");
        if (editor == null) return;
        var p = e.GetPosition(editor);
        var dx = p.X - _gcDragStartPointer.X;
        var dy = p.Y - _gcDragStartPointer.Y;
        if (Math.Abs(dx) > 3 || Math.Abs(dy) > 3)
            _gcClickCandidate = false;
        vm.Location = new Avalonia.Point(_gcDragStartLocation.X + dx, _gcDragStartLocation.Y + dy);
        e.Handled = true;
    }

    private void OnGroupCommentPointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (!_gcDragging) return;
        _gcDragging = false;
        _viewModel.BlueprintVM.SetGroupCommentDragging(null);
        if (sender is Avalonia.Controls.Control c)
        {
            e.Pointer.Capture(null);
            if (c.DataContext is GroupCommentVM vm)
            {
                if (_gcClickCandidate)
                    vm.IsCollapsed = !vm.IsCollapsed;   // click toggles collapse
                else
                    _viewModel.BlueprintVM.SnapGroupCommentToNode(vm);   // proximity snap / free
            }
        }
        e.Handled = true;
    }

    /// <summary>Note hover drives the SINGLE shared dashed-frame overlay.</summary>
    private void OnGroupCommentPointerEntered(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (sender is Avalonia.Controls.Control { DataContext: GroupCommentVM vm })
        {
            vm.IsHovered = true;
            _viewModel.BlueprintVM.UpdateGroupCommentHighlight(vm);
        }
    }

    private void OnGroupCommentPointerExited(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (sender is Avalonia.Controls.Control { DataContext: GroupCommentVM vm })
        {
            vm.IsHovered = false;
            _viewModel.BlueprintVM.UpdateGroupCommentHighlight(null);
        }
    }

    private void OnGroupCommentEditKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (sender is not Avalonia.Controls.Control { DataContext: GroupCommentVM vm }) return;
        if (e.Key == Avalonia.Input.Key.Enter)
        {
            vm.CommitEdit();
            e.Handled = true;
        }
        else if (e.Key == Avalonia.Input.Key.Escape)
        {
            vm.CancelEdit();
            e.Handled = true;
        }
    }

    private void OnGroupCommentEditLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Avalonia.Controls.Control { DataContext: GroupCommentVM vm })
            vm.CommitEdit();
    }
}
