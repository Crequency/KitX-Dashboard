using System.IO;
using Common.BasicHelper.Utils.Extensions;
using KitX.Core.Contract.Configuration;
using Serilog;
using Serilog.Events;

namespace KitX.Dashboard.Services;

/// <summary>
/// Shared Serilog configuration built from the contract <see cref="ILogConf"/>.
/// The contract <see cref="LogLevel"/> mirrors Serilog's numeric values, so a
/// direct cast is safe.
/// </summary>
internal static class LoggerConfigurator
{
    /// <summary>
    /// Reconfigures the global <see cref="Log.Logger"/> from the given log config section.
    /// </summary>
    /// <param name="logConf">The log configuration section.</param>
    /// <param name="writeToConsole">Whether to also write to the console.</param>
    internal static void Configure(ILogConf logConf, bool writeToConsole = false)
    {
        var logdir = logConf.LogFilePath.GetFullPath();

        if (!Directory.Exists(logdir))
            Directory.CreateDirectory(logdir);

        var minLevel = (LogEventLevel)logConf.LogLevel;

        var builder = new LoggerConfiguration()
            .MinimumLevel.Is(minLevel);

        if (writeToConsole)
            builder = builder.WriteTo.Console(outputTemplate: logConf.LogTemplate, restrictedToMinimumLevel: minLevel);

        Log.Logger = builder
            .WriteTo.File(
                $"{logdir}Log_.log",
                outputTemplate: logConf.LogTemplate,
                rollingInterval: RollingInterval.Hour,
                fileSizeLimitBytes: logConf.LogFileSingleMaxSize,
                buffered: true,
                flushToDiskInterval: new(0, 0, logConf.LogFileFlushInterval),
                restrictedToMinimumLevel: minLevel,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: logConf.LogFileMaxCount
            )
            .CreateLogger();
    }
}
