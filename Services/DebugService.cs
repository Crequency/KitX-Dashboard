using Common.BasicHelper.Utils.Extensions;
using KitX_Dashboard.Managers;
using KitX_Dashboard.Network;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace KitX_Dashboard.Services;

internal class DebugService
{
    /// <summary>
    /// 执行命令
    /// </summary>
    /// <param name="cmd">命令</param>
    /// <returns>执行结果</returns>
    internal static string? ExecuteCommand(string cmd)
    {
        var header = cmd.GetCommandHeader();
        if (header is null) return null;
        var args = cmd.Trim()[header.Length..].GetCommandArgs();
        if (args is null) return null;
        var name = header.ToFunctionName();
        if (name is null) return null;
        var foo = typeof(DebugCommands).GetMethod(name);
        if (foo is null) return null;
        if (args.ContainsKey("--times"))
        {
            if (int.TryParse(args["--times"], out int times))
                for (var i = 0; i < times - 1; ++i)
                    _ = foo?.Invoke(null, new Dictionary<string, string>[] { args });
        }
        return foo?.Invoke(null, new Dictionary<string, string>[] { args }) as string;
    }
}

internal class DebugCommands
{
    private static string SaveConfig()
    {
        ConfigManager.SaveConfigs();

        return "AppConfig saved!";
    }

    public static string? Help(Dictionary<string, string> args)
    {
        StringBuilder doc = new();
        doc.AppendLine("You can append `help` to any command to see documents of this command.");
        doc.AppendLine("Or append command name to `help` command to see documents of this command.");
        if (args.Count == 1 && args.Keys.ToArray()[0].Equals("")) return doc.ToString();
        foreach (var item in args.Keys)
            doc.AppendLine(DebugService.ExecuteCommand($"{item} help") ?? $"No help doc for {item}.");
        return doc.ToString();
    }

    public static string? Version(Dictionary<string, string> args)
    {
        if (args.ContainsKey("help"))
            return "Print version of KitX Dashboard.";
        return Assembly.GetEntryAssembly()?.GetName()?.Version?.ToString();
    }

    public static string? Save(Dictionary<string, string> args)
    {
        if (args.ContainsKey("help"))
            return "" +
                "Save datas to disk.\n" +
                "\t--type [config] | saveAction KitX Dashboard config file.\n" +
                "\tconfig | saveAction KitX Dashboard config file.\n";
        if (args.ContainsKey("--type"))
            return args["--type"] switch
            {
                "config" => SaveConfig(),
                _ => "Missing value of `--type`.",
            };
        else if (args.ContainsKey("config"))
        {
            return SaveConfig();
        }
        else return "Missing arguments.";
    }

    public static string? Config(Dictionary<string, string> args)
    {
        if (args.ContainsKey("help"))
        {
            return "" +
                "Edit KitX Dashboard app config.\n" +
                "\t--set developing...\n" +
                "\t--get developing...\n";
        }
        if (args.ContainsKey("--set"))
        {
            return "Missing value of `--set`.";
        }
        else if (args.ContainsKey("--get"))
        {

            return "Missing value of `--get`.";
        }
        else if (args.ContainsKey("saveAction"))
        {
            return SaveConfig();
        }
        else return "Missing arguments.";
    }

    public static string? Send(Dictionary<string, string> args)
    {
        if (args.ContainsKey("help"))
        {
            return "" +
                "Send data in network.\n" +
                "\t--type [DeviceUdpPack, ClientMessage, HostMessage, HostBroadCast] |\n" +
                "\t\tDeviceUdpPack | [--value] Send a device udp pack through string.\n" +
                "\t\tClientMessage | [--value] Send client message to master device.\n" +
                "\t\tHostMessage   | [--value] [--to] Send message to clients as master device.\n" +
                "\t\tHostBroadcast | [--value] Broadcast a message to every clients.";
        }
        if (args.ContainsKey("--type"))
        {
            switch (args["--type"].ToLower())
            {
                //  DeviceUdpPack
                case "deviceudppack":
                    if (args.ContainsKey("--value"))
                    {
                        DevicesDiscoveryServer.Messages2BroadCast.Enqueue(args["--value"]);
                        return "Appended value to broadcast list.";
                    }
                    else return "Missing value of `--value`.";
                //  ClientMessage
                case "clientmessage":
                    if (args.ContainsKey("--value"))
                    {
                        DevicesNetwork.devicesClient?.Send(args["--value"].FromUTF8());
                        return $"Sent msg: {args["--value"]}";
                    }
                    else return "Missing value of `--value`.";
                //  HostMessage
                case "hostmessage":
                    if (args.ContainsKey("--value"))
                    {
                        if (args.ContainsKey("--to"))
                        {
                            DevicesNetwork.devicesServer?.Send(
                                args["--value"].FromUTF8(),
                                args["--to"]
                            );
                            return $"Sent msg: {args["--value"]}, to: {args["--to"]}";
                        }
                        else return "Missing value of `--to`.";
                    }
                    else return "Missing value of `--value`.";
                //  HostBroadcast
                case "hostbroadcast":
                    if (args.ContainsKey("--value"))
                    {
                        DevicesNetwork.devicesServer?.BroadCast(
                            args["--value"].FromUTF8(),
                            null
                        );
                        return $"Broadcast msg: {args["--value"]}";
                    }
                    else return "Missing value of `--value`";
                default:
                    return "Missing value of `--type`.";
            }
        }
        else return "Missing arguments.";
    }

    public static string? Cache(Dictionary<string, string> args)
    {
        try
        {
            if (args.ContainsKey("help"))
            {
                return "" +
                    "Load file to CacheManager.\n" +
                    "\t--file | with path of file, if contain space, quote it.";
            }
            if (args.ContainsKey("--file"))
            {
                var path = args["--file"];

                if (path.StartsWith('"') && path.EndsWith('"'))
                    path = path[1..^1];

                Log.Information($"Debug Tool: Request CacheManager to load {path}");

                path = Path.GetFullPath(path);

                if (!File.Exists(path)) return "File not found.";

                if (args.ContainsKey("--way"))
                {
                    switch (args["--way"])
                    {
                        case "complete": break;
                        case "fragment":
                            //ToDo: Load file in fragments.
                            return "";
                    }
                }

                var info = new FileInfo(path);

                if (info.Length > 2.0 * 1024 * 1024 * 1024 - 500)
                    return "File larger than 2 GB, unsupported by this way";

                var id = Program.CacheManager?.LoadFileToCache(path);

                return $"File loaded, ID (MD5): {id?.Result ?? "null"}";
            }
            else return "Missing arguments.";
        }
        catch (Exception ex)
        {
            return $"Error when executing, ex: {ex.Message}";
        }
    }

    public static string? Dispose(Dictionary<string, string> args)
    {

        if (args.ContainsKey("--type"))
        {
            switch (args["--type"])
            {
                case "file":
                    if (args.ContainsKey("--id"))
                    {
                        var id = args["--id"];
                        var result = Program.CacheManager?.DisposeFileCache(id);
                        if (result is null) return "Unknown error occursed.";
                        if (!(bool)result) return "Dispose failed.";
                        return "Disposed.";
                    }
                    else return "Missing ID for file.";
                default:
                    return "Missing value of `--type`";
            }
        }
        else return "Missing arguments.";
    }

    public static string? Start(Dictionary<string, string> args)
    {
        if (args.ContainsKey("help"))
            return "" +
                "- plugins-services\n" +
                "- devices-services\n" +
                "- devices-discovery-server\n" +
                "- all\n";

        if (args.ContainsKey("plugins-services"))
            Program.WebManager?.Start(startAll: false, startPluginsNetwork: true);
        if (args.ContainsKey("devices-services"))
            Program.WebManager?.Start(startAll: false, startDevicesNetwork: true);
        if (args.ContainsKey("devices-discovery-server"))
            Program.WebManager?.Start(startAll: false, startDevicesDiscoveryServer: true);
        if (args.ContainsKey("all"))
            Program.WebManager?.Start();

        return "Start action requested.";
    }

    public static string? Stop(Dictionary<string, string> args)
    {
        if (args.ContainsKey("help"))
            return "" +
                "- plugins-services\n" +
                "- devices-services\n" +
                "- devices-discovery-server\n" +
                "- all";

        if (args.ContainsKey("plugins-services"))
            Program.WebManager?.Stop(stopAll: false, stopPluginsServices: true);
        if (args.ContainsKey("devices-services"))
            Program.WebManager?.Stop(stopAll: false, stopDevicesServices: true);
        if (args.ContainsKey("devices-discovery-server"))
            Program.WebManager?.Stop(stopAll: false, stopDevicesDiscoveryServer: true);
        if (args.ContainsKey("all"))
            Program.WebManager?.Stop();

        return "Stop action requested.";
    }
}

internal static class DebugServiceTool
{
    /// <summary>
    /// 获取命令头
    /// </summary>
    /// <param name="cmd">命令</param>
    /// <returns>命令头</returns>
    internal static string? GetCommandHeader(this string cmd)
    {
        var command = cmd.Trim();
        if (command is null) return null;
        var header = command.Split(' ')[0];
        return header;
    }

    /// <summary>
    /// 获取命令参数
    /// </summary>
    /// <param name="cmd">命令</param>
    /// <returns>参数字典</returns>
    internal static Dictionary<string, string>? GetCommandArgs(this string cmd)
    {
        var args = new Dictionary<string, string>();
        var command = cmd.Trim();
        var quotes = command.ReplaceQuotes();
        command = quotes.Item1;
        var quoteMap = quotes.Item2;
        var pairs = command?.Split(' ');
        if (pairs is null) return args;
        for (var i = 0; i < pairs.Length; i++)
        {
            if (pairs[i].Trim().StartsWith("--"))
            {
                if (i == pairs.Length - 1) return null;
                args.Add(pairs[i], pairs[i + 1]);
            }
            else args.Add(pairs[i], string.Empty);
        }
        foreach (var item in args)
        {
            if (quoteMap.ContainsKey(item.Value))
            {
                args[item.Key] = quoteMap[item.Value];
            }
        }
        return args;
    }

    /// <summary>
    /// 替换文本中的引号内容
    /// </summary>
    /// <param name="text">文本</param>
    /// <returns>新的引号键以及对应的值</returns>
    internal static (string, Dictionary<string, string>) ReplaceQuotes(this string text)
    {
        //  记录被替换的引号内容的键与内容
        var rst = new Dictionary<string, string>();
        var count = 0;  //  引号计数
        var lastPosition = -1;  //  上一个开引号的位置, -1 表示上一个引号是闭引号
        var len = text.Length;  //  源文本串长度
        for (var i = 0; i < len; i++)
        {
            if (text[i].Equals('"'))
            {
                if (lastPosition == -1) lastPosition = i;
                else
                {
                    var key = $"$$_Quotes_{++count}_$$";    //  按键数量替换
                    var value = text[lastPosition..(i + 1)];    //  替换的文本
                    var delta = key.Length - value.Length;  //  长度差值
                    len += delta;   //  总长度设为替换后的总长度
                    text = $"{text[0..lastPosition]}{key}{text[(i + 1)..]}";    //  替换
                    rst.Add(key, value);  //  添加键与替换的文本
                    i += delta; //  指针归位
                    lastPosition = -1;  //  设为闭引号
                }
            }
        }
        return (text, rst);
    }

    /// <summary>
    /// 获取字典中的值
    /// </summary>
    /// <param name="src">字典</param>
    /// <param name="key">键</param>
    /// <returns>值</returns>
    internal static string? Value(this Dictionary<string, string> src, string key)
        => src.ContainsKey(key) ? src[key] : null;

    /// <summary>
    /// 将命令转为可能的函数名称
    /// </summary>
    /// <param name="cmd">命令</param>
    /// <returns>可能的函数名称</returns>
    internal static string ToFunctionName(this string cmd)
        => $"{cmd[0].ToString().ToUpper()}{cmd[1..].ToLower()}";
}
