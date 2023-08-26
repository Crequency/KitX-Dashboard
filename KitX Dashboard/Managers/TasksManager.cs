using Serilog;
using System;
using System.Threading.Tasks;

namespace KitX.Dashboard.Managers;

internal class TasksManager
{
    /// <summary>
    /// 执行任务, 并带有更好的日志显示
    /// </summary>
    /// <param name="action">要执行的动作</param>
    /// <param name="name">日志显示名称</param>
    /// <param name="prompt">日志提示</param>
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

    /// <summary>
    /// 异步执行任务, 并带有更好的日志显示
    /// </summary>
    /// <param name="action">要执行的动作</param>
    /// <param name="name">任务名称</param>
    /// <param name="prompt">日志提示</param>
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
