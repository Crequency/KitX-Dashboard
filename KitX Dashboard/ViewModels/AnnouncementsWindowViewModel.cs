using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using FluentAvalonia.UI.Controls;
using KitX.Core.Contract.Announcement;
using KitX.Core.Contract.Configuration;
using KitX.Dashboard.Views;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels;

internal class AnnouncementsWindowViewModel : ViewModelBase
{
    private readonly IAnnouncementService _announcementService;
    private readonly IConfigService _configService;

    public AnnouncementsWindowViewModel(
        IAnnouncementService announcementService,
        IConfigService configService)
    {
        _announcementService = announcementService;
        _configService = configService;

        InitCommands();

        InitEvents();
    }

    public sealed override void InitCommands()
    {
        ConfirmReceivedCommand = ReactiveCommand.Create(() =>
        {
            var config = _announcementService.AnnouncementConfig;

            var accepted = config.Accepted;

            if (SelectedMenuItem is null)
                return;

            var key = SelectedMenuItem.Content!.ToString();

            if (key is null)
                return;

            if (!accepted.Contains(key))
                accepted.Add(key);

            _announcementService.SaveAnnouncementConfig();

            var found = false;

            var navView = Window?.AnouncementsNavigationView;

            if (navView is null)
                return;

            foreach (NavigationViewItem item in navView.MenuItems.Cast<NavigationViewItem>())
            {
                if (found)
                {
                    SelectedMenuItem = item;
                    break;
                }

                if (item == SelectedMenuItem)
                    found = true;
            }
        });

        ConfirmReceivedAllCommand = ReactiveCommand.Create(() =>
        {
            var config = _announcementService.AnnouncementConfig;

            var accepted = config.Accepted;

            var navView = Window?.AnouncementsNavigationView;

            if (navView is null)
                return;

            foreach (NavigationViewItem item in navView.MenuItems.Cast<NavigationViewItem>())
            {
                var key = item.Content?.ToString();

                if (key is null)
                    continue;

                if (!accepted.Contains(key))
                    accepted.Add(key);
            }

            _announcementService.SaveAnnouncementConfig();

            Window?.Close();
        });
    }

    public sealed override void InitEvents() { }

    internal double Window_Width
    {
        get => _configService.AppConfig.Windows.AnnouncementWindow.Size.Width!.Value;
        set => _configService.AppConfig.Windows.AnnouncementWindow.Size.Width = value;
    }

    internal double Window_Height
    {
        get => _configService.AppConfig.Windows.AnnouncementWindow.Size.Height!.Value;
        set => _configService.AppConfig.Windows.AnnouncementWindow.Size.Height = value;
    }

    private NavigationViewItem? _selectedMenuItem;

    internal NavigationViewItem? SelectedMenuItem
    {
        get => _selectedMenuItem;
        set
        {
            _selectedMenuItem = value;

            if (_selectedMenuItem is null)
                return;

            var key = SelectedMenuItem!.Content?.ToString();

            if (key is null)
                return;

            Markdown = Sources[key];

            this.RaiseAndSetIfChanged(ref _selectedMenuItem, value);
        }
    }

    private string _markdown = string.Empty;

    internal string Markdown
    {
        get => _markdown;
        set => this.RaiseAndSetIfChanged(ref _markdown, value);
    }

    private Dictionary<string, string> _sources = [];

    internal Dictionary<string, string> Sources
    {
        get => _sources;
        set
        {
            _sources = value;

            var navView = Window?.AnouncementsNavigationView;

            navView?.MenuItems?.Clear();

            foreach (var item in Sources.Reverse())
            {
                navView?.MenuItems?.Add(new NavigationViewItem() { Content = item.Key });
            }

            if (navView is not null)
                SelectedMenuItem = navView.MenuItems?.First() as NavigationViewItem;
        }
    }

    internal AnnouncementsWindow? Window { get; set; }

    internal ReactiveCommand<Unit, Unit>? ConfirmReceivedCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? ConfirmReceivedAllCommand { get; set; }
}
