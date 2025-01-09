using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Common.BasicHelper.Graphics.Screen;
using KitX.Dashboard.Configuration;
using KitX.Dashboard.Converters;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Utils;
using KitX.Dashboard.ViewModels;

namespace KitX.Dashboard.Views;

public partial class AnnouncementsWindow : Window, IView
{
    private readonly AnnouncementsWindowViewModel _viewModel = new();

    private static AppConfig AppConfig => ConfigManager.Instance.AppConfig;

    public AnnouncementsWindow()
    {
        InitializeComponent();

        _viewModel.Window = this;

        DataContext = _viewModel;
    }

    internal AnnouncementsWindow UpdateSource(Dictionary<string, string> src)
    {
        _viewModel.Sources = src;

        return this;
    }

    protected override void OnOpened(EventArgs e)
    {
        var config = AppConfig.Windows.AnnouncementWindow;

        var screen = Screens.ScreenFromWindow(this);

        config.Size = config.Size.SuggestResolution(screen, out var notScaled);

        var centerPos = config.Location.BringToCenter(screen, notScaled ?? config.Size);

        SizeChanged += (_, _) =>
        {
            if (WindowState != WindowState.Maximized)
                config.Size = new(Width, Height);
        };

        PositionChanged += (_, _) =>
        {
            if (WindowState == WindowState.Normal)
                config.Location = new(left: Position.X, top: Position.Y);
        };

        ClientSize = new(config.Size.Width!.Value, config.Size.Height!.Value);

        Position = new((int)centerPos.Left, (int)centerPos.Top);

        base.OnOpened(e);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        IView.SaveAppConfigChanges();

        base.OnClosing(e);
    }
}
