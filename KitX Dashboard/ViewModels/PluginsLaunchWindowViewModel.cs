using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Common.BasicHelper.Utils.Extensions;
using KitX.Dashboard.Network.PluginsNetwork;
using KitX.Dashboard.Views;
using KitX.Shared.CSharp.Plugin;
using KitX.Shared.CSharp.WebCommand;
using ReactiveUI;

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

            CheckPluginIndex();

            this.RaisePropertyChanged(nameof(SearchItems));
            this.RaisePropertyChanged(nameof(SelectedPluginInfo));
            this.RaisePropertyChanged(nameof(SelectedFunction));
        };
    }

    public string pluginsCount = "0";

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
        get => PluginIndexInRange(SelectedPluginIndex) ? PluginInfos[SelectedPluginIndex] : null;
        set
        {
            if (value is null) return;

            var index = PluginInfos.IndexOf(value);

            this.RaiseAndSetIfChanged(ref selectedPluginIndex, index);
        }
    }

    private Function? selectedFunction;

    public Function? SelectedFunction
    {
        get
        {
            if (SelectedPluginInfo is null) selectedFunction = null;

            return selectedFunction;
        }

        set
        {
            this.RaiseAndSetIfChanged(ref selectedFunction, value);

            this.RaisePropertyChanged(nameof(HavingParameters));
        }
    }

    public static ObservableCollection<PluginInfo> PluginInfos => ViewInstances.PluginInfos;

    private bool isSelectingPlugin = true;

    public bool IsSelectingPlugin
    {
        get => isSelectingPlugin;
        set
        {
            if (IsSelectingFunction)
                IsSelectingFunction = false;

            this.RaiseAndSetIfChanged(ref isSelectingPlugin, value);

            if (value)
            {
                SearchingText = SelectedPluginInfo?.Name;

                this.RaisePropertyChanged(nameof(SearchItems));
            }
        }
    }

    private bool isSelectingFunction = false;

    public bool IsSelectingFunction
    {
        get => isSelectingFunction;
        set
        {
            if (IsSelectingPlugin)
                IsSelectingPlugin = false;

            this.RaiseAndSetIfChanged(ref isSelectingFunction, value);


            if (value)
            {
                SearchingText = SelectedFunction?.Name;

                this.RaisePropertyChanged(nameof(SearchItems));
            }
        }
    }

    public bool HavingParameters
    {
        get
        {
            if (SelectedFunction is null) return false;

            return SelectedFunction.Value.Parameters.Count != 0;
        }
    }

    public IEnumerable<string> SearchItems
    {
        get
        {
            if (IsSelectingPlugin)
            {
                return PluginInfos.Select(x => x.Name).Distinct();
            }
            else if (IsSelectingFunction)
            {
                return SelectedPluginInfo?.Functions.Select(f => f.Name) ?? [];
            }
            else return [];
        }
    }

    private string? searchingText;

    public string? SearchingText
    {
        get => searchingText;
        set
        {
            this.RaiseAndSetIfChanged(ref searchingText, value);
        }
    }

    private Vector scrollViewerOffset = new(0, 0);

    public Vector ScrollViewerOffset
    {
        get => scrollViewerOffset;
        set => this.RaiseAndSetIfChanged(ref scrollViewerOffset, value);
    }

    private bool isInDirectSelectingMode = false;

    public bool IsInDirectSelectingMode
    {
        get => isInDirectSelectingMode;
        set => this.RaiseAndSetIfChanged(ref isInDirectSelectingMode, value);
    }

    private void BringSelectedButtonIntoView(int perLineButtonsCount, double scrollviewerHeight, double scrollviewerOffsetY)
    {
        var viewerHeight = (int)Math.Floor(scrollviewerHeight);

        var viewerOffsetY = (int)Math.Floor(scrollviewerOffsetY);

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

    private static bool PluginIndexInRange(int index) => index >= 0 && index < PluginInfos.Count;

    private void CheckPluginIndex()
    {
        if (PluginIndexInRange(SelectedPluginIndex) == false)
        {
            if (SelectedPluginIndex >= PluginInfos.Count)
                SelectedPluginIndex = PluginInfos.Count - 1;
            else if (SelectedPluginIndex < 0)
                SelectedPluginIndex = 0;
        }
    }

    internal void SelectLeftOne(int perLineCount, double scrollviewerHeight, double scrollviewerOffsetY)
    {
        if (PluginIndexInRange(selectedPluginIndex - 1))
        {
            SelectedPluginIndex--;

            BringSelectedButtonIntoView(perLineCount, scrollviewerHeight, scrollviewerOffsetY);
        }
    }

    internal void SelectRightOne(int perLineCount, double scrollviewerHeight, double scrollviewerOffsetY)
    {
        if (PluginIndexInRange(SelectedPluginIndex + 1))
        {
            SelectedPluginIndex++;

            BringSelectedButtonIntoView(perLineCount, scrollviewerHeight, scrollviewerOffsetY);
        }
    }

    internal void SelectUpOne(int perLineCount, double scrollviewerHeight, double scrollviewerOffsetY)
    {
        var targetIndex = SelectedPluginIndex - perLineCount;

        if (PluginIndexInRange(targetIndex))
        {
            SelectedPluginIndex = targetIndex;

            BringSelectedButtonIntoView(perLineCount, scrollviewerHeight, scrollviewerOffsetY);
        }
    }

    internal void SelectDownOne(int perLineCount, double scrollviewerHeight, double scrollviewerOffsetY)
    {
        var targetIndex = SelectedPluginIndex + perLineCount;

        if (PluginIndexInRange(targetIndex))
        {
            SelectedPluginIndex = targetIndex;

            BringSelectedButtonIntoView(perLineCount, scrollviewerHeight, scrollviewerOffsetY);
        }
        else
        {
            targetIndex = PluginInfos.Count - 1;

            if ((targetIndex / perLineCount) - (SelectedPluginIndex / perLineCount) == 0)
                return;

            if (PluginIndexInRange(targetIndex))
            {
                SelectedPluginIndex = targetIndex;

                BringSelectedButtonIntoView(perLineCount, scrollviewerHeight, scrollviewerOffsetY);
            }
        }
    }

    internal void SelectHomeOne(int perLineCount)
    {
        var currentLine = SelectedPluginIndex / perLineCount;

        var targetIndex = currentLine * perLineCount;

        if (PluginIndexInRange(targetIndex))
            SelectedPluginIndex = targetIndex;
    }

    internal void SelectEndOne(int perLineCount)
    {
        var currentLine = SelectedPluginIndex / perLineCount;

        var targetIndex = ((currentLine + 1) * perLineCount) - 1;

        while (PluginIndexInRange(targetIndex) == false && targetIndex >= 0)
            targetIndex--;

        if (PluginIndexInRange(targetIndex))
            SelectedPluginIndex = targetIndex;
    }

    internal void SubmitSearchingText()
    {
        var text = SearchingText;

        if (IsSelectingPlugin)
        {
            var index = PluginInfos.IndexOf(x => x.Name.Equals(text));

            if (index != -1)
            {
                SelectedPluginIndex = index;

                if (SelectedFunction is not null) SelectedFunction = null;

                IsSelectingFunction = true;
            }
        }
        else if (IsSelectingFunction)
        {
            if (SelectedPluginInfo is not null)
                foreach (var func in SelectedPluginInfo.Functions)
                    if (func.Name.Equals(text))
                    {
                        SelectedFunction = func;

                        break;
                    }

            if (SelectedPluginInfo is not null && SelectedFunction is not null && (HavingParameters == false))
            {
                var plugConnector = PluginsServer.Instance.FindConnector(SelectedPluginInfo);

                if (plugConnector is not null)
                {
                    var connector = new Connector()
                        .SetSerializer(x => JsonSerializer.Serialize(x))
                        .SetSender(plugConnector.Request)
                        ;

                    var request = connector.Request()
                        .ReceiveCommand()
                        .UpdateCommand(cmd =>
                        {
                            cmd.FunctionName = SelectedFunction.Value.Name;

                            return cmd;
                        })
                        .Send()
                        ;
                }
            }
        }
        else
        {

        }
    }
}
