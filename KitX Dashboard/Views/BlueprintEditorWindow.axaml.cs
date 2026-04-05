using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KitX.Core.Contract.Workflow;
using KitX.Dashboard.ViewModels;
using NodeEditor.Controls;
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
        // Subscribe to ViewModel property changes for connector color updates
        if (_viewModel.Drawing.Connectors is System.Collections.Specialized.INotifyCollectionChanged cc)
            cc.CollectionChanged += (_, _) => ApplyConnectorColors();
        if (_viewModel.Drawing.Nodes is System.Collections.Specialized.INotifyCollectionChanged nc)
            nc.CollectionChanged += (_, _) => ApplyConnectorColors();

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
                // Re-apply colors after layout pass completes
                Dispatcher.UIThread.Post(ApplyConnectorColors, DispatcherPriority.Background);
            });
        }
    }

    /// <summary>
    /// Sets connector Stroke colors based on source pin PinType.
    /// Uses retry mechanism to handle delayed visual tree construction.
    /// This remains in code-behind as it operates on the visual tree directly,
    /// which is an Avalonia rendering concern rather than business logic.
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
