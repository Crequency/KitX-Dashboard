using System.Collections.Generic;
using KitX.Shared.CSharp.Plugin;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// Represents a plugin function entry in the BlueprintEditor node palette.
/// Used as a bindable item in the "Plugin Functions" dynamic list.
/// </summary>
public class PluginFunctionPaletteItem
{
    public string PluginName { get; init; } = string.Empty;
    public string FunctionName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public List<Parameter> Parameters { get; init; } = [];
    public string ReturnValueType { get; init; } = "void";
}
