﻿using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Services;
using KitX.Dashboard.ViewModels.Pages;
using KitX.Dashboard.Views.Pages.Controls;
using Serilog;
using System;

namespace KitX.Dashboard.Views.Pages;

public partial class SettingsPage : UserControl
{
    private readonly SettingsPageViewModel viewModel = new();

    public SettingsPage()
    {
        InitializeComponent();

        DataContext = viewModel;

        InitSettingsPage();
    }




    private void InitSettingsPage()
    {
        this.FindControl<NavigationView>("SettingsNavigationView").SelectedItem
            = this.FindControl<NavigationViewItem>(SelectedViewName);
    }




    private static void SaveChanges()
    {
        EventService.Invoke(nameof(EventService.ConfigSettingsChanged));
    }






    private void SettingsNavigationView_SelectionChanged(
        object? sender,
        NavigationViewSelectionChangedEventArgs e)
    {
        try
        {
            var tag = ((sender as NavigationView)?.SelectedItem as Control)?.Tag?.ToString();

            if (tag is null) return;

            SelectedViewName = tag;

            this.FindControl<Frame>("SettingsFrame").Navigate(SelectedViewType());
        }
        catch (NullReferenceException o)
        {
            Log.Warning(o, o.Message);
        }
    }

    private static string SelectedViewName
    {
        get => ConfigManager.AppConfig.Pages.Settings.SelectedViewName;
        set
        {
            ConfigManager.AppConfig.Pages.Settings.SelectedViewName = value;
            SaveChanges();
        }
    }

    private static Type SelectedViewType() => SelectedViewName switch
    {
        "View_General" => typeof(Settings_General),
        "View_Personalise" => typeof(Settings_Personalise),
        "View_Performence" => typeof(Settings_Performence),
        "View_Update" => typeof(Settings_Update),
        "View_About" => typeof(Settings_About),
        _ => typeof(Settings_General),
    };
}
