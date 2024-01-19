using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Services;
using KitX.Web.Rules;
using KitX.Web.Rules.Plugin;
using KitX.Web.Rules.Device;
using ReactiveUI;
using Serilog;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Text;

namespace KitX.Dashboard.ViewModels;

internal class PluginDetailWindowViewModel : ViewModelBase, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    public PluginDetailWindowViewModel()
    {
        InitCommands();

        InitEvents();
    }

    internal void InitCommands()
    {
        FinishCommand = ReactiveCommand.Create<object?>(
            parent => (parent as Window)?.Close()
        );
    }

    internal void InitEvents()
    {
        EventService.ThemeConfigChanged += () => PropertyChanged?.Invoke(
            this,
            new(nameof(TintColor))
        );
    }

    internal PluginInfo? PluginDetail { get; set; }

    internal string? DisplayName
    {
        get
        {
            if (PluginDetail is not null)
            {
                var key = ConfigManager.AppConfig.App.AppLanguage;
                var exist = PluginDetail?.DisplayName.ContainsKey(key);

                if (exist is not null && (bool)exist)
                    return PluginDetail?.DisplayName[key];
                else return PluginDetail?.Name;
            }
            else return PluginDetail?.Name;
        }
    }

    internal string? Version => PluginDetail?.Version;

    internal string? AuthorName => PluginDetail?.AuthorName;

    internal string? PublisherName => PluginDetail?.PublisherName;

    internal string? AuthorLink => PluginDetail?.AuthorLink;

    internal string? PublisherLink => PluginDetail?.PublisherLink;

    internal string? SimpleDescription
    {
        get
        {
            if (PluginDetail is not null)
            {
                var key = ConfigManager.AppConfig.App.AppLanguage;
                var exist = PluginDetail?.SimpleDescription.ContainsKey(key);

                if (exist is not null && (bool)exist)
                    return PluginDetail?.SimpleDescription[key];
                else return PluginDetail?.SimpleDescription.Values.ToArray()[0];
            }
            else return PluginDetail?.SimpleDescription.Values.ToArray()[0];
        }
    }

    internal string? ComplexDescription
    {
        get
        {
            if (PluginDetail is not null)
            {
                var key = ConfigManager.AppConfig.App.AppLanguage;
                var exist = PluginDetail?.ComplexDescription.ContainsKey(key);

                if (exist is not null && (bool)exist)
                    return PluginDetail?.ComplexDescription[key];
                else return PluginDetail?.ComplexDescription.Values.ToArray()[0];
            }
            else return PluginDetail?.ComplexDescription.Values.ToArray()[0];
        }
    }

    internal string? TotalDescriptionInMarkdown
    {
        get
        {
            if (PluginDetail is not null)
            {
                var key = ConfigManager.AppConfig.App.AppLanguage;
                var exist = PluginDetail?.TotalDescriptionInMarkdown.ContainsKey(key);

                if (exist is not null && (bool)exist)
                    return PluginDetail?.TotalDescriptionInMarkdown[key];
                else return PluginDetail?.TotalDescriptionInMarkdown.Values.ToArray()[0];
            }
            else return PluginDetail?.TotalDescriptionInMarkdown.Values.ToArray()[0];
        }
    }

    internal string? PublishDate => PluginDetail?.PublishDate
        .ToLocalTime().ToString("yyyy.MM.dd");

    internal string? LastUpdateDate => PluginDetail?.LastUpdateDate
        .ToLocalTime().ToString("yyyy.MM.dd");

    internal static Color TintColor => ConfigManager.AppConfig.App.Theme switch
    {
        "Light" => Colors.WhiteSmoke,
        "Dark" => Colors.Black,
        "Follow" =>
            Application.Current?.ActualThemeVariant == ThemeVariant.Light ? Colors.WhiteSmoke : Colors.Black,
        _ => Color.Parse(ConfigManager.AppConfig.App.ThemeColor),
    };

    private readonly ObservableCollection<string> functions = new();

    private readonly ObservableCollection<string> tags = new();

    internal Bitmap IconDisplay
    {
        get
        {
            var location = $"{nameof(PluginDetailWindowViewModel)}.{nameof(IconDisplay)}.getter";

            try
            {
                if (PluginDetail is null) return App.DefaultIcon;

                var src = Convert.FromBase64String(PluginDetail.Value.IconInBase64);

                using var ms = new MemoryStream(src);

                return new(ms);
            }
            catch (Exception e)
            {
                Log.Warning(
                    e,
                    $"In {location}: " +
                        $"Failed to transform icon from base64 to byte[] " +
                        $"or create bitmap from `MemoryStream`. {e.Message}"
                );

                return App.DefaultIcon;
            }
        }
    }

    internal void InitFunctionsAndTags()
    {
        if (PluginDetail is null) return;

        if (PluginDetail?.Functions is null) return;

        if (PluginDetail?.Tags is null) return;

        var langKey = ConfigManager.AppConfig.App.AppLanguage;

        foreach (var func in PluginDetail.Value.Functions)
        {
            var sb = new StringBuilder();

            sb.Append(func.ReturnValueType);
            sb.Append(' ');

            if (func.DisplayNames.TryGetValue(langKey, out var name))
                sb.Append(name);
            else sb.Append(func.Name);

            sb.Append('(');

            var index = 0;

            foreach (var param in func.Parameters)
            {
                sb.Append(param.Type);

                sb.Append(' ');

                if (param.DisplayNames.TryGetValue(langKey, out var paramName))
                    sb.Append(paramName);
                else sb.Append(param.Name);

                if (index != func.Parameters.Count - 1)
                    sb.Append(", ");

                ++index;
            }

            sb.Append(')');

            Functions.Add(sb.ToString());
        }

        foreach (var tag in PluginDetail.Value.Tags)
            Tags.Add($"{{{tag.Key}: {tag.Value}}}");
    }

    internal ObservableCollection<string> Functions => functions;

    internal ObservableCollection<string> Tags => tags;

    internal ReactiveCommand<object?, Unit>? FinishCommand { get; set; }
}
