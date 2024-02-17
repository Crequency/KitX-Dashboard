using ReactiveUI;
using System.Reactive;
using AvaloniaEdit.Document;
using KitX.Dashboard.Services;

namespace KitX.Dashboard.ViewModels;

internal class DebugWindowViewModel : ViewModelBase
{
    public DebugWindowViewModel()
    {
        InitCommands();

        InitEvents();
    }

    public override void InitCommands()
    {
        SubmitCodesCommand = ReactiveCommand.Create<IDocument>(SubmitCodes);
    }

    public override void InitEvents()
    {

    }

    internal async void SubmitCodes(IDocument doc)
    {
        var code = doc.Text;

        var result = await DebugService.ExecuteCodesAsync(code);

        ExecutionResult = result ?? string.Empty;
    }

    private string _executionResult = string.Empty;

    public string ExecutionResult
    {
        get => _executionResult;
        set => this.RaiseAndSetIfChanged(ref _executionResult, value);
    }

    internal ReactiveCommand<IDocument, Unit>? SubmitCodesCommand { get; set; }
}
