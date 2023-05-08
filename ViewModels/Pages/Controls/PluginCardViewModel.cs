using Avalonia.Media.Imaging;
using KitX.Web.Rules;
using KitX_Dashboard.Data;
using KitX_Dashboard.Managers;
using Serilog;
using System;
using System.IO;

namespace KitX_Dashboard.ViewModels.Pages.Controls;

internal class PluginCardViewModel
{
    internal PluginStruct pluginStruct = new();

    public PluginCardViewModel()
    {
        pluginStruct.IconInBase64 = GlobalInfo.KitXIconBase64;
        Log.Information($"Icon Loaded: {pluginStruct.IconInBase64}");
    }

    internal string DisplayName => pluginStruct.DisplayName
        .ContainsKey(ConfigManager.AppConfig.App.AppLanguage)
        ? pluginStruct.DisplayName[ConfigManager.AppConfig.App.AppLanguage]
        : pluginStruct.DisplayName.Values.GetEnumerator().Current;

    internal string Version => pluginStruct.Version;

    internal string SimpleDescription => pluginStruct.SimpleDescription
        .ContainsKey(ConfigManager.AppConfig.App.AppLanguage)
        ? pluginStruct.SimpleDescription[ConfigManager.AppConfig.App.AppLanguage]
        : pluginStruct.SimpleDescription.GetEnumerator().Current.Value;

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
}
