﻿using Common.BasicHelper.Utils.Extensions;
using FluentAvalonia.UI.Controls;
using KitX.Dashboard.Data;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Views;
using ReactiveUI;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Text.Json;
using System.Threading.Tasks;

namespace KitX.Dashboard.ViewModels;

internal class AnouncementsWindowViewModel : ViewModelBase, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    public AnouncementsWindowViewModel()
    {
        InitCommands();
    }

    public static JsonSerializerOptions JsonSerializerOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true,
    };

    private void InitCommands()
    {
        ConfirmReceivedCommand = ReactiveCommand.Create(async () =>
        {
            if (SelectedMenuItem is null || Readed is null) return;

            var key = SelectedMenuItem.Content!.ToString();

            if (key is null) return;

            if (!Readed.Contains(key))
                Readed.Add(key);

            var ConfigFilePath = GlobalInfo.AnnouncementsJsonPath.GetFullPath();

            await File.WriteAllTextAsync(
                ConfigFilePath,
                JsonSerializer.Serialize(Readed, JsonSerializerOptions)
            );

            var finded = false;

            var navView = Window?.AnouncementsNavigationView;

            if (navView is not null)
            {
                foreach (NavigationViewItem item in navView.MenuItems.Cast<NavigationViewItem>())
                {
                    if (finded)
                    {
                        SelectedMenuItem = item;
                        break;
                    }

                    if (item == SelectedMenuItem)
                        finded = true;
                }
            }
        });

        ConfirmReceivedAllCommand = ReactiveCommand.Create(async () =>
        {
            if (Readed is null) return;

            var navView = Window?.AnouncementsNavigationView;

            if (navView is not null)
            {
                foreach (NavigationViewItem item in navView.MenuItems.Cast<NavigationViewItem>())
                {
                    var key = item.Content?.ToString();

                    if (key is null) continue;

                    if (!Readed.Contains(key))
                        Readed.Add(key);
                }

                var ConfigFilePath = GlobalInfo.AnnouncementsJsonPath.GetFullPath();

                var options = new JsonSerializerOptions()
                {
                    WriteIndented = true,
                    IncludeFields = true,
                };

                await File.WriteAllTextAsync(
                    ConfigFilePath,
                    JsonSerializer.Serialize(Readed, options)
                );

                Window?.Close();
            }
        });
    }

    internal static double Window_Width
    {
        get => ConfigManager.AppConfig.Windows.AnnouncementWindow.Window_Width;
        set => ConfigManager.AppConfig.Windows.AnnouncementWindow.Window_Width = value;
    }

    internal static double Window_Height
    {
        get => ConfigManager.AppConfig.Windows.AnnouncementWindow.Window_Height;
        set => ConfigManager.AppConfig.Windows.AnnouncementWindow.Window_Height = value;
    }

    private NavigationViewItem? selectedMenuItem;

    internal NavigationViewItem? SelectedMenuItem
    {
        get => selectedMenuItem;
        set
        {
            selectedMenuItem = value;

            if (SelectedMenuItem is null) return;

            var key = SelectedMenuItem.Content?.ToString();

            if (key is null) return;

            Markdown = Sources[key];

            PropertyChanged?.Invoke(
                this,
                new(nameof(SelectedMenuItem))
            );
        }
    }

    private string markdown = string.Empty;

    internal string Markdown
    {
        get => markdown;
        set
        {
            markdown = value;
            PropertyChanged?.Invoke(
                this,
                new(nameof(Markdown))
            );
        }
    }

    private Dictionary<string, string> sources = [];

    internal Dictionary<string, string> Sources
    {
        get => sources;
        set
        {
            sources = value;

            var navView = Window?.AnouncementsNavigationView;

            navView?.MenuItems?.Clear();

            foreach (var item in Sources.Reverse())
            {
                navView?.MenuItems?.Add(new NavigationViewItem()
                {
                    Content = item.Key
                });
            }

            if (navView is not null)
                SelectedMenuItem = navView.MenuItems.First() as NavigationViewItem;
        }
    }

    internal AnouncementsWindow? Window { get; set; }

    internal List<string>? Readed { get; set; }

    internal ReactiveCommand<Unit, Task>? ConfirmReceivedCommand { get; set; }

    internal ReactiveCommand<Unit, Task>? ConfirmReceivedAllCommand { get; set; }
}
