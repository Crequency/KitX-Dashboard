using System;
using System.Net.Http;
using System.Reactive;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using Common.BasicHelper.Utils.Extensions;
using Fizzler;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Network.DevicesNetwork;
using KitX.Dashboard.Network.DevicesNetwork.DevicesServerControllers.V1;
using KitX.Dashboard.Services;
using KitX.Dashboard.ViewModels;
using KitX.Dashboard.Views;
using KitX.Shared.CSharp.Device;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using ReactiveUI;
using Serilog;

namespace KitX.Dashboard.Models;

public class DeviceCase : ViewModelBase
{
    public DeviceCase(DeviceInfo deviceInfo)
    {
        this.deviceInfo = deviceInfo;

        InitCommands();

        InitEvents();
    }

    public override void InitCommands()
    {
        AuthorizeAndExchangeDeviceKeyCommand = ReactiveCommand.Create<DeviceInfo>(
            async info =>
            {
                if (ConstantTable.IsExchangingDeviceKey)
                {
                    var box = MessageBoxManager.GetMessageBoxStandard(
                        Translate("Text_Log_Warning"),
                        Translate("Text_Device_Tip_ExchangingDeviceKey"),
                        ButtonEnum.Ok,
                        Icon.Warning
                    );

                    return;
                }

                ConstantTable.IsExchangingDeviceKey = true;

                var keyData = new byte[8];

                var random = new Random();

                for (var i = 0; i < keyData.Length; ++i)
                    keyData[i] = (byte)(random.Next(1, 10) + '0');

                var key = keyData.ToASCII();

                if (SecurityManager.Instance.LocalDeviceKey is null)
                {
                    var box = MessageBoxManager.GetMessageBoxStandard(
                        Translate("Text_Log_Error"),
                        Translate("Text_Device_Tip_SecuritySystemFailed"),
                        ButtonEnum.Ok,
                        Icon.Error
                    );

                    await box.ShowWindowAsync();

                    ConstantTable.IsExchangingDeviceKey = false;

                    return;
                }

                var localKey = SecurityManager.Instance.GetPrivateDeviceKey();

                var localKeyJson = JsonSerializer.Serialize(localKey);

                var localKeyEncrypted = SecurityManager.AesEncrypt(localKeyJson, key);

                var sender = DevicesDiscoveryServer.Instance.DefaultDeviceInfo;

                var address = $"{sender.Device.IPv4}:{sender.DevicesServerPort}";

                var target = $"{info.Device.IPv4}:{info.DevicesServerPort}";

                var sha1 = SecurityManager.GetSHA1(key);

                var url = $"http://{target}/Api/V1/Device/{nameof(DeviceController.ExchangeKey)}?verifyCodeSHA1={sha1}&address={address}";

                ConstantTable.ExchangeDeviceKeyCode = key;

                var window = new ExchangeDeviceKeyWindow().DisplayVerificationCode(key);

                window.OnCancel(async () =>
                {
                    window.Canceled();

                    ConstantTable.IsExchangingDeviceKey = false;

                    var url = $"http://{target}/Api/V1/Device/{nameof(DeviceController.CancelExchangingKey)}";

                    using var http = new HttpClient();

                    var response = await http.PostAsync(url, null);

                    Log.Information($"In {nameof(DeviceController)}: Requested {url} with responsed {response.StatusCode} - {response}");
                });

                ViewInstances.ShowWindow(window);

                EventService.OnReceiveCancelExchangingDeviceKey += () => Dispatcher.UIThread.Post(() => window.Canceled());

                using var http = new HttpClient();

                var response = await http.PostAsync(
                    url,
                    new StringContent(
                        JsonSerializer.Serialize(localKeyEncrypted),
                        Encoding.UTF8,
                        "application/json"
                    )
                );

                if (response.IsSuccessStatusCode)
                {

                }
                else
                {
                    var box = MessageBoxManager.GetMessageBoxStandard(
                        Translate("Text_Log_Error"),
                        new StringBuilder()
                            .AppendLine($"Requested: {url}")
                            .AppendLine($"Responsed: {response.StatusCode} - {response.RequestMessage}")
                            .ToString()
                        ,
                        ButtonEnum.Ok,
                        Icon.Error
                    );

                    await box.ShowWindowAsync();

                    window.Close();

                    ConstantTable.IsExchangingDeviceKey = false;
                }
            },
            this.WhenAnyValue(x => x.IsAuthorized, y => y == false)
        );

        UnAuthorizeCommand = ReactiveCommand.Create<DeviceInfo>(
            async info =>
            {
                if (info.IsCurrentDevice())
                {
                    var box = MessageBoxManager.GetMessageBoxStandard(
                        Translate("Text_Log_Error"),
                        Translate("Text_Device_Tip_DeleteYourSelfError"),
                        ButtonEnum.Ok,
                        Icon.Forbidden
                    );

                    await box.ShowWindowAsync();

                    return;
                }

                SecurityManager.Instance.RemoveDeviceKey(info);
            },
            this.WhenAnyValue(x => x.IsAuthorized)
        );
    }

    public override void InitEvents()
    {

    }

    private DeviceInfo deviceInfo;

    public DeviceInfo DeviceInfo
    {
        get => deviceInfo;
        set
        {
            this.RaiseAndSetIfChanged(ref deviceInfo, value);

            Update();

            if (IsAuthorized && (IsConnected == false) && (IsCurrentDevice == false))
                Connect();
        }
    }

    private void Update()
    {
        this.RaisePropertyChanged(nameof(IsAuthorized));
        this.RaisePropertyChanged(nameof(IsConnected));
        this.RaisePropertyChanged(nameof(IsCurrentDevice));
        this.RaisePropertyChanged(nameof(IsMainDevice));
    }

    private void Connect()
    {
        TasksManager.RunTask(async () =>
        {
            using var http = new HttpClient();

            var targetKey = SecurityManager.SearchDeviceKey(DeviceInfo.Device);

            if (targetKey is null) return;

            var localKey = SecurityManager.Instance.GetPrivateDeviceKey();

            var localKeyJson = JsonSerializer.Serialize(localKey);

            var localKeyEncrypted = SecurityManager.Instance.EncryptString(localKeyJson);

            var address = $"{DeviceInfo.Device.IPv4}:{DeviceInfo.DevicesServerPort}";

            var url = $"http://{address}/Api/V1/Device/{nameof(DeviceController.Connect)}";

            var response = await http.PostAsync(
                url,
                new StringContent(
                    JsonSerializer.Serialize(localKeyEncrypted),
                    Encoding.UTF8,
                    "application/json"
                )
            );

            if (response.IsSuccessStatusCode)
            {
                Log.Information($"Connected to {DeviceInfo.Device.DeviceName} with response {response}");

                var body = response.Content.ToString();

                if (body is null) return;

                ConnectionToken = SecurityManager.RsaDecryptString(targetKey, body);

                Update();
            }
            else
            {
                Log.Warning(
                    new StringBuilder()
                        .AppendLine($"Requested: {url}")
                        .AppendLine($"Responsed: {response.StatusCode} - {response.ReasonPhrase}")
                        .AppendLine(response.RequestMessage?.ToString())
                        .ToString()
                );
            }

        }, $"Connecting {DeviceInfo.Device.DeviceName}");
    }

    public bool IsAuthorized => SecurityManager.IsDeviceAuthorized(DeviceInfo.Device);

    public bool IsConnected => IsCurrentDevice || DevicesServer.Instance.IsDeviceSignedIn(DeviceInfo.Device) || ConnectionToken is not null;

    public bool IsCurrentDevice => DeviceInfo.IsCurrentDevice();

    public bool IsMainDevice => DeviceInfo.IsMainDevice;

    private string? connectionToken;

    public string? ConnectionToken
    {
        get => connectionToken;
        set => this.RaiseAndSetIfChanged(ref connectionToken, value);
    }

    internal ReactiveCommand<DeviceInfo, Unit>? AuthorizeAndExchangeDeviceKeyCommand { get; set; }

    internal ReactiveCommand<DeviceInfo, Unit>? UnAuthorizeCommand { get; set; }
}
