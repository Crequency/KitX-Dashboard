using CommandLine;

namespace KitX.Dashboard.Options;

public class StartupOptions : BaseOptions
{
    [Option("import-plugin", HelpText = "Import a plugin via CLI")]
    public string? PluginPath { get; set; }

    [Option("disable-single-process-check", Default = false, HelpText = "Allow user run multiple `KitX.Dashboard.exe`")]
    public bool DisableSingleProcessCheck { get; set; }

    [Option("disable-config-hot-reload", Default = false, HelpText = "Do not reload config when it is edited outside")]
    public bool DisableConfigHotReload { get; set; }

    [Option("disable-network-system-on-startup", Default = false, HelpText = "Do not power network system at startup")]
    public bool DisableNetworkSystemOnStartup { get; set; }
}
