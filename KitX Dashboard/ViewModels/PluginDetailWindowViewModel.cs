using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using KitX.Dashboard.Services;
using KitX.Shared.Plugin;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Text;

namespace KitX.Dashboard.ViewModels;

internal class PluginDetailWindowViewModel : ViewModelBase
{
    private PluginInfo? pluginDetail;

    public PluginDetailWindowViewModel()
    {
        InitCommands();

        InitEvents();
    }

    public override void InitCommands()
    {
        FinishCommand = ReactiveCommand.Create<object?>(
            parent => (parent as Window)?.Close()
        );
    }

    public override void InitEvents()
    {
        EventService.ThemeConfigChanged += () => this.RaisePropertyChanged(nameof(TintColor));
    }

    internal PluginInfo? PluginDetail { get => pluginDetail; set => pluginDetail = value; }

    internal string? PublishDate => PluginDetail?.PublishDate.ToLocalTime().ToString("yyyy.MM.dd");

    internal string? LastUpdateDate => PluginDetail?.LastUpdateDate.ToLocalTime().ToString("yyyy.MM.dd");

    internal static Color TintColor => Instances.ConfigManager.AppConfig.App.Theme switch
    {
        "Light" => Colors.WhiteSmoke,
        "Dark" => Colors.Black,
        "Follow" => Application.Current?.ActualThemeVariant == ThemeVariant.Light ? Colors.WhiteSmoke : Colors.Black,
        _ => Color.Parse(Instances.ConfigManager.AppConfig.App.ThemeColor),
    };

    private readonly ObservableCollection<string> functions = [];

    private readonly ObservableCollection<string> tags = [];

    internal void InitFunctionsAndTags()
    {
        if (PluginDetail is null) return;

        if (PluginDetail?.Functions is null) return;

        if (PluginDetail?.Tags is null) return;

        foreach (var func in PluginDetail.Value.Functions)
        {
            var sb = new StringBuilder()
                .Append(func.ReturnValueType)
                .Append(' ')
                .Append(func.Name)
                .Append('(')
                ;

            var index = 0;

            foreach (var param in func.Parameters)
            {
                sb.Append(param.Type)
                    .Append(' ')
                    .Append(param.Name)
                    ;

                if (index != func.Parameters.Count - 1)
                    sb.Append(", ");

                ++index;
            }

            sb.Append(')');

            Functions.Add(sb.ToString());
        }

        foreach (var tag in PluginDetail.Value.Tags)
            Tags.Add($"{{ {tag.Key}: {tag.Value} }}");
    }

    internal ObservableCollection<string> Functions => functions;

    internal ObservableCollection<string> Tags => tags;

    internal ReactiveCommand<object?, Unit>? FinishCommand { get; set; }
}
