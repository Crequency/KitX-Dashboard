using Avalonia;

namespace KitX.Dashboard.Controls;

/// <summary>
/// Attached properties for Blueprint pin styling on NodeEditor Pin controls.
/// These allow the Pin ControlTheme template to access pin type information
/// without modifying the NodeEditorAvalonia library.
/// </summary>
public class PinProperties
{
    /// <summary>Whether this pin is an execution flow pin (triangle) vs data pin (circle)</summary>
    public static readonly AttachedProperty<bool> IsExecutionProperty =
        AvaloniaProperty.RegisterAttached<PinProperties, Avalonia.Controls.Control, bool>("IsExecution");

    /// <summary>Hex color string for this pin's type (e.g., "#32CD32" for execution)</summary>
    public static readonly AttachedProperty<string> PinTypeColorProperty =
        AvaloniaProperty.RegisterAttached<PinProperties, Avalonia.Controls.Control, string>("PinTypeColor", "#FFFFFF");

    /// <summary>Whether this pin currently has a connection</summary>
    public static readonly AttachedProperty<bool> IsConnectedProperty =
        AvaloniaProperty.RegisterAttached<PinProperties, Avalonia.Controls.Control, bool>("IsConnected");

    public static bool GetIsExecution(AvaloniaObject obj) => obj.GetValue(IsExecutionProperty);
    public static void SetIsExecution(AvaloniaObject obj, bool value) => obj.SetValue(IsExecutionProperty, value);

    public static string GetPinTypeColor(AvaloniaObject obj) => obj.GetValue(PinTypeColorProperty);
    public static void SetPinTypeColor(AvaloniaObject obj, string value) => obj.SetValue(PinTypeColorProperty, value);

    public static bool GetIsConnected(AvaloniaObject obj) => obj.GetValue(IsConnectedProperty);
    public static void SetIsConnected(AvaloniaObject obj, bool value) => obj.SetValue(IsConnectedProperty, value);
}
