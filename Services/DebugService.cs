using System.Collections.Generic;
using System.Linq;
using System.Reflection;

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
        var foo = typeof(DebugCommands).GetMethod(name);
        return foo?.Invoke(null, new Dictionary<string, string>[] { args }) as string;
    }
}

internal class DebugCommands
{
    private static string SaveConfig()
    {
        Helper.SaveConfig();
        return "Config saved!";
    }

    public static string? Version(Dictionary<string, string> _)
        => Assembly.GetEntryAssembly()?.GetName()?.Version?.ToString();

    public static string? Save(Dictionary<string, string> args)
    {
        if (args.ContainsKey("--type"))
            switch (args["--type"])
            {
                case "config":
                    return SaveConfig();
                default:
                    return "Missing value of `--type`.";
            }
        else if (args.ContainsKey("config"))
        {
            return SaveConfig();
        }
        else return "Missing arguments.";
    }

    public static string? Config(Dictionary<string, string> args)
    {
        if (args.ContainsKey("--set"))
        {
            return "Missing value of `--set`.";
        }
        else if (args.ContainsKey("--get"))
        {

            return "Missing value of `--get`.";
        }
        else if (args.ContainsKey("save"))
        {
            return SaveConfig();
        }
        else return "Missing arguments.";
    }

    public static string? Send(Dictionary<string, string> args)
    {
        if (args.ContainsKey("--type"))
        {
            switch (args["--type"].ToLower())
            {
                //  DeviceUdpPack
                case "deviceudppack":
                    if (args.ContainsKey("--value"))
                    {
                        DevicesServer.Messages2BroadCast.Enqueue(args["--value"]);
                        return "Appended value to broadcast list.";
                    }
                    else return "Missing value of `--value`.";
                default:
                    return "Missing value of `--type`.";
            }
        }
        else return "Missing arguments.";
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
