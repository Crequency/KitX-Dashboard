using System;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using ReactiveUI;
using KitX.Core.Contract.Workflow;

namespace KitX.Dashboard.ViewModels;

internal class WorkflowScriptEditorWindowViewModel : ViewModelBase
{
    private CancellationTokenSource? _cancellationTokenSource;

    private readonly IDisposable _codeDocumentSubscription;

    private readonly IWorkflowService _workflowService;

    /// <summary>
    /// Constructor with DI injection
    /// </summary>
    /// <param name="workflowService">Workflow service injected via DI</param>
    public WorkflowScriptEditorWindowViewModel(IWorkflowService workflowService)
    {
        _workflowService = workflowService;

        InitCommands();
        InitEvents();

        // 订阅CodeDocument属性变化
        _codeDocumentSubscription = this.WhenAnyValue(x => x.CodeDocument)
            .Subscribe(document =>
            {
                // 在这里处理CodeDocument变化的逻辑
                if (document != null)
                {
                    // 例如：可以在这里触发代码分析或保存操作
                    // SubmitCodes(document);
                }
            });
    }

    public sealed override void InitCommands()
    {
        CancelExecutionCommand = ReactiveCommand.Create(() => _cancellationTokenSource?.Cancel());
    }

    public sealed override void InitEvents() { }

    internal void SubmitCodes(IDocument doc)
    {
        IsExecuting = true;

        var code = doc.Text;

        var tokenSource = new CancellationTokenSource();

        _cancellationTokenSource = tokenSource;

        Task.Run(
            async () =>
            {
                // Use injected service instead of static method
                var result = await _workflowService.ExecuteCodesAsync(code, cancellationToken: tokenSource.Token);

                tokenSource.Dispose();

                _cancellationTokenSource = null;

                Dispatcher.UIThread.Invoke(() =>
                {
                    ExecutionResult = result ?? string.Empty;

                    IsExecuting = false;
                });
            },
            tokenSource.Token
        );
    }

    internal void CancelExecution()
    {
        _cancellationTokenSource?.Cancel();
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

    internal IDocument? CodeDocument { get; set; }

    internal ReactiveCommand<Unit, Unit>? CancelExecutionCommand { get; set; }
}
