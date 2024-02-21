using KitX.Dashboard.Views;
using KitX.Shared.Plugin;
using ReactiveUI;
using System.Collections.ObjectModel;

namespace KitX.Dashboard.ViewModels;

internal class PluginsLaunchWindowViewModel : ViewModelBase
{
    public PluginsLaunchWindowViewModel()
    {
        InitCommands();

        InitEvents();
    }

    public override void InitCommands()
    {

    }

    public override void InitEvents()
    {
        PluginInfos.CollectionChanged += (_, _) =>
        {
            PluginsCount = $"{PluginInfos.Count}";
        };
    }

    public string pluginsCount = $"0";

    public string PluginsCount
    {
        get => pluginsCount;
        set
        {
            this.RaiseAndSetIfChanged(ref pluginsCount, value);
            this.RaisePropertyChanged(nameof(NoPlugins_TipHeight));
            this.RaisePropertyChanged(nameof(SelectedPluginInfo));
        }
    }

    public static double NoPlugins_TipHeight => PluginInfos.Count == 0 ? 40 : 0;

    private int selectedPluginIndex = 0;

    public int SelectedPluginIndex
    {
        get => selectedPluginIndex;
        set
        {
            this.RaiseAndSetIfChanged(ref selectedPluginIndex, value);
            this.RaisePropertyChanged(nameof(SelectedPluginInfo));
        }
    }

    public PluginInfo? SelectedPluginInfo
    {
        get => (SelectedPluginIndex >= 0 && SelectedPluginIndex < PluginInfos.Count) ? PluginInfos[SelectedPluginIndex] : null;
        set
        {
            if (value is null) return;

            var index = PluginInfos.IndexOf(value.Value);

            this.RaiseAndSetIfChanged(ref selectedPluginIndex, index);
        }
    }

    private Function? selectedFunction;

    public Function? SelectedFunction
    {
        get => selectedFunction;
        set => this.RaiseAndSetIfChanged(ref selectedFunction, value);
    }

    private Parameter? selectedParameter;

    public Parameter? SelectedParameter
    {
        get => selectedParameter;
        set => this.RaiseAndSetIfChanged(ref selectedParameter, value);
    }

    public static ObservableCollection<PluginInfo> PluginInfos => ViewInstances.PluginInfos;

    public string? SearchingText { get; set; }

    private static bool SelectedPluginIndexInRange(int index) => index >= 0 && index < PluginInfos.Count;

    internal void SelectRightOne()
    {
        if (SelectedPluginIndexInRange(SelectedPluginIndex + 1))
            SelectedPluginIndex++;
    }

    internal void SelectLeftOne()
    {
        if (SelectedPluginIndexInRange(selectedPluginIndex - 1))
            SelectedPluginIndex--;
    }

    internal void SelectUpOne()
    {

    }

    internal void SelectDownOne()
    {

    }

    internal void SelectPluginInfo()
    {

    }
}
