using Avalonia;
using Avalonia.ReactiveUI;
using Common.BasicHelper.Utils.Extensions;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Services;
using System;
using System.IO;

namespace KitX.Dashboard;

class Program
{
    /// <summary>
    /// Main entry for program
    /// </summary>
    /// <param name="args"></param>
    /// Initialization code. Don't use any Avalonia, third-party APIs or any
    /// SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    /// yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            EventService.Init();

            Helper.ProcessStartupArguments(args);

            Helper.StartUpCheck();

            ConfigManager.AppConfig.App.RanTime++;

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

            Helper.Exit();
        }
        catch (Exception e)
        {
            // Any unhandled exception will be catched here!
            File.AppendAllText(
                "./dump.log".GetFullPath(),
                $"""
                [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Message}
                {e.StackTrace}
                """
            );

#if !DEBUG
            Environment.Exit(1);
#endif

        }
    }

    /// <summary>
    /// Build Avalonia app
    /// </summary>
    /// <returns>Avalonia AppBuilder</returns>
    /// Do not remove this, it also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI()
            .With(
                new MacOSPlatformOptions
                {
                    ShowInDock = true,
                }
            )
            .With(
                new X11PlatformOptions
                {
                    EnableMultiTouch = true,
                }
            );
}
