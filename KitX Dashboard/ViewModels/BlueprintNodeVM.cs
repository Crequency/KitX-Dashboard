using CommunityToolkit.Mvvm.ComponentModel;
using KitX.Core.Contract.Workflow;
using NodifyM.Avalonia.ViewModelBase;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// Replaces NodeViewModel + _nodeIdMap + BlueprintNodeContentViewModel.
/// Input/Output collections inherited from NodeViewModelBase hold BlueprintConnectorVM instances.
/// </summary>
public partial class BlueprintNodeVM : NodeViewModelBase
{
    /// <summary>Original BlueprintNode.Id for round-trip export</summary>
    [ObservableProperty]
    private string _blueprintNodeId = string.Empty;

    /// <summary>Node type for category color rendering</summary>
    [ObservableProperty]
    private BlueprintNodeType _nodeType = BlueprintNodeType.Entry;

    /// <summary>Header color hex (category primary color)</summary>
    [ObservableProperty]
    private string _categoryColor = "#607D8B";

    /// <summary>Body color hex (category lighter color)</summary>
    [ObservableProperty]
    private string _categoryColorLight = "#455A64";

    /// <summary>Display title shown in node header</summary>
    [ObservableProperty]
    private string _displayTitle = string.Empty;

    /// <summary>Node name used for type inference and round-trip export</summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>Returns (Primary, Light) hex color pair for a node category</summary>
    public static (string Primary, string Light) GetCategoryColors(BlueprintNodeType type) => type switch
    {
        BlueprintNodeType.Entry => ("#4CAF50", "#2E7D32"),          // Green
        BlueprintNodeType.Branch or BlueprintNodeType.Loop
            or BlueprintNodeType.Break => ("#FF9800", "#BF6E00"),   // Orange
        BlueprintNodeType.Const or BlueprintNodeType.Get
            or BlueprintNodeType.Set => ("#2196F3", "#1565C0"),     // Blue
        BlueprintNodeType.Call or BlueprintNodeType.CallHelper
            or BlueprintNodeType.Print or BlueprintNodeType.Pause => ("#9C27B0", "#7B1FA2"), // Purple
        _ => ("#607D8B", "#455A64")                                 // Gray fallback
    };
}
