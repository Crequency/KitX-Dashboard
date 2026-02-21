using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using KitX.Dashboard.ViewModels;
using AvaloniaEdit;
using Avalonia.Interactivity;
using System.IO;
using TextMateSharp.Grammars;
using AvaloniaEdit.TextMate;
using Avalonia.Styling;
using Avalonia.Platform.Storage;
using Avalonia.Input;
using KitX.Core.Contract.Event;
using KitX.Core.Event;
using KitX.Core.Contract.Workflow;
using KitX.Dashboard;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using Material.Icons;

namespace KitX.Dashboard.Views;

public partial class WorkflowScriptEditorWindow : Window, IView
{
    private readonly WorkflowScriptEditorWindowViewModel viewModel;

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
        InitializeTheme();

        // 订阅主题变更事件以实现主题适配
        var eventService = App.GetService<IEventService>();
        eventService.Subscribe(EventNames.ThemeConfigChanged, (s, e) => OnThemeChanged());

        // 订阅窗口键盘事件
        KeyDown += WorkflowScriptEditorWindow_KeyDown;

        // 获取代码编辑器
        var codeEditor = this.FindControl<TextEditor>("CodeEditor");

        if (codeEditor != null)
        {
            // 确保 Document 已初始化
            if (codeEditor.Document == null)
            {
                codeEditor.Document = new AvaloniaEdit.Document.TextDocument();
            }
            viewModel.CodeDocument = codeEditor.Document;
            codeEditor.Text = @"// Workflow Script Example
// You can use KitX plugins directly in your workflow scripts

using System;
using System.Threading.Tasks;

public class WorkflowScript
{
    public static async Task Main()
    {
        // Example usage of plugins
        // var result = await SomePlugin.SomeMethod();
        // Console.WriteLine($""Result: {result}"");

        Console.WriteLine(""Hello from workflow script!"");
    }
}";
        }

        // 获取按钮
        var runButton = this.FindControl<Button>("RunButton");
        var stopButton = this.FindControl<Button>("StopButton");
        var saveButton = this.FindControl<Button>("SaveButton");
        var exportButton = this.FindControl<Button>("ExportButton");
        var statusText = this.FindControl<TextBlock>("StatusText");

        // 运行控制按钮事件
        if (runButton != null)
            runButton.Click += RunButton_Click;

        if (stopButton != null)
            stopButton.Click += StopButton_Click;

        if (saveButton != null)
            saveButton.Click += SaveButton_Click;

        if (exportButton != null)
            exportButton.Click += ExportButton_Click;

        // 模板按钮事件
        var templateTimer = this.FindControl<Button>("TemplateTimer");
        var templateCondition = this.FindControl<Button>("TemplateCondition");
        var templatePlugin = this.FindControl<Button>("TemplatePlugin");
        var templateErrorHandling = this.FindControl<Button>("TemplateErrorHandling");

        if (templateTimer != null)
            templateTimer.Click += (s, e) => InsertTemplate("timer");
        if (templateCondition != null)
            templateCondition.Click += (s, e) => InsertTemplate("condition");
        if (templatePlugin != null)
            templatePlugin.Click += (s, e) => InsertTemplate("plugin");
        if (templateErrorHandling != null)
            templateErrorHandling.Click += (s, e) => InsertTemplate("errorhandling");

        // 代码片段按钮事件
        var snippetLog = this.FindControl<Button>("SnippetLog");
        var snippetNotify = this.FindControl<Button>("SnippetNotify");
        var snippetFile = this.FindControl<Button>("SnippetFile");
        var snippetHttp = this.FindControl<Button>("SnippetHttp");
        var snippetAsync = this.FindControl<Button>("SnippetAsync");

        if (snippetLog != null)
            snippetLog.Click += (s, e) => InsertSnippet("log");
        if (snippetNotify != null)
            snippetNotify.Click += (s, e) => InsertSnippet("notify");
        if (snippetFile != null)
            snippetFile.Click += (s, e) => InsertSnippet("file");
        if (snippetHttp != null)
            snippetHttp.Click += (s, e) => InsertSnippet("http");
        if (snippetAsync != null)
            snippetAsync.Click += (s, e) => InsertSnippet("async");

        // 插件选择按钮
        var selectPluginButton = this.FindControl<Button>("SelectPluginButton");
        if (selectPluginButton != null)
            selectPluginButton.Click += SelectPluginButton_Click;

        // 标签页切换
        var tabCode = this.FindControl<RadioButton>("TabCode");
        var tabPluginApi = this.FindControl<RadioButton>("TabPluginApi");
        var tabVariables = this.FindControl<RadioButton>("TabVariables");

        if (tabCode != null)
            tabCode.Checked += (s, e) => SwitchRightPanel("code");
        if (tabPluginApi != null)
            tabPluginApi.Checked += (s, e) => SwitchRightPanel("pluginapi");
        if (tabVariables != null)
            tabVariables.Checked += (s, e) => SwitchRightPanel("variables");

        // 面板折叠按钮
        var collapseToolbox = this.FindControl<Button>("CollapseToolbox");
        var collapseRightPanel = this.FindControl<Button>("CollapseRightPanel");

        if (collapseToolbox != null)
            collapseToolbox.Click += (s, e) => ToggleToolbox();
        if (collapseRightPanel != null)
            collapseRightPanel.Click += (s, e) => ToggleRightPanel();

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
                    statusText.Text = viewModel.IsExecuting ? "运行中..." : "就绪";
            }
            else if (e.PropertyName == nameof(viewModel.ExecutionResult))
            {
                // 当有执行结果时，输出到控制台
                AppendConsoleOutput(viewModel.ExecutionResult, "info");
            }
        };

        // 初始化调试控制台
        InitializeDebugConsole();
    }

    // TODO (后端连通): 调试功能需要与工作流执行引擎集成
    // 需要完成:
    // 1. 实现断点支持 - 在代码编辑器行号区域显示断点图标
    // 2. 实现变量监视器 - 从 WorkflowExecutionContext 获取当前变量
    // 3. 实现单步调试 - F10(跳过)/F11(进入)
    // 4. 实时日志输出 - 通过 IWorkflowService.ExecuteCodesAsync 的回调获取执行状态

    private System.Collections.ObjectModel.ObservableCollection<string> consoleMessages = new();

    private void InitializeDebugConsole()
    {
        // 设置控制台数据源
        var consoleOutput = this.FindControl<ItemsControl>("ConsoleOutput");
        if (consoleOutput != null)
        {
            consoleOutput.ItemsSource = consoleMessages;
        }

        // 添加欢迎消息
        AppendConsoleOutput("=== KitX 工作流编辑器 ===", "info");
        AppendConsoleOutput("准备就绪", "info");

        // 绑定过滤按钮事件
        var filterAll = this.FindControl<Button>("FilterAll");
        var filterError = this.FindControl<Button>("FilterError");
        var filterWarning = this.FindControl<Button>("FilterWarning");
        var filterInfo = this.FindControl<Button>("FilterInfo");
        var clearConsoleBtn = this.FindControl<Button>("ClearConsoleBtn");
        var toggleDebugConsoleBtn = this.FindControl<Button>("ToggleDebugConsoleBtn");

        if (filterAll != null)
            filterAll.Click += (s, e) => FilterConsole("all");
        if (filterError != null)
            filterError.Click += (s, e) => FilterConsole("error");
        if (filterWarning != null)
            filterWarning.Click += (s, e) => FilterConsole("warning");
        if (filterInfo != null)
            filterInfo.Click += (s, e) => FilterConsole("info");
        if (clearConsoleBtn != null)
            clearConsoleBtn.Click += (s, e) => DoClearConsole();
        if (toggleDebugConsoleBtn != null)
            toggleDebugConsoleBtn.Click += (s, e) => DoToggleDebugConsole();
    }

    private string currentFilter = "all";

    private void AppendConsoleOutput(string message, string type)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var formattedMessage = $"[{timestamp}] {message}";

        // 根据类型添加样式标记
        consoleMessages.Add(formattedMessage);

        // 自动滚动到底部
        var scrollViewer = this.FindControl<ScrollViewer>("ConsoleScrollViewer");
        if (scrollViewer != null)
        {
            scrollViewer.Offset = new Avalonia.Vector(0, scrollViewer.Extent.Height);
        }
    }

    private void FilterConsole(string filter)
    {
        currentFilter = filter;
        // TODO: 实现过滤功能 - 根据类型显示/隐藏日志
        // 需要在 consoleMessages 中存储日志类型信息
    }

    private void DoClearConsole()
    {
        consoleMessages.Clear();
        AppendConsoleOutput("控制台已清除", "info");
    }

    private bool isDebugConsoleExpanded = true;

    private void DoToggleDebugConsole()
    {
        var debugConsoleGrid = this.FindControl<Grid>("DebugConsoleGrid");
        if (debugConsoleGrid != null)
        {
            isDebugConsoleExpanded = !isDebugConsoleExpanded;
            debugConsoleGrid.Height = isDebugConsoleExpanded ? 150 : 30;
        }
    }

    // ===== 快捷键系统 =====
    // 支持的快捷键:
    // - Ctrl+Space: 触发智能提示 (显示可用代码片段和模板)
    // - Ctrl+Shift+T: 弹出模板选择菜单
    // - F5: 运行工作流
    // - Shift+F5: 停止执行
    // - F9: 切换断点
    // - Ctrl+S: 保存
    // - Ctrl+F: 查找
    // - Ctrl+H: 替换
    private void WorkflowScriptEditorWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        // 获取代码编辑器
        var codeEditor = this.FindControl<TextEditor>("CodeEditor");

        // 检查是否在编辑器中有焦点
        bool isEditorFocused = codeEditor?.IsFocused ?? false;

        switch (e.Key)
        {
            // F5 / Shift+F5: 运行/停止工作流
            case Key.F5:
                if (e.KeyModifiers == KeyModifiers.None)
                {
                    RunButton_Click(this, new RoutedEventArgs());
                }
                else if (e.KeyModifiers == KeyModifiers.Shift)
                {
                    StopButton_Click(this, new RoutedEventArgs());
                }
                e.Handled = true;
                break;

            // F9: 切换断点
            case Key.F9:
                if (e.KeyModifiers == KeyModifiers.None)
                {
                    ToggleBreakpoint(codeEditor);
                    e.Handled = true;
                }
                break;

            // Ctrl+Space: 触发智能提示 (当前为显示提示列表)
            case Key.Space:
                if (e.KeyModifiers == KeyModifiers.Control)
                {
                    ShowIntellisense(codeEditor);
                    e.Handled = true;
                }
                break;

            // Ctrl+Shift+T: 插入工作流模板
            case Key.T:
                if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
                {
                    ShowTemplateMenu();
                    e.Handled = true;
                }
                break;

            // Ctrl+S: 保存
            case Key.S:
                if (e.KeyModifiers == KeyModifiers.Control)
                {
                    SaveButton_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
                break;

            // Ctrl+O: 打开文件
            case Key.O:
                if (e.KeyModifiers == KeyModifiers.Control)
                {
                    OpenFile();
                    e.Handled = true;
                }
                break;

            // Ctrl+N: 新建文件
            case Key.N:
                if (e.KeyModifiers == KeyModifiers.Control)
                {
                    NewFile();
                    e.Handled = true;
                }
                break;

            // Ctrl+F: 查找
            case Key.F:
                if (e.KeyModifiers == KeyModifiers.Control)
                {
                    ShowFindPanel(codeEditor);
                    e.Handled = true;
                }
                break;

            // Ctrl+H: 替换
            case Key.H:
                if (e.KeyModifiers == KeyModifiers.Control)
                {
                    ShowReplacePanel(codeEditor);
                    e.Handled = true;
                }
                break;

            // Ctrl+G: 跳转到指定行
            case Key.G:
                if (e.KeyModifiers == KeyModifiers.Control)
                {
                    ShowGoToLine(codeEditor);
                    e.Handled = true;
                }
                break;

            // Ctrl+/: 注释/取消注释
            case Key.Oem2:  // "/" 键
                if (e.KeyModifiers == KeyModifiers.Control)
                {
                    ToggleComment(codeEditor);
                    e.Handled = true;
                }
                break;

            // Tab: 接受代码片段 (当有活动补全时)
            case Key.Tab:
                if (isEditorFocused && hasActiveCompletion)
                {
                    AcceptCompletion(codeEditor);
                    e.Handled = true;
                }
                break;
        }
    }

    private bool hasActiveCompletion = false;

    private void ToggleBreakpoint(TextEditor? codeEditor)
    {
        if (codeEditor == null || codeEditor.Document == null) return;

        var line = codeEditor.Document.GetLineByOffset(codeEditor.CaretOffset);
        var lineNumber = codeEditor.Document.LineCount;  // 使用文档行数作为近似值

        // TODO: 实现断点支持 - 需要与调试引擎集成
        // 当前仅在控制台输出信息
        AppendConsoleOutput($"[断点] 切换断点功能 (当前行: {lineNumber})", "info");
    }

    private void ShowIntellisense(TextEditor? codeEditor)
    {
        if (codeEditor == null) return;

        // TODO: 实现智能提示 - 显示可用的代码片段、模板和插件 API
        // 当前显示提示信息
        hasActiveCompletion = true;
        AppendConsoleOutput("[智能提示] Ctrl+Space 触发智能提示 (功能开发中)", "info");

        // 模拟显示一个简单的补全列表
        var intellisenseTip = this.FindControl<TextBlock>("IntellisenseTip");
        if (intellisenseTip != null)
        {
            intellisenseTip.IsVisible = true;
            intellisenseTip.Text = "可用: Console, File, Plugin, Task (按 Tab 插入)";
        }
    }

    private void ShowTemplateMenu()
    {
        // 弹出模板选择菜单
        AppendConsoleOutput("[模板] 按 Ctrl+Shift+T 弹出模板选择菜单", "info");

        // TODO: 实现模板选择弹出菜单
        // 临时: 使用默认模板
        InsertTemplate("timer");
    }

    private async void OpenFile()
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var openOptions = new FilePickerOpenOptions
        {
            Title = "打开工作流脚本",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("C# 文件")
                {
                    Patterns = new[] { "*.cs" }
                },
                new FilePickerFileType("所有文件")
                {
                    Patterns = new[] { "*" }
                }
            }
        };

        var result = await topLevel.StorageProvider.OpenFilePickerAsync(openOptions);
        if (result.Count > 0)
        {
            var file = result[0];
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync();

            var codeEditor = this.FindControl<TextEditor>("CodeEditor");
            if (codeEditor != null && codeEditor.Document != null)
            {
                codeEditor.Document.Text = content;
                AppendConsoleOutput($"[文件] 已打开: {file.Name}", "info");
            }
        }
    }

    private void NewFile()
    {
        var codeEditor = this.FindControl<TextEditor>("CodeEditor");
        if (codeEditor != null && codeEditor.Document != null)
        {
            codeEditor.Document.Text = @"// New Workflow Script
// Created: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + @"

using System;
using System.Threading.Tasks;

public class WorkflowScript
{
    public static async Task Main()
    {
        // Your workflow code here
    }
}";
            AppendConsoleOutput("[文件] 已新建工作流脚本", "info");
        }
    }

    private void ShowFindPanel(TextEditor? codeEditor)
    {
        if (codeEditor == null) return;

        // 使用 AvaloniaEdit 内置的搜索面板
        var searchPanel = AvaloniaEdit.Search.SearchPanel.Install(codeEditor);
        searchPanel.Open();
        AppendConsoleOutput("[查找] Ctrl+F 打开查找面板", "info");
    }

    private void ShowReplacePanel(TextEditor? codeEditor)
    {
        if (codeEditor == null) return;

        // 使用 AvaloniaEdit 内置的搜索面板（支持替换）
        var searchPanel = AvaloniaEdit.Search.SearchPanel.Install(codeEditor);
        searchPanel.Open();
        AppendConsoleOutput("[替换] Ctrl+H 打开替换面板", "info");
    }

    private void ShowGoToLine(TextEditor? codeEditor)
    {
        if (codeEditor == null || codeEditor.Document == null) return;

        // TODO: 实现跳转到指定行对话框
        // 临时: 跳转到第一行
        codeEditor.CaretOffset = codeEditor.Document.GetLineByNumber(1).Offset;
        var currentLine = codeEditor.Document.GetLineByOffset(codeEditor.CaretOffset);
        AppendConsoleOutput($"[跳转] 已跳转到第 1 行", "info");
    }

    private void ToggleComment(TextEditor? codeEditor)
    {
        if (codeEditor == null || codeEditor.Document == null) return;

        var currentLine = codeEditor.Document.GetLineByOffset(codeEditor.CaretOffset);
        var lineNumber = 1;  // 获取当前行号
        for (int i = 1; i <= codeEditor.Document.LineCount; i++)
        {
            var line = codeEditor.Document.GetLineByNumber(i);
            if (line.Offset <= codeEditor.CaretOffset && codeEditor.CaretOffset <= line.EndOffset)
            {
                lineNumber = i;
                break;
            }
        }
        var lineText = codeEditor.Document.GetText(currentLine.Offset, currentLine.Length);

        // 简单实现: 如果行首不是 // 则添加，否则移除
        if (lineText.TrimStart().StartsWith("//"))
        {
            // 移除注释
            var commentIndex = lineText.IndexOf("//");
            codeEditor.Document.Remove(currentLine.Offset + commentIndex, 2);
        }
        else
        {
            // 添加注释
            codeEditor.Document.Insert(currentLine.Offset, "//");
        }

        AppendConsoleOutput($"[编辑] 切换注释于第 {lineNumber} 行", "info");
    }

    private void AcceptCompletion(TextEditor? codeEditor)
    {
        if (codeEditor == null) return;

        // TODO: 实现接受补全
        hasActiveCompletion = false;

        var intellisenseTip = this.FindControl<TextBlock>("IntellisenseTip");
        if (intellisenseTip != null)
        {
            intellisenseTip.IsVisible = false;
        }
    }

    private void InitializeEditor()
    {
        var textEditor = this.FindControl<TextEditor>("CodeEditor");

        if (textEditor != null)
        {
            SetEditor(textEditor, ".cs");
        }
    }

    // ===== 主题适配系统 =====
    // 确保编辑器窗口内的所有元素响应主题变化
    private void InitializeTheme()
    {
        // 初始化时设置编辑器主题
        ApplyThemeToEditor();

        // 订阅窗口主题变化事件
        this.ActualThemeVariantChanged += (s, e) => OnThemeChanged();
    }

    private void OnThemeChanged()
    {
        // 主题变更时的处理
        AppendConsoleOutput($"[主题] 主题已切换至: {ActualThemeVariant}", "info");

        // 重新应用编辑器语法高亮主题
        InitializeEditor();

        // 刷新界面元素（如果需要）
        RefreshUIForTheme();
    }

    private void ApplyThemeToEditor()
    {
        var codeEditor = this.FindControl<TextEditor>("CodeEditor");
        if (codeEditor != null)
        {
            SetEditor(codeEditor, ".cs");
        }
    }

    private void RefreshUIForTheme()
    {
        // TODO: 根据主题更新需要特殊处理的 UI 元素
        // 例如:
        // - 编辑器背景色
        // - 状态栏颜色
        // - 调试控制台颜色等
    }

    private void SetEditor(TextEditor? textEditor, string ext)
    {
        if (textEditor is null)
            return;

        var registryOptions = new RegistryOptions(ActualThemeVariant == ThemeVariant.Light ? ThemeName.LightPlus : ThemeName.DarkPlus);
        var textMateInstallation = textEditor.InstallTextMate(registryOptions);
        textMateInstallation.SetGrammar(registryOptions.GetScopeByLanguageId(registryOptions.GetLanguageByExtension(ext).Id));
    }

    private void RunButton_Click(object? sender, RoutedEventArgs e)
    {
        if (viewModel.CodeDocument != null)
        {
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

        var saveOptions = new FilePickerSaveOptions
        {
            Title = "保存工作流脚本",
            DefaultExtension = "cs",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("C# 文件")
                {
                    Patterns = new[] { "*.cs" }
                },
                new FilePickerFileType("所有文件")
                {
                    Patterns = new[] { "*" }
                }
            }
        };

        var result = await topLevel.StorageProvider.SaveFilePickerAsync(saveOptions);
        if (result != null && viewModel.CodeDocument != null)
        {
            await using var stream = await result.OpenWriteAsync();
            using var writer = new StreamWriter(stream);
            await writer.WriteAsync(viewModel.CodeDocument.Text);
        }
    }

    private async void ExportButton_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var saveOptions = new FilePickerSaveOptions
        {
            Title = "导出工作流",
            DefaultExtension = "kworkflow",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("KitX 工作流文件")
                {
                    Patterns = new[] { "*.kworkflow" }
                }
            }
        };

        var result = await topLevel.StorageProvider.SaveFilePickerAsync(saveOptions);
        if (result != null && viewModel.CodeDocument != null)
        {
            // TODO: 导出为工作流格式（包含元数据）
            await using var stream = await result.OpenWriteAsync();
            using var writer = new StreamWriter(stream);
            await writer.WriteAsync(viewModel.CodeDocument.Text);
        }
    }

    private void InsertTemplate(string templateType)
    {
        var codeEditor = this.FindControl<TextEditor>("CodeEditor");
        if (codeEditor == null) return;

        string template = templateType switch
        {
            "timer" => @"// 定时任务模板
public class TimerWorkflow
{
    private static System.Timers.Timer? _timer;

    public static void Start(TimeSpan interval)
    {
        _timer = new System.Timers.Timer(interval.TotalMilliseconds);
        _timer.Elapsed += async (s, e) =>
        {
            // 在此处编写定时执行的任务
            Console.WriteLine($""[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 定时任务执行"");
        };
        _timer.Start();
    }

    public static void Stop()
    {
        _timer?.Stop();
        _timer?.Dispose();
    }
}",
            "condition" => @"// 条件触发模板
public class ConditionWorkflow
{
    public static async Task<bool> CheckCondition(Func<bool> condition)
    {
        try
        {
            return condition();
        }
        catch (Exception ex)
        {
            Console.WriteLine($""条件检查错误: {ex.Message}"");
            return false;
        }
    }

    public static async Task ExecuteBranch(bool condition, Action trueBranch, Action falseBranch)
    {
        if (condition)
        {
            trueBranch();
        }
        else
        {
            falseBranch();
        }
    }
}",
            "plugin" => @"// 插件调用模板
public class PluginWorkflow
{
    public static async Task<object?> CallPlugin(string pluginName, string methodName, params object[] args)
    {
        // TODO: 通过插件服务调用指定插件的方法
        // var plugin = PluginService.GetPlugin(pluginName);
        // return await plugin.InvokeAsync(methodName, args);
        Console.WriteLine($""调用插件: {pluginName}.{methodName}"");
        return null;
    }
}",
            "errorhandling" => @"// 错误处理模板
public class ErrorHandlingWorkflow
{
    public static async Task ExecuteWithErrorHandling(Func<Task> action, Action<Exception>? onError = null)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Console.WriteLine($""错误: {ex.Message}"");
            onError?.Invoke(ex);
        }
        finally
        {
            Console.WriteLine(""执行完成"");
        }
    }
}",
            _ => ""
        };

        var offset = codeEditor.CaretOffset;
        codeEditor.Document.Insert(offset, template);
    }

    private void InsertSnippet(string snippetType)
    {
        var codeEditor = this.FindControl<TextEditor>("CodeEditor");
        if (codeEditor == null) return;

        string snippet = snippetType switch
        {
            "log" => "Console.WriteLine($\"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] \");",
            "notify" => "// 发送通知\n// NotificationService.SendNotification(\"标题\", \"内容\");",
            "file" => "// 文件操作\n// var content = await File.ReadAllTextAsync(\"path/to/file.txt\");\n// await File.WriteAllTextAsync(\"path/to/file.txt\", content);",
            "http" => "using var client = new System.Net.Http.HttpClient();\nvar response = await client.GetAsync(\"https://api.example.com/data\");",
            "async" => "await Task.Run(() =>\n{\n    // 异步执行的任务\n});",
            _ => ""
        };

        var offset = codeEditor.CaretOffset;
        codeEditor.Document.Insert(offset, snippet);
    }

    private async void SelectPluginButton_Click(object? sender, RoutedEventArgs e)
    {
        // 打开插件选择对话框
        var dialog = new PluginSelectorDialog();
        var result = await dialog.ShowDialog<List<PluginSelectableItem>>(this);

        if (result != null && result.Count > 0)
        {
            // 更新选中的插件列表
            UpdateSelectedPluginsList(result);

            // 刷新插件 API 面板
            RefreshPluginApiPanel(result);
        }
    }

    private void UpdateSelectedPluginsList(List<PluginSelectableItem> selectedPlugins)
    {
        var selectedPluginsList = this.FindControl<ItemsControl>("SelectedPluginsList");
        if (selectedPluginsList != null)
        {
            selectedPluginsList.ItemsSource = selectedPlugins;

            var noPluginTip = this.FindControl<TextBlock>("NoPluginTip");
            if (noPluginTip != null)
            {
                noPluginTip.IsVisible = selectedPlugins.Count == 0;
            }
        }
    }

    private void RefreshPluginApiPanel(List<PluginSelectableItem> selectedPlugins)
    {
        // 生成模拟的 API 列表
        var apiList = new List<PluginApiItem>();

        foreach (var plugin in selectedPlugins)
        {
            // 模拟每个插件的方法
            var methods = GetMockPluginMethods(plugin.Name);
            foreach (var method in methods)
            {
                apiList.Add(new PluginApiItem
                {
                    PluginName = plugin.Name,
                    Name = method.Name,
                    Signature = method.Signature,
                    Description = method.Description
                });
            }
        }

        var pluginApiList = this.FindControl<ItemsControl>("PluginApiList");
        if (pluginApiList != null)
        {
            pluginApiList.ItemsSource = apiList;

            var noPluginApiTip = this.FindControl<TextBlock>("NoPluginApiTip");
            if (noPluginApiTip != null)
            {
                noPluginApiTip.IsVisible = apiList.Count == 0;
            }
        }
    }

    // TODO (后端连通): 从插件清单 (manifest.json) 解析真实的 API 方法
    // 需要完成:
    // 1. 读取插件的 manifest.json 文件
    // 2. 解析 Commands/Actions 列表
    // 3. 提取方法名、参数、返回值类型
    // 4. 生成 C# 方法签名
    // 5. 实现 Kscript.CSharp.Parser 将插件 API 转换为可调用代码
    private List<(string Name, string Signature, string Description)> GetMockPluginMethods(string pluginName)
    {
        // 模拟插件方法
        return pluginName switch
        {
            "FileHelper" => new List<(string, string, string)>
            {
                ("ReadFile", "Task<string> ReadFile(string path)", "读取文件内容"),
                ("WriteFile", "Task WriteFile(string path, string content)", "写入文件内容"),
                ("DeleteFile", "bool DeleteFile(string path)", "删除文件"),
                ("ListFiles", "string[] ListFiles(string directory)", "列出目录文件"),
            },
            "NetworkScanner" => new List<(string, string, string)>
            {
                ("ScanNetwork", "Task<List<Device>> ScanNetwork()", "扫描网络设备"),
                ("Ping", "bool Ping(string ip)", "Ping 指定 IP"),
                ("GetDeviceInfo", "DeviceInfo GetDeviceInfo(string ip)", "获取设备信息"),
            },
            "ImageProcessor" => new List<(string, string, string)>
            {
                ("Resize", "byte[] Resize(byte[] image, int width, int height)", "调整图片尺寸"),
                ("Compress", "byte[] Compress(byte[] image, int quality)", "压缩图片"),
                ("ConvertFormat", "byte[] ConvertFormat(byte[] image, string format)", "转换格式"),
            },
            _ => new List<(string, string, string)>
            {
                ("Execute", "object? Execute(params object[] args)", "执行操作"),
            }
        };
    }

    private void SwitchRightPanel(string panelType)
    {
        var propertyEditor = this.FindControl<ScrollViewer>("PropertyEditorPanel");
        var pluginApiPanel = this.FindControl<ScrollViewer>("PluginApiPanel");
        var variablePanel = this.FindControl<ScrollViewer>("VariableMonitorPanel");
        var rightPanelTitle = this.FindControl<TextBlock>("RightPanelTitle");

        if (propertyEditor == null || pluginApiPanel == null || variablePanel == null) return;

        // 先隐藏所有面板
        propertyEditor.IsVisible = false;
        pluginApiPanel.IsVisible = false;
        variablePanel.IsVisible = false;

        // 根据选择显示对应面板
        switch (panelType)
        {
            case "code":
                propertyEditor.IsVisible = true;
                if (rightPanelTitle != null) rightPanelTitle.Text = "属性";
                break;
            case "pluginapi":
                pluginApiPanel.IsVisible = true;
                if (rightPanelTitle != null) rightPanelTitle.Text = "插件 API";
                break;
            case "variables":
                variablePanel.IsVisible = true;
                if (rightPanelTitle != null) rightPanelTitle.Text = "变量监视器";
                break;
        }
    }

    private void ToggleToolbox()
    {
        // TODO: 实现工具箱折叠/展开
    }

    private void ToggleRightPanel()
    {
        // TODO: 实现右侧面板折叠/展开
    }

    private void PluginApiItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is PluginApiItem apiItem)
        {
            var codeEditor = this.FindControl<TextEditor>("CodeEditor");
            if (codeEditor == null) return;

            // 生成方法调用代码
            var code = $"// {apiItem.Description}\n{apiItem.PluginName}.{apiItem.Name}(";

            // 添加参数占位符
            var paramStart = apiItem.Signature.IndexOf('(');
            var paramEnd = apiItem.Signature.IndexOf(')');
            if (paramStart > 0 && paramEnd > paramStart)
            {
                var parameters = apiItem.Signature.Substring(paramStart + 1, paramEnd - paramStart - 1);
                if (!string.IsNullOrWhiteSpace(parameters))
                {
                    // 简化参数为占位符
                    var paramList = parameters.Split(',')
                        .Select(p => p.Trim().Split(' ').Last())
                        .Select(p => p + "Value")
                        .ToList();
                    code += string.Join(", ", paramList);
                }
            }

            code += ");";

            // 插入到编辑器
            var offset = codeEditor.CaretOffset;
            codeEditor.Document.Insert(offset, code);
        }
    }
}

public class PluginApiItem
{
    public string PluginName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
