using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using KitX.Dashboard.Services;
using KitX.Dashboard.ViewModels;
using SharpHook.Native;
using System;

namespace KitX.Dashboard.Views;

public partial class PluginsLaunchWindow : Window
{
    private readonly PluginsLaunchWindowViewModel viewModel = new();

    private readonly Action? OnHideAction;

    private bool pluginsLaunchWindowDisplayed = false;

    private int? previousSelectedPluginIndex = null;

    public PluginsLaunchWindow()
    {
        InitializeComponent();

        DataContext = viewModel;

        OnHideAction = () => pluginsLaunchWindowDisplayed = false;

        EventService.OnExiting += Close;

        Initialize();
    }

    private void Initialize()
    {
        if (this.FindControl<AutoCompleteBox>("MainAutoCompleteBox") is AutoCompleteBox box)
        {
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    viewModel.SubmitSearchingText();

                    e.Handled = true;
                }
            };
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
            case Key.Enter:
                // ToDo: Select Plugin Info
                return;
        }

        var perLineCount = (int)Math.Floor((Width - 40) / 80);

        var viewerHeight = viewer.DesiredSize.Height;

        var viewerOffsetY = viewer.Offset.Y;

        switch (e.Key)
        {
            case Key.Left:
                viewModel.SelectLeftOne(
                    perLineCount,
                    viewerHeight,
                    viewerOffsetY
                );
                break;
            case Key.Right:
                viewModel.SelectRightOne(
                    perLineCount,
                    viewerHeight,
                    viewerOffsetY
                );
                break;
            case Key.Up:
                viewModel.SelectUpOne(
                    perLineCount,
                    viewerHeight,
                    viewerOffsetY
                );
                break;
            case Key.Down:
                viewModel.SelectDownOne(
                    perLineCount,
                    viewerHeight,
                    viewerOffsetY
                );
                break;
            case Key.Home:
                viewModel.SelectHomeOne(perLineCount);
                break;
            case Key.End:
                viewModel.SelectEndOne(perLineCount);
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var box = this.FindControl<AutoCompleteBox>("MainAutoCompleteBox");

        switch (e.Key)
        {
            case Key.Tab:
                if (viewModel.IsInDirectSelectingMode == false)
                {
                    if (e.KeyModifiers == KeyModifiers.Shift)
                    {
                        if (viewModel.IsSelectingFunction)
                            viewModel.IsSelectingPlugin = true;
                    }
                    else
                    {
                        if (viewModel.IsSelectingPlugin)
                            viewModel.IsSelectingFunction = true;
                    }
                }
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
                if (viewModel.IsInDirectSelectingMode == false)
                {
                    viewModel.IsInDirectSelectingMode = true;

                    if (this.FindControl<ScrollViewer>("PluginsScrollViewer") is ScrollViewer viewer)
                        viewer.Focus();
                }
                else
                {
                    previousSelectedPluginIndex = viewModel.SelectedPluginIndex;

                    viewModel.IsInDirectSelectingMode = false;

                    box?.Focus();
                }
                e.Handled = true;
                break;
        }

        base.OnKeyDown(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);

        base.OnPointerPressed(e);
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
