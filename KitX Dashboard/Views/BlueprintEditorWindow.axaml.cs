using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KitX.Core.Contract.Workflow;
using KitX.Dashboard.ViewModels;

namespace KitX.Dashboard.Views;

public partial class BlueprintEditorWindow : Window, IView
{
    private readonly BlueprintEditorViewModel _viewModel;
    private readonly IWorkflowEditorBridge? _bridge;
    private Popup? _contextPopup;

    /// <summary>
    /// Creates a BlueprintEditorWindow associated with a WorkflowEditor via bridge.
    /// </summary>
    /// <param name="bridge">Bridge to the associated WorkflowEditor (required)</param>
    public BlueprintEditorWindow(IWorkflowEditorBridge bridge)
    {
        InitializeComponent();

        _bridge = bridge;

        // Use DI to get the ViewModel
        _viewModel = App.GetService<BlueprintEditorViewModel>();
        _viewModel.SetBridge(bridge);
        DataContext = _viewModel;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        // Handle right-click on nodes via ContextRequested event
        var editor = this.FindControl<NodifyM.Avalonia.Controls.NodifyEditor>("EditorControl");
        if (editor != null)
        {
            editor.ContextRequested += OnEditorContextRequested;
        }

        // Auto-import current script from WorkflowEditor via bridge
        var sourceCode = _bridge?.GetCurrentScript();
        var helpers = _bridge?.GetHelperFunctions();

        if (!string.IsNullOrEmpty(sourceCode))
        {
            Dispatcher.UIThread.Post(async () =>
            {
                await _viewModel.ImportFromBlockScriptCommand.ExecuteAsync((sourceCode, helpers));
            });
        }
    }

    /// <summary>
    /// Handles right-click on the editor canvas. Shows a context menu when
    /// right-clicking on a node (BlueprintNodeVM).
    /// </summary>
    private void OnEditorContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not NodifyM.Avalonia.Controls.NodifyEditor editor)
            return;

        if (e.Source is not Avalonia.Visual visual)
            return;

        // Walk up the visual tree to find a BaseNode
        var node = FindAncestor<NodifyM.Avalonia.Controls.BaseNode>(visual);
        if (node == null) return;

        // Only show menu for BlueprintNodeVM (not for scope blocks)
        if (node.DataContext is not BlueprintNodeVM nodeVm) return;

        // Re-select the node — NodifyEditor.OnPointerPressed already called
        // SelectItem(null, false) which deselected everything on right-click.
        editor.SelectItem(node, false);

        e.Handled = true;

        // Close existing popup if open
        CloseContextPopup();

        // Build menu panel
        var panel = new StackPanel
        {
            Background = Avalonia.Media.Brush.Parse("#2D2D2D"),
            MinWidth = 180,
        };

        // Delete button
        panel.Children.Add(CreateMenuButton("_Delete", _viewModel.DeleteSelectedNodesCommand, null));

        // Separator
        panel.Children.Add(new Border
        {
            Height = 1,
            Background = Avalonia.Media.Brush.Parse("#444444"),
            Margin = new Avalonia.Thickness(4, 2),
        });

        // Move to Scope items (flat list under a header)
        if (_viewModel.ScopeBlocks.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Move to Scope:",
                Foreground = Avalonia.Media.Brush.Parse("#999999"),
                FontSize = 11,
                Margin = new Avalonia.Thickness(8, 4, 8, 2),
            });

            foreach (var scope in _viewModel.ScopeBlocks)
            {
                panel.Children.Add(CreateMenuButton(
                    $"  {scope.DisplayName} ({scope.ContainedNodeIds.Count} nodes)",
                    _viewModel.MoveSelectedNodesToScopeCommand,
                    scope.ScopeId));
            }
        }

        // Separator
        panel.Children.Add(new Border
        {
            Height = 1,
            Background = Avalonia.Media.Brush.Parse("#444444"),
            Margin = new Avalonia.Thickness(4, 2),
        });

        // Remove from Scope
        panel.Children.Add(CreateMenuButton("Remove from Scope", _viewModel.RemoveSelectedNodesFromScopeCommand, null));

        // Wrap in a border with rounding and shadow
        var border = new Border
        {
            Background = Avalonia.Media.Brush.Parse("#2D2D2D"),
            BorderBrush = Avalonia.Media.Brush.Parse("#555555"),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(4),
            Padding = new Avalonia.Thickness(2),
            Child = panel,
            BoxShadow = new Avalonia.Media.BoxShadows(
                new Avalonia.Media.BoxShadow
                {
                    Color = Avalonia.Media.Color.FromArgb(80, 0, 0, 0),
                    OffsetX = 2, OffsetY = 2, Blur = 8
                }),
        };

        // Create and show popup at pointer position
        _contextPopup = new Popup
        {
            Placement = PlacementMode.Pointer,
            PlacementTarget = this,
            IsLightDismissEnabled = true,
            Child = border,
        };

        // Popup must be in the logical tree to work
        ((ISetLogicalParent)_contextPopup).SetParent(this);
        _contextPopup.Open();
    }

    private Button CreateMenuButton(string text, System.Windows.Input.ICommand command, object? parameter)
    {
        return new Button
        {
            Content = text,
            Command = command,
            CommandParameter = parameter,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            Padding = new Avalonia.Thickness(8, 4),
            Background = Avalonia.Media.Brushes.Transparent,
            Foreground = Avalonia.Media.Brushes.White,
            FontSize = 13,
        };
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

    /// <summary>
    /// Walks up the visual tree to find an ancestor of type T.
    /// </summary>
    private static T? FindAncestor<T>(Avalonia.Visual visual) where T : Avalonia.Visual
    {
        var current = visual.GetVisualParent();
        while (current != null)
        {
            if (current is T result) return result;
            current = current.GetVisualParent();
        }
        return null;
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Cleanup();
        base.OnClosed(e);
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
