using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Common.BasicHelper.Utils.Extensions;
using KitX.Dashboard.Configuration;
using KitX.Dashboard.Network.DevicesNetwork;
using KitX.Shared.CSharp.Device;

namespace KitX.Dashboard.Managers;

public class SecurityManager : ManagerBase
{
    private static SecurityManager? _instance;

    public static SecurityManager Instance => _instance ??= new();

    private DeviceKey? localDeviceKey;

    public DeviceKey? LocalDeviceKey { get => localDeviceKey; set => localDeviceKey = value; }

    private RSA? RsaInstance;

    public SecurityManager()
    {
        Initialize();
    }

    private void Initialize()
    {
        var local = DevicesDiscoveryServer.Instance.DefaultDeviceInfo;

        var device = local.Device ?? throw new ArgumentNullException(
            nameof(local.Device),
            "It seems that you didn't run Devices Discovery System."
        );

        LocalDeviceKey = SecurityConfig.DeviceKeys.FirstOrDefault(x => x.Device.Equals(device));

        if (LocalDeviceKey is not null) return;

        AddLocalDevice(device);
    }

    private void AddLocalDevice(DeviceLocator device)
    {
        var rsa = RSA.Create();

        rsa.KeySize = 2048;

        var privateKey = rsa.ExportParameters(true);
        var publicKey = rsa.ExportParameters(false);

        if (privateKey.Modulus is null || privateKey.D is null || publicKey.Modulus is null || publicKey.Exponent is null)
            throw new InvalidOperationException("Couldn't generate RSA keys.");

        RsaInstance = rsa;

        AddDeviceKey(new()
        {
            Device = device,
            RsaPrivateKeyModulus = Convert.ToBase64String(privateKey.Modulus),
            RsaPrivateKeyD = Convert.ToBase64String(privateKey.D),
            RsaPublicKeyModulus = Convert.ToBase64String(publicKey.Modulus),
            RsaPublicKeyExponent = Convert.ToBase64String(publicKey.Exponent),
        });
    }

    public SecurityManager AddDeviceKey(DeviceKey deviceKey)
    {
        SecurityConfig.DeviceKeys.Add(deviceKey);

        SecurityConfig.Save(SecurityConfig.ConfigFileLocation!);

        return this;
    }

    public string? EncryptString(string data)
    {
        if (RsaInstance is null) return null;

        var dataBytes = data.FromUTF8();

        return Convert.ToBase64String(
            RsaInstance.Encrypt(dataBytes, RSAEncryptionPadding.OaepSHA256)
        );
    }

    public string? DecryptString(string encryptedData)
    {
        if (RsaInstance is null) return null;

        var encryptedDataBytes = Convert.FromBase64String(encryptedData);

        return RsaInstance.Decrypt(encryptedDataBytes, RSAEncryptionPadding.OaepSHA256).ToUTF8();
    }

    public static string GetSHA1(string data)
    {
        var hash = SHA1.HashData(data.FromUTF8());

        var sb = new StringBuilder();

        foreach (var item in hash) sb.Append(item.ToString("x2"));

        return sb.ToString();
    }
}
