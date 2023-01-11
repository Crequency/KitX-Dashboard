
var tip_copyright = () =>
{
    Console.WriteLine(
        $"""
        KitX Repair Tool (C) Crequency
        Environment: {Environment.Version}
        OS Version: {Environment.OSVersion}
        """);
};

var log_exception = (Exception e) =>
{
    Console.WriteLine(e.Message);
    Console.WriteLine(e.StackTrace);
};

T? ask<T>(string tip = "Input: ", Func<string?, T>? parse = null)
{
    Console.Write(tip);
    string? input = Console.ReadLine();
    if (input is null) return default(T);
    if (parse is not null) return parse(input);
    else throw new Exception(input);
};

var menu = () =>
{
    Console.WriteLine(
        """
        1. (root) Linux wayland repair (add `LC_ALL=C` to environment variables)
        """);
    return ask("Your select: ", x => int.TryParse(x, out int y) ? y : -1);
};

tip_copyright();
switch (menu())
{
    case 1:
        if (!OperatingSystem.IsLinux())
        {
            Console.WriteLine("Only on linux.");
            break;
        }
        try
        {
            File.AppendAllLines("/etc/environment", new string[] { "LC_ALL=C" });
        }
        catch (Exception e)
        {
            log_exception(e);
        }
        break;
    default:
        break;
}

