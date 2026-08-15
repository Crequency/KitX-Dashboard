using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using KitX.Core.Contract.Workflow;
using KitX.ToolKit.Models;
using Serilog;
using V6Workflow = KitX.WorkflowV6.Ir.Workflow;

namespace KitX.Dashboard.Services;

/// <summary>
/// File access for a ToolKit's <b>bundled</b> workflows (Bench RFC §10 storage layout:
/// <c>Data/Toolkits/{toolkitId}/workflows/*.kcs</c>). This mirrors
/// <c>KitX.ToolKit.Bench.ToolkitFileStore</c> on the Dashboard side without widening the
/// ToolKit contract: it writes the minimal v6 template for "new workflow" and gives the
/// v6 editor an optional bundle path for load/save.
/// </summary>
public static class ToolkitWorkflowFileService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Built-in minimal runnable v6 workflow template (UX v2 §4.5/V17). The v6 IR has no
    /// Entry/Return statements — an empty body IS the minimal runnable program, and the
    /// editor projects it as the default empty program on open.
    /// </summary>
    private static readonly V6Workflow MinimalWorkflowTemplate = new();

    /// <summary>Absolute root of a ToolKit's storage directory.</summary>
    public static string GetToolkitRoot(string toolkitId)
        => Path.Combine(AppContext.BaseDirectory, "Data", "Toolkits", toolkitId);

    /// <summary>Resolves a config workflow's relative <c>File</c> to an absolute path under the ToolKit root.</summary>
    public static string ResolveWorkflowPath(string toolkitId, string relativeFile)
    {
        if (string.IsNullOrWhiteSpace(relativeFile))
            relativeFile = "workflow.kcs";
        if (!relativeFile.EndsWith(".kcs", StringComparison.OrdinalIgnoreCase))
            relativeFile += ".kcs";

        var root = GetToolkitRoot(toolkitId);
        var path = Path.GetFullPath(Path.Combine(root, relativeFile));
        if (!path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(path, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Workflow path escapes the ToolKit root.");

        return path;
    }

    /// <summary>
    /// Writes the built-in minimal runnable v6 workflow template (UX v2 §4.5/V17).
    /// </summary>
    public static async Task WriteMinimalWorkflowAsync(Toolkit toolkit, ToolkitWorkflow workflow)
    {
        var path = ResolveWorkflowPath(toolkit.GetId(), workflow.File);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var now = DateTime.UtcNow;
        var kcs = new KcsFileFormat
        {
            Id = workflow.Id,
            Name = workflow.Name,
            Description = string.Empty,
            Author = toolkit.Meta.Author,
            CreatedTime = now,
            LastModifiedTime = now,
            IrVersion = "v6",
            VariableConstants = [],
            IrData = KitX.WorkflowV6.Serialization.WorkflowSerializer.Serialize(MinimalWorkflowTemplate),
        };

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(kcs, JsonOptions));
        Log.Information("[ToolkitWorkflowFileService] Wrote minimal workflow {Id} to {Path}", workflow.Id, path);
    }

    /// <summary>Loads a KCS envelope from an explicit bundle path (editor entry point).</summary>
    public static async Task<KcsFileFormat?> LoadAsync(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<KcsFileFormat>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[ToolkitWorkflowFileService] Failed to load {Path}", path);
            return null;
        }
    }

    /// <summary>Saves a KCS envelope to an explicit bundle path (editor save path).</summary>
    public static async Task SaveAsync(string path, KcsFileFormat kcs)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(kcs, JsonOptions));
        Log.Information("[ToolkitWorkflowFileService] Saved workflow {Id} to {Path}", kcs.Id, path);
    }
}
