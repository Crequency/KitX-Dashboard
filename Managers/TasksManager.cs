using Common.BasicHelper.Util.Extension;
using System;
using System.Collections.Generic;

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
}
