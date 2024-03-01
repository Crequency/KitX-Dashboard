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

public partial class AnouncementsWindow : Window, IView
{
    private readonly AnouncementsWindowViewModel viewModel = new();

    private readonly AppConfig appConfig = ConfigManager.Instance.AppConfig;

    public AnouncementsWindow()
    {
        InitializeComponent();

        viewModel.Window = this;

        DataContext = viewModel;

        var config = appConfig.Windows.AnnouncementWindow;

        var screen = Screens.ScreenFromWindow(this);

        var nowRes = config.Size.SuggestResolution(screen);

        var centerPos = config.Location.BringToCenter(screen, nowRes);

        ClientSize = new(nowRes.Width!.Value, nowRes.Height!.Value);

        Position = new((int)centerPos.Left, (int)centerPos.Top);

        ClientSizeProperty.Changed.Subscribe(size =>
        {
            if (WindowState != WindowState.Maximized)
                config.Size = new Resolution(ClientSize.Width, ClientSize.Height);
        });

        PositionChanged += (_, args) =>
        {
            if (WindowState == WindowState.Normal)
                config.Location = new(left: Position.X, top: Position.Y);
        };
    }

    internal AnouncementsWindow UpdateSource(Dictionary<string, string> src)
    {
        viewModel.Sources = src;

        return this;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        IView.SaveAppConfigChanges();

        base.OnClosing(e);
    }
}
