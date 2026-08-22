using System;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Csharpell.Core;
using KitX.Core.Contract.Configuration;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;

namespace KitX.Dashboard.Services;

public static class DebugService
{
    /// <summary>
    /// Single reusable engine instance (D13.4 — the property previously allocated a
    /// new <c>CSharpScriptEngine</c> per execution).
    /// </summary>
    private static readonly CSharpScriptEngine Engine = new();

    public static async Task<string?> ExecuteCodesAsync(
        string code,
        bool includeTimestamp = true,
        CancellationToken cancellationToken = default
    )
    {
        // D13.4: developer gate — arbitrary C# execution is refused unless the
        // Developer Setting is enabled (second confirmation happens at the command
        // sites, which refuse to open the tool window when it is off).
        var configService = App.GetService<IConfigService>();
        if (!configService.AppConfig.App.DeveloperSetting)
            return includeTimestamp
                ? $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [E] Debug execution disabled — enable Developer Setting first."
                : null;

        var sw = new Stopwatch();

        var begin = DateTime.Now;

        sw.Start();

        try
        {
            var result = (
                await Engine.ExecuteAsync(
                    code,
                    options =>
                    {
                        options = options
                            .WithReferences(Assembly.GetExecutingAssembly())
                            .WithImports("KitX", "KitX.Dashboard")
                            .WithLanguageVersion(LanguageVersion.Preview);

                        return options;
                    },
                    addDefaultImports: true,
                    runInReplMode: false,
                    cancellationToken: cancellationToken
                )
            )?.ToString();

            sw.Stop();

            return includeTimestamp
                ? new StringBuilder()
                    .AppendLine($"[{begin:yyyy-MM-dd HH:mm:ss}] [I] Posted.")
                    .AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [I] Ended, took {sw.ElapsedMilliseconds} ms.")
                    .AppendLine(result)
                    .ToString()
                : result;
        }
        catch (Exception e)
        {
            sw.Stop();

            return includeTimestamp
                ? new StringBuilder()
                    .AppendLine($"[{begin:yyyy-MM-dd HH:mm:ss}] [I] Posted.")
                    .AppendLine(
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [E] Exception caught after {sw.ElapsedMilliseconds} ms, Message: {e.Message}"
                    )
                    .AppendLine(e.StackTrace)
                    .ToString()
                : e.StackTrace;
        }
        ;
    }
}
