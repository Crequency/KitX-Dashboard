using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Timers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using KitX.Core.Contract.Event;
using KitX.Dashboard;
using KitX.Dashboard.ViewModels;
using MsBox.Avalonia;

namespace KitX.Dashboard.Views;

public partial class ExchangeDeviceKeyWindow : Window
{
    private readonly ExchangeDeviceKeyWindowViewModel viewModel = App.GetService<ExchangeDeviceKeyWindowViewModel>();

    private Action<string>? OnVerificationCodeEnteredAction;

    private Timer? waitingAcceptingDeviceKeyTimer;

    /// <summary>
    /// Named handler for the accept-device-key event so it can be unsubscribed on close
    /// (D11: DisplayVerificationCode used to subscribe a fresh lambda per display).
    /// </summary>
    private EventHandler<DeviceKeyEventArgs>? _onAcceptingDeviceKeyHandler;

    private string? _displayedCode;

    /// <summary>Named OnExiting handler so the window can unsubscribe it on close (D11).</summary>
    private readonly EventHandler<EventArgs> _onExitingHandler;

    public ExchangeDeviceKeyWindow()
    {
        InitializeComponent();

        DataContext = viewModel;

        _onExitingHandler = (s, e) => Close();

        var eventService = App.GetService<IEventService>();
        eventService.Subscribe(EventNames.OnExiting, _onExitingHandler);
    }

    public ExchangeDeviceKeyWindow OnVerificationCodeEntered(Action<string> action)
    {
        OnVerificationCodeEnteredAction = action;

        return this;
    }

    public ExchangeDeviceKeyWindow OnCancel(Action action)
    {
        _ = viewModel.OnCancel(action).OnCancel(Close);

        return this;
    }

    public async Task<ExchangeDeviceKeyWindow> OnErrorDecodeAsync(string? message = null)
    {
        var box = MessageBoxManager.GetMessageBoxStandard(
            "",
            message ?? "Verification code is incorrect.",
            MsBox.Avalonia.Enums.ButtonEnum.Ok,
            MsBox.Avalonia.Enums.Icon.Error,
            WindowStartupLocation.CenterScreen
        );

        await box.ShowAsPopupAsync(this);

        return this;
    }

    public ExchangeDeviceKeyWindow DisplayVerificationCode(string code)
    {
        viewModel.IsEditable = false;

        viewModel.VerificationCodeString = code;

        // D11: unsubscribe any previous handler before (re-)subscribing, so repeated
        // displays never accumulate subscriptions on the singleton event bus.
        var eventService = App.GetService<IEventService>();
        if (_onAcceptingDeviceKeyHandler is not null)
            eventService.Unsubscribe(EventNames.OnAcceptingDeviceKey, _onAcceptingDeviceKeyHandler);

        _displayedCode = code;
        _onAcceptingDeviceKeyHandler = (s, e) =>
        {
            if (code.Equals(e.Key))
            {
                Dispatcher.UIThread.Post(Close);
            }
        };
        eventService.Subscribe(EventNames.OnAcceptingDeviceKey, _onAcceptingDeviceKeyHandler);

        waitingAcceptingDeviceKeyTimer = new() { Interval = 60 * 1000, AutoReset = false };

        waitingAcceptingDeviceKeyTimer.Elapsed += (_, _) =>
        {
            Dispatcher.UIThread.Post(Close);
        };

        waitingAcceptingDeviceKeyTimer.Start();

        return this;
    }

    public ExchangeDeviceKeyWindow ReturnToEdit()
    {
        viewModel.Backspace(false);

        return this;
    }

    public ExchangeDeviceKeyWindow Log(string info)
    {
        viewModel.Logs.Add(info);

        return this;
    }

    public ExchangeDeviceKeyWindow ClearLogs()
    {
        viewModel.Logs.Clear();

        return this;
    }

    public ExchangeDeviceKeyWindow Canceled()
    {
        viewModel.CanceledPanelOpacity = 1.0;

        var timer = new Timer(2 * 1000);

        timer.Elapsed += (_, _) =>
        {
            Dispatcher.UIThread.Post(Close);

            timer.Stop();
            timer.Dispose();
        };

        timer.Start();

        return this;
    }

    public ExchangeDeviceKeyWindow Success()
    {
        viewModel.SuccessedPanelOpacity = 1.0;

        var timer = new Timer(2 * 1000);

        timer.Elapsed += (_, _) =>
        {
            Dispatcher.UIThread.Post(Close);

            timer.Stop();
            timer.Dispose();
        };

        timer.Start();

        return this;
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        if (e.PhysicalKey == PhysicalKey.Tab)
        {
            e.Handled = true;

            return;
        }

        if (viewModel.IsVerifying || (viewModel.IsEditable == false))
            return;

        if (e.Key == Key.V && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;

            var clipboard = Clipboard;

            if (clipboard is null)
                return;

            var text = await clipboard.TryGetTextAsync();

            var regex = @"[1-9]{8}";

            if (text is null || (Regex.IsMatch(text, regex) == false))
                return;

            viewModel.Paste(text);

            return;
        }

        var inMainKeys = e.Key >= Key.D1 && e.Key <= Key.D9;
        var inNumPad = e.Key >= Key.NumPad1 && e.Key <= Key.NumPad9;

        var code = char.MinValue;

        if (e.PhysicalKey == PhysicalKey.Backspace || inMainKeys || inNumPad)
            e.Handled = true;
        else
            return;

        var boxes = this.FindControl<StackPanel>("VerifyCodeBoxes");

        if (boxes is null)
            return;

        if (e.PhysicalKey == PhysicalKey.Backspace)
            viewModel.Backspace();
        else if (inMainKeys)
            code = (char)((int)e.Key - (int)Key.D0 + '0');
        else
            code = (char)((int)e.Key - (int)Key.NumPad0 + '0');

        if (code != char.MinValue)
        {
            var result = viewModel.NextCode(code);

            if (result == false)
                OnVerificationCodeEnteredAction?.Invoke(viewModel.VerificationCodeString);
        }

        base.OnKeyDown(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);

        base.OnPointerPressed(e);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        waitingAcceptingDeviceKeyTimer?.Stop();
        waitingAcceptingDeviceKeyTimer?.Dispose();

        // D11: unsubscribe the event-bus handlers when the window closes.
        var eventService = App.GetService<IEventService>();
        if (_onAcceptingDeviceKeyHandler is not null)
        {
            eventService.Unsubscribe(EventNames.OnAcceptingDeviceKey, _onAcceptingDeviceKeyHandler);
            _onAcceptingDeviceKeyHandler = null;
        }
        eventService.Unsubscribe(EventNames.OnExiting, _onExitingHandler);

        base.OnClosing(e);
    }
}
