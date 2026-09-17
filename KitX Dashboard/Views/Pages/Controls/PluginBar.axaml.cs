using System.Collections.ObjectModel;
using Avalonia.Controls;
using KitX.Core.Plugin;
using KitX.Dashboard.ViewModels.Pages.Controls;

namespace KitX.Dashboard.Views.Pages.Controls;

public partial class PluginBar : UserControl
{
    private readonly PluginBarViewModel viewModel = App.GetService<PluginBarViewModel>();

    public PluginBar()
    {
        InitializeComponent();

        DataContext = viewModel;
    }

    public PluginBar(PluginInstallation plugin, ref ObservableCollection<PluginBar> pluginBars)
    {
        InitializeComponent();

        viewModel.Plugin = plugin;
        viewModel.PluginBars = pluginBars;
        viewModel.PluginBar = this;

        DataContext = viewModel;

        // D11: the VM is transient per card and subscribes to the singleton event bus —
        // dispose it when the card leaves the visual tree (RepoPage rebuilds the list on
        // every refresh, which would otherwise leak one subscription per card).
        Unloaded += (_, _) => viewModel.Dispose();
    }

    /// <summary>
    /// The plugin installation backing this bar. Exposed so page-level filtering
    /// (RepoPage search box) can match on plugin name / ID without reaching into
    /// the bar's private view model.
    /// </summary>
    internal PluginInstallation? Plugin => viewModel.Plugin;
}
