using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Text.Json;
using KitX.ToolKit.Contracts;
using KitX.ToolKit.Models;
using Material.Icons;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels;

/// <summary>
/// A single panel control's live state + interaction surface (ToolKit 前后端分离 GUI 稿 §5).
/// The panel is a projection of the instance's DataStore keys; this VM holds the rendered
/// value and is updated from the backend's <see cref="KitX.ToolKit.Contracts.Events.UiControlStateChangedEvent"/>
/// (and DataStore changes) pushed through the Bench event channel.
///
/// <para>Interactive controls write back through <see cref="IPanelRuntime"/>: value controls
/// (Input/Number/Select/Switch) call <see cref="IPanelRuntime.SetControlValue"/>; command
/// controls (Button/Dialog) raise <see cref="IPanelRuntime.RaiseControlEvent"/>.</para>
/// </summary>
internal sealed class PanelControlViewModel : ReactiveObject
{
    private readonly string _instanceId;
    private readonly IPanelRuntime _panelRuntime;

    private string _text;
    private object? _value;
    private bool _enabled = true;
    private readonly ObservableCollection<string> _logEntries = [];

    public PanelControlViewModel(UiControl control, string instanceId, IPanelRuntime panelRuntime)
    {
        Type = control.Type;
        Id = control.Id;
        StaticText = control.Text ?? string.Empty;
        _text = StaticText;
        _instanceId = instanceId;
        _panelRuntime = panelRuntime;
        Options = control.Options;
        _selectItems = ParseStaticItems(control.Options);

        ClickCommand = ReactiveCommand.Create(() => _panelRuntime.RaiseControlEvent(_instanceId, Id, "Click", null));
        SubmitCommand = ReactiveCommand.Create(() => _panelRuntime.RaiseControlEvent(_instanceId, Id, "Submit", Value));
        SelectCommand = ReactiveCommand.Create<string>(v => _panelRuntime.SetControlValue(_instanceId, Id, v));
        ToggleCommand = ReactiveCommand.Create<bool>(v => _panelRuntime.SetControlValue(_instanceId, Id, v));
        NumberCommand = ReactiveCommand.Create<double>(v => _panelRuntime.SetControlValue(_instanceId, Id, v));
        DialogConfirmCommand = ReactiveCommand.Create<string>(result => _panelRuntime.RaiseControlEvent(_instanceId, Id, "Confirm", result));

        if (!Enum.TryParse<MaterialIconKind>(StaticText, true, out var iconKind))
            iconKind = MaterialIconKind.Image;
        IconKind = iconKind;
    }

    /// <summary>The fixed control type discriminant (Text/Input/Button/...).</summary>
    internal string Type { get; }

    /// <summary>Control id.</summary>
    internal string Id { get; }

    /// <summary>Static/initial text from the config (not live-bound).</summary>
    internal string StaticText { get; }

    /// <summary>Type-specific props (e.g. <c>{"Items":[...]}</c> for Select).</summary>
    internal Dictionary<string, object?>? Options { get; }

    /// <summary>The live-displayed text/value.</summary>
    internal string Text
    {
        get => _text;
        private set => this.RaiseAndSetIfChanged(ref _text, value);
    }

    /// <summary>The live value for value controls (Input/Number/Select/Switch/Progress).</summary>
    internal object? Value
    {
        get => _value;
        private set => this.RaiseAndSetIfChanged(ref _value, value);
    }

    /// <summary>Live enabled state.</summary>
    internal bool IsEnabled
    {
        get => _enabled;
        private set => this.RaiseAndSetIfChanged(ref _enabled, value);
    }

    private bool _isApplyingBackend;
    private double _numberValue;
    private string? _selectedValue;
    private IReadOnlyList<string> _selectItems;

    /// <summary>Two-way Number value (UI edit writes back through NumberCommand).</summary>
    internal double NumberValue
    {
        get => _numberValue;
        set
        {
            var changed = _numberValue != value;
            if (changed)
            {
                _numberValue = value;
                this.RaisePropertyChanged();
                if (!_isApplyingBackend)
                    NumberCommand?.Execute(value);
            }
        }
    }

    /// <summary>Two-way Select value (UI edit writes back through SelectCommand).</summary>
    internal string? SelectedValue
    {
        get => _selectedValue;
        set
        {
            var changed = _selectedValue != value;
            if (changed)
            {
                _selectedValue = value;
                this.RaisePropertyChanged();
                if (!_isApplyingBackend && value is not null)
                    SelectCommand?.Execute(value);
            }
        }
    }

    /// <summary>Appended log entries (Log control).</summary>
    internal ObservableCollection<string> LogEntries => _logEntries;

    /// <summary>Parsed Material icon kind (falls back to Image).</summary>
    internal MaterialIconKind IconKind { get; }

    /// <summary>Select options: static config <c>Options["Items"]</c> initially, replaced
    /// wholesale by backend "options" pushes (<c>UiSet(controlId, "options", jsonArray)</c>).</summary>
    internal IReadOnlyList<string> SelectItems
    {
        get => _selectItems;
        private set => this.RaiseAndSetIfChanged(ref _selectItems, value);
    }

    /// <summary>Button click → UIEvent "Click".</summary>
    internal ReactiveCommand<Unit, Unit>? ClickCommand { get; }

    /// <summary>Input submit → UIEvent "Submit" (carries the current value).</summary>
    internal ReactiveCommand<Unit, Unit>? SubmitCommand { get; }

    /// <summary>Select change → write-back.</summary>
    internal ReactiveCommand<string, Unit>? SelectCommand { get; }

    /// <summary>Switch toggle → write-back.</summary>
    internal ReactiveCommand<bool, Unit>? ToggleCommand { get; }

    /// <summary>Number change → write-back.</summary>
    internal ReactiveCommand<double, Unit>? NumberCommand { get; }

    /// <summary>Dialog confirm → UIEvent "Confirm" (carries the chosen result).</summary>
    internal ReactiveCommand<string, Unit>? DialogConfirmCommand { get; }

    /// <summary>Applies a value change from the backend for the given property.</summary>
    internal void Apply(string prop, JsonElement? value)
    {
        if (prop == "enabled")
        {
            IsEnabled = NormalizeBool(value);
            return;
        }

        if (prop == "text")
        {
            Text = value is { ValueKind: JsonValueKind.String } s ? s.GetString()! : value?.GetRawText() ?? StaticText;
            return;
        }

        if (prop == "value")
        {
            object converted = Type switch
            {
                "Switch" => value is { ValueKind: JsonValueKind.True } or { ValueKind: JsonValueKind.False }
                    ? value.Value.GetBoolean()
                    : NormalizeBool(value),
                "Number" or "Progress" => value is { ValueKind: JsonValueKind.Number } n ? n.GetDouble() : 0d,
                _ => value is { ValueKind: JsonValueKind.String } sv ? sv.GetString() : value?.GetRawText(),
            };

            _isApplyingBackend = true;
            try
            {
                Value = converted;
                if (converted is double number)
                    NumberValue = number;
                if (converted is string str)
                    SelectedValue = str;
            }
            finally
            {
                _isApplyingBackend = false;
            }

            return;
        }

        if (prop == "options")
        {
            // Dynamic Select items: the backend pushes either a JSON array or a
            // JSON-array string. The current selection survives when still present;
            // clearing it bypasses the write-back path (a backend push is not a user edit).
            var items = ParseOptionsValue(value);
            SelectItems = items;
            if (SelectedValue is { } current && !items.Contains(current))
            {
                _isApplyingBackend = true;
                try { SelectedValue = null; }
                finally { _isApplyingBackend = false; }
            }
            return;
        }

        if (prop == "log")
        {
            var entry = value is { ValueKind: JsonValueKind.String } l ? l.GetString() : value?.GetRawText();
            if (entry is not null)
                LogEntries.Add(entry);
        }
    }

    private static IReadOnlyList<string> ParseStaticItems(Dictionary<string, object?>? options)
    {
        if (options is null || !options.TryGetValue("Items", out var items) || items is null)
            return [];
        return items switch
        {
            System.Text.Json.Nodes.JsonArray arr => arr.Select(n => n?.ToString() ?? string.Empty).ToList(),
            IEnumerable<string> strs => strs.ToList(),
            _ => [],
        };
    }

    /// <summary>Normalises a backend "options" push (JSON array or JSON-array string;
    /// scalars inside are rendered via raw text) into the Select item list.</summary>
    private static IReadOnlyList<string> ParseOptionsValue(JsonElement? value)
    {
        if (value is not { } v)
            return [];
        if (v.ValueKind == JsonValueKind.String)
        {
            var text = v.GetString();
            if (string.IsNullOrWhiteSpace(text))
                return [];
            try { v = JsonDocument.Parse(text).RootElement; }
            catch (JsonException) { return []; }
        }
        if (v.ValueKind != JsonValueKind.Array)
            return [];
        return v.EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() ?? string.Empty : e.GetRawText())
            .ToList();
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
