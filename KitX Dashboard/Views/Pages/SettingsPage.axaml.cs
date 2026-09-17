using System;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using KitX.Core.Contract.Configuration;
using KitX.Dashboard.ViewModels.Pages;
using KitX.Dashboard.Views.Pages.Controls;
using Serilog;

namespace KitX.Dashboard.Views.Pages;

public partial class SettingsPage : UserControl, IView
{
    private readonly SettingsPageViewModel viewModel = App.GetService<SettingsPageViewModel>();

    public SettingsPage()
    {
        InitializeComponent();

        DataContext = viewModel;

        InitSettingsPage();
    }

    private void InitSettingsPage()
    {
        var nav = this.FindControl<NavigationView>("SettingsNavigationView");

        if (nav is not null)
            nav.SelectedItem = this.FindControl<NavigationViewItem>(SelectedViewName);
    }

    private void SettingsNavigationView_SelectionChanged(object? sender, NavigationViewSelectionChangedEventArgs e)
    {
        try
        {
            var tag = ((sender as NavigationView)?.SelectedItem as Control)?.Tag?.ToString();

            if (tag is null)
                return;

            SelectedViewName = tag;

            this.FindControl<Frame>("SettingsFrame")?.Navigate(SelectedViewType());
        }
        catch (NullReferenceException o)
        {
            Log.Warning(o, o.Message);
        }
    }

    private static string SelectedViewName
    {
        get => App.GetService<IConfigService>().AppConfig.Pages.Settings.SelectedViewName;
        set
        {
            App.GetService<IConfigService>().AppConfig.Pages.Settings.SelectedViewName = value;

            IView.SaveAppConfigChanges();
        }
    }

    private static Type SelectedViewType() =>
        SelectedViewName switch
        {
            "View_General" => typeof(Settings_General),
            "View_Personalise" => typeof(Settings_Personalise),
            // D15: "View_Performence" kept for JSON-compat with saved configs.
            "View_Performence" or "View_Performance" => typeof(Settings_Performance),
            "View_Update" => typeof(Settings_Update),
            "View_About" => typeof(Settings_About),
            _ => typeof(Settings_General),
        };
}
