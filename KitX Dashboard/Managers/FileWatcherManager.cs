using Common.BasicHelper.Utils.Extensions;
using System;
using System.Collections.Generic;
using System.IO;

namespace KitX.Dashboard.Managers;

internal class FileWatcherManager
{
    private readonly Dictionary<string, FileWatcher> Watchers = new();








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





    public FileWatcherManager UnregisterWatcher(string name)
    {
        if (Watchers.TryGetValue(name, out var watcher))
        {
            watcher?.Dispose();
            Watchers.Remove(name);
        }

        return this;
    }






    public FileWatcherManager IncreaseExceptCount(string name, int count = 1)
    {
        if (Watchers.TryGetValue(name, out var watcher))
            watcher?.IncreaseExceptCount(count);

        return this;
    }






    public FileWatcherManager DecreaseExceptCount(string name, int count = 1)
    {
        if (Watchers.TryGetValue(name, out var watcher))
            watcher?.DecreaseExceptCount(count);

        return this;
    }




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





    public void IncreaseExceptCount(int count) => ExceptCounts += count;





    public void DecreaseExceptCount(int count) => ExceptCounts -= count;

    public void Dispose()
    {
        watcher?.Dispose();

        watcher = null;
    }
}
