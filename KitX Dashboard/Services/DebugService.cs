using Csharpell.Core;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using System;
using System.Text;
using System.Reflection;
using System.Threading.Tasks;

namespace KitX.Dashboard.Services;

internal class DebugService
{
    private static readonly CSharpScriptEngine Engine = new();

    internal static async Task<string?> ExecuteCodesAsync(string code)
    {
        try
        {
            var result = (await Engine.ExecuteAsync(code, options =>
            {
                options = options
                    .WithReferences(Assembly.GetExecutingAssembly())
                    .WithImports("KitX", "KitX.Dashboard")
                    .WithLanguageVersion(LanguageVersion.Preview)
                    ;

                return options;

            }))?.ToString();

            return new StringBuilder()
                .AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Executed, result below:")
                .AppendLine(result)
                .ToString()
                ;
        }
        catch (Exception e)
        {
            return new StringBuilder()
                .AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Exception: {e.Message}")
                .AppendLine(e.StackTrace)
                .ToString();
        };
    }
}
