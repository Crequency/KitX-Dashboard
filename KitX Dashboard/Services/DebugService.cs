using System;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Csharpell.Core;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;

namespace KitX.Dashboard.Services;

public static class DebugService
{
    private static readonly CSharpScriptEngine Engine = new();

    public static async Task<string?> ExecuteCodesAsync(string code, CancellationToken cancellationToken = default)
    {
        var sw = new Stopwatch();

        var begin = DateTime.Now;

        sw.Start();

        try
        {
            var result = (await Engine.ExecuteAsync(
                code,
                options =>
                {
                    options = options
                        .WithReferences(Assembly.GetExecutingAssembly())
                        .WithImports("KitX", "KitX.Dashboard")
                        .WithLanguageVersion(LanguageVersion.Preview)
                        ;

                    return options;

                },
                addDefaultImports: true,
                runInReplMode: false,
                cancellationToken: cancellationToken
            ))?.ToString();

            sw.Stop();

            return new StringBuilder()
                .AppendLine($"[{begin:yyyy-MM-dd HH:mm:ss}] [I] Posted.")
                .AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [I] Ended, took {sw.ElapsedMilliseconds} ms.")
                .AppendLine(result)
                .ToString()
                ;
        }
        catch (Exception e)
        {
            sw.Stop();

            return new StringBuilder()
                .AppendLine($"[{begin:yyyy-MM-dd HH:mm:ss}] [I] Posted.")
                .AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [E] Exception caught after {sw.ElapsedMilliseconds} ms, Message: {e.Message}")
                .AppendLine(e.StackTrace)
                .ToString()
                ;
        };
    }
}
