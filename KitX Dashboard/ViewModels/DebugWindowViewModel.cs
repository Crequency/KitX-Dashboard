using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using KitX.Dashboard.Services;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels;

internal class DebugWindowViewModel : ViewModelBase
{
    private CancellationTokenSource? _cancellationTokenSource;

    public DebugWindowViewModel()
    {
        InitCommands();

        InitEvents();
    }

    public override void InitCommands()
    {
        SubmitCodesCommand = ReactiveCommand.Create<IDocument>(SubmitCodes);

        CancelExecutionCommand = ReactiveCommand.Create(() => _cancellationTokenSource?.Cancel());
    }

    public override void InitEvents()
    {

    }

    internal void SubmitCodes(IDocument doc)
    {
        IsExecuting = true;

        var code = doc.Text;

        var tokenSource = new CancellationTokenSource();

        _cancellationTokenSource = tokenSource;

        Task.Run(async () =>
        {
            var result = await DebugService.ExecuteCodesAsync(code, tokenSource.Token);

            tokenSource.Dispose();

            _cancellationTokenSource = null;

            Dispatcher.UIThread.Invoke(() =>
            {
                ExecutionResult = result ?? string.Empty;

                IsExecuting = false;
            });
        });
    }

    private string _executionResult = string.Empty;

    public string ExecutionResult
    {
        get => _executionResult;
        set => this.RaiseAndSetIfChanged(ref _executionResult, value);
    }

    private bool _isExecuting;

    public bool IsExecuting
    {
        get => _isExecuting;
        set => this.RaiseAndSetIfChanged(ref _isExecuting, value);
    }

    internal ReactiveCommand<IDocument, Unit>? SubmitCodesCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? CancelExecutionCommand { get; set; }
}
