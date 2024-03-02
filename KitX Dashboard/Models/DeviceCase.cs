using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reactive;
using System.Text;
using System.Text.Json;
using Common.BasicHelper.Utils.Extensions;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Network.DevicesNetwork;
using KitX.Dashboard.ViewModels;
using KitX.Dashboard.Views;
using KitX.Shared.CSharp.Device;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using ReactiveUI;

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

                    return;
                }

                var localKey = new DeviceKey()
                {
                    Device = SecurityManager.Instance.LocalDeviceKey.Device,
                    RsaPrivateKeyD = SecurityManager.Instance.LocalDeviceKey.RsaPrivateKeyD,
                    RsaPrivateKeyModulus = SecurityManager.Instance.LocalDeviceKey.RsaPrivateKeyModulus
                };

                var window = new ExchangeDeviceKeyWindow().DisplayVerificationCode(key);

                window.Show();

                var localKeyJson = JsonSerializer.Serialize(localKey);

                var localKeyEncrypted = SecurityManager.AesEncrypt(localKeyJson, key);

                var port = DevicesDiscoveryServer.Instance.DefaultDeviceInfo.DevicesServerPort;

                var sha1 = SecurityManager.GetSHA1(key);

                var url = $"http://{info.Device.IPv4}:{info.DevicesServerPort}/Api/Device/ExchangeKey?verifyCodeSHA1={sha1}&port={port}";

                ConstantTable.ExchangeDeviceKeyCode = key;

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
        }
    }

    private void Update()
    {
        this.RaisePropertyChanged(nameof(IsAuthorized));
        this.RaisePropertyChanged(nameof(IsCurrentDevice));
        this.RaisePropertyChanged(nameof(IsMainDevice));
    }

    public bool IsAuthorized => SecurityManager.IsDeviceAuthorized(DeviceInfo.Device);

    public bool IsCurrentDevice => DeviceInfo.IsCurrentDevice();

    public bool IsMainDevice => DeviceInfo.IsMainDevice;

    internal ReactiveCommand<DeviceInfo, Unit>? AuthorizeAndExchangeDeviceKeyCommand { get; set; }

    internal ReactiveCommand<DeviceInfo, Unit>? UnAuthorizeCommand { get; set; }
}
