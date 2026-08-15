using System.Collections.Generic;
using System.Collections.Specialized;
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

    public PanelHostWindow()
    {
        InitializeComponent();

        DataContext = viewModel;
        viewModel.PanelOpenRequested += OnPanelOpenRequested;
        Closed += (_, _) =>
        {
            viewModel.PanelOpenRequested -= OnPanelOpenRequested;
            viewModel.Dispose();
        };
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
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }
}
