using System.Text.Json;
using KitX.Dashboard.Managers;
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
    public IActionResult GetDeviceInfo([FromQuery] string token)
    {
        if (DevicesServer.Instance.IsDeviceTokenExist(token))
            return Ok(DevicesDiscoveryServer.Instance.DefaultDeviceInfo);
        else
            return BadRequest("You should connect to this device first.");
    }

    [ApiExplorerSettings(GroupName = "V1")]
    [HttpPost("/ExchangeKey", Name = nameof(ExchangeKey))]
    public IActionResult ExchangeKey([FromQuery] string verifyCodeSHA1, [FromQuery] int port, [FromBody] string deviceKey)
    {
        DevicesServer.Instance.RequireAcceptDeviceKey(verifyCodeSHA1, port, deviceKey);

        return Ok();
    }

    [ApiExplorerSettings(GroupName = "V1")]
    [HttpPost("/ExchangeKeyBack", Name = nameof(ExchangeKeyBack))]
    public IActionResult ExchangeKeyBack([FromQuery] string deviceKey)
    {
        if (ConstantTable.ExchangeDeviceKeyCode is null) return BadRequest();

        var deviceKeyDecrypted = SecurityManager.AesDecrypt(deviceKey, ConstantTable.ExchangeDeviceKeyCode);

        var deviceKeyInstance = JsonSerializer.Deserialize<DeviceKey>(deviceKeyDecrypted);

        if (deviceKeyInstance is null) return BadRequest();

        SecurityManager.Instance.AddDeviceKey(deviceKeyInstance);

        return Ok();
    }
}
