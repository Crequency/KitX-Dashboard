using Common.BasicHelper.Utils.Extensions;
using Fleck;
using KitX.Dashboard.Names;
using KitX.Dashboard.Views;
using KitX.Shared.Plugin;
using KitX.Shared.WebCommand;
using KitX.Shared.WebCommand.Details;
using KitX.Shared.WebCommand.Infos;
using Serilog;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace KitX.Dashboard.Network.PluginsNetwork;

public class PluginConnector
{
    private readonly IWebSocketConnection? _connection;

    private readonly IWebSocketConnectionInfo? _connectionInfo;

    private string? _path;

    private bool _initialized = false;

    private PluginInfo? _pluginInfo;

    private ServerStatus connectorStatus = ServerStatus.Pending;

    private readonly JsonSerializerOptions serializerOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true,
        PropertyNameCaseInsensitive = true,
    };

    public delegate void PluginStatusUpdatedHandler();

    public event PluginStatusUpdatedHandler PluginStatusUpdated = new(() => { });

    public PluginConnector()
    {

    }

    public PluginConnector(IWebSocketConnection socket)
    {
        _connection = socket;
        _connectionInfo = socket.ConnectionInfo;
    }

    public string? Path
    {
        get => _path;
        set
        {
            _path = value;

            PluginStatusUpdated.Invoke();
        }
    }

    public string? ConnectionId => Path;

    public PluginInfo? PluginInfo
    {
        get => _pluginInfo;
        set
        {
            _pluginInfo = value;

            PluginStatusUpdated.Invoke();
        }
    }

    public bool PluginInfoAvailable => PluginInfo is null;

    public ServerStatus ConnectorStatus
    {
        get => connectorStatus;
        set
        {
            connectorStatus = value;

            PluginStatusUpdated.Invoke();
        }
    }

    public PluginConnector Initialize()
    {
        _initialized = true;

        Path = _connectionInfo!.Path.Trim('/');

        return this;
    }

    public PluginConnector Run()
    {
        if (_initialized == false)
            Initialize();

        var location = $"{nameof(PluginConnector)}.{nameof(Run)}";

        _connection!.OnOpen = () =>
        {
        };

        _connection.OnClose = () =>
        {
            try
            {
                if (PluginInfo is not null)
                    ViewInstances.PluginInfos.Remove(PluginInfo.Value);
            }
            catch (Exception e)
            {
                Log.Warning(e, $"In {location}: {e.Message}");
            }

            PluginsServer.Instance.PluginConnectors.Remove(this);
        };

        _connection.OnMessage = message =>
        {
            var kwc = JsonSerializer.Deserialize<Request>(message, serializerOptions);

            var command = JsonSerializer.Deserialize<Command>(kwc.Content, serializerOptions);

            switch (command.Request)
            {
                case CommandRequestInfo.RegisterPlugin:

                    var body = command.Body.ToUTF8(count: command.BodyLength);

                    PluginInfo = JsonSerializer.Deserialize<PluginInfo>(body, serializerOptions);

                    ArgumentNullException.ThrowIfNull(PluginInfo, nameof(PluginInfo));

                    ArgumentNullException.ThrowIfNull(PluginInfo.Value.Tags, nameof(PluginInfo.Value.Tags));

                    PluginInfo.Value.Tags.Add(nameof(ConnectionId), ConnectionId ?? string.Empty);
                    PluginInfo.Value.Tags.Add(
                        nameof(PluginTagsNames.JoinTime),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss(FF)")
                    );

                    ViewInstances.PluginInfos.Add(PluginInfo.Value);

                    Log.Information($"In {location}: New plugin registered with {body.Replace("\r", "").Replace("\n", "")}");

                    break;
                case CommandRequestInfo.RequestWorkingDetail:
                    SendWorkingDetail();
                    break;
                case CommandRequestInfo.ReportStatus:
                    break;
                case CommandRequestInfo.RequestCommand:
                    break;
            }

            ;
        };

        _connection.OnError = ex =>
        {
            try
            {
                if (PluginInfo is not null)
                    ViewInstances.PluginInfos.Remove(PluginInfo.Value);
            }
            catch (Exception e)
            {
                Log.Warning(e, $"In {location}: {e.Message}");
            }

            PluginsServer.Instance.PluginConnectors.Remove(this);

            Log.Error(ex, $"In {location}: {ex.Message}");
        };

        return this;
    }

    private void SendMessage<T>(T content) => _connection!.Send(
        JsonSerializer.Serialize(content, serializerOptions)
    );

    private void SendWorkingDetail()
    {
        if (_path.IsNullOrWhiteSpace())
            SendMessage(new PluginWorkingDetail()
            {
                PluginDataDirectory = null,
                PluginSaveDirectory = null,
            });
        else
        {

        }
    }

    internal async void Request(Request request)
    {
        await _connection!.Send(JsonSerializer.Serialize(request, serializerOptions));
    }

    public async Task<PluginConnector> CloseAsync()
    {
        await Task.Run(() =>
        {
            _connection!.Close();
        });

        return this;
    }
}
