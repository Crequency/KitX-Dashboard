﻿using Serilog.Events;

namespace KitX.Dashboard.Models;

internal class SupportedLogLevel
{
    internal LogEventLevel LogEventLevel { get; set; }

    internal string? LogLevelName { get; set; }

    internal string? LogLevelDisplayName { get; set; }
}
