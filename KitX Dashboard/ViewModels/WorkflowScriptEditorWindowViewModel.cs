using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using ReactiveUI;
using KitX.Core.Contract.Tasks;
using KitX.Core.Contract.Workflow;
using KitX.Core.Tasks;
using KitX.Dashboard.Services;
using KitX.Shared.CSharp.Plugin;
using Serilog;

namespace KitX.Dashboard.ViewModels;

internal class WorkflowScriptEditorWindowViewModel : ViewModelBase
{
    private CancellationTokenSource? _cancellationTokenSource;

    private readonly IDisposable _codeDocumentSubscription;

    private readonly IBlockScriptService _blockScriptService;
    private readonly IWorkflowPluginService _workflowPluginService;

    private readonly ITasksService _tasksService;

    /// <summary>
    /// 构造函数，通过DI注入
    /// </summary>
    /// <param name="blockScriptService">通过DI注入的BlockScript服务</param>
    /// <param name="workflowPluginService">通过DI注入的Workflow插件服务</param>
    /// <param name="scriptExecutionService">通过DI注入的脚本执行服务</param>
    /// <param name="kcsFileService">通过DI注入的KCS文件服务</param>
    /// <param name="mainProgramAnalyzer">通过DI注入的主程序分析器</param>
    /// <param name="tasksService">通过DI注入的任务服务</param>
    public WorkflowScriptEditorWindowViewModel(
        IBlockScriptService blockScriptService,
        IWorkflowPluginService workflowPluginService,
        ITasksService tasksService)
    {
                _blockScriptService = blockScriptService;
                _workflowPluginService = workflowPluginService;
                _tasksService = tasksService;

        InitCommands();
        InitEvents();

        // 如果是块脚本模式，初始化默认的 HelperFuncCompare 辅助函数
        if (UseBlockMode)
        {
            InitializeDefaultHelperFunctions();
        }

        // 订阅CodeDocument属性变化
        _codeDocumentSubscription = this.WhenAnyValue(x => x.CodeDocument)
            .Subscribe(document =>
            {
                if (document != null)
                {
                    // 根据模式选择不同的常量解析方式
                    var constants = UseBlockMode
                        ? _blockScriptService.ParseConstantsFromBlockScript(document.Text)
                        : _workflowPluginService.ParseConstantsFromCode(document.Text);
                    UpdateVariableConstants(constants);
                }
            });
    }

    /// <summary>
    /// 初始化默认的辅助函数（块脚本模式）
    /// </summary>
    private void InitializeDefaultHelperFunctions()
    {
        // 添加 HelperFuncCompare - 比较两个数，返回 bool
        // op: "BEQ"(==), "BNE"(!=), "BLT"(<), "BGT"(>), "BLE"(<=), "BGE"(>=)
        var helperFuncCompare = new HelperFunction
        {
            Name = "HelperFuncCompare",
            ReturnType = "bool",
            Parameters = new List<HelperFunctionParameter>
            {
                new() { Name = "op", Type = "string" },
                new() { Name = "value1", Type = "object?" },
                new() { Name = "value2", Type = "object?" }
            },
            Code = @"var v1 = Convert.ToDouble(value1);
var v2 = Convert.ToDouble(value2);
return op switch
{
    ""BEQ"" => v1 == v2,
    ""BNE"" => v1 != v2,
    ""BLT"" => v1 < v2,
    ""BGT"" => v1 > v2,
    ""BLE"" => v1 <= v2,
    ""BGE"" => v1 >= v2,
    _ => false
};"
        };
        HelperFunctions.Add(helperFuncCompare);

        // 添加 HelperFuncAdd - 将两个数相加
        var helperFuncAdd = new HelperFunction
        {
            Name = "HelperFuncAdd",
            ReturnType = "int",
            Parameters = new List<HelperFunctionParameter>
            {
                new() { Name = "value1", Type = "object?" },
                new() { Name = "value2", Type = "object?" }
            },
            Code = @"var v1 = Convert.ToInt32(value1);
var v2 = Convert.ToInt32(value2);
return v1 + v2;"
        };
        HelperFunctions.Add(helperFuncAdd);
    }

    public sealed override void InitCommands()
    {
        CancelExecutionCommand = ReactiveCommand.Create(() => _cancellationTokenSource?.Cancel());

        // 添加辅助函数命令
        AddHelperFunctionCommand = ReactiveCommand.Create(() =>
        {
            var newFunction = new HelperFunction
            {
                Name = $"HelperFunction{HelperFunctions.Count + 1}",
                ReturnType = "object",
                Parameters = new List<HelperFunctionParameter>(),
                Code = "// Helper function body\nreturn null;"
            };
            HelperFunctions.Add(newFunction);
            SelectedHelperFunction = newFunction;
        });

        // 删除辅助函数命令
        RemoveHelperFunctionCommand = ReactiveCommand.Create<HelperFunction>((helperFunction) =>
        {
            if (helperFunction != null)
            {
                HelperFunctions.Remove(helperFunction);
                if (SelectedHelperFunction == helperFunction)
                {
                    SelectedHelperFunction = HelperFunctions.FirstOrDefault();
                }
            }
        });

        // 恢复常量默认值命令
        ResetConstantCommand = ReactiveCommand.Create<VariableConstant>((constant) =>
        {
            if (constant != null)
            {
                constant.UserValue = constant.DefaultValue;
                // 触发UI更新
                this.RaisePropertyChanged(nameof(VariableConstants));
            }
        });

        // 恢复所有常量命令
        ResetAllConstantsCommand = ReactiveCommand.Create(() =>
        {
            foreach (var constant in VariableConstants)
            {
                constant.UserValue = constant.DefaultValue;
            }
            this.RaisePropertyChanged(nameof(VariableConstants));
        });
    }

    public sealed override void InitEvents() { }

    /// <summary>
    /// 解析代码中的常量（自动根据当前模式选择解析方式）
    /// </summary>
    /// <param name="code">代码内容</param>
    public void ParseConstantsFromCode(string code)
    {
        var constants = UseBlockMode
            ? _blockScriptService.ParseConstantsFromBlockScript(code)
            : _workflowPluginService.ParseConstantsFromCode(code);
        UpdateVariableConstants(constants);
    }

    /// <summary>
    /// 更新可变常量列表
    /// </summary>
    private void UpdateVariableConstants(List<VariableConstant> newConstants)
    {
        // 保留用户已修改的值
        foreach (var newConstant in newConstants)
        {
            var existing = VariableConstants.FirstOrDefault(c => c.Name == newConstant.Name);
            if (existing != null)
            {
                newConstant.UserValue = existing.UserValue;
            }
        }

        // 更新列表
        VariableConstants.Clear();
        foreach (var constant in newConstants)
        {
            VariableConstants.Add(constant);
        }
    }

    /// <summary>
    /// Builds a dictionary of user-edited constant values that differ from defaults.
    /// Used to sync UI edits into the execution pipeline.
    /// </summary>
    private Dictionary<string, object?>? GetUserConstantOverrides()
    {
        if (VariableConstants.Count == 0) return null;

        var overrides = new Dictionary<string, object?>();
        foreach (var constant in VariableConstants)
        {
            // Only include values that the user has changed
            if (!object.Equals(constant.UserValue, constant.DefaultValue))
            {
                overrides[constant.Name] = constant.UserValue;
            }
        }

        return overrides.Count > 0 ? overrides : null;
    }

    /// <summary>
    /// 获取值的类型名称
    /// </summary>
    private string GetTypeName(object? value)
    {
        return value switch
        {
            null => "object",
            int => "int",
            double => "double",
            float => "float",
            bool => "bool",
            string => "string",
            _ => value.GetType().Name
        };
    }

    /// <summary>
    /// 提交代码执行
    /// </summary>
    internal void SubmitCodes(IDocument doc)
    {
        // 首先在UI线程获取代码文本，避免跨线程访问
        string codeText;
        try
        {
            codeText = doc.Text;
        }
        catch (InvalidOperationException)
        {
            ExecutionResult = "Error: Cannot access document from background thread.";
            return;
        }

        // 非Block模式：不做语法限制，直接执行
        if (UseBlockMode)
        {
            // BlockScript模式：验证块脚本
            var validationResult = _blockScriptService.ValidateBlockScript(codeText);
            if (!validationResult.IsValid)
            {
                Log.Error("[WorkflowScriptEditorWindowViewModel] Block script validation failed: {Errors}", string.Join("; ", validationResult.Errors));
                ExecutionResult = $"Block script validation failed: {string.Join("; ", validationResult.Errors)}";
                return;
            }
        }

        IsExecuting = true;

        // 获取已连接的插件列表
        var connectedPlugins = UIStateService.PluginInfos?.ToList() ?? new List<PluginInfo>();

        var tokenSource = new CancellationTokenSource();

        _cancellationTokenSource = tokenSource;

        _tasksService.RunTaskAsync(
            async () =>
            {
                string? result;

                var constantOverrides = GetUserConstantOverrides();
                var executionResult = await _blockScriptService.ExecuteBlockScriptAsync(
                    codeText,
                    HelperFunctions.ToList(),
                    constantOverrides,
                    tokenSource.Token
                );

                if (executionResult.IsSuccess)
                {
                    var output = string.Join("\n", executionResult.Output);
                    result = $"Blocks executed: {executionResult.ExecutedBlockCount}\nExecution time: {executionResult.ExecutionTimeMs}ms\nOutput:\n{output}";
                }
                else
                {
                    result = $"Error: {executionResult.ErrorMessage}";
                }

                tokenSource.Dispose();

                _cancellationTokenSource = null;

                Dispatcher.UIThread.Invoke(() =>
                {
                    ExecutionResult = result ?? string.Empty;

                    IsExecuting = false;
                });
            },
            tokenSource.Token,
            nameof(SubmitCodes)
        );
    }

    internal void CancelExecution()
    {
        _cancellationTokenSource?.Cancel();
    }

    #region Properties

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

    /// <summary>
    /// 主程序代码
    /// </summary>
    private string? _mainProgramCode;

    public string? MainProgramCode
    {
        get => _mainProgramCode;
        set => this.RaiseAndSetIfChanged(ref _mainProgramCode, value);
    }

    /// <summary>
    /// 辅助函数文档
    /// </summary>
    internal IDocument? HelperFunctionDocument { get; set; }

    /// <summary>
    /// 辅助函数列表
    /// </summary>
    public ObservableCollection<HelperFunction> HelperFunctions { get; set; } = [];

    /// <summary>
    /// 当前选中的辅助函数
    /// </summary>
    private HelperFunction? _selectedHelperFunction;

    public HelperFunction? SelectedHelperFunction
    {
        get => _selectedHelperFunction;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedHelperFunction, value);
            // 通知UI更新辅助函数代码编辑器
            this.RaisePropertyChanged(nameof(HelperFunctionDocument));
            // 通知UI更新是否正在编辑辅助函数
            this.RaisePropertyChanged(nameof(IsEditingHelperFunction));
            // 更新参数列表
            Parameters.Clear();
            if (value?.Parameters != null)
            {
                foreach (var param in value.Parameters)
                {
                    Parameters.Add(param);
                }
            }
        }
    }

    /// <summary>
    /// 是否正在编辑辅助函数
    /// </summary>
    public bool IsEditingHelperFunction => _selectedHelperFunction != null;

    /// <summary>
    /// 当前选中辅助函数的参数列表（用于UI绑定）
    /// </summary>
    public ObservableCollection<HelperFunctionParameter> Parameters { get; set; } = [];

    /// <summary>
    /// 可变常量列表
    /// </summary>
    public ObservableCollection<VariableConstant> VariableConstants { get; set; } = [];

    /// <summary>
    /// 是否使用块脚本模式
    /// </summary>
    private bool _useBlockMode = true;  // 默认开启块脚本模式

    public bool UseBlockMode
    {
        get => _useBlockMode;
        set => this.RaiseAndSetIfChanged(ref _useBlockMode, value);
    }

    #endregion

    #region Commands

    internal ReactiveCommand<Unit, Unit>? CancelExecutionCommand { get; set; }

    /// <summary>
    /// 添加辅助函数命令
    /// </summary>
    internal ReactiveCommand<Unit, Unit>? AddHelperFunctionCommand { get; set; }

    /// <summary>
    /// 删除辅助函数命令
    /// </summary>
    internal ReactiveCommand<HelperFunction, Unit>? RemoveHelperFunctionCommand { get; set; }

    /// <summary>
    /// 恢复常量默认值命令
    /// </summary>
    internal ReactiveCommand<VariableConstant, Unit>? ResetConstantCommand { get; set; }

    /// <summary>
    /// 恢复所有常量命令
    /// </summary>
    internal ReactiveCommand<Unit, Unit>? ResetAllConstantsCommand { get; set; }

    #endregion
}
