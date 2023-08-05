using Avalonia.Controls;
using KitX.Dashboard.ViewModels.Pages.Controls;
using KitX.Web.Rules;

namespace KitX.Dashboard.Views.Pages.Controls;

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
}
