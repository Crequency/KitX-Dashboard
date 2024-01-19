using Serilog;
using System;
using System.Threading.Tasks;

namespace KitX.Dashboard.Managers;

internal class TasksManager
{






    public static void RunTask(
        Action action,
        string name = nameof(Action),
        string prompt = ">>> ",
        bool catchException = false)
    {
        Log.Information($"{prompt}Task `{name}` began.");

        if (catchException)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Log.Error(e, $"{prompt}Task `{name}` failed: {e.Message}");
            }
        }
        else action();

        Log.Information($"{prompt}Task `{name}` done.");
    }







    public static async Task RunTaskAsync(
        Action action,
        string name = nameof(Action),
        string prompt = ">>> ",
        bool catchException = false)
    {
        Log.Information($"{prompt}Task `{name}` began.");

        if (catchException)
        {
            try
            {
                await Task.Run(action);
            }
            catch (Exception e)
            {
                Log.Error(e, $"{prompt}Task `{name}` failed: {e.Message}");
            }
        }
        else await Task.Run(action);

        Log.Information($"{prompt}Task `{name}` done.");
    }
}
