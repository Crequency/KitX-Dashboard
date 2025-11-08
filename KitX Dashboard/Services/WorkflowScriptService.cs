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
            // 直接使用 PluginsServer.Instance，无需反射
            PluginsServer pluginsServer = PluginsServer.Instance;

            // 创建并设置插件管理器
            Parser.SetPluginManager(new Kscript.CSharp.Parser.Core.RealPluginManager(
                pluginsServer,
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
    /// 执行工作流脚本代码
    /// </summary>
    /// <param name="code">要执行的代码</param>
    /// <param name="includeTimestamp">是否包含时间戳</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>执行结果</returns>
    public static async Task<string?> ExecuteCodesAsync(
        string code,
        bool includeTimestamp = true,
        CancellationToken cancellationToken = default
    )
    {
        var sw = new Stopwatch();
        var begin = DateTime.Now;
        sw.Start();

        try
        {
            // 生成包含插件API的程序集
            Assembly? pluginApiAssembly = null;
            if (AvailablePlugins.Any())
            {
                try
                {
                    // 确保插件管理器已初始化
                    if (!Parser.IsInitialized)
                        InitializePluginManager();

                    // TODO: 在这里添加插件动态加载逻辑
                    // 检查 AvailablePlugins 中的所有插件是否已在 Parser._pluginManager 中加载
                    // 如果有未加载的插件，需要先加载它们

                    pluginApiAssembly = Parser.Generate(AvailablePlugins, "KitXWorkflowPlugins");
                }
                catch (Exception ex)
                {
                    // 如果插件API生成失败，记录错误但继续执行脚本
                    var error = $"Failed to generate plugin API: {ex.Message}";
                    Log.Error(error);
                }
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
