using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using Common.BasicHelper.Utils.Extensions;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Services;
using KitX.Dashboard.Views;
using KitX.Shared.CSharp.Device;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace KitX.Dashboard.Network.DevicesNetwork.DevicesServerControllers.V1;

[ApiController]
[Route("Api/V1/[controller]")]
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
    [HttpPost(nameof(ExchangeKey), Name = nameof(ExchangeKey))]
    public IActionResult ExchangeKey([FromQuery] string verifyCodeSHA1, [FromQuery] string address, [FromBody] string deviceKey)
    {
        if (ConstantTable.IsExchangingDeviceKey) return BadRequest("Remote device is exchanging device key.");

        if (SecurityManager.Instance.LocalDeviceKey is null) return BadRequest("Remote device didn't set up device key.");

        ConstantTable.IsExchangingDeviceKey = true;

        Dispatcher.UIThread.Post(() =>
        {
            var window = new ExchangeDeviceKeyWindow();

            EventService.OnReceiveCancelExchangingDeviceKey += () => Dispatcher.UIThread.Post(() => window.Canceled());

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
                            var url = $"http://{address}/Api/V1/Device/{nameof(ExchangeKeyBack)}";

                            if (SecurityManager.Instance.LocalDeviceKey is null)
                            {
                                await window.OnErrorDecodeAsync();

                                ConstantTable.IsExchangingDeviceKey = false;

                                return;
                            }

                            var currentKey = SecurityManager.Instance.GetPrivateDeviceKey();

                            if (currentKey is null)
                            {
                                await window.OnErrorDecodeAsync();

                                ConstantTable.IsExchangingDeviceKey = false;

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

                                ConstantTable.IsExchangingDeviceKey = false;
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
                }).OnCancel(async () =>
                {
                    window.Canceled();

                    ConstantTable.IsExchangingDeviceKey = false;

                    var url = $"http://{address}/Api/V1/Device/{nameof(CancelExchangingKey)}";

                    using var http = new HttpClient();

                    var response = await http.PostAsync(url, null);

                    Log.Information($"In {nameof(DeviceController)}: Requested {url} with responsed {response.StatusCode} - {response}");
                }),
                ViewInstances.MainWindow,
                false
            );
        });

        return Ok();
    }

    [ApiExplorerSettings(GroupName = "V1")]
    [HttpPost(nameof(ExchangeKeyBack), Name = nameof(ExchangeKeyBack))]
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

    [ApiExplorerSettings(GroupName = "V1")]
    [HttpPost(nameof(CancelExchangingKey), Name = nameof(CancelExchangingKey))]
    public IActionResult CancelExchangingKey()
    {
        if (ConstantTable.IsExchangingDeviceKey == false) return BadRequest("Remote device isn't exchanging device key.");

        EventService.Invoke(nameof(EventService.OnReceiveCancelExchangingDeviceKey));

        ConstantTable.IsExchangingDeviceKey = false;

        return Ok();
    }

    [ApiExplorerSettings(GroupName = "V1")]
    [HttpPost(nameof(Connect), Name = nameof(Connect))]
    public IActionResult Connect([FromQuery] string deviceBase64, [FromBody] string deviceNameEncrypted)
    {
        var device = JsonSerializer.Deserialize<DeviceLocator>(Convert.FromBase64String(deviceBase64).ToUTF8());

        if (device is null) return BadRequest($"You provided wrong {nameof(deviceBase64)} which is not a type of `{nameof(DeviceLocator)}`.");

        var key = SecurityManager.SearchDeviceKey(device);

        if (key is null) return BadRequest("You are not authorized by remote device.");

        var deviceNameDecrypted = SecurityManager.RsaDecryptString(key, deviceNameEncrypted);

        if (deviceNameDecrypted is null) return StatusCode(500, "Remote crashed when decrypting device name.");

        if (device.DeviceName.Equals(deviceNameDecrypted) == false)
            return BadRequest("You provided incorrect encrypted device name.");

        var token = DevicesServer.Instance.SignInDevice(device);

        return Ok(SecurityManager.Instance.EncryptString(token));
    }
}
