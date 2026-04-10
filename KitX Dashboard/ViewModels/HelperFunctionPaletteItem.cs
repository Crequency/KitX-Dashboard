using System.Collections.Generic;
using KitX.Core.Contract.Workflow;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// Represents a helper function entry in the BlueprintEditor node palette.
/// Used as a bindable item in the "Helper Functions" dynamic list.
/// </summary>
public class HelperFunctionPaletteItem
{
    public string FunctionName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public List<HelperFunctionParameter> Parameters { get; init; } = [];
    public string ReturnType { get; init; } = "object";
}
