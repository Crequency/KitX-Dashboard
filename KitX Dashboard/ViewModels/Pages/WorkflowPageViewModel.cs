using System;
using System.Collections.ObjectModel;
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
            var workflow = await _storageService.CreateWorkflowAsync("New Workflow");
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
        };

        _eventService.Subscribe(EventNames.LanguageChanged, (s, e) =>
        {
            this.RaisePropertyChanged(nameof(WorkflowCountTip));
        });
    }

    private async Task LoadWorkflowsAsync()
    {
        var workflows = await _storageService.DiscoverWorkflowsAsync();
        WorkflowCases.Clear();
        foreach (var w in workflows)
            WorkflowCases.Add(w);
    }

    private async void OpenWorkflowEditorAsync(IWorkflowCase workflow)
    {
        // Check if an editor window is already open for this workflow
        if (UIStateService.WorkflowEditorWindows.TryGetValue(workflow.Id, out var existingWindow))
        {
            existingWindow.Activate();
            return;
        }

        // Create new editor window
        // TODO: Phase 3 - replace with WorkflowEditorWindow and pass workflowId
        var editorWindow = new WorkflowScriptEditorWindow();

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
            "Delete Workflow",
            $"Are you sure you want to delete \"{workflow.Name}\"?",
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
            await _workflowService.RunWorkflowAsync(workflow.Id);
            workflow.IsRunning = true;
            this.RaisePropertyChanged(nameof(WorkflowCases));
        }
        catch (Exception ex)
        {
            await MessageBoxManager.GetMessageBoxStandard("Error", ex.Message, icon: Icon.Error)
                .ShowWindowAsync();
        }
    }

    private async void StopWorkflowAsync(IWorkflowCase workflow)
    {
        try
        {
            await _workflowService.StopWorkflowAsync(workflow.Id);
            workflow.IsRunning = false;
            this.RaisePropertyChanged(nameof(WorkflowCases));
        }
        catch (Exception ex)
        {
            await MessageBoxManager.GetMessageBoxStandard("Error", ex.Message, icon: Icon.Error)
                .ShowWindowAsync();
        }
    }

    internal string? SearchingText { get; set; }

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
