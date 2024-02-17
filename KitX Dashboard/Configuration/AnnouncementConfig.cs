using System.Collections.Generic;

namespace KitX.Dashboard.Configuration;

public class AnnouncementConfig : ConfigBase
{
    public List<string> Accepted { get; set; } = [];
}
