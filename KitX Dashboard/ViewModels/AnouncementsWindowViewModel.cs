using Avalonia.Collections;
using Common.BasicHelper.Utils.Extensions;
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

    private void InitCommands()
    {
        ConfirmReceivedCommand = ReactiveCommand.Create(async () =>
        {
            if (SelectedMenuItem is null || Readed is null) return;

            var key = SelectedMenuItem.Content.ToString();

            if (key is null) return;

            if (!Readed.Contains(key))
                Readed.Add(key);

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

            var finded = false;

            foreach (var item in MenuItems)
            {
                if (finded)
                {
                    SelectedMenuItem = item;
                    break;
                }

                if (item == SelectedMenuItem)
                    finded = true;
            }
        });

        ConfirmReceivedAllCommand = ReactiveCommand.Create(async () =>
        {
            if (Readed is null) return;

            foreach (var item in MenuItems)
            {
                var key = item.Content.ToString();

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

            var key = SelectedMenuItem.Content.ToString();

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

    internal AvaloniaList<NavigationViewItem> MenuItems { get; set; } = new();

    private Dictionary<string, string> sources = new();

    internal Dictionary<string, string> Sources
    {
        get => sources;
        set
        {
            sources = value;

            MenuItems.Clear();

            foreach (var item in Sources.Reverse())
            {
                MenuItems.Add(new()
                {
                    Content = item.Key
                });
            }

            SelectedMenuItem = MenuItems.First();
        }
    }

    internal AnouncementsWindow? Window { get; set; }

    internal List<string>? Readed { get; set; }

    internal ReactiveCommand<Unit, Task>? ConfirmReceivedCommand { get; set; }

    internal ReactiveCommand<Unit, Task>? ConfirmReceivedAllCommand { get; set; }
}
