using Microsoft.AspNetCore.Mvc;

namespace KitX.Dashboard.Network.DevicesNetwork.DevicesServerControllers;

[ApiController]
[Route("Api/[controller]")]
[ApiExplorerSettings(GroupName = "V1")]
public class DeviceController : ControllerBase
{
    [ApiExplorerSettings(GroupName = "V1")]
    [HttpGet("{name}", Name = nameof(GreetingTest))]
    public string GreetingTest(string name)
    {
        return $"Hello, {name} !";
    }
}
