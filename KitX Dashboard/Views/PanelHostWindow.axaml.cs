using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using KitX.Dashboard.Services;
using KitX.Dashboard.ViewModels;

namespace KitX.Dashboard.Views;

/// <summary>
/// The ToolKit <b>use surface</b> window: lists every instance across mounted ToolKits and
/// lets the user end them. Each instance is a runtime incarnation of a mounted ToolKit
/// (ToolKit 实例模型定稿) — the panel view and run monitor live here, distinct from the
/// Bench design surface. Opening it from the Tray or hotkey shows/hides this window.
/// </summary>
public partial class PanelHostWindow : Window
{
    private readonly PanelHostViewModel viewModel = App.GetService<PanelHostViewModel>();
    private readonly string _baseTitle;

    public PanelHostWindow()
    {
        InitializeComponent();

        DataContext = viewModel;
        _baseTitle = Title ?? string.Empty;
        viewModel.PanelOpenRequested += OnPanelOpenRequested;
        // Dialog pending indicator (C27): append a pulse marker to the window title while
        // the single-slot dialog queue is non-empty.
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) =>
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            viewModel.PanelOpenRequested -= OnPanelOpenRequested;
            viewModel.Dispose();
            // The window cannot be re-shown after Close; drop the static singleton so the
            // tray / workbench spawn creates a fresh window (and a fresh VM) next time.
            App.GetService<IWindowService>().PanelHostWindow = null;
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PanelHostViewModel.HasPendingDialog))
            return;
        Title = viewModel.HasPendingDialog ? _baseTitle + " ⚠" : _baseTitle;
    }

    private readonly Dictionary<ScrollViewer, NotifyCollectionChangedEventHandler> _logScrollHandlers = [];
    private readonly Dictionary<ScrollViewer, DispatcherTimer> _logScrollTimers = [];

    /// <summary>
    /// Auto-scroll each Log control to the newest entry, unless the global pause switch
    /// is on or the user has scrolled up to read history. Follows are coalesced through a
    /// short debounce window so a burst of appended entries (G2) collapses into at most
    /// one ScrollToEnd instead of one Dispatcher post per entry.
    /// </summary>
    private void OnLogScrollAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not ScrollViewer scroll || scroll.DataContext is not PanelControlViewModel control)
            return;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!viewModel.IsLogPaused && IsNearBottom(scroll))
                scroll.ScrollToEnd();
        };

        NotifyCollectionChangedEventHandler handler = (_, _) =>
        {
            if (viewModel.IsLogPaused)
                return;
            timer.Stop();
            timer.Start();
        };

        _logScrollHandlers[scroll] = handler;
        _logScrollTimers[scroll] = timer;
        control.LogEntries.CollectionChanged += handler;

        // Jump to the newest entries when the control first appears (existing logs).
        timer.Start();
    }

    private void OnLogScrollDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not ScrollViewer scroll)
            return;
        if (_logScrollHandlers.Remove(scroll, out var handler) && scroll.DataContext is PanelControlViewModel control)
            control.LogEntries.CollectionChanged -= handler;
        if (_logScrollTimers.Remove(scroll, out var timer))
            timer.Stop();
    }

    /// <summary>True when the view sits at (or near) the newest entry, so a follow is wanted.</summary>
    private static bool IsNearBottom(ScrollViewer scroll)
    {
        if (scroll.Viewport.Height <= 0)
            return true;
        var remaining = scroll.Extent.Height - (scroll.Offset.Y + scroll.Viewport.Height);
        return remaining <= 40;
    }

    /// <summary>Present the window without stealing foreground focus more than necessary.</summary>
    private void OnPanelOpenRequested()
    {
        if (!IsVisible)
        {
            // auto/silent surface requests must not yank keyboard focus (C28);
            // the next manual tray open resets ShowActivated to true.
            ShowActivated = false;
            Show();
            return;
        }

        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
    }
}
