using Avalonia;
using Avalonia.Controls;
using KitX.Dashboard.Views;
using KitX.Shared.Plugin;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

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

            this.RaisePropertyChanged(nameof(SearchItems));
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

    private int selectedPluginIndex = -1;

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
        get => SelectedPluginIndexInRange(SelectedPluginIndex) ? PluginInfos[SelectedPluginIndex] : null;
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

    public static IEnumerable<string> SearchItems => PluginInfos.Select(x => x.Name).Distinct();
    //.Concat(
    //    PluginInfos.SelectMany(
    //        x => x.SimpleDescription.Select(x => x.Value)
    //    )
    //).Concat(
    //    PluginInfos.SelectMany(
    //        x => x.ComplexDescription.Select(x => x.Value)
    //    )
    //).Distinct();

    public string? SearchingText { get; set; }

    private Vector scrollViewerOffset = new(0, 0);

    public Vector ScrollViewerOffset
    {
        get => scrollViewerOffset;
        set => this.RaiseAndSetIfChanged(ref scrollViewerOffset, value);
    }

    private void BringSelectedButtonIntoView(int perLineButtonsCount, object? scrollvierwer)
    {
        if (scrollvierwer is not ScrollViewer viewer) return;

        var viewerHeight = (int)Math.Floor(viewer?.DesiredSize.Height ?? 240);

        var viewerOffsetY = (int)Math.Floor(viewer?.Offset.Y ?? 0);

        var lineIndex = SelectedPluginIndex / perLineButtonsCount;

        var up = lineIndex * 80;

        var down = up + 80;

        var targetY = lineIndex * 80;

        var condition = (up >= viewerOffsetY && down <= viewerOffsetY + viewerHeight);

        if (condition == false)
        {
            ScrollViewerOffset = new(0, targetY);
        }
    }

    private static bool SelectedPluginIndexInRange(int index) => index >= 0 && index < PluginInfos.Count;

    internal void SelectRightOne(double WindowWidth, object? scrollviewer)
    {
        var perLineCount = (int)Math.Floor((WindowWidth - 40) / 80);

        if (SelectedPluginIndexInRange(SelectedPluginIndex + 1))
        {
            SelectedPluginIndex++;

            BringSelectedButtonIntoView(perLineCount, scrollviewer);
        }
    }

    internal void SelectLeftOne(double WindowWidth, object? scrollviewer)
    {
        var perLineCount = (int)Math.Floor((WindowWidth - 40) / 80);

        if (SelectedPluginIndexInRange(selectedPluginIndex - 1))
        {
            SelectedPluginIndex--;

            BringSelectedButtonIntoView(perLineCount, scrollviewer);
        }
    }

    internal void SelectUpOne(double WindowWidth, object? scrollviewer)
    {
        var perLineCount = (int)Math.Floor((WindowWidth - 40) / 80);

        var targetIndex = SelectedPluginIndex - perLineCount;

        if (SelectedPluginIndexInRange(targetIndex))
        {
            SelectedPluginIndex = targetIndex;

            BringSelectedButtonIntoView(perLineCount, scrollviewer);
        }
    }

    internal void SelectDownOne(double WindowWidth, object? scrollviewer)
    {
        var perLineCount = (int)Math.Floor((WindowWidth - 40) / 80);

        var targetIndex = SelectedPluginIndex + perLineCount;

        if (SelectedPluginIndexInRange(targetIndex))
        {
            SelectedPluginIndex = targetIndex;

            BringSelectedButtonIntoView(perLineCount, scrollviewer);
        }
        else
        {
            targetIndex = PluginInfos.Count - 1;

            if ((targetIndex / perLineCount) - (SelectedPluginIndex / perLineCount) == 0)
                return;

            if (SelectedPluginIndexInRange(targetIndex))
            {
                SelectedPluginIndex = targetIndex;

                BringSelectedButtonIntoView(perLineCount, scrollviewer);
            }
        }
    }

    internal void SelectPluginInfo()
    {

    }
}
