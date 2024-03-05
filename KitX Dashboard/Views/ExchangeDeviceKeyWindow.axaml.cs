using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Timers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using KitX.Dashboard.Services;
using KitX.Dashboard.ViewModels;
using MsBox.Avalonia;

namespace KitX.Dashboard.Views;

public partial class ExchangeDeviceKeyWindow : Window
{
    private readonly ExchangeDeviceKeyWindowViewModel viewModel = new();

    private Action<string>? OnVerificationCodeEnteredAction;

    private Timer? waittingAcceptingDeviceKeyTimer;

    public ExchangeDeviceKeyWindow()
    {
        InitializeComponent();

        DataContext = viewModel;

        EventService.OnExiting += Close;
    }

    public ExchangeDeviceKeyWindow OnVerificationCodeEntered(Action<string> action)
    {
        OnVerificationCodeEnteredAction = action;

        return this;
    }

    public ExchangeDeviceKeyWindow OnCancel(Action action)
    {
        _ = viewModel
            .OnCancel(action)
            .OnCancel(Close)
            ;

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

        EventService.OnAcceptingDeviceKey += keyCode =>
        {
            if (code.Equals(keyCode))
            {
                ConstantTable.ExchangeDeviceKeyCode = null;

                Dispatcher.UIThread.Post(Close);
            }
        };

        waittingAcceptingDeviceKeyTimer = new()
        {
            Interval = 60 * 1000,
            AutoReset = false
        };

        waittingAcceptingDeviceKeyTimer.Elapsed += (_, _) =>
        {
            ConstantTable.ExchangeDeviceKeyCode = null;

            Dispatcher.UIThread.Post(Close);
        };

        waittingAcceptingDeviceKeyTimer.Start();

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

    public ExchangeDeviceKeyWindow Success()
    {
        viewModel.SuccessedPanelOpacity = 1.0;

        var timer = new Timer(3 * 1000);

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

        if (viewModel.IsVerifing || (viewModel.IsEditable == false)) return;

        if (e.Key == Key.V && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;

            var clipboard = Clipboard;

            if (clipboard is null) return;

            var text = await clipboard.GetTextAsync();

            var regex = @"[1-9]{8}";

            if (text is null || (Regex.IsMatch(text, regex) == false)) return;

            viewModel.Paste(text);

            return;
        }

        var inMainKeys = e.Key >= Key.D1 && e.Key <= Key.D9;
        var inNumPad = e.Key >= Key.NumPad1 && e.Key <= Key.NumPad9;

        var code = char.MinValue;

        if (e.PhysicalKey == PhysicalKey.Backspace || inMainKeys || inNumPad)
            e.Handled = true;
        else return;

        var boxes = this.FindControl<StackPanel>("VerifyCodeBoxes");

        if (boxes is null) return;

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
        waittingAcceptingDeviceKeyTimer?.Stop();
        waittingAcceptingDeviceKeyTimer?.Dispose();

        base.OnClosing(e);
    }
}
