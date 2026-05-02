using System;
using System.Timers;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Common.BasicHelper.Core.TaskSystem;
using FluentAvalonia.UI.Controls;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Event;
using KitX.Core.Event;
using KitX.Dashboard.Converters;
using KitX.Dashboard.Generators;
using KitX.Dashboard.Names;
using KitX.Dashboard.Services;
using KitX.Dashboard.Utils;
using KitX.Dashboard.ViewModels;
using Serilog;

namespace KitX.Dashboard.Views;

public partial class MainWindow : Window, IView
{
    private readonly MainWindowViewModel viewModel = new();
    private readonly SignalTasksManager _signalTasksManager;

    private static IAppConfig AppConfig => App.GetService<IConfigService>().AppConfig;

    public MainWindow()
    {
        const string location = $"{nameof(MainWindow)}";

        InitializeComponent();

        UIStateService.MainWindow = this;

        DataContext = viewModel;

        _signalTasksManager = App.GetService<SignalTasksManager>();

        _signalTasksManager.SignalRun(
            nameof(SignalsNames.MainWindowOpenedSignal),
            () =>
            {
                var config = AppConfig.Windows.MainWindow;

                var screen = Screens.ScreenFromWindow(this);

                config.Size = config.Size.SuggestResolution(screen, out var notScaled);

                var centerPos = config.Location.BringToCenter(screen, notScaled ?? config.Size);

                try
                {
                    _signalTasksManager.SignalRun(
                        nameof(SignalsNames.MainWindowOpenedSignal),
                        () => WindowState = config.WindowState.ToAvalonia()
                    );

                    if (config.IsHidden)
                        _signalTasksManager.SignalRun(nameof(SignalsNames.MainWindowOpenedSignal), Hide);
                }
                catch (Exception e)
                {
                    Log.Error(e, $"In {location}: {e.Message}");
                }

                SizeChanged += (_, _) =>
                {
                    if (WindowState == Avalonia.Controls.WindowState.Maximized)
                        return;

                    config.Size.Width = ClientSize.Width;
                    config.Size.Height = ClientSize.Height;
                };

                PositionChanged += (_, _) =>
                {
                    if (WindowState != Avalonia.Controls.WindowState.Normal)
                        return;

                    config.Location.Left = Position.X;
                    config.Location.Top = Position.Y;
                };

                if (WindowState != Avalonia.Controls.WindowState.Normal)
                    return;

                ClientSize = new(config.Size.Width!.Value, config.Size.Height!.Value);

                Position = new((int)centerPos.Left, (int)centerPos.Top);
            }
        );

        InitMainWindow();
    }

    private void InitMainWindow()
    {
        MainNavigationView.SelectedItem = this.FindControl<NavigationViewItem>(SelectedPageName);

        UpdateGreetingText();

        var eventService = App.GetService<IEventService>();
        eventService.Subscribe(EventNames.LanguageChanged, (s, e) => UpdateGreetingText());

        eventService.Subscribe(EventNames.GreetingTextIntervalUpdated, (s, e) => UpdateGreetingText());

        var timer = new Timer() { AutoReset = true, Interval = 1000 * 60 * AppConfig.Windows.MainWindow.GreetingUpdateInterval };

        timer.Elapsed += (_, _) => UpdateGreetingText();

        timer.Start();

        _signalTasksManager.RaiseSignal(nameof(SignalsNames.MainWindowInitSignal));
    }

    internal void UpdateGreetingText()
    {
        try
        {
            if (Application.Current is null)
                return;

            Dispatcher.UIThread.Invoke(() =>
            {
                Application
                    .Current.Resources.MergedDictionaries[0]
                    .TryGetResource(GreetingTextGenerator.GetKey(), ActualThemeVariant, out object? text);

                if (text is null)
                    return;

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

    private static Type GetPageTypeFromName(string name) =>
        name switch
        {
            "Page_Home" => typeof(Pages.HomePage),
            "Page_Lib" => typeof(Pages.LibPage),
            "Page_Repo" => typeof(Pages.RepoPage),
            "Page_Account" => typeof(Pages.AccountPage),
            "Page_Settings" => typeof(Pages.SettingsPage),
            "Page_Market" => typeof(Pages.MarketPage),
            "Page_Device" => typeof(Pages.DevicesPage),
            "Page_Workflow" => typeof(Pages.WorkflowPage),
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

    private void MainNavigationView_SelectionChanged(object? sender, NavigationViewSelectionChangedEventArgs e)
    {
        try
        {
            if (sender is null)
                return;

            var navView = sender as NavigationView;

            if (navView?.SelectedItem is not Control control || control.Tag is null)
                return;

            var pageName = control.Tag.ToString();

            if (pageName is null)
                return;

            SelectedPageName = pageName;

            MainFrame.Navigate(GetPageTypeFromName(SelectedPageName));
        }
        catch (NullReferenceException o)
        {
            Log.Warning(o, o.Message);
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        _signalTasksManager.RaiseSignal(nameof(SignalsNames.MainWindowOpenedSignal));
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (ConstantTable.Exiting)
            return;

        e.Cancel = true;

        Hide();

        AppConfig.Windows.MainWindow.IsHidden = true;

        IView.SaveAppConfigChanges();

        base.OnClosing(e);
    }
}
