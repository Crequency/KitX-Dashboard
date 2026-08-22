// ─────────────────────────────────────────────────────────────────────────────
// PanelControlViewModel Select dynamic-options tests.
//
// The Select control's item list starts from the static config (Options["Items"])
// and is replaced wholesale by backend "options" pushes — the channel
// UiSet(controlId, "options", jsonArray) uses via UiControlStateChangedEvent.
// Covered: initial static items, array/string-array pushes, selection survival,
// clearing a dead selection WITHOUT triggering the front-end write-back path
// (a backend push is not a user edit), and malformed payloads degrading to empty.
// ─────────────────────────────────────────────────────────────────────────────

using System.Text.Json;
using KitX.Dashboard.ViewModels;
using KitX.ToolKit.Contracts;
using KitX.ToolKit.Models;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

public class PanelSelectOptionsTests
{
    private static PanelControlViewModel MakeSelect(
        Dictionary<string, object?>? options = null,
        RecordingPanelRuntime? runtime = null)
    {
        runtime ??= new RecordingPanelRuntime();
        return new PanelControlViewModel(
            new UiControl { Type = "Select", Id = "session", Options = options },
            "inst-1", runtime);
    }

    private static JsonElement? Json(string text)
    {
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void Static_Items_Are_The_Initial_Options()
    {
        var vm = MakeSelect(new Dictionary<string, object?> { ["Items"] = new[] { "a", "b" } });
        Assert.Equal(new[] { "a", "b" }, vm.SelectItems);
    }

    [Fact]
    public void Options_Push_Array_Replaces_Items()
    {
        var vm = MakeSelect(new Dictionary<string, object?> { ["Items"] = new[] { "a" } });
        vm.Apply("options", Json("""["s1","s2","s3"]"""));
        Assert.Equal(new[] { "s1", "s2", "s3" }, vm.SelectItems);
    }

    [Fact]
    public void Options_Push_JsonString_Array_Is_Parsed()
    {
        // Workflows frequently hand the value through as a JSON string.
        var vm = MakeSelect();
        vm.Apply("options", Json("\"[\\\"x\\\",\\\"y\\\"]\""));
        Assert.Equal(new[] { "x", "y" }, vm.SelectItems);
    }

    [Fact]
    public void Options_Push_Keeps_Live_Selection_When_Present()
    {
        var runtime = new RecordingPanelRuntime();
        var vm = MakeSelect(runtime: runtime);

        vm.Apply("value", Json("\"s2\""));
        vm.Apply("options", Json("""["s1","s2"]"""));

        Assert.Equal("s2", vm.SelectedValue);
        Assert.Empty(runtime.WriteBacks); // backend pushes must not echo a write-back
    }

    [Fact]
    public void Options_Push_Clears_Dead_Selection_Without_WriteBack()
    {
        var runtime = new RecordingPanelRuntime();
        var vm = MakeSelect(runtime: runtime);

        vm.Apply("value", Json("\"old\""));
        vm.Apply("options", Json("""["s1","s2"]"""));

        Assert.Null(vm.SelectedValue);
        Assert.Empty(runtime.WriteBacks);
    }

    [Fact]
    public void Options_Push_Malformed_Degrades_To_Empty()
    {
        var vm = MakeSelect(new Dictionary<string, object?> { ["Items"] = new[] { "a" } });
        vm.Apply("options", Json("\"not json[\""));
        Assert.Empty(vm.SelectItems);

        vm.Apply("options", Json("""{"nope":1}"""));
        Assert.Empty(vm.SelectItems);

        vm.Apply("options", null);
        Assert.Empty(vm.SelectItems);
    }

    [Fact]
    public void NonString_Array_Items_Render_As_RawText()
    {
        var vm = MakeSelect();
        vm.Apply("options", Json("""[1, true, "txt"]"""));
        Assert.Equal(new[] { "1", "true", "txt" }, vm.SelectItems);
    }

    private sealed class RecordingPanelRuntime : IPanelRuntime
    {
        public List<(string ControlId, object? Value)> WriteBacks { get; } = [];

        public JsonElement? GetControlValue(string instanceId, string controlId) => null;
        public void SetControlValue(string instanceId, string controlId, object? value)
            => WriteBacks.Add((controlId, value));
        public void RaiseControlEvent(string instanceId, string controlId, string eventName, object? value) { }
        public void RequestPanelOpen(string instanceId) { }
        public IReadOnlyList<string> GetControlLog(string instanceId, string controlId) => [];
    }
}
