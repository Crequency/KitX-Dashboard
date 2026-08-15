using Avalonia.Controls;
using Avalonia.Controls.Templates;
using KitX.ToolKit.Models;

namespace KitX.Dashboard.Views;

/// <summary>
/// Selects the on-canvas preview template for a <see cref="UiControl"/> definition based on
/// its <see cref="UiControl.Type"/> discriminant (the fixed ten-control set). Reuses the same
/// card-styled control visuals as the Panel host, but renders the control's <b>definition</b>
/// (static preview) rather than a live bound instance. Templates are supplied from the view.
/// </summary>
public sealed class UiControlPreviewTemplateSelector : IDataTemplate
{
    public IDataTemplate? TextTemplate { get; set; }
    public IDataTemplate? IconTemplate { get; set; }
    public IDataTemplate? ButtonTemplate { get; set; }
    public IDataTemplate? InputTemplate { get; set; }
    public IDataTemplate? NumberTemplate { get; set; }
    public IDataTemplate? SelectTemplate { get; set; }
    public IDataTemplate? SwitchTemplate { get; set; }
    public IDataTemplate? LogTemplate { get; set; }
    public IDataTemplate? ProgressTemplate { get; set; }
    public IDataTemplate? DialogTemplate { get; set; }

    public Control? Build(object? param)
    {
        if (param is not UiControl control)
            return null;
        var template = control.Type switch
        {
            "Text" => TextTemplate,
            "Icon" => IconTemplate,
            "Button" => ButtonTemplate,
            "Input" => InputTemplate,
            "Number" => NumberTemplate,
            "Select" => SelectTemplate,
            "Switch" => SwitchTemplate,
            "Log" => LogTemplate,
            "Progress" => ProgressTemplate,
            "Dialog" => DialogTemplate,
            _ => TextTemplate,
        };
        return template?.Build(param);
    }

    public bool Match(object? data) => data is UiControl;
}
