using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KitX.Core.Contract.Workflow;

namespace KitX.Dashboard.Services;

// Phase 12-prep stubs.
//
// The legacy KitX.Workflow library has been archived to Package\Archive\KitX.Workflow.
// Until the Dashboard front-end is migrated to the new KitX.WorkflowIR CFG-session
// model, a few places in BlueprintEditorViewModel still reference workflow-side
// abstractions/concretes that previously lived in the old library. These minimal
// local stand-ins keep the Dashboard compiling; the real implementations will be
// wired back up (via the new library's DI entry AddKitXWorkflowIR) when the
// editor migration lands. All stubs throw NotImplementedException because the
// features are intentionally disconnected for now.

/// <summary>
/// Minimal stand-in for the old KitX.Workflow.Abstractions.IBlockScriptExecutor.
/// Only the <see cref="SetDebugger"/> surface is currently consumed by the editor;
/// the full execution contract will be re-introduced from KitX.WorkflowIR.
/// </summary>
public interface IBlockScriptExecutor
{
    /// <summary>Attaches (or detaches, when null) a debug controller.</summary>
    void SetDebugger(IBlueprintDebugController? debugger);
}

/// <summary>
/// Stand-in for the legacy KitX.Workflow.BlockScripting.BlueprintDebugger concrete.
/// Implements the contract surface so the editor compiles, but every member throws
/// because interactive blueprint debugging is pending migration to the new IR backend.
/// </summary>
public class BlueprintDebugger : IBlueprintDebugController
{
    public event Action<string>? NodeExecuting;
    public event Action<string>? NodeExecuted;
    public event Action<string>? BlockEntered;
    public event Action<string, object?>? VariableChanged;
    public event Action? ExecutionPaused;
    public event Action? ExecutionResumed;

    public ExecutionSpeed Speed => throw new NotImplementedException();
    public bool IsPaused => throw new NotImplementedException();
    public IReadOnlyDictionary<string, object?> CurrentVariableSnapshot =>
        throw new NotImplementedException();

    public void SetBreakpoint(string nodeId) => throw new NotImplementedException();
    public void RemoveBreakpoint(string nodeId) => throw new NotImplementedException();
    public void ClearBreakpoints() => throw new NotImplementedException();
    public bool HasBreakpoint(string nodeId) => throw new NotImplementedException();
    public void Pause() => throw new NotImplementedException();
    public void StepNext() => throw new NotImplementedException();
    public void Continue() => throw new NotImplementedException();
    public void SetSpeed(ExecutionSpeed speed) => throw new NotImplementedException();
    public void UpdateVariableSnapshot(Dictionary<string, object?> variables) =>
        throw new NotImplementedException();
    public Task CheckpointAsync(string statementId, string? blockName, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

/// <summary>
/// Stand-in for the legacy KitX.Workflow.Services.WorkflowOutput static facade.
/// The execution-output buffer will be re-provided by the new KitX.WorkflowIR
/// execution backend; until then this returns empty output.
/// </summary>
public static class WorkflowOutput
{
    /// <summary>Always returns empty (workflow execution disconnected).</summary>
    public static string GetAndClear() => string.Empty;
}
