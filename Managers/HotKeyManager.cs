﻿using Common.BasicHelper.Utils.Extensions;
using SharpHook;
using SharpHook.Native;
using System;
using System.Collections.Generic;

namespace KitX_Dashboard.Managers;

internal class HotKeyManager
{
    private const int keysLimitation = 5;

    private readonly Queue<KeyCode>? keyPressed;

    private readonly Dictionary<string, Action<KeyCode[]>>? hotKeyHandlers;

    public HotKeyManager()
    {
        keyPressed = new();

        hotKeyHandlers = new();
    }

    public HotKeyManager Hook()
    {
        var hook = new TaskPoolGlobalHook();

        hook.KeyPressed += (_, args) =>
        {
            keyPressed!.Enqueue(args.Data.KeyCode);

            if (keyPressed!.Count > keysLimitation)
                _ = keyPressed.Dequeue();

            VerifyKeys();
        };

        hook.RunAsync();

        return this;
    }

    private void VerifyKeys()
    {
        var index = 0;

        var tmpList = new KeyCode[keysLimitation];

        keyPressed!.ForEach(x =>
        {
            tmpList[index] = x;

            ++index;

        }, true);

        foreach (var handler in hotKeyHandlers!.Values)
            handler.Invoke(tmpList);
    }

    public HotKeyManager RegisterHotKeyHandler(string name, Action<KeyCode[]> handler)
    {
        hotKeyHandlers!.Add(name, handler);

        return this;
    }

    public HotKeyManager UnregisterHotKeyHandler(string name)
    {
        if (hotKeyHandlers!.TryGetValue(name, out _))
        {
            hotKeyHandlers.Remove(name);
        }

        return this;
    }
}
