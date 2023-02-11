using System;
using System.Collections.Generic;
using System.IO;

namespace KitX_Dashboard.Managers;

internal class FileWatcherManager
{
    private readonly Dictionary<string, FileWatcher> Watchers = new();

    public FileWatcherManager()
    {

    }

    /// <summary>
    /// 注册文件监控
    /// </summary>
    /// <param name="name">监控名称</param>
    /// <param name="filePath">监控文件路径</param>
    /// <param name="onchange">文件变更时事件</param>
    /// <exception cref="InvalidOperationException">试图注册已经注册的监控</exception>
    public void RegisterWatcher(string name, string filePath,
        Action<object, FileSystemEventArgs> onchange)
    {
        if (!Watchers.ContainsKey(name))
        {
            FileWatcher watcher = new(filePath, onchange);
            Watchers.Add(name, watcher);
        }
        else throw new InvalidOperationException("In RegisterWatcher()");
    }

    /// <summary>
    /// 注销文件监控, 不存在则什么也不做
    /// </summary>
    /// <param name="name">监控名称</param>
    public void UnregisterWatcher(string name)
    {
        if (Watchers.ContainsKey(name))
        {
            Watchers[name].Dispose();
            Watchers.Remove(name);
        }
    }

    /// <summary>
    /// 增加例外次数
    /// </summary>
    /// <param name="name">要增加的监控</param>
    /// <param name="count">要增加的次数</param>
    public void IncreaseExceptCount(string name, int count = 1)
    {
        if (Watchers.ContainsKey(name))
            Watchers[name].IncreaseExceptCount(count);
    }

    /// <summary>
    /// 减少例外次数
    /// </summary>
    /// <param name="name">要减少的监控</param>
    /// <param name="count">要减少的例外次数</param>
    public void DecreaseExceptCount(string name, int count = 1)
    {
        if (Watchers.ContainsKey(name))
            Watchers[name].DecreaseExceptCount(count);
    }

    /// <summary>
    /// 清空所有监控
    /// </summary>
    public void Clear()
    {
        foreach (KeyValuePair<string, FileWatcher> item in Watchers)
            item.Value.Dispose();
        Watchers.Clear();
    }
}

internal class FileWatcher : IDisposable
{
    private readonly FileSystemWatcher watcher = new();
    private int ExceptCounts = 0;

    /// <summary>
    /// 配置文件热加载实现
    /// </summary>
    public FileWatcher(string filename, Action<object, FileSystemEventArgs> onchanged)
    {
        watcher.Changed += (x, y) =>
        {
            if (ExceptCounts > 0)
                --ExceptCounts;
            else onchanged(x, y);
        };
        watcher.NotifyFilter = NotifyFilters.LastWrite;
        string filepath = Path.GetFullPath(filename);
        string? path = Path.GetDirectoryName(filepath);
        if (path == null)
            throw new NullReferenceException("In FileWatcher(): Failed in GetDirectoryName()");
        watcher.Path = path;
        watcher.Filter = Path.GetFileName(filepath);
        watcher.EnableRaisingEvents = true;
    }

    /// <summary>
    /// 增加例外次数
    /// </summary>
    /// <param name="count">次数</param>
    public void IncreaseExceptCount(int count) => ExceptCounts += count;

    /// <summary>
    /// 减少例外次数
    /// </summary>
    /// <param name="count">次数</param>
    public void DecreaseExceptCount(int count) => ExceptCounts -= count;

    public void Dispose()
    {
        watcher.Dispose();
    }
}
