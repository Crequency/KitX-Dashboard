using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using KitX.Dashboard.Services;
using KitX.Dashboard.ViewModels;
using Serilog;
using SharpHook.Native;
using System;

namespace KitX.Dashboard.Views;

public partial class PluginsLaunchWindow : Window
{
    private readonly PluginsLaunchWindowViewModel viewModel = new();

    private Action? OnHideAction;

    private bool pluginsLaunchWindowDisplayed = false;

    private int? previousSelectedPluginIndex = null;

    public PluginsLaunchWindow()
    {
        var location = $"{nameof(PluginsLaunchWindow)}.ctor";

        InitializeComponent();

        DataContext = viewModel;

        OnHideAction = () => pluginsLaunchWindowDisplayed = false;

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

        if (this.FindControl<AutoCompleteBox>("MainAutoCompleteBox") is AutoCompleteBox box)
        {
            //box.AttachedToVisualTree += (_, _) => box.Focus();
            //box.TextChanged += (_, _) =>
            //{
            //    if (box.Text?.Equals("`") ?? false)
            //        box.Text = "";
            //};
            //box.KeyDown += (_, e) =>
            //{
            //    if (e.PhysicalKey == PhysicalKey.Backquote)
            //        e.Handled = true;
            //};
        }

        if (this.FindControl<ScrollViewer>("PluginsScrollViewer") is ScrollViewer viewer)
        {
            viewer.KeyDown += PluginsScrollViewer_KeyDown;
        }

        RegisterGlobalHotKey();
    }

    private void PluginsScrollViewer_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not ScrollViewer viewer) return;

        if (viewer.IsFocused == false) return;

        switch (e.Key)
        {
            case Key.Left:
                viewModel.SelectLeftOne(Width, sender);
                break;
            case Key.Right:
                viewModel.SelectRightOne(Width, sender);
                break;
            case Key.Up:
                viewModel.SelectUpOne(Width, sender);
                break;
            case Key.Down:
                viewModel.SelectDownOne(Width, sender);
                break;
            case Key.Enter:
                viewModel.SelectPluginInfo();
                break;
        }
    }

    private void RegisterGlobalHotKey()
    {
        Instances.KeyHookManager?.RegisterHotKeyHandler(nameof(PluginsLaunchWindow), codes =>
        {
            var count = codes.Length;

            var tmpList = codes;

            if (count < 3) return;

            if (tmpList[count - 3] != KeyCode.VcLeftControl) return;

            if (tmpList[count - 2] != KeyCode.VcLeftMeta) return;

            if (tmpList[count - 1] != KeyCode.VcC) return;

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
        });
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);

        base.OnPointerPressed(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Tab:
                e.Handled = true;
                break;
            case Key.Escape:
                Hide();
                OnHideAction?.Invoke();
                break;
        }

        switch (e.PhysicalKey)
        {
            case PhysicalKey.Backquote:
                if (viewModel.SelectedPluginIndex == -1)
                {
                    viewModel.SelectedPluginIndex = previousSelectedPluginIndex ?? 0;

                    if (this.FindControl<ScrollViewer>("PluginsScrollViewer") is ScrollViewer viewer)
                        viewer.Focus();
                }
                else
                {
                    previousSelectedPluginIndex = viewModel.SelectedPluginIndex;

                    viewModel.SelectedPluginIndex = -1;

                    if (this.FindControl<AutoCompleteBox>("MainAutoCompleteBox") is AutoCompleteBox box)
                        box.Focus();
                }
                e.Handled = true;
                break;
        }

        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);

        //if (viewModel.SelectedPluginIndex == -1)
        //    if (this.FindControl<AutoCompleteBox>("MainAutoCompleteBox") is AutoCompleteBox box)
        //        box.Focus();
    }

    protected override void OnResized(WindowResizedEventArgs e)
    {
        if (ExperimentalFlags.EnablePluginLaunchWindowWidthSnap)
        {
            var basicWidth = 80;
            var addonWidth = 40;
            var windowWidth = (int)e.ClientSize.Width;
            var oneLineCount = (windowWidth - addonWidth) / basicWidth;
            var left = (windowWidth - addonWidth) % basicWidth;

            if (left < basicWidth / 2)
                Width = oneLineCount * basicWidth + addonWidth;
            else
                Width = (oneLineCount + 1) * basicWidth + addonWidth;
        }

        base.OnResized(e);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!ConstantTable.Exiting)
        {
            e.Cancel = true;

            Hide();

            OnHideAction?.Invoke();
        }

        base.OnClosing(e);
    }
}
