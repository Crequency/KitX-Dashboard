using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using KitX.Dashboard.Services;
using KitX.Shared.CSharp.Plugin;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Text;

namespace KitX.Dashboard.ViewModels;

internal class PluginDetailWindowViewModel : ViewModelBase
{
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

    private PluginInfo? pluginDetail;

    internal PluginInfo? PluginDetail
    {
        get => pluginDetail;
        set => this.RaiseAndSetIfChanged(ref pluginDetail, value);
    }

    internal string? PublishDate => PluginDetail?.PublishDate.ToLocalTime().ToString("yyyy.MM.dd");

    internal string? LastUpdateDate => PluginDetail?.LastUpdateDate.ToLocalTime().ToString("yyyy.MM.dd");

    internal static Color TintColor => AppConfig.App.Theme switch
    {
        "Light" => Colors.WhiteSmoke,
        "Dark" => Colors.Black,
        "Follow" => Application.Current?.ActualThemeVariant == ThemeVariant.Light ? Colors.WhiteSmoke : Colors.Black,
        _ => Color.Parse(AppConfig.App.ThemeColor),
    };

    internal void InitFunctionsAndTags()
    {
        if (PluginDetail is null) return;

        if (PluginDetail?.Functions is null) return;

        if (PluginDetail?.Tags is null) return;

        foreach (var func in PluginDetail.Functions)
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

        foreach (var tag in PluginDetail.Tags)
            Tags.Add($"{{ {tag.Key}: {tag.Value} }}");
    }

    internal ObservableCollection<string> Functions { get; set; } = [];

    internal ObservableCollection<string> Tags { get; set; } = [];

    internal ReactiveCommand<object?, Unit>? FinishCommand { get; set; }
}
