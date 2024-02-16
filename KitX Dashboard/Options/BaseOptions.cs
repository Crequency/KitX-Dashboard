using CommandLine;

namespace KitX.Dashboard.Options;

public class BaseOptions
{
    [Option('v', "verbose", HelpText = "View detailed output")]
    public bool Verbose { get; set; }
}
