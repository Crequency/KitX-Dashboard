using Avalonia.Controls;
using KitX.Dashboard.Models;
using KitX.Dashboard.ViewModels.Pages.Controls;
using System.Collections.ObjectModel;

namespace KitX.Dashboard.Views.Pages.Controls;

public partial class PluginBar : UserControl
{
    private readonly PluginBarViewModel viewModel = new();

    public PluginBar()
    {
        InitializeComponent();

        DataContext = viewModel;
    }

    public PluginBar(Plugin plugin, ref ObservableCollection<PluginBar> pluginBars)
    {
        InitializeComponent();

        viewModel.PluginDetail = plugin;
        viewModel.PluginBars = pluginBars;
        viewModel.PluginBar = this;

        DataContext = viewModel;
    }
}
