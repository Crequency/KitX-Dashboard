using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
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
            Services.UIStateService.PanelHostWindow = null;
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PanelHostViewModel.HasPendingDialog))
            return;
        Title = viewModel.HasPendingDialog ? _baseTitle + " ⚠" : _baseTitle;
    }

    private readonly Dictionary<ScrollViewer, NotifyCollectionChangedEventHandler> _logScrollHandlers = [];

    /// <summary>Auto-scroll each Log control unless the global pause switch is on.</summary>
    private void OnLogScrollAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not ScrollViewer scroll || scroll.DataContext is not PanelControlViewModel control)
            return;
        NotifyCollectionChangedEventHandler handler = (_, _) =>
        {
            if (!viewModel.IsLogPaused)
                Dispatcher.UIThread.Post(() => scroll.ScrollToEnd());
        };
        _logScrollHandlers[scroll] = handler;
        control.LogEntries.CollectionChanged += handler;
    }

    private void OnLogScrollDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not ScrollViewer scroll || !_logScrollHandlers.Remove(scroll, out var handler))
            return;
        if (scroll.DataContext is PanelControlViewModel control)
            control.LogEntries.CollectionChanged -= handler;
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
