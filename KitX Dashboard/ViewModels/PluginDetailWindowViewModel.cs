using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Event;
using KitX.Dashboard;
using KitX.Shared.CSharp.Plugin;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels;

internal class PluginDetailWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IConfigService _configService;
    private readonly IEventService _eventService;

    /// <summary>Named handler so <see cref="Dispose"/> can unsubscribe it (D11).</summary>
    private readonly EventHandler<EventArgs> _themeConfigChangedHandler;

    public PluginDetailWindowViewModel(IConfigService configService, IEventService eventService)
    {
        _configService = configService;
        _eventService = eventService;

        _themeConfigChangedHandler = (s, e) => this.RaisePropertyChanged(nameof(TintColor));

        InitCommands();

        InitEvents();
    }

    public sealed override void InitCommands()
    {
        FinishCommand = ReactiveCommand.Create<object?>(parent => (parent as Window)?.Close());
    }

    public sealed override void InitEvents()
    {
        _eventService.Subscribe(EventNames.ThemeConfigChanged, _themeConfigChangedHandler);
    }

    /// <summary>
    /// Unsubscribes every subscription made in <see cref="InitEvents"/>. The window is
    /// created per plugin-detail view (D11).
    /// </summary>
    public void Dispose()
    {
        _eventService.Unsubscribe(EventNames.ThemeConfigChanged, _themeConfigChangedHandler);
    }

    private PluginInfo? pluginDetail;

    internal PluginInfo? PluginDetail
    {
        get => pluginDetail;
        set => this.RaiseAndSetIfChanged(ref pluginDetail, value);
    }

    internal string? PublishDate => PluginDetail?.PublishDate.ToLocalTime().ToString("yyyy.MM.dd");

    internal string? LastUpdateDate => PluginDetail?.LastUpdateDate.ToLocalTime().ToString("yyyy.MM.dd");

    internal Color TintColor =>
        _configService.AppConfig.App.Theme switch
        {
            "Light" => Colors.WhiteSmoke,
            "Dark" => Colors.Black,
            "Follow" => Application.Current?.ActualThemeVariant == ThemeVariant.Light ? Colors.WhiteSmoke : Colors.Black,
            _ => Color.Parse(_configService.AppConfig.App.ThemeColor),
        };

    internal void InitFunctionsAndTags()
    {
        if (PluginDetail is null)
            return;

        if (PluginDetail?.Functions is null)
            return;

        if (PluginDetail?.Tags is null)
            return;

        foreach (var func in PluginDetail.Functions)
        {
            var sb = new StringBuilder().Append(func.ReturnValueType).Append(' ').Append(func.Name).Append('(');

            var index = 0;

            foreach (var param in func.Parameters)
            {
                sb.Append(param.Type).Append(' ').Append(param.Name);

                if (index != func.Parameters.Count - 1)
                    sb.Append(", ");

                ++index;
            }

            sb.Append(')');

            Functions.Add(sb.ToString());
        }

        foreach (var tag in PluginDetail.Tags)
            Tags.Add($"{{ {tag.Key}: {tag.Value} }}");

        // 展示插件支持的触发器
        if (PluginDetail.SupportedTriggers?.Count > 0)
        {
            foreach (var trigger in PluginDetail.SupportedTriggers)
                Tags.Add($"{{ Trigger: {trigger} }}");
        }
    }

    internal ObservableCollection<string> Functions { get; set; } = [];

    internal ObservableCollection<string> Tags { get; set; } = [];

    internal ReactiveCommand<object?, Unit>? FinishCommand { get; set; }
}
