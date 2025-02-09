using System.Reactive;
using Avalonia;
using FluentAvalonia.UI.Controls;
using KitX.Dashboard.Managers;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels.Pages;

internal class WorkflowPageViewModel : ViewModelBase
{
    public WorkflowPageViewModel()
    {
        InitCommands();
    }

    public override void InitCommands() => throw new System.NotImplementedException();

    public override void InitEvents() => throw new System.NotImplementedException();

    internal static bool IsPaneOpen
    {
        get => ConfigManager.Instance.AppConfig.Pages.Home.IsNavigationViewPaneOpened;
        set
        {
            ConfigManager.Instance.AppConfig.Pages.Home.IsNavigationViewPaneOpened = value;

            SaveAppConfigChanges();
        }
    }
}
