using Avalonia;
using FluentAvalonia.UI.Controls;
using KitX.Dashboard.Configuration;
using KitX.Dashboard.Views;
using ReactiveUI;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive;

namespace KitX.Dashboard.ViewModels;

internal class AnouncementsWindowViewModel : ViewModelBase, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    private static AppConfig AppConfig => Instances.ConfigManager.AppConfig;

    public AnouncementsWindowViewModel()
    {
        InitCommands();
    }

    public override void InitCommands()
    {
        ConfirmReceivedCommand = ReactiveCommand.Create(() =>
        {
            var config = Instances.ConfigManager.AnnouncementConfig;

            var accepted = config.Accepted;

            if (SelectedMenuItem is null || accepted is null) return;

            var key = SelectedMenuItem.Content!.ToString();

            if (key is null) return;

            if (!accepted.Contains(key))
                accepted.Add(key);

            config.Save(config.ConfigFileLocation!);

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

        ConfirmReceivedAllCommand = ReactiveCommand.Create(() =>
        {
            var config = Instances.ConfigManager.AnnouncementConfig;

            var accepted = config.Accepted;

            var navView = Window?.AnouncementsNavigationView;

            if (navView is not null)
            {
                foreach (NavigationViewItem item in navView.MenuItems.Cast<NavigationViewItem>())
                {
                    var key = item.Content?.ToString();

                    if (key is null) continue;

                    if (!accepted.Contains(key))
                        accepted.Add(key);
                }

                config.Save(config.ConfigFileLocation!);

                Window?.Close();
            }
        });
    }

    public override void InitEvents()
    {

    }

    internal static double Window_Width
    {
        get => AppConfig.Windows.AnnouncementWindow.Size.Width!.Value;
        set => AppConfig.Windows.AnnouncementWindow.Size.Width = value;
    }

    internal static double Window_Height
    {
        get => AppConfig.Windows.AnnouncementWindow.Size.Height!.Value;
        set => AppConfig.Windows.AnnouncementWindow.Size.Height = value;
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

    internal ReactiveCommand<Unit, Unit>? ConfirmReceivedCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? ConfirmReceivedAllCommand { get; set; }
}
