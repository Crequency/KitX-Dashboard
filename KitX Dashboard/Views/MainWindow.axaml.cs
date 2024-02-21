using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Common.BasicHelper.Graphics.Screen;
using FluentAvalonia.UI.Controls;
using KitX.Dashboard.Configuration;
using KitX.Dashboard.Converters;
using KitX.Dashboard.Generators;
using KitX.Dashboard.Names;
using KitX.Dashboard.Services;
using KitX.Dashboard.Utils;
using KitX.Dashboard.ViewModels;
using Serilog;
using System;
using System.Timers;

namespace KitX.Dashboard.Views;

public partial class MainWindow : Window, IView
{
    private readonly MainWindowViewModel viewModel = new();

    private static AppConfig AppConfig => Instances.ConfigManager.AppConfig;

    public MainWindow()
    {
        var location = $"{nameof(MainWindow)}";

        InitializeComponent();

        ViewInstances.MainWindow = this;

        DataContext = viewModel;

        var config = AppConfig.Windows.MainWindow;

        var screen = Screens.ScreenFromWindow(this);

        var nowRes = config.Size.SuggestResolution(screen);

        var centerPos = config.Location.BringToCenter(screen, nowRes);

        ClientSize = new(nowRes.Width!.Value, nowRes.Height!.Value);

        Position = new((int)centerPos.Left, (int)centerPos.Top);

        try
        {
            Instances.SignalTasksManager?.SignalRun(
                nameof(SignalsNames.MainWindowOpenedSignal),
                () => WindowState = config.WindowState
            );

            if (config.IsHidden)
                Instances.SignalTasksManager?.SignalRun(
                    nameof(SignalsNames.MainWindowOpenedSignal),
                    Hide
                );
        }
        catch (Exception e)
        {
            Log.Error(e, $"In {location}: {e.Message}");
        }

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

        InitMainWindow();
    }

    private void InitMainWindow()
    {
        MainNavigationView.SelectedItem = this.FindControl<NavigationViewItem>(SelectedPageName);

        RequestedThemeVariant = AppConfig.App.Theme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            "Follow" => ThemeVariant.Default,
            _ => ThemeVariant.Default
        };

        UpdateGreetingText();

        EventService.LanguageChanged += () => UpdateGreetingText();

        EventService.GreetingTextIntervalUpdated += () => UpdateGreetingText();

        var timer = new Timer()
        {
            AutoReset = true,
            Interval = 1000 * 60 * AppConfig.Windows.MainWindow.GreetingUpdateInterval
        };

        timer.Elapsed += (_, _) => UpdateGreetingText();

        timer.Start();

        Instances.SignalTasksManager?.RaiseSignal(nameof(SignalsNames.MainWindowInitSignal));
    }

    internal void UpdateGreetingText()
    {
        try
        {
            if (Application.Current is null) return;

            Dispatcher.UIThread.Invoke(() =>
            {
                Application.Current.Resources.MergedDictionaries[0].TryGetResource(
                    GreetingTextGenerator.GetKey(), ActualThemeVariant, out object? text
                );

                if (text is null) return;

                Dispatcher.UIThread.Post(() =>
                {
                    Resources["GreetingText"] = text as string;
                });
            });
        }
        catch (ArgumentOutOfRangeException e)
        {
            Log.Warning(e, $"No Language Resources Loaded.");
        }
    }

    private static Type GetPageTypeFromName(string name) => name switch
    {
        "Page_Home" => typeof(Pages.HomePage),
        "Page_Lib" => typeof(Pages.LibPage),
        "Page_Repo" => typeof(Pages.RepoPage),
        "Page_Account" => typeof(Pages.AccountPage),
        "Page_Settings" => typeof(Pages.SettingsPage),
        "Page_Market" => typeof(Pages.MarketPage),
        "Page_Device" => typeof(Pages.DevicePage),
        _ => typeof(Pages.HomePage),
    };

    private static string SelectedPageName
    {
        get => AppConfig.Windows.MainWindow.Tags["SelectedPage"];
        set
        {
            AppConfig.Windows.MainWindow.Tags["SelectedPage"] = value;

            IView.SaveAppConfigChanges();
        }
    }

    private void MainNavigationView_SelectionChanged(
        object? sender,
        NavigationViewSelectionChangedEventArgs e
    )
    {
        try
        {
            if (sender is null) return;

            var navView = sender as NavigationView;

            if (navView?.SelectedItem is not Control control || control.Tag is null) return;

            var pageName = control.Tag.ToString();

            if (pageName is null) return;

            SelectedPageName = pageName;

            MainFrame.Navigate(GetPageTypeFromName(SelectedPageName));
        }
        catch (NullReferenceException o)
        {
            Log.Warning(o, o.Message);
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (!ConstantTable.Exiting)
        {
            e.Cancel = true;

            Hide();

            AppConfig.Windows.MainWindow.IsHidden = true;

            IView.SaveAppConfigChanges();
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        Instances.SignalTasksManager?.RaiseSignal(nameof(SignalsNames.MainWindowOpenedSignal));
    }
}
