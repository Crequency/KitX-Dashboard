using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using KitX.Core.Contract.Event;
using KitX.Core.Contract.Hotkey;
using KitX.Dashboard;
using KitX.Dashboard.ViewModels;

namespace KitX.Dashboard.Views;

public partial class PluginsLaunchWindow : Window
{
    private readonly PluginsLaunchWindowViewModel viewModel = App.GetService<PluginsLaunchWindowViewModel>();
    private readonly IKeyHookService _keyHookService;

    private readonly Action? OnHideAction;

    private bool pluginsLaunchWindowDisplayed = false;

    private int? previousSelectedPluginIndex = null;

    /// <summary>Grid cell height (px) of the plugin launch grid (D13.11).</summary>
    private const double PluginCardHeight = 80;

    /// <summary>Horizontal padding budget (px) subtracted when computing per-line card count.</summary>
    private const double PluginCardWidthPadding = 40;

    public PluginsLaunchWindow()
    {
        InitializeComponent();

        DataContext = viewModel;

        _keyHookService = App.GetService<IKeyHookService>();

        OnHideAction = () => pluginsLaunchWindowDisplayed = false;

        var eventService = App.GetService<IEventService>();
        eventService.Subscribe(EventNames.OnExiting, (s, e) => Close());

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
        if (sender is not ScrollViewer viewer)
            return;

        if (viewer.IsFocused == false)
            return;

        switch (e.Key)
        {
            case Key.Enter:
                // ToDo: Select Plugin Info
                return;
        }

        var perLineCount = (int)Math.Floor((Width - PluginCardWidthPadding) / PluginCardHeight);

        var viewerHeight = viewer.DesiredSize.Height;

        var viewerOffsetY = viewer.Offset.Y;

        switch (e.Key)
        {
            case Key.Left:
                viewModel.SelectLeftOne(perLineCount, viewerHeight, viewerOffsetY);
                break;
            case Key.Right:
                viewModel.SelectRightOne(perLineCount, viewerHeight, viewerOffsetY);
                break;
            case Key.Up:
                viewModel.SelectUpOne(perLineCount, viewerHeight, viewerOffsetY);
                break;
            case Key.Down:
                viewModel.SelectDownOne(perLineCount, viewerHeight, viewerOffsetY);
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
        // D10 note: the contract IKeyHookService only exposes the no-params handler
        // overload; this window needs the key-codes variant (Action<string[]>) to
        // validate the pressed chord, which only the concrete KeyHookManager provides.
        // If the contract gains a codes-based overload, replace this cast.
        if (_keyHookService is KitX.Core.Hotkey.KeyHookManager keyHookManager)
        {
            // D13 note: hardcoded Ctrl+Win+C chord, no occupancy check. The registration
            // key must match KeyHookManager's joined-KeyCode format for the hook to fire;
            // the handler re-validates the pressed codes defensively.
            keyHookManager.RegisterHotKeyHandler(
                nameof(PluginsLaunchWindow),
                codes =>
                {
                    var count = codes.Length;

                    var tmpList = codes;

                    if (count < 3)
                        return;

                    if (tmpList[count - 3] != "VcLeftControl")
                        return;

                    if (tmpList[count - 2] != "VcLeftMeta")
                        return;

                    if (tmpList[count - 1] != "VcC")
                        return;

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
            );
        }
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
        // D13.3: the width-snap experiment (ExperimentalFlags.EnablePluginLaunchWindowWidthSnap)
        // was a permanently-false dead switch — removed. Keep OnResized only for the
        // base behavior; re-add snapping here if the experiment is revived.

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
