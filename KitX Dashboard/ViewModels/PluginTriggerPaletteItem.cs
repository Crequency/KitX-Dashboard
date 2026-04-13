namespace KitX.Dashboard.ViewModels;

/// <summary>
/// Represents a plugin trigger entry in the BlueprintEditor node palette.
/// Used as a bindable item in the "Plugin Triggers" dynamic list within the Entry group.
/// </summary>
public class PluginTriggerPaletteItem
{
    public string PluginName { get; init; } = string.Empty;
    public string TriggerName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
}
