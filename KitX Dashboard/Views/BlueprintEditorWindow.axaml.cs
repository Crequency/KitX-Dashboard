using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KitX.Core.Contract.Workflow;
using KitX.Dashboard.Controls;
using KitX.Dashboard.ViewModels;
using NodeEditor.Controls;
using NodeEditor.Model;
using NodeEditor.Mvvm;
using Serilog;

namespace KitX.Dashboard.Views;

public partial class BlueprintEditorWindow : Window, IView
{
    private readonly BlueprintEditorViewModel _viewModel;
    private string? _pendingSourceCode;
    private List<HelperFunction>? _pendingHelperFunctions;
    private int _colorRetryCount;
    private const int MaxColorRetries = 10;
    private DispatcherTimer? _colorRetryTimer;
    private bool _suppressSelectionFix;

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
        // Subscribe to collection changes for visual updates
        if (_viewModel.Drawing.Connectors is System.Collections.Specialized.INotifyCollectionChanged cc)
            cc.CollectionChanged += (_, _) => ScheduleVisualUpdate();
        if (_viewModel.Drawing.Nodes is System.Collections.Specialized.INotifyCollectionChanged nc)
            nc.CollectionChanged += (_, _) => ScheduleVisualUpdate();

        // Subscribe to selection changes to fix overlapping node selection
        _viewModel.Drawing.SelectionChanged += OnSelectionChanged;

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
                Dispatcher.UIThread.Post(ScheduleVisualUpdate, DispatcherPriority.Background);
            });
        }
    }

    /// <summary>
    /// Schedules a visual update for connector colors and pin attached properties
    /// </summary>
    private void ScheduleVisualUpdate()
    {
        Dispatcher.UIThread.Post(() =>
        {
            ApplyConnectorColors();
            ApplyPinProperties();
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Fixes overlapping node selection: when multiple nodes are selected at the same
    /// position, only keep the topmost one (last in the Nodes collection = highest Z-order).
    /// </summary>
    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (_suppressSelectionFix) return;

        var drawing = _viewModel.Drawing;
        var selectedNodes = drawing.GetSelectedNodes();
        if (selectedNodes == null || selectedNodes.Count <= 1) return;

        // Find the topmost node: last in the Nodes collection that is also selected
        INode? topmost = null;
        foreach (var node in drawing.Nodes)
        {
            if (selectedNodes.Contains(node))
                topmost = node;
        }

        if (topmost == null) return;

        // Only fix when multiple selected nodes overlap at similar positions
        var selectedList = selectedNodes.ToList();
        var hasOverlap = false;
        foreach (var node in selectedList)
        {
            if (node == topmost) continue;
            var topRect = new Avalonia.Rect(topmost.X, topmost.Y, topmost.Width, topmost.Height);
            var nodeRect = new Avalonia.Rect(node.X, node.Y, node.Width, node.Height);
            if (topRect.Intersects(nodeRect))
            {
                hasOverlap = true;
                break;
            }
        }

        if (!hasOverlap) return;

        _suppressSelectionFix = true;
        try
        {
            drawing.SetSelectedNodes(new HashSet<INode> { topmost });
        }
        finally
        {
            _suppressSelectionFix = false;
        }
    }

    /// <summary>
    /// Sets attached properties (IsExecution, PinTypeColor, IsConnected) on Pin controls
    /// so the overridden Pin ControlTheme can render the correct shape and color.
    /// </summary>
    private void ApplyPinProperties()
    {
        var editor = EditorControl;
        if (editor == null) return;

        var pins = editor.GetVisualDescendants().OfType<Pin>().ToList();
        foreach (var pinControl in pins)
        {
            if (pinControl.PinSource is PinViewModel pinVm)
            {
                var pinType = _viewModel.GetPinType(pinVm);
                if (pinType.HasValue)
                {
                    PinProperties.SetIsExecution(pinControl, pinType.Value == PinType.Execution);
                    PinProperties.SetPinTypeColor(pinControl,
                        BlueprintEditorViewModel.GetHexColorForPinType(pinType.Value));

                    // Check if connected: scan connectors for this pin
                    var isConnected = _viewModel.Drawing.Connectors
                        .OfType<ConnectorViewModel>()
                        .Any(c => c.Start == pinVm || c.End == pinVm);
                    PinProperties.SetIsConnected(pinControl, isConnected);
                }
            }
        }
    }

    /// <summary>
    /// Sets connector Stroke colors based on source pin PinType.
    /// Also handles manually-drawn connectors that weren't created through LoadBlueprint.
    /// </summary>
    private void ApplyConnectorColors()
    {
        var editor = EditorControl;
        if (editor == null) return;

        var viewModelConnectorCount = _viewModel.Drawing.Connectors?.Count ?? 0;
        if (viewModelConnectorCount == 0) return;

        var connectors = editor.GetVisualDescendants().OfType<Connector>().ToList();
        var appliedCount = 0;

        foreach (var connector in connectors)
        {
            if (connector.ConnectorSource is ConnectorViewModel cvm)
            {
                var pinType = _viewModel.GetConnectorPinType(cvm);
                if (pinType.HasValue)
                {
                    connector.Stroke = BlueprintEditorViewModel.GetBrushForPinType(pinType.Value);
                    connector.Fill = null;
                    appliedCount++;
                }
            }
        }

        Log.Debug("[Color] Applied {Applied}/{Total} connector colors (visual={Visual})",
            appliedCount, viewModelConnectorCount, connectors.Count);

        if (appliedCount < viewModelConnectorCount && _colorRetryCount < MaxColorRetries)
        {
            _colorRetryCount++;
            _colorRetryTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _colorRetryTimer.Tick -= OnColorRetryTick;
            _colorRetryTimer.Tick += OnColorRetryTick;
            _colorRetryTimer.Start();
        }
        else
        {
            _colorRetryCount = 0;
            _colorRetryTimer?.Stop();
        }
    }

    private void OnColorRetryTick(object? sender, EventArgs e)
    {
        _colorRetryTimer?.Stop();
        ApplyConnectorColors();
    }
}
