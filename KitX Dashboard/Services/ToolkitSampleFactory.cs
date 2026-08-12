using KitX.ToolKit.Models;

namespace KitX.Dashboard.Services;

/// <summary>
/// Builds a valid in-memory <see cref="Toolkit"/> for the ToolKit management page / Bench
/// window scaffold. This is scaffolding only ("not officially released"): there is no
/// ToolKit storage service yet, so the page seeds itself from a built-in sample so the
/// Bench orchestration pipeline (Activate → view config → manual fire) is reachable for
/// development.
/// </summary>
public static class ToolkitSampleFactory
{
    /// <summary>
    /// Creates a small but valid ToolKit: workflows A → B → C (a strict DAG via
    /// WorkflowCompletion edges) plus a Manual trigger that starts the chain.
    /// </summary>
    public static Toolkit Create(string name = "AI Assistant Toolkit")
    {
        return new Toolkit
        {
            Meta = new ToolkitMeta
            {
                Name = name,
                Version = "1.0.0",
                Author = "KitX",
                Description = "示例 ToolKit：演示 Bench 编排（Manual 触发 + WorkflowCompletion 边）",
                MinKitXVersion = "3.25.4.0",
                Tags = ["sample", "bench"],
            },
            Workflows =
            [
                new ToolkitWorkflow { Id = "wf-a", Name = "初始化", File = "workflows/a.kcs" },
                new ToolkitWorkflow { Id = "wf-b", Name = "处理", File = "workflows/b.kcs" },
                new ToolkitWorkflow { Id = "wf-c", Name = "收束", File = "workflows/c.kcs" },
            ],
            Plugins =
            [
                new PluginRequirement { Name = "KitX.DataStore", Version = ">=0.1.0", Source = "builtin" },
            ],
            Triggers =
            [
                new Trigger
                {
                    Id = "manual",
                    Type = TriggerType.Manual,
                    Bindings = [new TriggerBinding { Workflow = "wf-a" }],
                },
                new Trigger
                {
                    Id = "edge-ab",
                    Type = TriggerType.WorkflowCompletion,
                    Config = new TriggerConfig { From = "wf-a" },
                    Bindings = [new TriggerBinding { Workflow = "wf-b" }],
                },
                new Trigger
                {
                    Id = "edge-bc",
                    Type = TriggerType.WorkflowCompletion,
                    Config = new TriggerConfig { From = "wf-b" },
                    Bindings = [new TriggerBinding { Workflow = "wf-c" }],
                },
            ],
            UiPanel = new UiPanel
            {
                Layout = "stack",
                Controls =
                [
                    new UiControl { Type = "Text", Id = "lbl_title", Text = "AI 翻译" },
                    new UiControl { Type = "Input", Id = "input" },
                    new UiControl { Type = "Button", Id = "btn_submit", Text = "翻译" },
                ],
            },
        };
    }
}
