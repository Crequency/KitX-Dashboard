using Avalonia.Controls;
using Avalonia.Media.Imaging;
using KitX.Web.Rules;
using KitX_Dashboard.Data;
using KitX_Dashboard.Managers;
using KitX_Dashboard.Views;
using ReactiveUI;
using Serilog;
using System;
using System.IO;
using System.Reactive;

namespace KitX_Dashboard.ViewModels.Pages.Controls;

internal class WorkshopCardViewModel
{
    internal WorkshopStruct workshopStruct = new();

    public WorkshopCardViewModel()
    {
        workshopStruct.IconInBase64 = GlobalInfo.KitXIconBase64;

        InitCommands();
    }

    private void InitCommands()
    {
        ViewDetailsCommand = ReactiveCommand.Create(() =>
        {
            // 原本是弹出插件细节窗口，但是现在不想用它（
        });
        ChangeStatusCommand = ReactiveCommand.Create(() =>
        {
            //WSStatus = "待机";
        });
    }

    internal string DisplayName => workshopStruct.DisplayName
        .ContainsKey(ConfigManager.AppConfig.App.AppLanguage)
        ?
        workshopStruct.DisplayName[ConfigManager.AppConfig.App.AppLanguage]
        :
        workshopStruct.DisplayName.Values.GetEnumerator().Current;

    internal string Version => workshopStruct.Version;

    internal string SimpleDescription => workshopStruct.SimpleDescription
        .ContainsKey(ConfigManager.AppConfig.App.AppLanguage)
        ?
        workshopStruct.SimpleDescription[ConfigManager.AppConfig.App.AppLanguage]
        :
        workshopStruct.SimpleDescription.GetEnumerator().Current.Value;

    internal string IconInBase64 => workshopStruct.IconInBase64;

    internal Bitmap IconDisplay
    {
        get
        {
            var location = $"{nameof(WorkshopCardViewModel)}.{nameof(IconDisplay)}.getter";

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

    internal string? WSStatus => workshopStruct.WorkshopStatus;

    internal ReactiveCommand<Unit, Unit>? ViewDetailsCommand { get; set; }
    internal ReactiveCommand<Unit, Unit>? ChangeStatusCommand { get; set; }
}
