using System;
using System.Text.Json;
using KitX.ToolKit.Models;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// A single panel control's display state (ToolKit 前后端分离 GUI 稿 §5). The panel is a
/// projection of the instance's DataStore keys; this VM holds the rendered value and is
/// updated from the backend's <see cref="KitX.ToolKit.Contracts.Events.UiControlStateChangedEvent"/>
/// (and DataStore changes) pushed through the Bench event channel.
/// </summary>
internal sealed class PanelControlViewModel : ReactiveObject
{
    private string _text;
    private bool _enabled = true;

    public PanelControlViewModel(UiControl control)
    {
        Type = control.Type;
        Id = control.Id;
        StaticText = control.Text ?? string.Empty;
        _text = StaticText;
    }

    /// <summary>The fixed control type discriminant (Text/Input/Button/...).</summary>
    internal string Type { get; }

    /// <summary>Control id.</summary>
    internal string Id { get; }

    /// <summary>Static/initial text from the config (not live-bound).</summary>
    internal string StaticText { get; }

    /// <summary>The live-displayed text/value.</summary>
    internal string Text
    {
        get => _text;
        private set => this.RaiseAndSetIfChanged(ref _text, value);
    }

    /// <summary>Live enabled state.</summary>
    internal bool IsEnabled
    {
        get => _enabled;
        private set => this.RaiseAndSetIfChanged(ref _enabled, value);
    }

    /// <summary>Applies a value change from the backend for the given property.</summary>
    internal void Apply(string prop, JsonElement? value)
    {
        if (prop == "enabled")
            IsEnabled = NormalizeBool(value);
        else if (prop is "text" or "value")
            Text = value is { ValueKind: JsonValueKind.String } s ? s.GetString()! : value?.GetRawText() ?? StaticText;
    }

    private static bool NormalizeBool(JsonElement? value) => value switch
    {
        { ValueKind: JsonValueKind.True } => true,
        { ValueKind: JsonValueKind.Number } n => n.GetDouble() != 0,
        { ValueKind: JsonValueKind.String } s => s.GetString() is { } str
            && str is not ("禁用" or "false" or "0" or ""),
        _ => false,
    };
}
