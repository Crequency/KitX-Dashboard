using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using Common.BasicHelper.Utils.Extensions;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels;

internal class ExchangeDeviceKeyWindowViewModel : ViewModelBase
{
    private readonly Queue<Action> OnCancelActions = [];

    public ExchangeDeviceKeyWindowViewModel()
    {
        InitCommands();

        InitEvents();
    }

    public override void InitCommands()
    {
        CancelCommand = ReactiveCommand.Create(() =>
        {
            OnCancelActions.ForEach(x => x.Invoke());
        });
    }

    public override void InitEvents()
    {

    }

    private int updatingIndex = 0;

    private readonly string[] verificationCode = ["0", "0", "0", "0", "0", "0", "0", "0"];

    public string[] VerificationCode => verificationCode;

    public bool IsVerifing => updatingIndex == VerificationCode.Length;

    private bool isEditable = true;

    public bool IsEditable
    {
        get => isEditable;
        set
        {
            this.RaiseAndSetIfChanged(ref isEditable, value);

            Update();
        }
    }

    public bool IsDisplayingVerificationCode => !IsEditable;

    public string VerificationCodeString
    {
        get => string.Join(null, VerificationCode);
        set
        {
            if (value.Length != verificationCode.Length) throw new InvalidCastException();

            for (var i = 0; i < verificationCode.Length; ++i)
                verificationCode[i] = value[i].ToString();

            Update();
        }
    }

    private double successedPanelOpacity = 0.0;

    public double SuccessedPanelOpacity
    {
        get => successedPanelOpacity;
        set => this.RaiseAndSetIfChanged(ref successedPanelOpacity, value);
    }

    public string CurrentCodeIndex => updatingIndex.ToString();

    public ObservableCollection<string> Logs { get; } = [];

    private void Update()
    {
        this.RaisePropertyChanged(nameof(VerificationCode));
        this.RaisePropertyChanged(nameof(CurrentCodeIndex));
        this.RaisePropertyChanged(nameof(VerificationCodeString));
        this.RaisePropertyChanged(nameof(IsVerifing));
        this.RaisePropertyChanged(nameof(IsDisplayingVerificationCode));
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
        OnCancelActions.Enqueue(action);

        return this;
    }

    internal ReactiveCommand<Unit, Unit>? CancelCommand { get; set; }
}
