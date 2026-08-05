using System;
using System.Threading.Tasks;
using System.Timers;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Common.BasicHelper.Core.TaskSystem;
using FluentAvalonia.UI.Controls;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Event;
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
    private readonly MainWindowViewModel viewModel = App.GetService<MainWindowViewModel>();
    private readonly SignalTasksManager _signalTasksManager;

    /// <summary>Greeting-text refresh timer — stopped/disposed on window close (D13).</summary>
    private readonly Timer _greetingTimer = new() { AutoReset = true };

    /// <summary>Debounces window geometry writes to the config (D13).</summary>
    private readonly System.Threading.CancellationTokenSource _geometrySaveCts = new();

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

                    ScheduleGeometrySave();
                };

                PositionChanged += (_, _) =>
                {
                    if (WindowState != Avalonia.Controls.WindowState.Normal)
                        return;

                    config.Location.Left = Position.X;
                    config.Location.Top = Position.Y;

                    ScheduleGeometrySave();
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

        _greetingTimer.Interval = 1000 * 60 * AppConfig.Windows.MainWindow.GreetingUpdateInterval;

        _greetingTimer.Elapsed += (_, _) => UpdateGreetingText();

        _greetingTimer.Start();

        _signalTasksManager.RaiseSignal(nameof(SignalsNames.MainWindowInitSignal));
    }

    /// <summary>
    /// Debounced geometry persistence (D13): resize/move events fire continuously
    /// during a drag — wait 500ms of quiescence before writing the config file.
    /// </summary>
    private void ScheduleGeometrySave()
    {
        _geometrySaveCts.Cancel();
        var token = _geometrySaveCts.Token;
        _ = Task.Delay(500, token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (token.IsCancellationRequested) return;
                IView.SaveAppConfigChanges();
            });
        }, token);
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
        {
            // D13: the main window never fully closes during normal operation (hide),
            // but when the app IS exiting, release the per-minute timer + debounce token.
            _greetingTimer.Stop();
            _greetingTimer.Dispose();
            _geometrySaveCts.Cancel();
            _geometrySaveCts.Dispose();

            base.OnClosing(e);
            return;
        }

        e.Cancel = true;

        Hide();

        AppConfig.Windows.MainWindow.IsHidden = true;

        IView.SaveAppConfigChanges();

        base.OnClosing(e);
    }
}
