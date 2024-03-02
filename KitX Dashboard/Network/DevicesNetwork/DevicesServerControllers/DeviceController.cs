using KitX.Shared.CSharp.Device;
using Microsoft.AspNetCore.Mvc;

namespace KitX.Dashboard.Network.DevicesNetwork.DevicesServerControllers;

[ApiController]
[Route("Api/V1/[controller]")]
[ApiExplorerSettings(GroupName = "V1")]
public class DeviceController : ControllerBase
{
    [ApiExplorerSettings(GroupName = "V1")]
    [HttpGet("", Name = nameof(GetDeviceInfo))]
    public IActionResult GetDeviceInfo([FromQuery] string token)
    {
        if (DevicesOrganizer.Instance.IsDeviceTokenExist(token))
            return Ok(DevicesDiscoveryServer.Instance.DefaultDeviceInfo);
        else
            return BadRequest("You should connect to this device first.");
    }

    [ApiExplorerSettings(GroupName = "V1")]
    [HttpPost("/ExchangeKey", Name = nameof(ExchangeKey))]
    public IActionResult ExchangeKey
    (
        [FromQuery] DeviceLocator locator,
        [FromQuery] string verifyCodeSHA1,
        [FromBody] string deviceKey
    )
    {
        DevicesOrganizer.Instance.RequireAcceptDeviceKey(
            locator,
            verifyCodeSHA1,
            deviceKey
        );

        return Ok();
    }
}
