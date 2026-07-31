namespace KitX.Dashboard.Services;

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Backend;
using KitX.Workflow.Serialization;
using KitX.WorkflowV6.Backend.RoslynBackend;
using Serilog;

// ─────────────────────────────────────────────────────────────────────────────
// WorkflowSessionManager — lightweight IWorkflowManagementService orchestrator.
//
// The new IR architecture has no "run-by-id" service (IExecutionBackend takes an
// IrWorkflow, not a workflowId). This orchestrator bridges that gap: it loads the
// stored IR (KcsFileFormat.IrData) for a workflow id, deserializes it, and runs it
// through IExecutionBackend. Run/stop state is tracked by id via a CancellationToken
// per active run.
//
// Since P3-δ the manager also dispatches v6 workflows (KcsFileFormat.IrVersion ==
// "v6"): it deserializes via the v6 WorkflowSerializer, applies the persisted
// VariableConstants overrides (the same semantics the editor uses at Run-time), and
// executes through StructuredRoslynBackend. This closes the "run-by-id for v6"
// gap that the ITriggerManager routing path depends on.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Runs/stops workflows by id, backed by the stored IR (KcsFileFormat) and the
/// execution backends. Dispatches v5 (WorkflowIR) and v6 (WorkflowV6) formats.
/// </summary>
public sealed class WorkflowSessionManager : IWorkflowManagementService
{
    private readonly IWorkflowStorageService _storage;
    private readonly IExecutionBackend _backend;
    private readonly StructuredRoslynBackend? _v6Backend;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new();

    public WorkflowSessionManager(IWorkflowStorageService storage, IExecutionBackend backend,
        StructuredRoslynBackend? v6Backend = null)
    {
        _storage = storage ?? throw new System.ArgumentNullException(nameof(storage));
        _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
        _v6Backend = v6Backend;
    }

    /// <inheritdoc/>
    public async Task<bool> RunWorkflowAsync(string workflowId)
    {
        var result = await RunWorkflowWithDetailsAsync(workflowId);
        return result.IsSuccess;
    }

    /// <inheritdoc/>
    public async Task<WorkflowRunResult> RunWorkflowWithDetailsAsync(string workflowId)
    {
        var data = await _storage.LoadWorkflowDataAsync(workflowId);
        if (data == null || string.IsNullOrWhiteSpace(data.IrData) || data.IrData == "{}")
            return new WorkflowRunResult(false, $"Workflow '{workflowId}' not found or IR invalid", null);

        // Stop any prior run of this id (single active run per workflow).
        if (_running.TryRemove(workflowId, out var priorCts))
            priorCts.Cancel();

        var cts = new CancellationTokenSource();
        _running[workflowId] = cts;

        try
        {
            if (data.IrVersion == "v6")
            {
                if (_v6Backend is null)
                    return new WorkflowRunResult(false, "v6 execution backend not available", null);

                var v6Ir = KitX.WorkflowV6.Serialization.WorkflowSerializer.Deserialize(data.IrData);
                if (v6Ir is null)
                    return new WorkflowRunResult(false, $"Workflow '{workflowId}' v6 IR invalid", null);

                v6Ir = KitX.WorkflowV6.Ir.WorkflowOverrides.ApplyConstantOverrides(
                    v6Ir, ToStringOverrides(data.VariableConstants));

                Log.Information("[WorkflowSessionManager] Running v6 workflow {Id}", workflowId);
                var result = await _v6Backend.ExecuteAsync(v6Ir, null, cts.Token);
                return new WorkflowRunResult(result.IsSuccess, result.ErrorMessage, result.Output);
            }

            var v5Ir = LoadV5Ir(data.IrData);
            if (v5Ir is null)
                return new WorkflowRunResult(false, $"Workflow '{workflowId}' v5 IR invalid", null);

            Log.Information("[WorkflowSessionManager] Running workflow {Id}", workflowId);
            var v5Result = await _backend.ExecuteAsync(v5Ir, null, cts.Token);
            return new WorkflowRunResult(v5Result.IsSuccess, v5Result.ErrorMessage, v5Result.Output);
        }
        catch (System.OperationCanceledException)
        {
            Log.Information("[WorkflowSessionManager] Workflow {Id} cancelled", workflowId);
            return new WorkflowRunResult(false, "Cancelled", null);
        }
        catch (System.Exception ex)
        {
            Log.Error(ex, "[WorkflowSessionManager] Workflow {Id} failed", workflowId);
            return new WorkflowRunResult(false, ex.Message, null);
        }
        finally
        {
            _running.TryRemove(workflowId, out _);
        }
    }

    /// <inheritdoc/>
    public Task<bool> StopWorkflowAsync(string workflowId)
    {
        if (_running.TryRemove(workflowId, out var cts))
        {
            cts.Cancel();
            Log.Information("[WorkflowSessionManager] Stopped workflow {Id}", workflowId);
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Validates that the stored IR is deserializable in its own format (v5/v6) —
    /// a cheap compile-readiness check for both engines.
    /// </remarks>
    public async Task<bool> CompileAndPersistWorkflowAsync(string workflowId)
    {
        var data = await _storage.LoadWorkflowDataAsync(workflowId);
        if (data == null || string.IsNullOrWhiteSpace(data.IrData) || data.IrData == "{}")
            return false;
        if (data.IrVersion == "v6")
            return KitX.WorkflowV6.Serialization.WorkflowSerializer.Deserialize(data.IrData) != null;
        return LoadV5Ir(data.IrData) != null;
    }

    private static KitX.Workflow.Ir.IrWorkflow? LoadV5Ir(string irData)
    {
        try { return IrSerializer.Deserialize(irData); }
        catch (System.Exception ex)
        {
            Log.Error(ex, "[WorkflowSessionManager] Failed to deserialize v5 IR");
            return null;
        }
    }

    /// <summary>Maps persisted <c>VariableConstants</c> (varName → object?) to string overrides.</summary>
    private static Dictionary<string, string?>? ToStringOverrides(Dictionary<string, object?>? variableConstants)
        => variableConstants is null || variableConstants.Count == 0
            ? null
            : variableConstants.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString());
}
