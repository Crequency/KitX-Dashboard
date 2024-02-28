using KitX.Shared.CSharp.Device;
using Microsoft.AspNetCore.Mvc;

namespace KitX.Dashboard.Network.DevicesNetwork.DevicesServerControllers;

[ApiController]
[Route("Api/[controller]")]
[ApiExplorerSettings(GroupName = "V1")]
public class DeviceController : ControllerBase
{
    [ApiExplorerSettings(GroupName = "V1")]
    [HttpGet("", Name = nameof(GetDeviceInfo))]
    public DeviceInfo GetDeviceInfo()
    {
        return DevicesDiscoveryServer.Instance.DefaultDeviceInfo;
    }

    public void ExchangeKey()
    {

    }
}
