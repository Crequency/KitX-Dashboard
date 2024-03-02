using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using KitX.Dashboard.ViewModels;
using MsBox.Avalonia;

namespace KitX.Dashboard.Views;

public partial class ExchangeDeviceKeyWindow : Window
{
    private readonly ExchangeDeviceKeyWindowViewModel viewModel = new();

    private Action<string>? OnVerificationCodeEnteredAction;

    public ExchangeDeviceKeyWindow()
    {
        InitializeComponent();

        DataContext = viewModel;
    }

    public ExchangeDeviceKeyWindow OnVerificationCodeEntered(Action<string> action)
    {
        OnVerificationCodeEnteredAction = action;

        return this;
    }

    public ExchangeDeviceKeyWindow OnCancel(Action action)
    {
        _ = viewModel.OnCancel(action);

        return this;
    }

    public async Task<ExchangeDeviceKeyWindow> OnErrorDecodeAsync()
    {
        var box = MessageBoxManager.GetMessageBoxStandard(
            "",
            "Verification code is incorrect.",
            MsBox.Avalonia.Enums.ButtonEnum.Ok,
            MsBox.Avalonia.Enums.Icon.Error,
            Avalonia.Controls.WindowStartupLocation.CenterScreen
        );

        await box.ShowWindowDialogAsync(this);

        return this;
    }

    public ExchangeDeviceKeyWindow ReturnToEdit()
    {
        viewModel.Backspace(false);

        return this;
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        if (e.PhysicalKey == PhysicalKey.Tab)
        {
            e.Handled = true;

            return;
        }

        if (viewModel.IsVerifing) return;

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
}
