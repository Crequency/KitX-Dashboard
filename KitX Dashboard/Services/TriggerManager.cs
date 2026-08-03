using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KitX.Core.Contract.Event;
using KitX.Core.Contract.Plugin;
using KitX.Core.Contract.Workflow;
using KitX.Shared.CSharp.WebCommand;
using KitX.Shared.CSharp.WebCommand.Infos;
using Serilog;
using PluginMessageReceivedEventArgs = KitX.Core.Contract.Plugin.Events.PluginMessageReceivedEventArgs;

namespace KitX.Dashboard.Services;

// ─────────────────────────────────────────────────────────────────────────────
// TriggerManager — routes plugin TriggerFired signals to subscribed workflows.
//
// Rebuilt from the archived Package/Archive/KitX.Workflow/Services/TriggerManager.cs
// (P3-δ): the archived version depended on the deleted KitX.Workflow.Hosting
// ServiceLocator; this version takes its services via constructor injection (all
// resolvable from the Dashboard DI container).
//
// A trigger is a pure signal (equivalent to pressing "Run"): plugins fire it via the
// TriggerFired WebCommand; this manager matches the firing plugin/trigger against
// registered TriggerConfig subscriptions ("PluginName.TriggerName" or wildcard
// "PluginName.*") and runs each matching workflow by id.
//
// Subscriptions are RUNTIME state only: RegisterWorkflowTrigger is called when the
// user starts a PluginEvent workflow (Run button — which also verifies the plugin is
// connected), UnregisterWorkflowTrigger on Stop. There is no startup re-subscription
// from persisted TriggerConfig (the archived InitializeFromPersistedWorkflows silently
// armed every saved workflow at launch without syncing the card's mounted indicator).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Routes plugin trigger signals to the workflows that subscribed to them.
/// </summary>
public class TriggerManager : ITriggerManager
{
    private readonly IPluginServer _pluginServer;
    private readonly IWorkflowManagementService _workflowManagement;
    private readonly IEventService _eventService;
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Subscription index: key = "PluginName.TriggerName" or "PluginName.*", value = workflow IDs.</summary>
    private Dictionary<string, List<string>> _triggerSubscriptions = new();

    /// <summary>Workflow trigger configurations: key = workflowId.</summary>
    private readonly Dictionary<string, TriggerConfig> _workflowTriggers = new();

    public TriggerManager(IPluginServer pluginServer,
        IWorkflowManagementService workflowManagement, IEventService eventService)
    {
        _pluginServer = pluginServer ?? throw new ArgumentNullException(nameof(pluginServer));
        _workflowManagement = workflowManagement ?? throw new ArgumentNullException(nameof(workflowManagement));
        _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
        _pluginServer.PluginMessageReceived += OnPluginMessageReceived;
        Log.Information("[TriggerManager] Initialized and subscribed to PluginMessageReceived");
    }

    /// <inheritdoc/>
    public void RegisterWorkflowTrigger(string workflowId, TriggerConfig config)
    {
        if (config.TriggerType != "PluginEvent" || string.IsNullOrEmpty(config.PluginName))
            return;

        _workflowTriggers[workflowId] = config;
        RebuildSubscriptionIndex();

        Log.Information("[TriggerManager] Registered trigger for workflow {WorkflowId}: " +
            "PluginName={PluginName}, TriggerName={TriggerName}",
            workflowId, config.PluginName, config.TriggerName);
    }

    /// <inheritdoc/>
    public void UnregisterWorkflowTrigger(string workflowId)
    {
        if (_workflowTriggers.Remove(workflowId))
        {
            RebuildSubscriptionIndex();
            Log.Information("[TriggerManager] Unregistered trigger for workflow {WorkflowId}", workflowId);
        }
    }

    /// <summary>Rebuilds the subscription index after any configuration change.</summary>
    private void RebuildSubscriptionIndex()
    {
        var newIndex = new Dictionary<string, List<string>>();

        foreach (var (workflowId, config) in _workflowTriggers)
        {
            if (config.TriggerType != "PluginEvent" || string.IsNullOrEmpty(config.PluginName))
                continue;

            var key = string.IsNullOrEmpty(config.TriggerName)
                ? $"{config.PluginName}.*"
                : $"{config.PluginName}.{config.TriggerName}";

            if (!newIndex.ContainsKey(key))
                newIndex[key] = [];
            newIndex[key].Add(workflowId);
        }

        _triggerSubscriptions = newIndex;
    }

    /// <summary>Handles plugin messages, identifies TriggerFired, and routes to matching workflows.</summary>
    private void OnPluginMessageReceived(object? sender, PluginMessageReceivedEventArgs e)
    {
        try
        {
            if (e.Message is null) return;

            var kwc = JsonSerializer.Deserialize<Request>(e.Message, _serializerOptions);
            if (kwc?.Content is null) return;

            var command = JsonSerializer.Deserialize<Command>(kwc.Content, _serializerOptions);
            if (command.Request != CommandRequestInfo.TriggerFired) return;

            // Find the plugin that sent this message.
            var connectionId = e.ConnectionId;
            var connection = _pluginServer.Connections
                .FirstOrDefault(c => c.ConnectionId == connectionId);
            var pluginName = connection?.PluginInfo?.Name ?? "Unknown";

            var triggerName = command.Tags?.TryGetValue("TriggerName", out var name) == true
                ? name : "Unknown";

            Log.Information("[TriggerManager] Trigger '{TriggerName}' fired by plugin '{PluginName}'",
                triggerName, pluginName);

            // Match against specific and wildcard subscriptions.
            var specificKey = $"{pluginName}.{triggerName}";
            var wildcardKey = $"{pluginName}.*";

            var matchingWorkflowIds = new HashSet<string>();
            if (_triggerSubscriptions.TryGetValue(specificKey, out var specific))
                foreach (var id in specific) matchingWorkflowIds.Add(id);
            if (_triggerSubscriptions.TryGetValue(wildcardKey, out var wildcard))
                foreach (var id in wildcard) matchingWorkflowIds.Add(id);

            foreach (var workflowId in matchingWorkflowIds)
            {
                Log.Information("[TriggerManager] Triggering workflow: {WorkflowId}", workflowId);
                _ = Task.Run(async () =>
                {
                    var runResult = await _workflowManagement.RunWorkflowWithDetailsAsync(workflowId);
                    _eventService.Publish(
                        WorkflowEventNames.WorkflowExecutionResult,
                        new WorkflowExecutionResultEventArgs(
                            workflowId, runResult.IsSuccess,
                            runResult.IsSuccess ? null : runResult.ErrorMessage ?? "Workflow execution failed",
                            runResult.Output));
                });
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[TriggerManager] Error processing trigger message");
        }
    }
}
