﻿using Avalonia.Controls;
using KitX.Dashboard.ViewModels.Pages.Controls;
using KitX.Shared.Plugin;

namespace KitX.Dashboard.Views.Pages.Controls;

public partial class PluginLaunchCard : UserControl
{
    private readonly PluginLaunchCardViewModel viewModel = new();

    internal string? IPEndPoint { get; set; }
    public PluginLaunchCard()
    {
        InitializeComponent();

        DataContext = viewModel;
    }

    public PluginLaunchCard(PluginInfo ps)
    {
        InitializeComponent();

        viewModel.pluginStruct = ps;

        viewModel.BuildFuncsUI(this);

        DataContext = viewModel;
    }
}
