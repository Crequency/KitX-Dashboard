using Avalonia.Controls;
using Avalonia.Media.Imaging;
using KitX.Dashboard.Data;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Views;
using KitX.Web.Rules;
using ReactiveUI;
using Serilog;
using System;
using System.IO;
using System.Reactive;

namespace KitX.Dashboard.ViewModels.Pages.Controls;

internal class PluginCardViewModel
{
    internal PluginStruct pluginStruct = new();

    public PluginCardViewModel()
    {
        pluginStruct.IconInBase64 = GlobalInfo.KitXIconBase64;

        InitCommands();
    }

    private void InitCommands()
    {
        ViewDetailsCommand = ReactiveCommand.Create(() =>
        {
            if (Instances.MainWindow is not null)
                new PluginDetailWindow()
                {
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                }
                .SetPluginStruct(pluginStruct)
                .Show(Instances.MainWindow);
        });
    }

    internal string DisplayName => pluginStruct.DisplayName.TryGetValue(
        ConfigManager.AppConfig.App.AppLanguage, out var lang
    ) ? lang : pluginStruct.DisplayName.Values.GetEnumerator().Current;

    internal string Version => pluginStruct.Version;

    internal string SimpleDescription => pluginStruct.SimpleDescription.TryGetValue(
        ConfigManager.AppConfig.App.AppLanguage, out var lang
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
