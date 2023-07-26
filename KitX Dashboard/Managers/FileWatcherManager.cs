using Common.BasicHelper.Utils.Extensions;
using System;
using System.Collections.Generic;
using System.IO;

namespace KitX_Dashboard.Managers;

internal class FileWatcherManager
{
    private readonly Dictionary<string, FileWatcher> Watchers = new();

    /// <summary>
    /// 注册文件监控
    /// </summary>
    /// <param name="name">监控名称</param>
    /// <param name="filePath">监控文件路径</param>
    /// <param name="onchange">文件变更时事件</param>
    /// <exception cref="InvalidOperationException">试图注册已经注册的监控</exception>
    public FileWatcherManager RegisterWatcher(
        string name,
        string filePath,
        Action<object, FileSystemEventArgs> onchange)
    {
        var location = $"{nameof(FileWatcherManager)}.{nameof(RegisterWatcher)}";

        if (!Watchers.ContainsKey(name))
        {
            var watcher = new FileWatcher(filePath, onchange);

            Watchers.Add(name, watcher);
        }
        else throw new InvalidOperationException($"FileWatcher {name} already exists.");

        return this;
    }

    /// <summary>
    /// 注销文件监控, 不存在则什么也不做
    /// </summary>
    /// <param name="name">监控名称</param>
    public FileWatcherManager UnregisterWatcher(string name)
    {
        if (Watchers.ContainsKey(name))
        {
            Watchers[name].Dispose();
            Watchers.Remove(name);
        }

        return this;
    }

    /// <summary>
    /// 增加例外次数
    /// </summary>
    /// <param name="name">要增加的监控</param>
    /// <param name="count">要增加的次数</param>
    public FileWatcherManager IncreaseExceptCount(string name, int count = 1)
    {
        if (Watchers.ContainsKey(name))
            Watchers[name].IncreaseExceptCount(count);

        return this;
    }

    /// <summary>
    /// 减少例外次数
    /// </summary>
    /// <param name="name">要减少的监控</param>
    /// <param name="count">要减少的例外次数</param>
    public FileWatcherManager DecreaseExceptCount(string name, int count = 1)
    {
        if (Watchers.ContainsKey(name))
            Watchers[name].DecreaseExceptCount(count);

        return this;
    }

    /// <summary>
    /// 清空所有监控
    /// </summary>
    public FileWatcherManager Clear()
    {
        foreach (KeyValuePair<string, FileWatcher> item in Watchers)
            item.Value.Dispose();

        Watchers.Clear();

        return this;
    }
}

internal class FileWatcher : IDisposable
{
    private int ExceptCounts = 0;

    private FileSystemWatcher? watcher = null;

    public FileWatcher(
        string filename,
        Action<object, FileSystemEventArgs> onchanged,
        NotifyFilters? notifyFilters = NotifyFilters.LastWrite)
    {
        var location = $"{nameof(FileWatcherManager)}.{nameof(FileWatcher)}";

        watcher = new();

        watcher.Changed += (x, y) =>
        {
            if (ExceptCounts > 0)
                --ExceptCounts;
            else onchanged(x, y);
        };

        watcher.NotifyFilter = notifyFilters ?? NotifyFilters.LastWrite;

        var filepath = filename.GetFullPath();
        var path = Path.GetDirectoryName(filepath)
            ?? throw new NullReferenceException("In FileWatcher(): Failed in GetDirectoryName()");

        watcher.Path = path;
        watcher.Filter = filepath.GetFullPath();
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
        watcher?.Dispose();

        watcher = null;
    }
}
