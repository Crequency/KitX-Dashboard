﻿using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Fleck;
using KitX.Dashboard.Configuration;
using KitX.Dashboard.Services;
using KitX.Shared.CSharp.Plugin;

namespace KitX.Dashboard.Network.PluginsNetwork;

public partial class PluginsServer : ConfigFetcher
{
    private static PluginsServer? _pluginsServer;

    public static PluginsServer Instance => _pluginsServer ??= new();

    private WebSocketServer? _server;

    private readonly List<PluginConnector> _connectors = [];

    public List<PluginConnector> PluginConnectors => _connectors;

    public PluginsServer()
    {
        InitializeServer();
    }

    private void InitializeServer()
    {
        var port = AppConfig.Web.UserSpecifiedPluginsServerPort ?? 0;

        port = port is >= 0 and <= 65535 ? port : 0;

        _server ??= new WebSocketServer($"ws://0.0.0.0:{port}");
    }

    public PluginsServer Run()
    {
        InitializeServer();

        _server!.Start(socket =>
        {
            if (RegexToVerifyConnectionId().IsMatch(socket.ConnectionInfo.Path.Trim('/')) == false)
            {
                socket.Send("Invalid connection id.");

                socket.Close();

                return;
            }

            var connector = new PluginConnector(socket).Run();

            _connectors.Add(connector);
        });

        EventService.Invoke(nameof(EventService.PluginsServerPortChanged), [_server!.Port]);

        return this;
    }

    public PluginConnector? FindConnector(PluginInfo info)
    {
        var query = PluginConnectors.Where(
            x => x.PluginInfo is not null && x.PluginInfo.Equals(info)
        );

        if (query.Any())
            return query.First();
        else
            return null;
    }

    public PluginConnector? FindConnector(string connectionId) => PluginConnectors.FirstOrDefault(
        x => x.ConnectionId?.Equals(connectionId) ?? false
    );

    public async Task<PluginsServer> Close()
    {
        await Task.Run(() =>
        {
            Task.WaitAll(
                _connectors.Select<PluginConnector, Task>(
                    c => c.CloseAsync()
                ).ToArray()
            );

            _connectors.Clear();

            _server?.Dispose();

            _server = null;
        });

        return this;
    }

    [GeneratedRegex(@"^[{]?[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}[}]?$")]
    private static partial Regex RegexToVerifyConnectionId();
}
