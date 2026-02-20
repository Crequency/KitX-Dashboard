# KitX Dashboard 开发者文档

## 文档信息

- **项目名称**: KitX Dashboard
- **文档版本**: v1.0
- **创建日期**: 2026-02-19
- **目的**: 为开发者提供 KitX Dashboard 开发指南

---

## 1. 项目概述

### 1.1 项目简介

KitX Dashboard 是 KitX 项目的桌面客户端，采用 Avalonia UI 框架构建。KitX Dashboard 采用了 Core-UI 分离架构，业务逻辑封装在独立的 `KitX.Core` 项目中，通过接口（`KitX.Core.Contract`）与 UI 层进行通信。

### 1.2 技术栈

| 类别 | 技术 | 版本 |
|------|------|------|
| 运行时 | .NET | 10.0+ |
| UI 框架 | Avalonia UI | 11.0+ |
| MVVM 框架 | ReactiveUI | 20.1+ |
| 依赖注入 | Microsoft.Extensions.DependencyInjection | 8.0+ |
| 日志框架 | Serilog | - |

---

## 2. 项目结构

### 2.1 整体架构

```
KitX/
├── KitX Clients/
│   ├── KitX Dashboard/           # UI 层 (Avalonia UI)
│   │   ├── ViewModels/          # 视图模型
│   │   └── Views/               # 视图
│   └── KitX Core/               # 业务逻辑层
│       ├── Configuration/        # 配置管理
│       ├── Device/              # 设备管理
│       ├── Plugin/              # 插件管理
│       ├── Security/            # 安全管理
│       ├── Activity/            # 活动记录
│       ├── Statistics/          # 统计服务
│       ├── Workflow/            # 工作流
│       ├── Event/               # 事件系统
│       ├── Task/                # 任务调度
│       ├── FileWatcher/         # 文件监控
│       ├── Hotkey/              # 全局热键
│       ├── Announcement/         # 公告系统
│       └── DI/                  # 依赖注入
├── KitX Standard/
│   └── KitX Core Contracts/     # 接口定义层
└── KitX.sln
```

### 2.2 项目引用关系

```
KitX Dashboard (UI 层 - 入口点程序)
    ├── KitX.Core (业务逻辑实现)
    ├── KitX.Core.Contract (接口定义)
    ├── KitX.Shared.CSharp (共享数据模型)
    └── KitX.Contract.CSharp (插件契约)

KitX.Core (业务逻辑项目)
    ├── KitX.Core.Contract (接口定义)
    ├── KitX.Shared.CSharp (共享数据模型)
    └── KitX.Contract.CSharp (插件契约)

KitX.Core.Contract (接口定义项目)
    └── KitX.Shared.CSharp (共享数据模型)
```

---

## 3. 开发环境搭建

### 3.1 环境要求

| 项目 | 要求 |
|------|------|
| 操作系统 | Windows 10/11, macOS, Linux |
| .NET SDK | 10.0+ |
| IDE | Visual Studio 2022, Rider, VS Code |
| Git | 2.0+ |

### 3.2 克隆项目

```bash
# 克隆主仓库
git clone git@github.com:Crequency/KitX.git

cd KitX

# 初始化子模块
git submodule update --init --recursive

# 设置引用
cheese reference --setup
```

### 3.3 编译项目

```bash
# 进入 Dashboard 目录
cd "KitX Clients/KitX Dashboard/KitX Dashboard"

# 编译
dotnet build

# 运行
dotnet run
```

---

## 4. 核心概念

### 4.1 依赖注入

KitX Dashboard 使用 Microsoft.Extensions.DependencyInjection 进行依赖注入。所有核心服务都通过接口暴露，ViewModel 通过构造函数注入接口。

```csharp
// 在 ViewModel 中使用依赖注入
public class MainWindowViewModel : ViewModelBase
{
    private readonly IConfigService _configService;
    private readonly IPluginService _pluginService;
    private readonly IDeviceService _deviceService;

    public MainWindowViewModel(
        IConfigService configService,
        IPluginService pluginService,
        IDeviceService deviceService)
    {
        _configService = configService;
        _pluginService = pluginService;
        _deviceService = deviceService;
    }
}
```

### 4.2 服务注册

在 `KitX.Core.DI.CoreServiceCollectionExtensions` 中注册核心服务：

```csharp
public static IServiceCollection AddCoreServices(this IServiceCollection services)
{
    services.AddSingleton<IConfigService, ConfigManager>();
    services.AddSingleton<ISecurityService, SecurityManager>();
    services.AddSingleton<IPluginService, PluginsManager>();
    // ... 其他服务
    return services;
}
```

### 4.3 事件驱动

Core 层通过事件向 UI 层推送状态变化：

```csharp
// 订阅设备发现事件
_deviceService.DeviceDiscovered += OnDeviceDiscovered;

private void OnDeviceDiscovered(object? sender, DeviceDiscoveredEventArgs e)
{
    // 更新 UI
}
```

### 4.4 UI 状态管理

Dashboard 项目使用 `UIStateService` 管理 UI 相关状态。这是一个静态服务，**不属于 Core 层**：

```csharp
// 访问 UI 状态
var devices = UIStateService.DeviceCases;
var plugins = UIStateService.PluginInfos;
var workflows = UIStateService.WorkflowCases;

// 设置窗口引用
UIStateService.MainWindow = this;

// 显示窗口
UIStateService.ShowWindow(new MyWindow());
```

**注意**: `UIStateService` 包含 Avalonia UI 特定类型，不适合放在 Core 项目中。

---

## 5. 开发指南

### 5.1 添加新服务

1. 在 `KitX.Core.Contract` 中定义接口
2. 在 `KitX.Core` 中实现接口
3. 在 `CoreServiceCollectionExtensions` 中注册服务
4. 在 ViewModel 中注入接口

### 5.2 添加新页面

1. 在 `KitX Dashboard/Views/Pages/` 创建 AXAML 视图
2. 在 `KitX Dashboard/ViewModels/` 创建对应的 ViewModel
3. 在 `App.axaml` 中注册路由

### 5.3 添加新功能

1. 在 Core 层实现业务逻辑
2. 通过事件或接口暴露功能
3. 在 ViewModel 中调用接口
4. 在 View 中展示结果

---

## 6. 测试

### 6.1 单元测试

```bash
# 运行所有测试
dotnet test
```

### 6.2 集成测试

请参阅 `KitX-Dashboard-集成测试指导手册.md`

---

## 7. 调试

### 7.1 日志调试

日志文件位置：
- Windows: `%LOCALAPPDATA%\KitX\logs\`
- macOS: `~/Library/Logs/KitX/`
- Linux: `~/.local/share/KitX/logs/`

### 7.2 断点调试

在 Visual Studio 或 Rider 中设置断点，然后启动调试。

---

## 8. 贡献代码

### 8.1 代码规范

- 遵循 C# 编码规范
- 所有公共接口和类必须有 XML 注释
- 使用 ReactiveUI 进行响应式编程

### 8.2 提交规范

请使用 conventional commit 格式：
- `feat:` 新功能
- `fix:` 修复 bug
- `docs:` 文档更新
- `refactor:` 代码重构

---

## 9. 常见问题

### 9.1 编译错误

**问题**: 子模块未正确初始化

**解决**:
```bash
git submodule update --init --recursive
```

### 9.2 运行时错误

**问题**: 服务未注册

**解决**: 检查 `CoreServiceCollectionExtensions` 中的服务注册

---

## 10. 参考资料

- [Avalonia UI 文档](https://docs.avaloniaui.net/)
- [ReactiveUI 文档](https://reactiveui.net/)
- [Microsoft.Extensions.DependencyInjection 文档](https://docs.microsoft.com/dotnet/core/extensions/dependency-injection)

---

**文档结束**

*本文档为开发者提供 KitX Dashboard 开发指南。*
