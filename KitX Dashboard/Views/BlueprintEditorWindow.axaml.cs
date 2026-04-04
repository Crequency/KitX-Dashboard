using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia.VisualTree;
using KitX.Core.Contract.Tasks;
using KitX.Core.Contract.Workflow;
using KitX.Dashboard.ViewModels;
using NodeEditor.Controls;
using NodeEditor.Mvvm;
using Serilog;

namespace KitX.Dashboard.Views;

public partial class BlueprintEditorWindow : Window, IView
{
    private readonly BlueprintEditorViewModel _viewModel;
    private IBlueprintService? _blueprintService;
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

        // Get services
        _blueprintService = App.GetService<IBlueprintService>();

        Loaded += OnLoaded;
    }

    /// <summary>
    /// Sets the BlockScript source code to import when the window loads
    /// </summary>
    /// <param name="sourceCode">BlockScript source code to import</param>
    /// <param name="helperFunctions">Optional helper functions for type resolution</param>
    public void SetSourceCode(string sourceCode, List<HelperFunction>? helperFunctions = null)
    {
        _pendingSourceCode = sourceCode;
        _pendingHelperFunctions = helperFunctions;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        UpdateStatus();

        // If there's pending source code, import it
        if (!string.IsNullOrEmpty(_pendingSourceCode))
        {
            var sourceCode = _pendingSourceCode;
            var helpers = _pendingHelperFunctions;
            _pendingSourceCode = null;
            _pendingHelperFunctions = null;

            // Call import asynchronously
            Dispatcher.UIThread.Post(async () =>
            {
                await _viewModel.ImportFromBlockScriptCommand.ExecuteAsync((sourceCode, helpers));
                UpdateStatus();
                // Re-apply colors after layout pass completes
                Dispatcher.UIThread.Post(ApplyConnectorColors, DispatcherPriority.Background);
            });
        }
    }

    private void UpdateStatus()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel?.Drawing?.Nodes == null) return;

            var nodeCount = _viewModel.Drawing.Nodes.Count;
            var connectionCount = _viewModel.Drawing.Connectors?.Count ?? 0;

            if (NodeCountText != null)
                NodeCountText.Text = $"Nodes: {nodeCount}";
            if (ConnectionCountText != null)
                ConnectionCountText.Text = $"Connections: {connectionCount}";

            // Apply connector colors after the visual tree is built
            ApplyConnectorColors();
        });
    }

    /// <summary>
    /// Sets connector Stroke colors based on source pin PinType.
    /// Uses retry mechanism to handle delayed visual tree construction.
    /// </summary>
    private void ApplyConnectorColors()
    {
        var editor = EditorControl;
        if (editor == null) return;

        var viewModelConnectorCount = _viewModel?.Drawing?.Connectors?.Count ?? 0;
        if (viewModelConnectorCount == 0) return;

        // Walk the visual tree to find all Connector controls
        var connectors = editor.GetVisualDescendants().OfType<Connector>().ToList();
        var appliedCount = 0;

        foreach (var connector in connectors)
        {
            if (connector.ConnectorSource is ConnectorViewModel cvm)
            {
                var pinType = _viewModel?.GetConnectorPinType(cvm);
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

        // If not all connectors were found in visual tree, schedule a retry
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

    private void NewButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel.NewBlueprintCommand.Execute(null);
        UpdateStatus();
        StatusText.Text = "New blueprint created";
    }

    private async void OpenButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open Blueprint",
            Filters = new()
            {
                new FileDialogFilter { Name = "KCS Files", Extensions = new() { "kcs" } },
                new FileDialogFilter { Name = "All Files", Extensions = new() { "*" } }
            }
        };

        var result = await dialog.ShowAsync(this);
        if (result != null && result.Length > 0)
        {
            StatusText.Text = "Opening...";
            try
            {
                await _viewModel.LoadBlueprintAsync(result[0]);
                UpdateStatus();
                StatusText.Text = _viewModel.StatusText;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Open error: {ex.Message}";
            }
        }
    }

    private async void SaveButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save Blueprint",
            Filters = new()
            {
                new FileDialogFilter { Name = "KCS Files", Extensions = new() { "kcs" } }
            },
            DefaultExtension = "kcs"
        };

        // Use current file path as default if available
        if (!string.IsNullOrEmpty(_viewModel.CurrentFilePath))
        {
            dialog.InitialFileName = System.IO.Path.GetFileName(_viewModel.CurrentFilePath);
        }

        var result = await dialog.ShowAsync(this);
        if (!string.IsNullOrEmpty(result))
        {
            StatusText.Text = "Saving...";
            try
            {
                await _viewModel.SaveBlueprintAsync(result);
                UpdateStatus();
                StatusText.Text = _viewModel.StatusText;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Save error: {ex.Message}";
            }
        }
    }

    private async void ImportFromBSButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Show a dialog for user to paste BlockScript source code
        var dialog = new Window
        {
            Title = "Import from BlockScript",
            Width = 500,
            Height = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var textBox = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Height = 300,
            Margin = new Avalonia.Thickness(10)
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(10, 0, 10, 10),
            Children =
            {
                new Button { Content = "Cancel", Width = 80, Margin = new Avalonia.Thickness(0, 0, 10, 0) },
                new Button { Content = "Import", Width = 80 }
            }
        };

        var cancelButton = (Button)buttonPanel.Children[0];
        var importButton = (Button)buttonPanel.Children[1];

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Paste BlockScript source code:", Margin = new Avalonia.Thickness(10, 10, 10, 5) });
        panel.Children.Add(textBox);
        panel.Children.Add(buttonPanel);

        dialog.Content = panel;

        cancelButton.Click += (s, args) => dialog.Close();
        importButton.Click += async (s, args) =>
        {
            var sourceCode = textBox.Text;
            if (!string.IsNullOrWhiteSpace(sourceCode))
            {
                dialog.Close();
                StatusText.Text = "Importing...";
                try
                {
                    await _viewModel.ImportFromBlockScriptCommand.ExecuteAsync((sourceCode, (List<HelperFunction>?)null));
                    UpdateStatus();
                    StatusText.Text = _viewModel.StatusText;
                    // Re-apply colors after layout pass completes
                    Dispatcher.UIThread.Post(ApplyConnectorColors, DispatcherPriority.Background);
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"Import error: {ex.Message}";
                }
            }
        };

        await dialog.ShowDialog(this);
    }

    private async void ExportToBSButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel.ExportToBlockScriptCommand.Execute(null);

        if (!string.IsNullOrEmpty(_viewModel.LastExportedSourceCode))
        {
            var dialog = new Window
            {
                Title = "Exported BlockScript",
                Width = 600,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var textBox = new TextBox
            {
                Text = _viewModel.LastExportedSourceCode,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                FontFamily = new Avalonia.Media.FontFamily("Consolas, Courier New"),
                Margin = new Avalonia.Thickness(10)
            };

            var closeButton = new Button { Content = "Close", Width = 80, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Margin = new Avalonia.Thickness(10) };
            closeButton.Click += (s, args) => dialog.Close();

            var panel = new StackPanel();
            panel.Children.Add(textBox);
            panel.Children.Add(closeButton);

            dialog.Content = panel;
            await dialog.ShowDialog(this);
        }
    }

    private async void RunButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        RunButton.IsEnabled = false;
        StopButton.IsEnabled = true;

        try
        {
            await _viewModel.ExecuteBlueprintCommand.ExecuteAsync(null);

            if (_viewModel.IsExecuting)
            {
                StatusText.Text = "Executing...";
            }
            else
            {
                StatusText.Text = _viewModel.StatusText;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
            Log.Error(ex, "Run button error");
        }
        finally
        {
            RunButton.IsEnabled = true;
            StopButton.IsEnabled = false;
        }
    }

    private void StopButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel.CancelExecutionCommand.Execute(null);
        StatusText.Text = "Execution cancelled";
    }

    private void AddEntryNode_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel.AddEntryNode();
        UpdateStatus();
    }

    private void AddBranchNode_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel.AddBranchNode();
        UpdateStatus();
    }

    private void AddLoopNode_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel.AddLoopNode();
        UpdateStatus();
    }

    private void AddBreakNode_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel.AddBreakNode();
        UpdateStatus();
    }

    private void AddConstNode_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel.AddConstNode();
        UpdateStatus();
    }

    private void AddCallNode_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel.AddCallNode();
        UpdateStatus();
    }

    private void AddCallHelperNode_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel.AddCallHelperNode();
        UpdateStatus();
    }

    private void AddPrintNode_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel.AddPrintNode();
        UpdateStatus();
    }

    private void AddPauseNode_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel.AddPauseNode();
        UpdateStatus();
    }
}
