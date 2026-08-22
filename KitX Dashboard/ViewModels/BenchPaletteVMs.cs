using System.Collections.ObjectModel;
using Material.Icons;

namespace KitX.Dashboard.ViewModels;

/// <summary>One addable component in the Bench palette (Bench UX v2 §4.2).</summary>
public sealed class BenchPaletteItemVM
{
    public BenchPaletteItemVM(string key, string title, string group, MaterialIconKind icon, string? hint = null)
    {
        Key = key;
        Title = title;
        Group = group;
        Icon = icon;
        Hint = hint;
    }

    /// <summary>Dispatch key used by the palette command (<c>Manual</c>, <c>Timer</c>, <c>Workflow</c>, ...).</summary>
    public string Key { get; }

    public string Title { get; }

    public string Group { get; }

    public MaterialIconKind Icon { get; }

    public string? Hint { get; }

    public bool IsVisible { get; set; } = true;

    public bool IsEnabled { get; set; } = true;
}

/// <summary>A grouped, search-filtered palette section (Bench UX v2 §4.1).</summary>
public sealed class BenchPaletteGroupVM
{
    public BenchPaletteGroupVM(string title, params BenchPaletteItemVM[] items)
    {
        Title = title;
        foreach (var item in items)
            Items.Add(item);
    }

    public string Title { get; }

    public ObservableCollection<BenchPaletteItemVM> Items { get; } = [];

    public bool HasVisibleItems { get; set; }
}

/// <summary>
/// A full ConfigValidator diagnostic with a best-effort node target parsed from the
/// validator message (Bench UX v2 §4.6). The canvas turns the target into a locate action.
/// </summary>
public sealed class BenchDiagnosticVM
{
    public BenchDiagnosticVM(string message, string? nodeId, string? nodeKind)
    {
        Message = message;
        NodeId = nodeId;
        NodeKind = nodeKind;
    }

    public string Message { get; }

    /// <summary>Best-effort canvas node id (trigger id / workflow id / "panel").</summary>
    public string? NodeId { get; }

    /// <summary>Canvas node discriminant label, used for locate dispatch.</summary>
    public string? NodeKind { get; }

    public bool CanLocate => !string.IsNullOrWhiteSpace(NodeId);
}
