using System;
using System.Threading.Tasks;
using Avalonia.Controls;
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

namespace KitX.Dashboard.Views;

public partial class WorkflowScriptEditorWindow : Window, IView
{
    private readonly WorkflowScriptEditorWindowViewModel viewModel;
    private bool _isEditingHelperFunction = false;
    private bool _isUpdatingOutput = false;

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

#PubVarBlock
var targetNum = 7;  // 目标数字，用户无法在UI看到
var currentLoop = 1;

#MainBlock
Print(""开始执行工作流"");
NextBlock = Loop(HelperFuncCompare(""BLE"", currentLoop, loopMax), ""LoopBody"", ""EndLogic"");  // 主循环

#Block LoopBody
Print(currentLoop);
currentLoop = HelperFuncAdd(currentLoop, 1);  // 函数调用，记得在默认的HelperFunction初始化程序中添加这个HelperFuncAdd
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
NextBlock = LoopBodyEnd(""MainBlock"");  // 返回到 MainBlock 的 Loop

#Block GreaterThanLogic
Print(""猜大了"");
NextBlock = LoopBodyEnd(""MainBlock"");  // 返回到 MainBlock 的 Loop

#Block SuccessLogic
Print(""猜对啦！""); // 这个Block没有Branch/Loop/LoopBodyEnd，自然进入下一行

#Block EndLogic
Print(""示例工作流结束"");";
            }
            else
            {
                // 旧KCS模式模板
                codeEditor.Text = @"// Workflow Script Example (Main Program)
// Use WorkflowOutput.WriteLine() to print debug messages
// This is a restricted C# script - no if/else, for/while, try/catch allowed
// Only the following syntax is supported:
// - Variable and constant declarations (e.g. int x = 5;)
// - Variable assignments (e.g. x = 10;)
// - Method calls (e.g. MyHelperFunction();)

// Define constants (will appear in the Variable Constants panel)
const string greeting = ""Hello from workflow script!"";
const int count = 3;

// Call helper functions
// var result = MyHelperFunction();

// Use the constants
WorkflowOutput.WriteLine(greeting);
WorkflowOutput.WriteLine($""Count: {count}"");

// You can call plugin functions directly like this:
// TestPlugin.WPF.Core.HelloKitX();

// Output after the plugin call will still execute
WorkflowOutput.WriteLine(""Plugin call completed!"");";
            }

            // 订阅代码变化事件
            codeEditor.TextChanged += (s, e) =>
            {
                if (codeEditor.Document != null)
                {
                    if (_isEditingHelperFunction)
                    {
                        // 正在编辑辅助函数，同步代码到 SelectedHelperFunction.Code
                        if (viewModel.SelectedHelperFunction != null)
                        {
                            viewModel.SelectedHelperFunction.Code = codeEditor.Document.Text;
                        }
                    }
                    else
                    {
                        // 正在编辑主程序
                        viewModel.MainProgramCode = codeEditor.Document.Text;
                        // 解析常量
                        viewModel.ParseConstantsFromCode(codeEditor.Document.Text);
                        // 更新UI
                        if (constantsItemsControl != null)
                        {
                            constantsItemsControl.ItemsSource = viewModel.VariableConstants;
                        }
                    }
                }
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
