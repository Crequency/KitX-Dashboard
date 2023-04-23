using Common.BasicHelper.Utils.Extensions;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace KitX_Dashboard.Managers;

internal class TasksManager
{
    private readonly Dictionary<string, Queue<Action>> SignalTasks = new();

    /// <summary>
    /// 触发信号
    /// </summary>
    /// <param name="signal">信号名称</param>
    internal void RaiseSignal(string signal)
    {
        if (SignalTasks.ContainsKey(signal))
        {
            var queue = SignalTasks[signal];
            queue.ForEach(x => x.Invoke());
            SignalTasks.Remove(signal);
        }
    }

    /// <summary>
    /// 信号发生时运行任务
    /// </summary>
    /// <param name="signal">信号</param>
    /// <param name="action">任务</param>
    internal void SignalRun(string signal, Action action)
    {
        if (SignalTasks.ContainsKey(signal))
            SignalTasks[signal].Enqueue(action);
        else SignalTasks.Add(signal, new Queue<Action>().Push(action));
    }

    /// <summary>
    /// 执行任务, 并带有更好的日志显示
    /// </summary>
    /// <param name="action">要执行的动作</param>
    /// <param name="name">日志显示名称</param>
    /// <param name="prompt">日志提示</param>
    public static void RunTask(
        Action action,
        string name = nameof(Action),
        string prompt = ">>> ")
    {
        Log.Information($"{prompt}Task `{name}` began.");

        action();

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
        string prompt = ">>> ")
    {
        Log.Information($"{prompt}Task `{name}` began.");

        await Task.Run(action);

        Log.Information($"{prompt}Task `{name}` done.");
    }
}
