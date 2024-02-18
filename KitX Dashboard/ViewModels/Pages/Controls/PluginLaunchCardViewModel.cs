using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FluentAvalonia.UI.Controls;
using KitX.Dashboard.Views;
using KitX.Dashboard.Views.Pages.Controls;
using KitX.Shared.Plugin;
using Material.Icons;
using Material.Icons.Avalonia;
using ReactiveUI;
using Serilog;
using System;
using System.IO;
using System.Reactive;
using MsBox.Avalonia;
using System.Collections.Generic;
using System.ComponentModel;

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

    internal void BuildFuncsUI(PluginLaunchCard pluginLaunchCard)
    {;

        if (pluginStruct.Functions is null) return;

        var FuncsUI = pluginLaunchCard.FindControl<ListBox>("Functions");

        foreach (var func in pluginStruct.Functions)
        {
            var listBox = new ListBox
            {
                Background = Brushes.Azure
            };

            var FuncDisplayName = func.DisplayNames.TryGetValue(
                               Instances.ConfigManager.AppConfig.App.AppLanguage, out var funclang
                                          ) ? funclang : func.DisplayNames.Values.GetEnumerator().Current;
            // 添加TextBlock
            listBox.Items.Add(new TextBlock
            {
                Text = FuncDisplayName
            });

            // 创建Button
            var button = new Button
            {
                FontSize = 14,
                FontWeight = FontWeight.Bold,
                Content = "Launch",//ToDo: Bind to Text_Public_Launch
                Command = LaunchFuncCommand,
                CommandParameter = func.Name
            };
            listBox.Items.Add(button);

            // 创建InfoBar
            var infoBar = new InfoBar
            {
                IsClosable = false,
                IsIconVisible = false,
                IsOpen = true
            };

            // 创建Grid
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            grid.Margin = new Thickness(0, 10, 10, 10);

            // 创建Parameters的UI
            Dictionary<string, string> param_args_info = [];
            foreach (var param in func.Parameters)
            {
                // 创建StackPanel
                var stackPanel = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Orientation = Orientation.Horizontal
                };
                Grid.SetColumn(stackPanel, 0);

                // 创建输入口
                if (param.Type == "string")
                {
                    var textBox = new TextBox
                    {
                        VerticalContentAlignment = VerticalAlignment.Center,
                        FontWeight = FontWeight.Bold,
                        Text = "666"
                    };
                    textBox.TextChanged += (sender, e) =>
                    {
                        var text = textBox.Text;
                        if (!string.IsNullOrEmpty(text))
                        {
                            if (param_args_info.ContainsKey(param.Name))
                            {
                                param_args_info[param.Name] = text;
                            }
                            else
                            {
                                param_args_info.Add(param.Name, text);
                            }
                        }
                    };
                    stackPanel.Children.Add(textBox);
                }
                else if (param.Type == "int")
                {
                    var numericUpDown = new NumericUpDown
                    {
                        VerticalContentAlignment = VerticalAlignment.Center,
                        FontWeight = FontWeight.Bold,
                        Value = 666
                    };
                    numericUpDown.ValueChanged += (sender, e) =>
                    {
                        var value = numericUpDown.Value;
                        if (value is not null)
                        {
                            if (param_args_info.ContainsKey(param.Name))
                            {
                                param_args_info[param.Name] = value.ToString() ?? "";
                            }
                            else
                            {
                                param_args_info.Add(param.Name, value.ToString() ?? "");
                            }
                        }
                    };
                    stackPanel.Children.Add(numericUpDown);
                }
                else if (param.Type == "bool")
                {
                    var checkBox = new CheckBox
                    {
                        VerticalContentAlignment = VerticalAlignment.Center,
                        FontWeight = FontWeight.Bold,
                        IsChecked = true
                    };
                    checkBox.IsCheckedChanged += (sender, e) =>
                    {
                        if (checkBox.IsChecked is not null)
                        {
                            if (param_args_info.ContainsKey(param.Name))
                            {
                                param_args_info[param.Name] = checkBox.IsChecked.ToString() ?? "";
                            }
                            else
                            {
                                param_args_info.Add(param.Name, checkBox.IsChecked.ToString() ?? "");
                            }
                        }
                    };
                    stackPanel.Children.Add(checkBox);
                }
                else if (param.Type == "enum")
                {
                    // 创建ComboBox
                    var comboBox = new ComboBox
                    {
                        VerticalContentAlignment = VerticalAlignment.Center,
                        FontWeight = FontWeight.Bold,
                        IsTextSearchEnabled = true,
                    };
                    comboBox.Items.Add(new[] { "spe" });
                    // Add event handler for comboBox selection changed
                    comboBox.SelectionChanged += (sender, e) =>
                    {
                        if (comboBox.SelectedItem is not null)
                        {
                            if (param_args_info.ContainsKey(param.Name))
                            {
                                param_args_info[param.Name] = comboBox.SelectedItem.ToString() ?? "";
                            }
                            else
                            {
                                param_args_info.Add(param.Name, comboBox.SelectedItem.ToString() ?? "");
                            }
                        }
                    };
                }

                // 创建MaterialIcon
                var materialIcon = new MaterialIcon
                {
                    Margin = new Thickness(5, 0, 0, 0),
                    Kind = MaterialIconKind.Input
                };
                stackPanel.Children.Add(materialIcon);

                // 添加StackPanel到Grid
                grid.Children.Add(stackPanel);

                // 创建TextBlock
                var textBlock = new TextBlock
                {
                    FontWeight = FontWeight.Bold,
                    TextAlignment = TextAlignment.Center,
                    Text = param.DisplayNames.TryGetValue(
                                               Instances.ConfigManager.AppConfig.App.AppLanguage, out var paramlang
                                            ) ? paramlang : param.DisplayNames.Values.GetEnumerator().Current
                };
                Grid.SetColumn(textBlock, 1);

                // 添加TextBlock到Grid
                grid.Children.Add(textBlock);
            }

            // 添加Grid到InfoBar
            infoBar.Content = grid;

            // 添加InfoBar到ListBox
            listBox.Items.Add(infoBar);

            // 添加ListBox到FuncsUI
            FuncsUI?.Items.Add(listBox);

            funcParametersInfo.Add(func.Name,param_args_info);
        }


    }

    /// <summary>
    /// 根据函数名称唤起函数
    /// </summary>
    /// <param name="funcName">函数名称</param>
    private void LaunchFunc(string funcName)
    {
        if (funcName is null) return;

        string funcParametersInfos = "";

        foreach(var param in funcParametersInfo[funcName])
        {
            funcParametersInfos += param.Key + ":" + param.Value + "\n";
        }

        MessageBoxManager.GetMessageBoxStandard(
            "Launching Func:",
            funcName + ":" + funcParametersInfos,
            icon: MsBox.Avalonia.Enums.Icon.Info
        ).ShowWindowAsync();
    }

    // {funcName: {paramName: paramValue}}
    private Dictionary<string, Dictionary<string,string>> funcParametersInfo = [];

    #endregion

    internal ReactiveCommand<Unit, Unit>? ViewDetailsCommand { get; set; }

    internal ReactiveCommand<string, Unit>? LaunchFuncCommand { get; set; }
}
