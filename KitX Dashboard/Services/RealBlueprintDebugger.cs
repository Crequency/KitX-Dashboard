namespace KitX.Dashboard.Services;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KitX.Core.Contract.Workflow;
using Serilog;

// ─────────────────────────────────────────────────────────────────────────────
// RealBlueprintDebugger — replaces the WorkflowStubs.cs BlueprintDebugger stub.
// Implements IBlueprintDebugController for the new IR library's RoslynExecutionBackend.
//
// The generated workflow code calls G.Debugger.CheckpointAsync(statementId, blockName, ct)
// between every statement. This controller:
//   • Fires NodeExecuting/NodeExecuted events for UI highlight
//   • Pauses (await) when StepByStep or when a breakpoint is hit
//   • Resumes on Continue()/StepNext() (SemaphoreSlim signal)
//   • Updates the variable snapshot for the debug panel
// (Dashboard-Frontend-Refactor-Handoff.md §F1.5)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A real IBlueprintDebugController that bridges the IR execution backend's
/// checkpoint calls to the Dashboard's debug UI (node highlight, step, breakpoints).
/// </summary>
public sealed class RealBlueprintDebugger : IBlueprintDebugController
{
    private readonly SemaphoreSlim _stepSignal = new(0, 1);
    private readonly HashSet<string> _breakpoints = new();
    private bool _paused;

    // ── Events (consumed by BlueprintEditorViewModel for UI updates) ──

    public event Action<string>? NodeExecuting;
    public event Action<string>? NodeExecuted;
    public event Action<string>? BlockEntered;
    public event Action<string, object?>? VariableChanged;
    public event Action? ExecutionPaused;
    public event Action? ExecutionResumed;

    // ── Properties ──

    public ExecutionSpeed Speed { get; private set; } = ExecutionSpeed.StepByStep;
    public bool IsPaused => _paused;
    public IReadOnlyDictionary<string, object?> CurrentVariableSnapshot { get; private set; }
        = new Dictionary<string, object?>();

    // ── Breakpoints ──

    public void SetBreakpoint(string nodeId) => _breakpoints.Add(nodeId);
    public void RemoveBreakpoint(string nodeId) => _breakpoints.Remove(nodeId);
    public void ClearBreakpoints() => _breakpoints.Clear();
    public bool HasBreakpoint(string nodeId) => _breakpoints.Contains(nodeId);

    // ── Flow control ──

    public void Pause()
    {
        _paused = true;
        ExecutionPaused?.Invoke();
    }

    public void StepNext()
    {
        // Release the semaphore to allow one more checkpoint to proceed.
        _paused = false;
        _stepSignal.Release();
    }

    public void Continue()
    {
        _paused = false;
        ExecutionResumed?.Invoke();
        _stepSignal.Release();
    }

    public void SetSpeed(ExecutionSpeed speed) => Speed = speed;

    // ── Variable snapshot ──

    public void UpdateVariableSnapshot(Dictionary<string, object?> variables)
    {
        CurrentVariableSnapshot = variables;
        foreach (var (name, value) in variables)
            VariableChanged?.Invoke(name, value);
    }

    // ── Checkpoint (called by the generated workflow code between statements) ──

    public async Task CheckpointAsync(string statementId, string? blockName, CancellationToken cancellationToken)
    {
        // Fire NodeExecuting for UI highlight.
        NodeExecuting?.Invoke(statementId);

        if (blockName is { Length: > 0 })
            BlockEntered?.Invoke(blockName);

        // Pause logic: StepByStep always pauses; breakpoints pause if hit.
        bool shouldPause = Speed == ExecutionSpeed.StepByStep || HasBreakpoint(statementId);

        if (shouldPause)
        {
            _paused = true;
            ExecutionPaused?.Invoke();

            // Wait for StepNext() or Continue() to release the signal.
            await _stepSignal.WaitAsync(cancellationToken).ConfigureAwait(false);

            ExecutionResumed?.Invoke();
        }

        // Fire NodeExecuted after the pause (or immediately if no pause).
        NodeExecuted?.Invoke(statementId);
    }
}
