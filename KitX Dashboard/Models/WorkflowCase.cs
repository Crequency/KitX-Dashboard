using System;

namespace KitX.Dashboard.Models
{
    public class WorkflowCase
    {
        public required string Name { get; set; } // 工作流名称
        public required string Description { get; set; } // 简介信息
        public required string IconPath { get; set; } // 图标路径
        public bool IsRunning { get; set; } // 运行状态
    }
}
