﻿using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using KitX.Web.Rules;
using KitX_Dashboard.ViewModels.Pages.Controls;

namespace KitX_Dashboard.Views.Pages.Controls;

public partial class PluginCard : UserControl
{
    private readonly PluginCardViewModel viewModel = new();

    internal string? IPEndPoint { get; set; }

    public PluginCard()
    {
        InitializeComponent();

        DataContext = viewModel;
    }

    public PluginCard(PluginStruct ps)
    {
        InitializeComponent();

        viewModel.pluginStruct = ps;

        DataContext = viewModel;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
