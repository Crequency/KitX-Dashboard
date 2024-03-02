using System.Collections.Generic;
using KitX.Shared.CSharp.Device;

namespace KitX.Dashboard.Configuration;

public class SecurityConfig : ConfigBase
{
    public List<DeviceKey> DeviceKeys { get; set; } = [];
}
