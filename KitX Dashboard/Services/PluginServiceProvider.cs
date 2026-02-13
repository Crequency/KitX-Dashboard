using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KitX.Dashboard.Network.PluginsNetwork;
using KitX.Dashboard.Views;
using KitX.Shared.CSharp.Plugin;
using KitX.Shared.CSharp.WebCommand;
using Kscript.CSharp.Parser.Core;
using Serilog;

namespace KitX.Dashboard.Services;

/// <summary>
/// 插件服务提供者实现 - 连接Dashboard和Parser
/// </summary>
public class PluginServiceProvider : IPluginServiceProvider
{
    private readonly PluginsServer _pluginsServer;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="pluginsServer">插件服务器实例</param>
    public PluginServiceProvider(PluginsServer pluginsServer)
    {
        _pluginsServer = pluginsServer ?? throw new ArgumentNullException(nameof(pluginsServer));
    }

    /// <summary>
    /// 获取所有运行中的插件信息
    /// </summary>
    public IEnumerable<PluginInfo> GetRunningPlugins()
    {
        return ViewInstances.PluginInfos;
    }

    /// <summary>
    /// 根据插件名称查找插件信息
    /// </summary>
    public PluginInfo? FindPlugin(string pluginName)
    {
        try
        {
            return ViewInstances.PluginInfos
                .FirstOrDefault(p => p.Name == pluginName);
        }
        catch (Exception ex)
        {
            Log.Error($"[PluginServiceProvider] 查找插件失败: {pluginName} - {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 查找插件连接器
    /// </summary>
    public object? FindConnector(PluginInfo pluginInfo)
    {
        try
        {
            return _pluginsServer.FindConnector(pluginInfo);
        }
        catch (Exception ex)
        {
            Log.Error($"[PluginServiceProvider] 查找连接器失败: {pluginInfo.Name} - {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 向插件连接器发送请求
    /// </summary>
    public async Task SendRequestAsync(object connector, object request)
    {
        try
        {
            if (connector is PluginConnector pluginConnector && request is Request req)
            {
                // PluginConnector.Request 是 async void 方法，不能直接 await
                // 我们需要使用 Task.Run 来包装它
                await Task.Run(() => pluginConnector.Request(req));
            }
            else
            {
                Log.Warning($"[PluginServiceProvider] SendRequestAsync 参数类型不匹配");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[PluginServiceProvider] 发送请求失败 - {ex.Message}");
        }
    }

    /// <summary>
    /// 订阅插件响应事件
    /// </summary>
    public void SubscribeToResponses(Action<string, string> responseHandler)
    {
        try
        {
            // 订阅PluginConnector的响应事件
            PluginConnector.OnPluginResponse += (requestId, response) =>
            {
                try
                {
                    responseHandler(requestId, response);
                }
                catch (Exception ex)
                {
                    Log.Error($"[PluginServiceProvider] 响应处理器异常: {ex.Message}");
                }
            };

            Log.Information("[PluginServiceProvider] 已订阅插件响应事件");
        }
        catch (Exception ex)
        {
            Log.Error($"[PluginServiceProvider] 订阅响应事件失败: {ex.Message}");
        }
    }
}
