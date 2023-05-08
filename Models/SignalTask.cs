using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KitX_Dashboard.Models;

internal class SignalTask
{
    internal Queue<Action> Actions { get; } = new();

    internal bool Reusable { get; set; } = false;

    internal SignalTask AddAction(Action action)
    {
        Actions.Enqueue(action);

        return this;
    }

    internal SignalTask Reuse(bool reusable = true)
    {
        Reusable = reusable;

        return this;
    }
}
