using Avalonia.Controls;
using Avalonia.Media.Imaging;
using KitX.Dashboard.Views;
using KitX.Shared.Plugin;
using ReactiveUI;
using Serilog;
using System;
using System.IO;
using System.Reactive;
using MsBox.Avalonia;
using System.ComponentModel;
using System.Collections.ObjectModel;

namespace KitX.Dashboard.ViewModels.Pages.Controls;

internal class PluginLaunchCardViewModel: ViewModelBase, INotifyPropertyChanged
{
    internal PluginInfo pluginStruct = new();

    public PluginLaunchCardViewModel()
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

        LaunchFuncCommand = ReactiveCommand.Create<string>(LaunchFunc);
    }

    public override void InitEvents() => throw new NotImplementedException();

    #region 插件信息提取

    internal string DisplayName => pluginStruct.DisplayName.TryGetValue(
        Instances.ConfigManager.AppConfig.App.AppLanguage, out var lang
    ) ? lang : pluginStruct.DisplayName.Values.GetEnumerator().Current;

    internal string Version => pluginStruct.Version;

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

    #endregion

    #region 函数的 UI创建及唤起


    /// <summary>
    /// 根据函数名称唤起函数
    /// </summary>
    /// <param name="funcName">函数名称</param>
    private void LaunchFunc(string funcName)
    {
        if (funcName is null) return;

        string? param_args_info = "";

        // 根据funcName获取函数信息
        foreach (var function in Functions)
        {
            if (function.Name == funcName)
            {
                foreach (var parameter in function.Parameters)
                {
                    param_args_info += parameter.Name + ":" + parameter.Value + "\n";
                }
            }
        }

        MessageBoxManager.GetMessageBoxStandard(
            "Launching Func:",
            param_args_info,
            icon: MsBox.Avalonia.Enums.Icon.Info
        ).ShowWindowAsync();
    }

    #endregion

    internal ObservableCollection<Function> functions = [];

    internal ObservableCollection<Function> Functions { get => functions; set => functions = value; }

    internal ReactiveCommand<Unit, Unit>? ViewDetailsCommand { get; set; }

    internal ReactiveCommand<string, Unit>? LaunchFuncCommand { get; set; }
}
