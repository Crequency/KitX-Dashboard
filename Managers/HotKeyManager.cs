using Avalonia.Threading;
using Common.BasicHelper.Utils.Extensions;
using KitX_Dashboard.Views;
using SharpHook;
using SharpHook.Native;
using System.Collections.Generic;

namespace KitX_Dashboard.Managers;

internal class HotKeyManager
{
    private const int keysLimitation = 5;

    private static readonly Queue<KeyCode> keyPressed = new();

    public static void RegisterPluginLaunchWindowHotKey()
    {
        var hook = new TaskPoolGlobalHook();

        hook.KeyPressed += (_, args) =>
        {
            keyPressed.Enqueue(args.Data.KeyCode);

            if (keyPressed.Count > keysLimitation)
                _ = keyPressed.Dequeue();

            VerifyKeys();
        };

        hook.RunAsync();
    }

    public static void VerifyKeys()
    {
        // Ctrl + Win + C: fot test

        var index = 0;

        var count = keyPressed.Count;

        var tmpList = new KeyCode[keysLimitation];

        keyPressed.ForEach(x =>
        {
            tmpList[index] = x;

            ++index;

        }, true);

        if (count >= 3 &&
            tmpList[count - 3] == KeyCode.VcLeftControl &&
            tmpList[count - 2] == KeyCode.VcLeftMeta &&
            tmpList[count - 1] == KeyCode.VcC)
        {
            Dispatcher.UIThread.Post(() =>
            {
                new PluginsLaunchWindow().Show();
            });
        }
    }
}
