using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Common.BasicHelper.Utils.Extensions;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Network.PluginsNetwork;
using KitX.Shared.CSharp.Device;
using KitX.Shared.CSharp.Security;
using KitX.Shared.CSharp.WebCommand;
using Microsoft.AspNetCore.Mvc;

namespace KitX.Dashboard.Network.DevicesNetwork.DevicesServerControllers.V1;

[ApiController]
[Route("Api/V1/[controller]")]
[ApiExplorerSettings(GroupName = "V1")]
public class PluginController : ControllerBase
{
    [ApiExplorerSettings(GroupName = "V1")]
    [HttpPost("Invoke", Name = nameof(Invoke))]
    public IActionResult Invoke([FromQuery] string token, [FromBody] string requestJsonBase64)
    {
        if (DevicesServer.Instance.IsDeviceTokenExist(token))
        {
            var requestJson = Convert.FromBase64String(requestJsonBase64).ToUTF8();

            var request = JsonSerializer.Deserialize<Request>(requestJson);

            if (request is null) return BadRequest($"Wrong format of {nameof(requestJson)}");

            var noTarget = request.Target is null;

            var isMe = request.Target?.IsSameDevice(DevicesDiscoveryServer.Instance.DefaultDeviceInfo.Device) ?? false;

            var isNotMe = !isMe;

            if (noTarget || isNotMe) return BadRequest(noTarget ? "Provide target field please." : "Please send to actual target.");

            var content = request.GetContent(toDecrypt =>
            {
                if (request.EncryptionInfo.IsEncrypted)
                {
                    switch (request.EncryptionInfo.EncryptionMethod)
                    {
                        case Shared.CSharp.WebCommand.Infos.EncryptionMethods.Custom:
                            // ToDo: Add custom encryption method support
                            throw new NotImplementedException();

                        case Shared.CSharp.WebCommand.Infos.EncryptionMethods.RSA:

                            var device = DevicesServer.Instance.SearchDeviceByToken(token);

                            ArgumentNullException.ThrowIfNull(device, nameof(device));

                            var key = SecurityManager.SearchDeviceKey(device);

                            ArgumentNullException.ThrowIfNull(key, nameof(key));

                            var toDecryptContent = JsonSerializer.Deserialize<EncryptedContent>(toDecrypt);

                            ArgumentNullException.ThrowIfNull(toDecryptContent, nameof(toDecryptContent));

                            return SecurityManager.RsaDecryptContent(key, toDecryptContent);

                        case Shared.CSharp.WebCommand.Infos.EncryptionMethods.AES:
                            // ToDo: Add AES encryption method support
                            throw new NotImplementedException();
                    }

                    throw new InvalidOperationException("Invalid encryption method.");
                }
                else return toDecrypt;
            });

            request.Match(content, matchCommand: command =>
            {
                var kwc = JsonSerializer.Deserialize<Command>(command);

                var connector = PluginsServer.Instance.FindConnector(kwc.PluginConnectionId);

                if (connector is null) return;

                connector.Request(request.Rebuild(request =>
                {
                    request.Content = command;

                    return request;
                }));
            });

            return Ok();
        }
        else
        {
            return BadRequest("You should connect to this device first.");
        }
    }
}

public static class PluginControllerExtensions
{
    public static async Task<HttpResponseMessage> RemoteInvoke(this string targetAddress, string token, Request request)
    {
        var requestJson = JsonSerializer.Serialize(request);

        var requestJsonBase64 = Convert.ToBase64String(requestJson.FromUTF8());

        var toSend = JsonSerializer.Serialize(requestJsonBase64);

        var url = $"http://{targetAddress}/Api/V1/Plugin/Invoke?token={token}";

        using var http = new HttpClient();

        var response = await http.PostAsync(
            url,
            new StringContent(toSend, Encoding.UTF8, "application/json")
        );

        return response;
    }
}
