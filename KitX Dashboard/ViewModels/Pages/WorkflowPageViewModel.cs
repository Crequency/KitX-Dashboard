using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using KitX.Core.Contract.Event;
using KitX.Core.Contract.Workflow;
using KitX.Core.Event;
using KitX.Core.Workflow;
using KitX.Dashboard;
using KitX.Dashboard.Services;
using KitX.Dashboard.Views;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels.Pages;

internal class WorkflowPageViewModel : ViewModelBase
{
    private readonly IWorkflowStorageService _storageService;
    private readonly IWorkflowService _workflowService;
    private readonly IEventService _eventService;

    public WorkflowPageViewModel()
    {
        _storageService = App.GetService<IWorkflowStorageService>();
        _workflowService = App.GetService<IWorkflowService>();
        _eventService = App.GetService<IEventService>();

        InitCommands();
        InitEvents();

        // Load workflows from storage on construction
        _ = LoadWorkflowsAsync();
    }

    public sealed override void InitCommands()
    {
        CreateWorkflowCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var workflow = await _storageService.CreateWorkflowAsync(
                TranslateTextWithSuffix("Workflow", "NewWorkflow") ?? "New Workflow");
            WorkflowCases.Add(workflow);
            _eventService.Publish(EventNames.WorkflowCreated, EventArgs.Empty);
        });

        OpenWorkflowCommand = ReactiveCommand.Create<IWorkflowCase>(workflow =>
        {
            OpenWorkflowEditorAsync(workflow);
        });

        DeleteWorkflowCommand = ReactiveCommand.Create<IWorkflowCase>(workflow =>
        {
            DeleteWorkflowAsync(workflow);
        });

        RunWorkflowCommand = ReactiveCommand.Create<IWorkflowCase>(workflow =>
        {
            RunWorkflowAsync(workflow);
        });

        StopWorkflowCommand = ReactiveCommand.Create<IWorkflowCase>(workflow =>
        {
            StopWorkflowAsync(workflow);
        });

        RefreshWorkflowsCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            await LoadWorkflowsAsync();
        });
    }

    public sealed override void InitEvents()
    {
        WorkflowCases.CollectionChanged += (_, _) =>
        {
            NoWorkflow_TipHeight = WorkflowCases.Count == 0 ? 300 : 0;
            WorkflowCount = WorkflowCases.Count;
            this.RaisePropertyChanged(nameof(WorkflowCountTip));
        };

        _eventService.Subscribe(EventNames.LanguageChanged, (s, e) =>
        {
            this.RaisePropertyChanged(nameof(WorkflowCountTip));
        });

        // Listen for rename events from editor windows
        _eventService.Subscribe(EventNames.WorkflowRenamed, (s, e) =>
        {
            if (e is WorkflowRenamedEventArgs args)
                SyncWorkflowMetadata(args.WorkflowId, args.NewName, null, null);
        });

        // Listen for save events to sync metadata changes
        _eventService.Subscribe(EventNames.WorkflowDataSaved, (s, e) =>
        {
            if (e is WorkflowSavedEventArgs args)
                SyncWorkflowMetadata(args.WorkflowId, args.WorkflowName, args.Description, args.Author);
        });

        // Listen for workflow execution results
        _eventService.Subscribe(EventNames.WorkflowExecutionResult, OnWorkflowExecutionResult);

        // Listen for plugin registration to auto-recover error workflows
        _eventService.Subscribe(EventNames.PluginRegistered, OnPluginRegistered);

        // Listen for plugin unregistration to set error state on dependent workflows
        _eventService.Subscribe(EventNames.PluginUnregistered, OnPluginUnregistered);
    }

    private async Task LoadWorkflowsAsync()
    {
        // Snapshot running and error state before clearing — these are in-memory only,
        // not persisted to disk. Refresh creates new instances, so we must restore.
        var runningIds = new System.Collections.Generic.HashSet<string>();
        var errorStates = new System.Collections.Generic.Dictionary<string, string?>();
        foreach (var w in WorkflowCases)
        {
            if (w.IsRunning) runningIds.Add(w.Id);
            if (w.IsError) errorStates[w.Id] = w.ErrorMessage;
        }

        var workflows = await _storageService.DiscoverWorkflowsAsync();
        WorkflowCases.Clear();

        foreach (var w in workflows)
        {
            // Restore running state from snapshot
            if (runningIds.Contains(w.Id))
                w.IsRunning = true;
            // Restore error state from snapshot
            if (errorStates.TryGetValue(w.Id, out var errMsg))
            {
                w.IsError = true;
                w.ErrorMessage = errMsg;
            }

            WorkflowCases.Add(w);
        }
    }

    /// <summary>
    /// Refreshes a single workflow item in the list by removing and re-inserting it.
    /// WorkflowCase is a POCO without INotifyPropertyChanged, so property changes
    /// (like IsRunning) won't propagate to bindings unless we trigger CollectionChanged.
    /// </summary>
    private static void RefreshWorkflowInList(IWorkflowCase workflow)
    {
        for (int i = 0; i < WorkflowCases.Count; i++)
        {
            if (WorkflowCases[i].Id == workflow.Id)
            {
                WorkflowCases.RemoveAt(i);
                WorkflowCases.Insert(i, workflow);
                return;
            }
        }
    }

    /// <summary>
    /// Syncs workflow metadata in the collection by replacing the item
    /// to trigger ObservableCollection.CollectionChanged and refresh UI bindings.
    /// WorkflowCase is a POCO without INotifyPropertyChanged, so simply setting
    /// properties won't update the card text.
    /// </summary>
    private static void SyncWorkflowMetadata(string workflowId, string? newName, string? description, string? author)
    {
        for (int i = 0; i < WorkflowCases.Count; i++)
        {
            if (WorkflowCases[i].Id == workflowId)
            {
                var existing = WorkflowCases[i];
                bool changed = false;
                if (newName != null && existing.Name != newName) { existing.Name = newName; changed = true; }
                if (description != null && existing.Description != description) { existing.Description = description; changed = true; }
                if (author != null && existing.Author != author) { existing.Author = author; changed = true; }
                if (changed)
                {
                    // Remove and re-add at same index to trigger CollectionChanged + binding refresh
                    WorkflowCases.RemoveAt(i);
                    WorkflowCases.Insert(i, existing);
                }
                return;
            }
        }
    }

    private async void OpenWorkflowEditorAsync(IWorkflowCase workflow)
    {
        // Check if an editor window is already open for this workflow
        if (UIStateService.WorkflowEditorWindows.TryGetValue(workflow.Id, out var existingWindow))
        {
            existingWindow.Activate();
            return;
        }

        // Create new unified editor window
        var editorWindow = new WorkflowEditorWindow();
        await editorWindow.LoadWorkflowAsync(workflow.Id);

        // Track the window
        UIStateService.WorkflowEditorWindows[workflow.Id] = editorWindow;
        editorWindow.Closed += (_, _) =>
        {
            UIStateService.WorkflowEditorWindows.Remove(workflow.Id);
        };

        UIStateService.ShowWindow(editorWindow);
    }

    private async void DeleteWorkflowAsync(IWorkflowCase workflow)
    {
        var result = await MessageBoxManager.GetMessageBoxStandard(
            TranslateTextWithSuffix("Workflow", "DeleteWorkflow") ?? "Delete Workflow",
            (TranslateTextWithSuffix("Workflow", "DeleteConfirm") ?? "Are you sure you want to delete \"$name\"?")
                .Replace("$name", workflow.Name),
            ButtonEnum.YesNo,
            Icon.Warning
        ).ShowWindowAsync();

        if (result == ButtonResult.Yes)
        {
            await _storageService.DeleteWorkflowAsync(workflow.Id);
            WorkflowCases.Remove(workflow);
            _eventService.Publish(EventNames.WorkflowDeleted, EventArgs.Empty);
        }
    }

    private async void RunWorkflowAsync(IWorkflowCase workflow)
    {
        try
        {
            // Always enter Running state on click — button becomes STOP
            workflow.IsRunning = true;

            if (workflow.TriggerConfig?.TriggerType == "PluginEvent"
                && !string.IsNullOrEmpty(workflow.TriggerConfig.PluginName))
            {
                // Pre-check: is the required plugin connected?
                var pluginServer = App.GetService<KitX.Core.Contract.Plugin.IPluginServer>();
                bool pluginConnected = pluginServer?.Connections
                    .Any(c => c.PluginInfo?.Name == workflow.TriggerConfig.PluginName) ?? false;

                if (!pluginConnected)
                {
                    // Plugin offline: still Running, but Error (yellow light + STOP button)
                    workflow.IsError = true;
                    workflow.ErrorMessage = $"Plugin '{workflow.TriggerConfig.PluginName}' is not connected";
                    RefreshWorkflowInList(workflow);
                    return;
                }

                // Plugin online: clear old errors, register trigger
                workflow.IsError = false;
                workflow.ErrorMessage = null;

                try
                {
                    var triggerManager = App.GetService<KitX.Core.Workflow.TriggerManager>();
                    triggerManager?.RegisterWorkflowTrigger(workflow.Id, workflow.TriggerConfig);
                }
                catch { /* non-critical */ }

                RefreshWorkflowInList(workflow);
            }
            else
            {
                // Manual: clear old errors, then run once.
                workflow.IsError = false;
                workflow.ErrorMessage = null;
                RefreshWorkflowInList(workflow);

                // Offload to thread pool to prevent UI deadlock — script execution
                // may synchronously wait for plugin responses (PluginCall uses
                // TaskCompletionSource.Result), which would deadlock the UI thread
                // if the response callback needs the same SynchronizationContext.
                await Task.Run(async () =>
                {
                    bool success = await _workflowService.RunWorkflowAsync(workflow.Id);
                    _eventService.Publish(EventNames.WorkflowExecutionResult,
                        new WorkflowExecutionResultEventArgs(workflow.Id, success,
                            success ? null : "Workflow execution failed"));
                });
            }
        }
        catch (Exception ex)
        {
            await MessageBoxManager.GetMessageBoxStandard(
                TranslateTextWithSuffix("Workflow", "Error") ?? "Error", ex.Message, icon: Icon.Error)
                .ShowWindowAsync();
        }
    }

    private async void StopWorkflowAsync(IWorkflowCase workflow)
    {
        try
        {
            if (workflow.TriggerConfig?.TriggerType == "PluginEvent")
            {
                // PluginEvent: unregister trigger and mark as stopped
                try
                {
                    var triggerManager = App.GetService<KitX.Core.Workflow.TriggerManager>();
                    triggerManager?.UnregisterWorkflowTrigger(workflow.Id);
                }
                catch { /* non-critical */ }
            }
            else
            {
                // Manual: stop the running execution
                await _workflowService.StopWorkflowAsync(workflow.Id);
            }

            workflow.IsError = false;
            workflow.ErrorMessage = null;
            workflow.IsRunning = false;
            RefreshWorkflowInList(workflow);
        }
        catch (Exception ex)
        {
            await MessageBoxManager.GetMessageBoxStandard(
                TranslateTextWithSuffix("Workflow", "Error") ?? "Error", ex.Message, icon: Icon.Error)
                .ShowWindowAsync();
        }
    }

    internal string? SearchingText { get; set; }

    /// <summary>
    /// Handles workflow execution result events — updates error state on the workflow card.
    /// </summary>
    private void OnWorkflowExecutionResult(object? sender, EventArgs e)
    {
        if (e is not WorkflowExecutionResultEventArgs args) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var workflow = WorkflowCases.FirstOrDefault(w => w.Id == args.WorkflowId);
            if (workflow is null) return;

            if (args.IsSuccess)
            {
                // Success: clear error. Manual workflows return to Stopped.
                workflow.IsError = false;
                workflow.ErrorMessage = null;
                if (workflow.TriggerConfig?.TriggerType != "PluginEvent")
                    workflow.IsRunning = false;
                RefreshWorkflowInList(workflow);
            }
            else
            {
                // Failure: keep Running (STOP button) but mark Error (yellow light).
                // User can click Stop to dismiss the error.
                workflow.IsError = true;
                workflow.ErrorMessage = args.ErrorMessage;
                if (workflow.TriggerConfig?.TriggerType != "PluginEvent")
                    workflow.IsRunning = false;
                RefreshWorkflowInList(workflow);
            }
        });
    }

    /// <summary>
    /// Handles plugin registration events — auto-recovers error workflows
    /// whose required plugin just came online.
    /// </summary>
    private void OnPluginRegistered(object? sender, EventArgs e)
    {
        if (e is not KitX.Core.Event.PluginEventArgs pa || pa.PluginInfo?.Name is null) return;
        var pluginName = pa.PluginInfo.Name;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            foreach (var workflow in WorkflowCases.ToList())
            {
                if (workflow.IsError
                    && workflow.TriggerConfig?.TriggerType == "PluginEvent"
                    && workflow.TriggerConfig.PluginName == pluginName)
                {
                    // Plugin came online → re-register trigger → clear error (green light)
                    try
                    {
                        var tm = App.GetService<KitX.Core.Workflow.TriggerManager>();
                        tm?.RegisterWorkflowTrigger(workflow.Id, workflow.TriggerConfig);
                    }
                    catch { /* non-critical */ }

                    workflow.IsError = false;
                    workflow.ErrorMessage = null;
                    // IsRunning stays true (already was), light turns green
                    RefreshWorkflowInList(workflow);
                }
            }
        });
    }

    /// <summary>
    /// Handles plugin unregistration events — sets error state on running workflows
    /// that depend on the disconnected plugin, and unregisters their triggers.
    /// </summary>
    private void OnPluginUnregistered(object? sender, EventArgs e)
    {
        if (e is not KitX.Core.Event.PluginEventArgs pa || pa.PluginInfo?.Name is null) return;
        var pluginName = pa.PluginInfo.Name;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            foreach (var workflow in WorkflowCases.ToList())
            {
                if (workflow.IsRunning
                    && !workflow.IsError
                    && workflow.TriggerConfig?.TriggerType == "PluginEvent"
                    && workflow.TriggerConfig.PluginName == pluginName)
                {
                    // Plugin went offline → unregister trigger → mark error (yellow light)
                    try
                    {
                        var tm = App.GetService<KitX.Core.Workflow.TriggerManager>();
                        tm?.UnregisterWorkflowTrigger(workflow.Id);
                    }
                    catch { /* non-critical */ }

                    workflow.IsError = true;
                    workflow.ErrorMessage = $"Plugin '{pluginName}' disconnected";
                    // IsRunning stays true → STOP button, yellow light
                    RefreshWorkflowInList(workflow);
                }
            }
        });
    }

    internal int workflowCount = 0;

    internal double noWorkflow_TipHeight = 0;

    internal int WorkflowCount
    {
        get => workflowCount;
        set => this.RaiseAndSetIfChanged(ref workflowCount, value);
    }

    internal double NoWorkflow_TipHeight
    {
        get => noWorkflow_TipHeight;
        set => this.RaiseAndSetIfChanged(ref noWorkflow_TipHeight, value);
    }

    internal string WorkflowCountTip =>
        TranslateTextWithSuffix("Workflow", "Count")
            ?.Replace("$count", WorkflowCount.ToString()) ?? "Language key not found";

    internal static ObservableCollection<IWorkflowCase> WorkflowCases => UIStateService.WorkflowCases;

    internal ReactiveCommand<Unit, Unit>? CreateWorkflowCommand { get; set; }

    internal ReactiveCommand<IWorkflowCase, Unit>? OpenWorkflowCommand { get; set; }

    internal ReactiveCommand<IWorkflowCase, Unit>? DeleteWorkflowCommand { get; set; }

    internal ReactiveCommand<IWorkflowCase, Unit>? RunWorkflowCommand { get; set; }

    internal ReactiveCommand<IWorkflowCase, Unit>? StopWorkflowCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? RefreshWorkflowsCommand { get; set; }
}
