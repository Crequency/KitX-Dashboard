﻿using System;
using System.Reactive;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels;

internal class ExchangeDeviceKeyWindowViewModel : ViewModelBase
{
    private Action? OnCancelAction;

    public ExchangeDeviceKeyWindowViewModel()
    {
        InitCommands();

        InitEvents();
    }

    public override void InitCommands()
    {
        CancelCommand = ReactiveCommand.Create(() =>
        {
            OnCancelAction?.Invoke();
        });
    }

    public override void InitEvents()
    {

    }

    private int updatingIndex = 0;

    private readonly string[] verificationCode = ["0", "0", "0", "0", "0", "0", "0", "0"];

    public string[] VerificationCode => verificationCode;

    public bool IsVerifing => updatingIndex == VerificationCode.Length;

    public string VerificationCodeString => string.Join(null, VerificationCode);

    public string CurrentCodeIndex => updatingIndex.ToString();

    private void Update()
    {
        this.RaisePropertyChanged(nameof(VerificationCode));
        this.RaisePropertyChanged(nameof(CurrentCodeIndex));
        this.RaisePropertyChanged(nameof(VerificationCodeString));
        this.RaisePropertyChanged(nameof(IsVerifing));
    }

    internal bool NextCode(char code)
    {
        if (updatingIndex == VerificationCode.Length) return false;

        verificationCode[updatingIndex++] = code.ToString();

        Update();

        if (updatingIndex == VerificationCode.Length) return false;

        return true;
    }

    internal bool Backspace(bool clear = true)
    {
        if (updatingIndex == 0) return false;

        if (clear)
        {
            if (verificationCode[updatingIndex].Equals("0") == false)
                verificationCode[updatingIndex] = "0";
            else verificationCode[--updatingIndex] = "0";
        }
        else --updatingIndex;

        Update();

        return true;
    }

    internal void Paste(string code)
    {
        for (var i = 0; i < verificationCode.Length; ++i)
            verificationCode[i] = code[i].ToString();

        updatingIndex = verificationCode.Length;

        Update();
    }

    internal ExchangeDeviceKeyWindowViewModel OnCancel(Action action)
    {
        OnCancelAction = action;

        return this;
    }

    internal ReactiveCommand<Unit, Unit>? CancelCommand { get; set; }
}
