using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using KitX.Dashboard.Services;
using KitX.Dashboard.ViewModels;
using KitX.Dashboard.Views.Pages.Controls;
using ReactiveUI;
using Serilog;
using SharpHook.Native;
using System;

namespace KitX.Dashboard.Views;

public partial class PluginsLaunchWindow : Window
{
    private readonly PluginsLaunchWindowViewModel viewModel = new();

    private Action? OnHideAction;

    private bool pluginsLaunchWindowDisplayed = false;

    public PluginsLaunchWindow()
    {
        var location = $"{nameof(PluginsLaunchWindow)}.ctor";

        InitializeComponent();

        DataContext = viewModel;

        OnHide(() => pluginsLaunchWindowDisplayed = false);

        EventService.OnExiting += Close;

        if (OperatingSystem.IsWindows() == false)
        {
            try
            {
                Background = Resources["ThemePrimaryAccent"] as SolidColorBrush;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, $"In {location}: {ex.Message}");
            }
        }

        RegisterGlobalHotKey();
    }

    private void RegisterGlobalHotKey()
    {
        Instances.HotKeyManager?.RegisterHotKeyHandler("", codes =>
        {
            var count = codes.Length;

            var tmpList = codes;

            if (count >= 3 &&
            tmpList[count - 3] == KeyCode.VcLeftControl &&
            tmpList[count - 2] == KeyCode.VcLeftMeta &&
            tmpList[count - 1] == KeyCode.VcC)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (pluginsLaunchWindowDisplayed)
                    {
                        Activate();

                        Focus();
                    }
                    else
                    {
                        Show();
                    }

                    pluginsLaunchWindowDisplayed = true;
                });
            }
        });
    }

    public PluginsLaunchWindow OnHide(Action onHideAction)
    {
        OnHideAction = onHideAction;

        return this;
    }

    private void PluginsLaunchWindow_PointerPressed(object? sender, PointerPressedEventArgs e)
        => BeginMoveDrag(e);

    private void PluginsLaunchWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();

            OnHideAction?.Invoke();
        }
    }
}
