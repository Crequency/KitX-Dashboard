using System.Collections.ObjectModel;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Metadata;
using KitX.Dashboard.Models;
using KitX.Dashboard.Services;
using KitX.Dashboard.Views;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels.Pages
{
    internal class WorkflowPageViewModel : ViewModelBase
    {
        public WorkflowPageViewModel()
        {
            InitCommands();
            InitEvents();

            testInit();
        }

        private void testInit()
        {
            WorkflowCases.Add(
                new WorkflowCase
                {
                    Name = "Test",
                    Description = "Test",
                    IconPath = "Test",
                    IsRunning = false,
                }
            );

            WorkflowCases.Add(
                new WorkflowCase
                {
                    Name = "Test",
                    Description = "Test",
                    IconPath = "Test",
                    IsRunning = true,
                }
            );
        }

        public sealed override void InitCommands()
        {
            RunWorkflowCommand = ReactiveCommand.Create(static () =>
            {
                // 运行工作流的逻辑
                // 临时调试用，弹出消息框
                var messageBoxStandardWindow = MessageBoxManager.GetMessageBoxStandard("FU", "CK", icon: Icon.Error).ShowWindowAsync();
                return Task.CompletedTask;
            });

            StopWorkflowCommand = ReactiveCommand.Create(static () =>
            {
                // 停止工作流的逻辑
                // 临时调试用，弹出消息框
                var messageBoxStandardWindow = MessageBoxManager.GetMessageBoxStandard("C", "XK", icon: Icon.Error).ShowWindowAsync();
                return Task.CompletedTask;
            });
            
            // 添加打开工作流脚本编辑器的命令
            OpenWorkflowEditorCommand = ReactiveCommand.Create(() =>
            {
                var editorWindow = new WorkflowScriptEditorWindow();
                editorWindow.Show();
                return Task.CompletedTask;
            });
        }

        public sealed override void InitEvents()
        {
            WorkflowCases.CollectionChanged += (_, _) =>
            {
                NoWorkflow_TipHeight = WorkflowCases.Count == 0 ? 300 : 0;
                WorkflowCount = WorkflowCases.Count;
            };

            EventService.LanguageChanged += () =>
            {
                this.RaisePropertyChanged(nameof(WorkflowCountTip));
            };
        }

        internal string? SearchingText { get; set; }

        internal int workflowCount = 0;

        internal double noWorkflow_TipHeight = 0;

        internal int WorkflowCount
        {
            get => workflowCount;
            set => this.RaiseAndSetIfChanged(ref workflowCount, value);
        }

        internal double NoWorkflow_TipHeight
        {
            get => noWorkflow_TipHeight;
            set => this.RaiseAndSetIfChanged(ref noWorkflow_TipHeight, value);
        }

        [DependsOn(nameof(WorkflowCount))]
        internal string WorkflowCountTip =>
            TranslateTextWithSuffix("Workflow", "Count")?.Replace("$count", WorkflowCount.ToString()) ?? "Language key not found";

        internal static ObservableCollection<WorkflowCase> WorkflowCases => ViewInstances.WorkflowCases;

        internal ReactiveCommand<Unit, Task>? RunWorkflowCommand { get; set; }

        internal ReactiveCommand<Unit, Task>? StopWorkflowCommand { get; set; }
        
        // 新增命令：打开工作流脚本编辑器
        internal ReactiveCommand<Unit, Task>? OpenWorkflowEditorCommand { get; set; }
    }
}
