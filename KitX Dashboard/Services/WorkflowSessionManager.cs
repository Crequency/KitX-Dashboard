namespace KitX.Dashboard.Services;

using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using KitX.Core.Contract.Workflow;
using KitX.Workflow.Backend;
using KitX.Workflow.Serialization;
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
// This replaces the archived KitX.Workflow.Services.WorkflowManagementService,
// which depended on the legacy CFG pipeline + IBlockScriptService.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Runs/stops workflows by id, backed by the stored IR (KcsFileFormat v2) and
/// <see cref="IExecutionBackend"/>.
/// </summary>
public sealed class WorkflowSessionManager : IWorkflowManagementService
{
    private readonly IWorkflowStorageService _storage;
    private readonly IExecutionBackend _backend;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new();

    public WorkflowSessionManager(IWorkflowStorageService storage, IExecutionBackend backend)
    {
        _storage = storage ?? throw new System.ArgumentNullException(nameof(storage));
        _backend = backend ?? throw new System.ArgumentNullException(nameof(backend));
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
        var ir = await LoadIrAsync(workflowId);
        if (ir == null)
            return new WorkflowRunResult(false, $"Workflow '{workflowId}' not found or IR invalid", null);

        // Stop any prior run of this id (single active run per workflow).
        if (_running.TryRemove(workflowId, out var priorCts))
            priorCts.Cancel();

        var cts = new CancellationTokenSource();
        _running[workflowId] = cts;

        try
        {
            Log.Information("[WorkflowSessionManager] Running workflow {Id}", workflowId);
            var result = await _backend.ExecuteAsync(ir, null, cts.Token);
            return new WorkflowRunResult(result.IsSuccess, result.ErrorMessage, result.Output);
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
    /// The Roslyn backend compiles on each ExecuteAsync (no separate persist step).
    /// This method validates the stored IR is deserializable — a cheap compile-readiness check.
    /// </remarks>
    public async Task<bool> CompileAndPersistWorkflowAsync(string workflowId)
    {
        var ir = await LoadIrAsync(workflowId);
        return ir != null;
    }

    private async Task<KitX.Workflow.Ir.IrWorkflow?> LoadIrAsync(string workflowId)
    {
        var data = await _storage.LoadWorkflowDataAsync(workflowId);
        if (data == null || string.IsNullOrWhiteSpace(data.IrData) || data.IrData == "{}")
        {
            Log.Warning("[WorkflowSessionManager] No IR data for workflow {Id}", workflowId);
            return null;
        }
        try { return IrSerializer.Deserialize(data.IrData); }
        catch (System.Exception ex)
        {
            Log.Error(ex, "[WorkflowSessionManager] Failed to deserialize IR for workflow {Id}", workflowId);
            return null;
        }
    }
}
