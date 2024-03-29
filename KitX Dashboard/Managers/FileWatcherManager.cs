﻿using Common.BasicHelper.Utils.Extensions;
using KitX.Dashboard.Names;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace KitX.Dashboard.Managers;

public class FileWatcherManager
{
    private readonly Dictionary<string, FileWatcher> Watchers = [];

    public FileWatcherManager()
    {
        AppFramework.AfterInitailization(() =>
        {
            Instances.SignalTasksManager!.RaiseSignal(nameof(SignalsNames.FileWatcherManagerInitializedSignal));
        });
    }

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

        var filepath = filename.GetFullPath();

        var path = Path.GetDirectoryName(filepath) ?? throw new NullReferenceException($"In {location}._ctor: Failed in {nameof(Path.GetDirectoryName)}");

        watcher = new()
        {
            NotifyFilter = notifyFilters ?? NotifyFilters.LastWrite,
            Path = path,
            Filter = Path.GetFileName(filepath.GetFullPath())
        };

        watcher.Changed += (x, y) =>
        {
            if (ExceptCounts > 0)
                --ExceptCounts;
            else
                onchanged(x, y);
        };

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
