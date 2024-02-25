namespace KitX.Dashboard.Models.Network;

public struct WebManagerOperationInfo
{
    public bool RunPluginsServer = true;

    public bool RunDevicesServer = true;

    public bool RunDevicesDiscoveryServer = true;

    public bool RunAll
    {
        readonly get => RunPluginsServer && RunDevicesServer && RunDevicesDiscoveryServer;
        set
        {
            RunPluginsServer = value;
            RunDevicesServer = value;
            RunDevicesDiscoveryServer = value;
        }
    }

    public bool ClosePluginsServer = true;

    public bool CloseDevicesServer = true;

    public bool CloseDevicesDiscoveryServer = true;

    public bool CloseAll
    {
        readonly get => ClosePluginsServer && CloseDevicesServer && CloseDevicesDiscoveryServer;
        set
        {
            ClosePluginsServer = value;
            CloseDevicesServer = value;
            CloseDevicesDiscoveryServer = value;
        }
    }

    public WebManagerOperationInfo()
    {

    }
}
