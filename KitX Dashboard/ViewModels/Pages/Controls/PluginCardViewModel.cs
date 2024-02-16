using Avalonia.Controls;
using Avalonia.Media.Imaging;
using KitX.Dashboard.Views;
using KitX.Shared.Plugin;
using ReactiveUI;
using Serilog;
using System;
using System.IO;
using System.Reactive;

namespace KitX.Dashboard.ViewModels.Pages.Controls;

internal class PluginCardViewModel : ViewModelBase
{
    internal PluginInfo pluginStruct = new();

    public PluginCardViewModel()
    {
        pluginStruct.IconInBase64 = ConstantTable.KitXIconBase64;

        InitCommands();
    }

    public override void InitCommands()
    {
        ViewDetailsCommand = ReactiveCommand.Create(() =>
        {
            if (ViewInstances.MainWindow is not null)
                new PluginDetailWindow()
                {
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                }
                .SetPluginInfo(pluginStruct)
                .Show(ViewInstances.MainWindow);
        });
    }

    public override void InitEvents() => throw new NotImplementedException();

    internal string DisplayName => pluginStruct.DisplayName.TryGetValue(
        Instances.ConfigManager.AppConfig.App.AppLanguage, out var lang
    ) ? lang : pluginStruct.DisplayName.Values.GetEnumerator().Current;

    internal string Version => pluginStruct.Version;

    internal string SimpleDescription => pluginStruct.SimpleDescription.TryGetValue(
        Instances.ConfigManager.AppConfig.App.AppLanguage, out var lang
    ) ? lang : pluginStruct.SimpleDescription.GetEnumerator().Current.Value;

    internal string IconInBase64 => pluginStruct.IconInBase64;

    internal Bitmap IconDisplay
    {
        get
        {
            var location = $"{nameof(PluginCardViewModel)}.{nameof(IconDisplay)}.getter";

            try
            {
                var src = Convert.FromBase64String(IconInBase64);

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

    internal ReactiveCommand<Unit, Unit>? ViewDetailsCommand { get; set; }
}
