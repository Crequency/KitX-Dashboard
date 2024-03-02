using System.Net.Http;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Services;
using KitX.Dashboard.Views;
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
    [HttpPost("ExchangeKey", Name = nameof(ExchangeKey))]
    public IActionResult ExchangeKey([FromQuery] string verifyCodeSHA1, [FromQuery] int port, [FromBody] string deviceKey)
    {
        if (SecurityManager.Instance.LocalDeviceKey is null) return BadRequest("Remote device didn't set up device key.");

        Dispatcher.UIThread.Post(() =>
        {
            var window = new ExchangeDeviceKeyWindow();

            ViewInstances.ShowWindow(
                window.OnVerificationCodeEntered(async code =>
                {
                    if (verifyCodeSHA1.Equals(SecurityManager.GetSHA1(code)))
                    {
                        var deviceKeyDecrypted = SecurityManager.AesDecrypt(deviceKey, code);

                        var deviceKeyInstance = JsonSerializer.Deserialize<DeviceKey>(deviceKeyDecrypted);

                        if (deviceKeyInstance is null) await window.OnErrorDecodeAsync();
                        else
                        {
                            var sender = deviceKeyInstance.Device;

                            var url = $"http://{sender.IPv4}:{port}/Api/Device/ExchangeKeyBack";

                            if (SecurityManager.Instance.LocalDeviceKey is null)
                            {
                                await window.OnErrorDecodeAsync();

                                return;
                            }

                            var currentKey = new DeviceKey()
                            {
                                Device = SecurityManager.Instance.LocalDeviceKey.Device,
                                RsaPrivateKeyD = SecurityManager.Instance.LocalDeviceKey.RsaPrivateKeyD,
                                RsaPrivateKeyModulus = SecurityManager.Instance.LocalDeviceKey.RsaPrivateKeyModulus,
                            };

                            if (currentKey is null)
                            {
                                await window.OnErrorDecodeAsync();

                                return;
                            }

                            var currentKeyJson = JsonSerializer.Serialize(currentKey);

                            var currentKeyEncrypted = SecurityManager.AesEncrypt(currentKeyJson, code);

                            using var http = new HttpClient();

                            var response = await http.PostAsync(
                                url,
                                new StringContent(
                                    JsonSerializer.Serialize(currentKeyEncrypted),
                                    Encoding.UTF8,
                                    "application/json"
                                )
                            );

                            if (response.IsSuccessStatusCode)
                            {
                                SecurityManager.Instance.AddDeviceKey(deviceKeyInstance);

                                window.Success();
                            }
                            else await window.OnErrorDecodeAsync(
                                new StringBuilder()
                                    .AppendLine($"Requested: {url}")
                                    .AppendLine($"Responsed: {response.StatusCode} - {response.ReasonPhrase}")
                                    .AppendLine(response.RequestMessage?.ToString())
                                    .ToString()
                            );
                        }
                    }
                    else
                    {
                        await window.OnErrorDecodeAsync();
                    }
                }),
                ViewInstances.MainWindow,
                false,
                true
            );
        });

        return Ok();
    }

    [ApiExplorerSettings(GroupName = "V1")]
    [HttpPost("ExchangeKeyBack", Name = nameof(ExchangeKeyBack))]
    public IActionResult ExchangeKeyBack([FromBody] string deviceKey)
    {
        if (ConstantTable.ExchangeDeviceKeyCode is null) return BadRequest();

        var deviceKeyDecrypted = SecurityManager.AesDecrypt(deviceKey, ConstantTable.ExchangeDeviceKeyCode);

        var deviceKeyInstance = JsonSerializer.Deserialize<DeviceKey>(deviceKeyDecrypted);

        if (deviceKeyInstance is null) return BadRequest();

        SecurityManager.Instance.AddDeviceKey(deviceKeyInstance);

        EventService.Invoke(nameof(EventService.OnAcceptingDeviceKey), [ConstantTable.ExchangeDeviceKeyCode]);

        return Ok();
    }
}
