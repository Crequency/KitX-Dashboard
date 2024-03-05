using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Common.BasicHelper.Utils.Extensions;
using DynamicData;
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

        LocalDeviceKey = SecurityConfig.DeviceKeys.FirstOrDefault(x => x.Device.IsSameDevice(device));

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

    public SecurityManager RemoveDeviceKey(DeviceInfo deviceInfo)
    {
        SecurityConfig.DeviceKeys.RemoveMany(
            SecurityConfig.DeviceKeys.Where(
                x => x.Device.IsSameDevice(deviceInfo.Device)
            )
        );

        SecurityConfig.Save(SecurityConfig.ConfigFileLocation!);

        return this;
    }

    public static bool IsDeviceAuthorized(DeviceLocator device) => SecurityConfig.DeviceKeys.Any(x => x.Device.IsSameDevice(device));

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

    private static byte[] ExpandKey(string key, int length)
    {
        var expandedKey = key.Length <= length ? key : key[..length];

        var expandIndex = 0;

        while (expandedKey.Length < length)
        {
            if (expandIndex == key.Length) expandIndex = 0;

            expandedKey += key[expandIndex];

            expandIndex++;
        }

        return expandedKey.FromASCII();
    }

    public static string AesEncrypt(string source, string key)
    {
        var data = source.FromUTF8();

        var expandedKey = ExpandKey(key, 16);

        var keyData = expandedKey;
        var iv = expandedKey;

        using var aes = Aes.Create();

        aes.Key = keyData;
        aes.IV = iv;

        var result = aes.EncryptCbc(data, iv, PaddingMode.ISO10126);

        return Convert.ToBase64String(result);
    }

    public static string AesDecrypt(string source, string key, bool isSourceInBase64 = true)
    {
        var data = isSourceInBase64 ? Convert.FromBase64String(source) : source.FromUTF8();

        var expandedKey = ExpandKey(key, 16);

        var keyData = expandedKey;
        var iv = expandedKey;

        using var aes = Aes.Create();

        aes.Key = keyData;
        aes.IV = iv;

        var result = aes.DecryptCbc(data, iv, PaddingMode.ISO10126);

        return result.ToUTF8();
    }
}
