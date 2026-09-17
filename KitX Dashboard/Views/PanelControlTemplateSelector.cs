using Avalonia.Controls;
using Avalonia.Controls.Templates;
using KitX.Dashboard.ViewModels;

namespace KitX.Dashboard.Views;

/// <summary>
/// Selects the render template for a <see cref="PanelControlViewModel"/> based on its
/// <see cref="PanelControlViewModel.Type"/> discriminant (the fixed ten-control set).
/// Each template is supplied from the view's resources.
/// </summary>
public sealed class PanelControlTemplateSelector : IDataTemplate
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
        if (param is not PanelControlViewModel vm)
            return null;
        var template = vm.Type switch
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

    public bool Match(object? data) => data is PanelControlViewModel;
}
