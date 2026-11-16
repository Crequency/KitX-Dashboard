using System;
using System.Globalization;
using Avalonia.Data.Converters;
using KitX.Dashboard.ViewModels;
using KitX.ToolKit.Contract.Surface;

namespace KitX.Dashboard.Converters;

/// <summary>
/// Draws the word the reader uses for an element kind, keeping the kind's own name as the fallback.
/// </summary>
/// <remarks>
/// <para><b>Why a converter and not a translated collection.</b> <see cref="SurfaceEditorViewModel.PaletteKinds"/>
/// and <see cref="SurfaceEditorViewModel.ElementTypes"/> hold the strings the editor compares against —
/// <c>AddKindCommand</c> takes its parameter from the palette, <c>ChangeType</c> switches on the
/// dropdown's value, and the schema's own name is the last one. Those have to stay
/// <c>"Button"</c>/<c>"Input"</c>; a collection that swapped to "按钮" would still be compared as if
/// it were the schema's word, and would additionally be frozen at whatever language was loaded when
/// it was first read. Translating at the point of display keeps one source of truth for the value
/// and one for the word.</para>
/// <para><b>Why the fallback is a <c>Translate</c> miss and not emptiness.</b> A client that has no
/// resource yet — the file case, a debug session, a language not shipped — must still draw something
/// a person can match against <c>surface.json</c>, and the schema word is exactly that. An empty
/// corner label would hide the kind entirely.</para>
/// <para><b>Why <see cref="SurfaceElementKind.Unknown"/> is not short-circuited.</b> Until this
/// round the switch had a <c>SurfaceElementKind.Unknown =&gt; value</c> arm above the general one,
/// which read as a policy — "the word for an unknown kind is not a word, so leave it alone" — and
/// was in practice a dead branch: every element kind is a sealed record and
/// <see cref="SurfaceElementKind.Of"/> covers all seven, so nothing produced that value. What it
/// actually did was guarantee that the one kind whose name most needs a translation would be the
/// one kind that never got one, because the dictionary was never asked. Removing it costs nothing
/// today and makes <c>Text_Bench_Surface_Kind_element</c> reachable the day an eighth kind arrives
/// without <c>Of</c> being updated — which is the case the constant exists for.</para>
/// </remarks>
public sealed class SurfaceKindNameConverter : IValueConverter
{
    /// <summary>The shared instance; the converters here are stateless, so one will do.</summary>
    public static readonly SurfaceKindNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            null or string { Length: 0 } => string.Empty,
            string kind => ViewModelBase.Translate(SurfaceElementKind.NameKeyOf(kind)) ?? kind,
            SurfaceElement element => ViewModelBase.Translate(SurfaceElementKind.NameKeyOf(element)) ??
                                      SurfaceElementKind.Of(element),
            _ => value,
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
