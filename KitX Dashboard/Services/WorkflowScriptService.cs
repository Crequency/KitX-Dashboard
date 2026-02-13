using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Csharpell.Core;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Kscript.CSharp.Parser;
using KitX.Shared.CSharp.Plugin;
using Serilog;
using KitX.Dashboard.Network.PluginsNetwork;
using KitX.Dashboard.Models;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Configuration;
using KitX.Dashboard.Network.DevicesNetwork;
using Common.BasicHelper.Utils.Extensions;

namespace KitX.Dashboard.Services;

public static class WorkflowScriptService
{
    private static CSharpScriptEngine Engine => new();

    // 存储当前可用的插件列表，用于生成API
    private static List<PluginInfo> AvailablePlugins { get; set; } = new();

    /// <summary>
    /// 初始化插件管理器
    /// </summary>
    public static void InitializePluginManager()
    {
        if (Parser.IsInitialized) return; // 已经初始化过了

        try
        {
            // 创建PluginServiceProvider
            var serviceProvider = new PluginServiceProvider(PluginsServer.Instance);

            // 设置到Parser
            Parser.SetPluginManager(new Kscript.CSharp.Parser.Core.RealPluginManager(
                serviceProvider,
                Log.Information,
                Log.Error
            ));

            Log.Information("[WorkflowScriptService] RealPluginManager 已设置");
        }
        catch (Exception ex)
        {
            Log.Error($"[WorkflowScriptService] 设置插件管理器失败: {ex.Message}");
            // 如果设置失败，使用默认的 MockPluginManager
            Parser.SetPluginManager(new Kscript.CSharp.Parser.Core.MockPluginManager());
        }
    }

    /// <summary>
    /// 更新可用插件列表
    /// </summary>
    /// <param name="plugins">插件列表</param>
    public static void UpdateAvailablePlugins(List<PluginInfo> plugins)
    {
        AvailablePlugins = plugins ?? new List<PluginInfo>();
    }

    /// <summary>
    /// 确保工作流所需的插件已加载并运行
    /// </summary>
    /// <param name="requiredPlugins">需要的插件列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否所有插件都已就绪</returns>
    private static async Task<bool> EnsurePluginsReadyAsync(
        List<PluginInfo> requiredPlugins,
        CancellationToken cancellationToken = default)
    {
        var success = true;

        foreach (var requiredPlugin in requiredPlugins)
        {
            // 1. 检查插件是否已安装
            var installedPlugin = PluginsManager.Plugins
                .FirstOrDefault(p => p.PluginInfo.Name == requiredPlugin.Name);

            if (installedPlugin == null)
            {
                Log.Error($"[WorkflowScriptService] 插件 {requiredPlugin.Name} 未安装");
                success = false;
                continue;
            }

            // 2. 检查插件是否正在运行
            var isRunning = KitX.Dashboard.Views.ViewInstances.PluginInfos
                .Any(p => p.Name == requiredPlugin.Name);

            if (!isRunning)
            {
                Log.Information($"[WorkflowScriptService] 启动插件: {requiredPlugin.Name}");

                // 3. 启动插件
                StartPlugin(installedPlugin);

                // 4. 等待插件连接（最多10秒）
                var connected = await WaitForPluginConnectionAsync(
                    requiredPlugin.Name,
                    TimeSpan.FromSeconds(10),
                    cancellationToken
                );

                if (!connected)
                {
                    Log.Error($"[WorkflowScriptService] 插件 {requiredPlugin.Name} 启动超时");
                    success = false;
                }
            }
            else
            {
                Log.Information($"[WorkflowScriptService] 插件 {requiredPlugin.Name} 已运行");
            }
        }

        return success;
    }

    /// <summary>
    /// 启动插件
    /// </summary>
    /// <param name="plugin">插件安装信息</param>
    private static void StartPlugin(PluginInstallation plugin)
    {
        try
        {
            var pd = plugin.PluginInfo;
            var loaderName = plugin.LoaderInfo.LoaderName;
            var loaderVersion = plugin.LoaderInfo.LoaderVersion;

            // 构建插件文件路径
            var pluginPath = $"{plugin.InstallPath}/{pd.RootStartupFileName}";
            var pluginFile = pluginPath.GetFullPath();

            // 构建 WebSocket 连接字符串
            var connectStr = "ws://" +
                $"{DevicesDiscoveryServer.Instance.DefaultDeviceInfo.Device.IPv4}" +
                $":{ConstantTable.PluginsServerPort}/";

            if (plugin.LoaderInfo.SelfLoad)
            {
                // 自加载插件
                Process.Start(pluginFile, $"--connect {connectStr}");
                Log.Information($"[WorkflowScriptService] 启动自加载插件: {pd.Name}");
            }
            else
            {
                // 通过加载器启动
                var loadersInstallPath = ConfigManager.Instance.AppConfig.Loaders.InstallPath;
                var loaderFile = $"{loadersInstallPath}/{loaderName}/{loaderVersion}/{loaderName}";

                if (OperatingSystem.IsWindows())
                    loaderFile += ".exe";

                var arg = $"--load \"{pluginFile}\" --connect {connectStr}";
                Process.Start(loaderFile, arg);

                Log.Information($"[WorkflowScriptService] 通过加载器启动插件: {pd.Name}");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[WorkflowScriptService] 启动插件失败: {plugin.PluginInfo.Name} - {ex.Message}");
        }
    }

    /// <summary>
    /// 等待插件连接
    /// </summary>
    /// <param name="pluginName">插件名称</param>
    /// <param name="timeout">超时时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否连接成功</returns>
    private static async Task<bool> WaitForPluginConnectionAsync(
        string pluginName,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.Now;
        var checkInterval = TimeSpan.FromMilliseconds(500);

        while (DateTime.Now - startTime < timeout)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            // 检查插件是否已连接
            var isConnected = KitX.Dashboard.Views.ViewInstances.PluginInfos
                .Any(p => p.Name == pluginName);

            if (isConnected)
            {
                Log.Information($"[WorkflowScriptService] 插件 {pluginName} 连接成功");
                return true;
            }

            // 等待一段时间再检查
            await Task.Delay(checkInterval, cancellationToken);
        }

        return false;
    }

    /// <summary>
    /// 执行工作流脚本代码
    /// </summary>
    /// <param name="code">要执行的代码</param>
    /// <param name="requiredPlugins">需要的插件列表（可选）</param>
    /// <param name="includeTimestamp">是否包含时间戳</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>执行结果</returns>
    public static async Task<string?> ExecuteCodesAsync(
        string code,
        List<PluginInfo>? requiredPlugins = null,
        bool includeTimestamp = true,
        CancellationToken cancellationToken = default)
    {
        var sw = new Stopwatch();
        var begin = DateTime.Now;
        sw.Start();

        try
        {
            // 如果有需要的插件，先确保它们已加载
            if (requiredPlugins != null && requiredPlugins.Any())
            {
                // 确保插件管理器已初始化
                if (!Parser.IsInitialized)
                    InitializePluginManager();

                // 确保插件已加载并运行
                var pluginsReady = await EnsurePluginsReadyAsync(requiredPlugins, cancellationToken);

                if (!pluginsReady)
                {
                    Log.Warning("[WorkflowScriptService] 部分插件未能成功启动，但继续执行脚本");
                }

                // 生成包含插件API的程序集
                Assembly? pluginApiAssembly = null;
                try
                {
                    pluginApiAssembly = Parser.Generate(requiredPlugins, "KitXWorkflowPlugins");
                    Log.Information($"[WorkflowScriptService] 成功生成插件API，包含 {requiredPlugins.Count} 个插件");
                }
                catch (Exception ex)
                {
                    // 如果插件API生成失败，记录错误但继续执行脚本
                    var error = $"Failed to generate plugin API: {ex.Message}";
                    Log.Error(error);
                }

                // 执行脚本
                var result = (
                    await Engine.ExecuteAsync(
                        code,
                        options =>
                        {
                            options = options
                                .WithReferences(Assembly.GetExecutingAssembly())
                                .WithImports(
                                    "KitX",
                                    "KitX.Dashboard",
                                    "KitX.Shared.CSharp.Plugin",
                                    "System",
                                    "System.Collections.Generic",
                                    "System.Threading.Tasks"
                                )
                                .WithLanguageVersion(LanguageVersion.Preview);

                            // 如果有插件API程序集，添加为引用
                            if (pluginApiAssembly != null)
                            {
                                options = options.WithReferences(pluginApiAssembly);
                            }

                            return options;
                        },
                        addDefaultImports: true,
                        runInReplMode: false,
                        cancellationToken: cancellationToken
                    )
                )?.ToString();

                sw.Stop();

                return includeTimestamp
                    ? new StringBuilder()
                        .AppendLine($"[{begin:yyyy-MM-dd HH:mm:ss}] [I] Workflow script posted.")
                        .AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [I] Script ended, took {sw.ElapsedMilliseconds} ms.")
                        .AppendLine(result)
                        .ToString()
                    : result;
            }
            else
            {
                // 没有插件依赖，直接执行
                return await ExecuteCodesWithoutPluginsAsync(code, includeTimestamp, cancellationToken);
            }
        }
        catch (Exception e)
        {
            sw.Stop();

            return includeTimestamp
                ? new StringBuilder()
                    .AppendLine($"[{begin:yyyy-MM-dd HH:mm:ss}] [I] Workflow script posted.")
                    .AppendLine(
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [E] Exception caught after {sw.ElapsedMilliseconds} ms, Message: {e.Message}"
                    )
                    .AppendLine(e.StackTrace)
                    .ToString()
                : e.StackTrace;
        }
    }

    /// <summary>
    /// 执行无插件依赖的脚本
    /// </summary>
    private static async Task<string?> ExecuteCodesWithoutPluginsAsync(
        string code,
        bool includeTimestamp,
        CancellationToken cancellationToken)
    {
        var sw = new Stopwatch();
        var begin = DateTime.Now;
        sw.Start();

        try
        {
            var result = (
                await Engine.ExecuteAsync(
                    code,
                    options => options
                        .WithReferences(Assembly.GetExecutingAssembly())
                        .WithImports("System", "System.Collections.Generic", "System.Threading.Tasks")
                        .WithLanguageVersion(LanguageVersion.Preview),
                    addDefaultImports: true,
                    runInReplMode: false,
                    cancellationToken: cancellationToken
                )
            )?.ToString();

            sw.Stop();

            return includeTimestamp
                ? new StringBuilder()
                    .AppendLine($"[{begin:yyyy-MM-dd HH:mm:ss}] [I] Workflow script posted.")
                    .AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [I] Script ended, took {sw.ElapsedMilliseconds} ms.")
                    .AppendLine(result)
                    .ToString()
                : result;
        }
        catch (Exception e)
        {
            sw.Stop();

            return includeTimestamp
                ? new StringBuilder()
                    .AppendLine($"[{begin:yyyy-MM-dd HH:mm:ss}] [I] Workflow script posted.")
                    .AppendLine(
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [E] Exception caught after {sw.ElapsedMilliseconds} ms, Message: {e.Message}"
                    )
                    .AppendLine(e.StackTrace)
                    .ToString()
                : e.StackTrace;
        }
    }
}
