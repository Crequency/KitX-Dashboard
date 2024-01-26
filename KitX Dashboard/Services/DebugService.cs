using Common.BasicHelper.Utils.Extensions;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Network;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace KitX.Dashboard.Services;

internal class DebugService
{
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
        if (args.TryGetValue("--times", out var timesString))
        {
            if (int.TryParse(timesString, out var times))
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
        if (args.TryGetValue("help", out _))
            return "Print version of KitX Dashboard.";
        return Assembly.GetEntryAssembly()?.GetName()?.Version?.ToString();
    }

    public static string? Save(Dictionary<string, string> args)
    {
        if (args.TryGetValue("help", out _))
            return "" +
                "Save datas to disk.\n" +
                "\t--type [config] | saveAction KitX Dashboard config file.\n" +
                "\tconfig | saveAction KitX Dashboard config file.\n";
        if (args.TryGetValue("--type", out var type))
            return type switch
            {
                "config" => SaveConfig(),
                _ => "Missing value of `--type`.",
            };
        else if (args.TryGetValue("config", out _))
        {
            return SaveConfig();
        }
        else return "Missing arguments.";
    }

    public static string? Config(Dictionary<string, string> args)
    {
        if (args.TryGetValue("help", out _))
        {
            return "" +
                "Edit KitX Dashboard app config.\n" +
                "\t--set developing...\n" +
                "\t--get developing...\n";
        }
        if (args.TryGetValue("--set", out _))
        {
            return "Missing value of `--set`.";
        }
        else if (args.TryGetValue("--get", out _))
        {
            return "Missing value of `--get`.";
        }
        else if (args.TryGetValue("saveAction", out _))
        {
            return SaveConfig();
        }
        else return "Missing arguments.";
    }

    public static string? Send(Dictionary<string, string> args)
    {
        if (args.TryGetValue("help", out _))
        {
            return "" +
                "Send data in network.\n" +
                "\t--type [DeviceUdpPack, ClientMessage, HostMessage, HostBroadCast] |\n" +
                "\t\tDeviceUdpPack | [--value] Send a device udp pack through string.\n" +
                "\t\tClientMessage | [--value] Send client message to master device.\n" +
                "\t\tHostMessage   | [--value] [--to] Send message to clients as master device.\n" +
                "\t\tHostBroadcast | [--value] Broadcast a message to every clients.";
        }
        if (args.TryGetValue("--type", out var type))
        {
            switch (type.ToLower())
            {
                //  DeviceUdpPack
                case "deviceudppack":
                    if (args.TryGetValue("--value", out var udpPackValue))
                    {
                        DevicesDiscoveryServer.Messages2BroadCast.Enqueue(udpPackValue);
                        return "Appended value to broadcast list.";
                    }
                    else return "Missing value of `--value`.";
                //  ClientMessage
                case "clientmessage":
                    if (args.TryGetValue("--value", out var clientMessageValue))
                    {
                        DevicesNetwork.devicesClient?.Send(clientMessageValue.FromUTF8());
                        return $"Sent msg: {clientMessageValue}";
                    }
                    else return "Missing value of `--value`.";
                //  HostMessage
                case "hostmessage":
                    if (args.TryGetValue("--value", out var hostMessageValue))
                    {
                        if (args.TryGetValue("--to", out var hostMessageTo))
                        {
                            DevicesNetwork.devicesServer?.Send(
                                hostMessageValue.FromUTF8(),
                                hostMessageTo
                            );
                            return $"Sent msg: {hostMessageValue}, to: {hostMessageTo}";
                        }
                        else return "Missing value of `--to`.";
                    }
                    else return "Missing value of `--value`.";
                //  HostBroadcast
                case "hostbroadcast":
                    if (args.TryGetValue("--value", out var broadcastValue))
                    {
                        DevicesNetwork.devicesServer?.BroadCast(
                            broadcastValue.FromUTF8(),
                            null
                        );
                        return $"Broadcast msg: {broadcastValue}";
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
            if (args.TryGetValue("help", out _))
            {
                return "" +
                    "Load file to CacheManager.\n" +
                    "\t--file | with path of file, if contain space, quote it.";
            }
            if (args.TryGetValue("--file", out var file))
            {
                var path = file;

                if (path.StartsWith('"') && path.EndsWith('"'))
                    path = path[1..^1];

                Log.Information($"Debug Tool: Request CacheManager to load {path}");

                path = Path.GetFullPath(path);

                if (!File.Exists(path)) return "File not found.";

                if (args.TryGetValue("--way", out var way))
                {
                    switch (way)
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

                var id = Instances.CacheManager?.LoadFileToCache(path);

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

        if (args.TryGetValue("--type", out var type))
        {
            switch (type)
            {
                case "file":
                    if (args.TryGetValue("--id", out var fileId))
                    {
                        var id = fileId;
                        var result = Instances.CacheManager?.DisposeFileCache(id);
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
        if (args.TryGetValue("help", out _))
            return "" +
                "- plugins-services\n" +
                "- devices-services\n" +
                "- devices-discovery-server\n" +
                "- all\n";

        if (args.TryGetValue("plugins-services", out _))
            Instances.WebManager?.Start(startAll: false, startPluginsNetwork: true);
        if (args.TryGetValue("devices-services", out _))
            Instances.WebManager?.Start(startAll: false, startDevicesNetwork: true);
        if (args.TryGetValue("devices-discovery-server", out _))
            Instances.WebManager?.Start(startAll: false, startDevicesDiscoveryServer: true);
        if (args.TryGetValue("all", out _))
            Instances.WebManager?.Start();

        return "Start action requested.";
    }

    public static string? Stop(Dictionary<string, string> args)
    {
        if (args.TryGetValue("help", out _))
            return "" +
                "- plugins-services\n" +
                "- devices-services\n" +
                "- devices-discovery-server\n" +
                "- all";

        if (args.TryGetValue("plugins-services", out _))
            Instances.WebManager?.Stop(stopAll: false, stopPluginsServices: true);
        if (args.TryGetValue("devices-services", out _))
            Instances.WebManager?.Stop(stopAll: false, stopDevicesServices: true);
        if (args.TryGetValue("devices-discovery-server", out _))
            Instances.WebManager?.Stop(stopAll: false, stopDevicesDiscoveryServer: true);
        if (args.TryGetValue("all", out _))
            Instances.WebManager?.Stop();

        return "Stop action requested.";
    }

    public static string? Hash(Dictionary<string, string> args)
    {
        if (args.TryGetValue("help", out _))
            return "" +
                "Return hashed value for parameters.\n" +
                "\t--way [MD5,SHA1,Common.Algorithm] |\n" +
                "\t\tMD5              | [--value] Use MD5 to hash parameters.\n" +
                "\t\tSHA1             | [--value] Use SHA1 to hash parameters.\n" +
                "\t\tCommon.Algorithm | [--value] Use Common.Algorithm to hash parameters.";

        if (args.TryGetValue("--way", out var way))
        {
            switch (way.ToLower())
            {
                case "md5":
                    {
                        var result = MD5.HashData(args["--value"].FromUTF8()).ToUTF8();
                        return result;
                    }
                case "sha1":
                    {
                        var result = SHA1.HashData(args["--value"].FromUTF8()).ToUTF8();
                        return result;
                    }
                case "common.algorithm":
                    {
                        return Common.Algorithm.Interop.Hash.FromString2Hex(args["--value"]);
                    }
                default:
                    return "No this way.";
            }
        }
        else return "Missing arguments.";
    }
}

internal static class DebugServiceTool
{
    internal static string? GetCommandHeader(this string cmd)
    {
        var command = cmd.Trim();
        if (command is null) return null;
        var header = command.Split(' ')[0];
        return header;
    }

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
            if (quoteMap.TryGetValue(item.Value, out var value))
            {
                args[item.Key] = value;
            }
        }
        return args;
    }

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

    internal static string? Value(this Dictionary<string, string> src, string key)
        => src.TryGetValue(key, out var value) ? value : null;

    internal static string ToFunctionName(this string cmd)
        => $"{cmd[0].ToString().ToUpper()}{cmd[1..].ToLower()}";
}
