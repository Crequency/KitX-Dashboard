using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using KitX.Dashboard.ViewModels;
using AvaloniaEdit;
using Avalonia.Interactivity;
using System.IO;
using TextMateSharp.Grammars;
using AvaloniaEdit.TextMate;
using Avalonia.Styling;
using Avalonia.Platform.Storage;
using KitX.Core.Contract.Event;
using KitX.Core.Event;
using KitX.Core.Contract.Workflow;
using KitX.Dashboard;
using KitX.Dashboard.Services;

namespace KitX.Dashboard.Views;

public partial class WorkflowScriptEditorWindow : Window, IView
{
    private readonly WorkflowScriptEditorWindowViewModel viewModel;
    private bool _isEditingHelperFunction = false;
    private bool _isUpdatingOutput = false;
    private CancellationTokenSource? _debounceCts;

    public WorkflowScriptEditorWindow()
    {
        InitializeComponent();

        // Use DI to get the ViewModel
        viewModel = App.GetService<WorkflowScriptEditorWindowViewModel>();

        DataContext = viewModel;

        Initialize();
    }

    private void Initialize()
    {
        InitializeEditor();

        var eventService = App.GetService<IEventService>();
        eventService.Subscribe(EventNames.ThemeConfigChanged, (s, e) => InitializeEditor());

        var codeEditor = this.FindControl<TextEditor>("CodeEditor");
        var outputTextBox = this.FindControl<TextBox>("OutputTextBox");
        var helperFunctionsListBox = this.FindControl<ListBox>("HelperFunctionsListBox");
        var constantsItemsControl = this.FindControl<ItemsControl>("ConstantsItemsControl");
        var parametersItemsControl = this.FindControl<ItemsControl>("ParametersItemsControl");

        if (codeEditor != null)
        {
            viewModel.CodeDocument = codeEditor.Document;

            // 根据 UseBlockMode 选择模板代码
            if (viewModel.UseBlockMode)
            {
                // BlockScript 模式模板
                codeEditor.Text = @"#ConstBlock
int guessNum = 5;  // 可变常量，用户在UI中可修改
int loopMax = 3;
int targetNum = 7;
int currentLoop;	// 无预赋值（值初始化），用户无法在UI中修改
// 这个currentLoop为什么不能放在PubVarBlock中：
// 它不是“一次性”的“边数据承载”变量，它是多处、多次使用且随运行而需要变化并持久存储的变量，它的最短生命周期远长于PubVarBlock中的一次性变量（赋值-使用1次后即可销毁，下次用到再重新创建）

#PubVarBlock
// 自动生成，为蓝图预留(是那些数据边为了临时承载数据而使用的变量）
// 除非你知道自己在做什么并且完全了解块脚本与蓝图互译的过程，否则不要在这个块中添加、删除或修改代码
// 直接编写BlockScript时不需要在这里设置变量
bool vaaa0001;
int vaaa0002;

#MainBlock
Print(""开始执行工作流"");
Set(""currentLoop"", 0);
// NextBlock = Loop(HelperFuncCompare(""BLE"", Get(""currentLoop""), loopMax), ""LoopBody"", ""EndLogic""); // 转化前的语句（有嵌套调用）
vaaa0001 = HelperFuncCompare(""BLE"", Get(""currentLoop""), loopMax);  // 脚本转蓝图后，再转回块脚本时，就会利用PubVarBlock中生成的“临时变量”来生成这样的语句（拆分嵌套调用）
NextBlock = Loop(vaaa0001, ""LoopBody"", ""EndLogic"");  // 主循环

#Block LoopBody
// Print(Get(currentLoop)); // 原始嵌套调用用法
// vaaa0001 = Get(currentLoop);
// Print(vaaa0001); // 其实逻辑上等价但是不推荐的方案──反正PubVarBlock的变量池是“无限大”的，没必要重复使用同一个变量。
vaaa0002 = Get(""currentLoop"");
Print(vaaa0002); // 更优的做法，每个临时变量实际上绑定了一条数据边。这样也方便后续直接对蓝图脚本进行Debug时监测数据边上的数据
Set(""currentLoop"", HelperFuncAdd(Get(""currentLoop""), 1));  // 函数嵌套调用，记得在默认的HelperFunction初始化程序中添加这个HelperFuncAdd
NextBlock = Branch(
    HelperFuncCompare(""BEQ"", guessNum, targetNum),  // ✅ 函数调用
    ""SuccessLogic"",
    ""CheckLogic""
);

#Block CheckLogic
NextBlock = Branch(
    HelperFuncCompare(""BLT"", guessNum, targetNum),
    ""LessThanLogic"",
    ""GreaterThanLogic""
);

#Block LessThanLogic
Print(""猜小了"");  // 其实用Print()也行
vaaa0001 = HelperFuncCompare(""BLE"", Get(""currentLoop""), loopMax);  // 原先Loop中的内置嵌套condition表达式由于被拆分，需要在LoopBodyEnd被调用前进行结算，以保持逻辑一致性
NextBlock = LoopBodyEnd(""MainBlock"");  // 返回到 MainBlock 的 Loop

#Block GreaterThanLogic
Print(""猜大了"");
vaaa0001 = HelperFuncCompare(""BLE"", Get(""currentLoop""), loopMax);  // 原先Loop中的内置嵌套condition表达式由于被拆分，需要在LoopBodyEnd被调用前进行结算，以保持逻辑一致性
// 这里仍是vaaa0001是因为在蓝图中实际上是一条边：CallHelper:HelperFuncCompare(BLE).Return --> Loop.Condition | PubVar=vaaa0001
NextBlock = LoopBodyEnd(""MainBlock"");  // 返回到 MainBlock 的 Loop

#Block SuccessLogic
Print(""猜对啦！""); // 这个Block没有Branch/Loop/LoopBodyEnd，自然进入下一行

#Block EndLogic
Print(""示例工作流结束"");";
            }
            else
            {
                // C# Script模式模板（无语法限制）
                codeEditor.Text = @"// Workflow Script Example (Full C# Script Mode)
	// Full C# syntax is supported: if/else, for/while, try/catch, etc.
	// Use WorkflowOutput.WriteLine() to print messages

	var greeting = ""Hello from workflow script!"";
	var count = 3;

	WorkflowOutput.WriteLine(greeting);
	WorkflowOutput.WriteLine($""Count: {count}"");

	// You can use any C# constructs:
	for (int i = 0; i < count; i++)
	{
	    WorkflowOutput.WriteLine($""  Step {i + 1}"");
	}

	// You can call plugin functions directly:
	// TestPlugin.WPF.Core.HelloKitX();

	WorkflowOutput.WriteLine(""Done!"");";
            }

            // 订阅代码变化事件（带防抖）
            codeEditor.TextChanged += (s, e) =>
            {
                if (codeEditor.Document == null) return;

                // Helper function editing: sync immediately (no debounce needed)
                if (_isEditingHelperFunction)
                {
                    if (viewModel.SelectedHelperFunction != null)
                    {
                        viewModel.SelectedHelperFunction.Code = codeEditor.Document.Text;
                    }
                    return;
                }

                // Main program editing: debounce parse-heavy operations
                viewModel.MainProgramCode = codeEditor.Document.Text;

                _debounceCts?.Cancel();
                _debounceCts = new CancellationTokenSource();
                var token = _debounceCts.Token;

                _ = Task.Delay(500, token).ContinueWith(t =>
                {
                    if (t.IsCanceled) return;
                    Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (codeEditor.Document == null) return;
                        viewModel.ParseConstantsFromCode(codeEditor.Document.Text);
                        if (constantsItemsControl != null)
                        {
                            constantsItemsControl.ItemsSource = viewModel.VariableConstants;
                        }
                    });
                }, token);
            };
        }

        if (helperFunctionsListBox != null)
        {
            helperFunctionsListBox.ItemsSource = viewModel.HelperFunctions;
        }

        if (constantsItemsControl != null)
        {
            constantsItemsControl.ItemsSource = viewModel.VariableConstants;
        }

        var runButton = this.FindControl<Button>("RunButton");
        var stopButton = this.FindControl<Button>("StopButton");
        var saveButton = this.FindControl<Button>("SaveButton");
        var openButton = this.FindControl<Button>("OpenButton");
        var addHelperFunctionButton = this.FindControl<Button>("AddHelperFunctionButton");
        var openBlueprintEditorButton = this.FindControl<Button>("OpenBlueprintEditorButton");
        var statusText = this.FindControl<TextBlock>("StatusText");

        if (runButton != null)
            runButton.Click += RunButton_Click;

        if (stopButton != null)
            stopButton.Click += StopButton_Click;

        if (saveButton != null)
            saveButton.Click += SaveButton_Click;

        if (openButton != null)
            openButton.Click += OpenButton_Click;

        if (addHelperFunctionButton != null)
            addHelperFunctionButton.Click += AddHelperFunctionButton_Click;

        if (openBlueprintEditorButton != null)
            openBlueprintEditorButton.Click += OpenBlueprintEditorButton_Click;

        // 使用事件监听替代Observable
        viewModel.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(viewModel.IsExecuting))
            {
                if (runButton != null)
                    runButton.IsEnabled = !viewModel.IsExecuting;

                if (stopButton != null)
                    stopButton.IsEnabled = viewModel.IsExecuting;

                if (statusText != null)
                    statusText.Text = viewModel.IsExecuting ? "Running..." : "Ready";
            }
            else if (e.PropertyName == nameof(viewModel.ExecutionResult))
            {
                // 防止循环更新
                if (_isUpdatingOutput) return;

                if (outputTextBox != null)
                {
                    _isUpdatingOutput = true;
                    outputTextBox.Text = viewModel.ExecutionResult ?? string.Empty;
                    _isUpdatingOutput = false;
                }
            }
            else if (e.PropertyName == nameof(viewModel.CurrentFilePath))
            {
                var currentFileLabel = this.FindControl<TextBlock>("CurrentFileLabel");
                if (currentFileLabel != null)
                {
                    currentFileLabel.Text = string.IsNullOrEmpty(viewModel.CurrentFilePath)
                        ? " (New File)"
                        : $" ({Path.GetFileName(viewModel.CurrentFilePath)})";
                }
            }
        };
    }

    private void InitializeEditor()
    {
        var textEditor = this.FindControl<TextEditor>("CodeEditor");

        SetEditor(textEditor, ".cs");
    }

    private void SetEditor(TextEditor? textEditor, string ext)
    {
        if (textEditor is null)
            return;

        var registryOptions = new RegistryOptions(ActualThemeVariant == ThemeVariant.Light ? ThemeName.LightPlus : ThemeName.DarkPlus);
        var textMateInstallation = textEditor.InstallTextMate(registryOptions);
        textMateInstallation.SetGrammar(registryOptions.GetScopeByLanguageId(registryOptions.GetLanguageByExtension(ext).Id));
    }

    private async void OpenButton_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var openOptions = new FilePickerOpenOptions
        {
            Title = "Open Workflow Script",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("KCS Files (*.kcs)")
                {
                    Patterns = new[] { "*.kcs" }
                },
                new FilePickerFileType("C# Files (*.cs)")
                {
                    Patterns = new[] { "*.cs" }
                },
                new FilePickerFileType("All Files")
                {
                    Patterns = new[] { "*" }
                }
            }
        };

        var result = await topLevel.StorageProvider.OpenFilePickerAsync(openOptions);
        if (result != null && result.Count > 0)
        {
            var file = result[0];
            var filePath = file.Path.LocalPath;

            if (filePath.EndsWith(".kcs"))
            {
                // 加载KCS格式文件
                await viewModel.LoadKcsFileAsync(filePath);

                // 确保切换回主程序编辑模式
                _isEditingHelperFunction = false;

                // 更新UI
                var codeEditor = this.FindControl<TextEditor>("CodeEditor");
                var helperFunctionsListBox = this.FindControl<ListBox>("HelperFunctionsListBox");
                var constantsItemsControl = this.FindControl<ItemsControl>("ConstantsItemsControl");

                if (codeEditor != null)
                {
                    codeEditor.Text = viewModel.MainProgramCode ?? string.Empty;
                }

                if (helperFunctionsListBox != null)
                {
                    helperFunctionsListBox.ItemsSource = viewModel.HelperFunctions;
                }

                if (constantsItemsControl != null)
                {
                    constantsItemsControl.ItemsSource = viewModel.VariableConstants;
                }

                viewModel.CurrentFilePath = filePath;
            }
            else
            {
                // 加载旧的.cs文件（仅主程序）
                try
                {
                    var content = await File.ReadAllTextAsync(filePath);
                    var codeEditor = this.FindControl<TextEditor>("CodeEditor");
                    if (codeEditor != null)
                    {
                        codeEditor.Text = content;
                    }
                    viewModel.CurrentFilePath = filePath;
                }
                catch (Exception ex)
                {
                    var outputTextBox = this.FindControl<TextBox>("OutputTextBox");
                    if (outputTextBox != null)
                    {
                        outputTextBox.Text = $"Error loading file: {ex.Message}";
                    }
                }
            }
        }
    }

    private async void RunButton_Click(object? sender, RoutedEventArgs e)
    {
        if (viewModel.CodeDocument != null)
        {
            // 如果当前正在编辑辅助函数，先保存辅助函数代码
            if (_isEditingHelperFunction && viewModel.SelectedHelperFunction != null)
            {
                viewModel.SelectedHelperFunction.Code = viewModel.CodeDocument.Text;
            }

            // 切换回主程序编辑模式
            _isEditingHelperFunction = false;
            viewModel.SelectedHelperFunction = null;

            var codeEditor = this.FindControl<TextEditor>("CodeEditor");
            var codeEditorTitle = this.FindControl<TextBlock>("CodeEditorTitle");

            if (codeEditor != null)
            {
                codeEditor.Text = viewModel.MainProgramCode ?? string.Empty;
            }

            if (codeEditorTitle != null)
            {
                codeEditorTitle.Text = "Main Program";
            }

            // 等待 UI 更新后再执行
            await Task.Delay(50);

            viewModel.SubmitCodes(viewModel.CodeDocument);
        }
    }

    private void StopButton_Click(object? sender, RoutedEventArgs e)
    {
        viewModel.CancelExecution();
    }

    private async void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        // 使用新的存储提供者API替代过时的SaveFileDialog
        var saveOptions = new FilePickerSaveOptions
        {
            Title = "Save Workflow Script",
            DefaultExtension = "kcs",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("KCS Files (*.kcs)")
                {
                    Patterns = new[] { "*.kcs" }
                },
                new FilePickerFileType("C# Files (*.cs)")
                {
                    Patterns = new[] { "*.cs" }
                },
                new FilePickerFileType("All Files")
                {
                    Patterns = new[] { "*" }
                }
            }
        };

        var result = await topLevel.StorageProvider.SaveFilePickerAsync(saveOptions);
        if (result != null)
        {
            var filePath = result.Path.LocalPath;

            if (filePath.EndsWith(".kcs"))
            {
                // 保存为KCS格式
                await viewModel.SaveKcsFileAsync(filePath);
                viewModel.CurrentFilePath = filePath;
            }
            else
            {
                // 保存为普通C#文件（仅保存主程序）
                if (viewModel.CodeDocument != null)
                {
                    await using var stream = await result.OpenWriteAsync();
                    using var writer = new StreamWriter(stream);
                    await writer.WriteAsync(viewModel.CodeDocument.Text);
                }
            }
        }
    }

    private void AddHelperFunctionButton_Click(object? sender, RoutedEventArgs e)
    {
        AddHelperFunction();
    }

    private void OpenBlueprintEditorButton_Click(object? sender, RoutedEventArgs e)
    {
        var codeEditor = this.FindControl<TextEditor>("CodeEditor");

        // Create bridge connecting BlueprintEditor to this WorkflowEditor
        var bridge = new WorkflowEditorBridge(
            viewModel,
            code => { if (codeEditor != null) codeEditor.Text = code; },
            () =>
            {
                // Trigger execution via the Run button's logic
                if (viewModel.CodeDocument != null)
                {
                    viewModel.SubmitCodes(viewModel.CodeDocument);
                }
            }
        );

        var blueprintEditorWindow = new BlueprintEditorWindow(bridge);
        blueprintEditorWindow.Show();
    }

    private void AddHelperFunctionListButton_Click(object? sender, RoutedEventArgs e)
    {
        AddHelperFunction();
    }

    private void AddHelperFunction()
    {
        viewModel.AddHelperFunctionCommand?.Execute().Subscribe();

        // 更新UI
        var helperFunctionsListBox = this.FindControl<ListBox>("HelperFunctionsListBox");
        if (helperFunctionsListBox != null)
        {
            helperFunctionsListBox.ItemsSource = null;
            helperFunctionsListBox.ItemsSource = viewModel.HelperFunctions;

            if (viewModel.SelectedHelperFunction != null)
            {
                helperFunctionsListBox.SelectedItem = viewModel.SelectedHelperFunction;
            }
        }
    }

    private void HelperFunctionsListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.SelectedItem is HelperFunction selectedFunction)
        {
            viewModel.SelectedHelperFunction = selectedFunction;

            // 切换到辅助函数编辑模式
            _isEditingHelperFunction = true;

            var codeEditor = this.FindControl<TextEditor>("CodeEditor");
            var codeEditorTitle = this.FindControl<TextBlock>("CodeEditorTitle");

            if (codeEditor != null && selectedFunction != null)
            {
                codeEditor.Text = selectedFunction.Code;
            }

            if (codeEditorTitle != null)
            {
                codeEditorTitle.Text = $"Helper Function: {selectedFunction?.Name}";
            }
        }
    }

    private void RemoveHelperFunctionButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is HelperFunction function)
        {
            viewModel.RemoveHelperFunctionCommand?.Execute(function).Subscribe();

            // 更新UI
            var helperFunctionsListBox = this.FindControl<ListBox>("HelperFunctionsListBox");
            if (helperFunctionsListBox != null)
            {
                helperFunctionsListBox.ItemsSource = null;
                helperFunctionsListBox.ItemsSource = viewModel.HelperFunctions;
            }

            // 如果删除的是当前正在编辑的函数，切换回主程序
            if (viewModel.SelectedHelperFunction == null)
            {
                _isEditingHelperFunction = false;
                var codeEditor = this.FindControl<TextEditor>("CodeEditor");
                var codeEditorTitle = this.FindControl<TextBlock>("CodeEditorTitle");

                if (codeEditor != null)
                {
                    codeEditor.Text = viewModel.MainProgramCode ?? string.Empty;
                }

                if (codeEditorTitle != null)
                {
                    codeEditorTitle.Text = "Main Program";
                }
            }
        }
    }

    private void HelperFunctionNameEditor_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Enter && sender is TextBox textBox)
        {
            // 按下回车时取消焦点，将焦点移到窗口上
            var topLevel = GetTopLevel(this);
            topLevel?.Focus();
            e.Handled = true;
        }
    }

    private void HelperFunctionNameEditor_LostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.DataContext is HelperFunction function)
        {
            // 当 TextBox 失去焦点时，同步更新 CodeEditorTitle 中的名称
            var codeEditorTitle = this.FindControl<TextBlock>("CodeEditorTitle");
            if (codeEditorTitle != null && viewModel.SelectedHelperFunction == function)
            {
                codeEditorTitle.Text = $"Helper Function: {function.Name}";
            }
        }
    }

    private void AddParameterButton_Click(object? sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedHelperFunction != null)
        {
            var newParam = new HelperFunctionParameter
            {
                Name = $"param{viewModel.SelectedHelperFunction.Parameters.Count + 1}",
                Type = "object"
            };
            // 同时添加到 ViewModel 的 ObservableCollection 和数据模型的 List
            viewModel.Parameters.Add(newParam);
            viewModel.SelectedHelperFunction.Parameters.Add(newParam);
        }
    }

    private void RemoveParameterButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is HelperFunctionParameter param)
        {
            if (viewModel.SelectedHelperFunction != null)
            {
                viewModel.Parameters.Remove(param);
                viewModel.SelectedHelperFunction.Parameters.Remove(param);
            }
        }
    }

    private void ResetConstantButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is VariableConstant constant)
        {
            viewModel.ResetConstantCommand?.Execute(constant).Subscribe();

            // 更新UI
            var constantsItemsControl = this.FindControl<ItemsControl>("ConstantsItemsControl");
            if (constantsItemsControl != null)
            {
                constantsItemsControl.ItemsSource = null;
                constantsItemsControl.ItemsSource = viewModel.VariableConstants;
            }
        }
    }

    private void ResetAllConstantsButton_Click(object? sender, RoutedEventArgs e)
    {
        viewModel.ResetAllConstantsCommand?.Execute().Subscribe();

        // 更新UI
        var constantsItemsControl = this.FindControl<ItemsControl>("ConstantsItemsControl");
        if (constantsItemsControl != null)
        {
            constantsItemsControl.ItemsSource = null;
            constantsItemsControl.ItemsSource = viewModel.VariableConstants;
        }
    }

    private void BackToMainProgramButton_Click(object? sender, RoutedEventArgs e)
    {
        // 切换回主程序编辑模式
        _isEditingHelperFunction = false;
        viewModel.SelectedHelperFunction = null;

        var codeEditor = this.FindControl<TextEditor>("CodeEditor");
        var codeEditorTitle = this.FindControl<TextBlock>("CodeEditorTitle");

        if (codeEditor != null)
        {
            codeEditor.Text = viewModel.MainProgramCode ?? string.Empty;
        }

        if (codeEditorTitle != null)
        {
            codeEditorTitle.Text = "Main Program";
        }

        // 取消选中列表
        var helperFunctionsListBox = this.FindControl<ListBox>("HelperFunctionsListBox");
        if (helperFunctionsListBox != null)
        {
            helperFunctionsListBox.SelectedItem = null;
        }
    }
}
