namespace KitX.Dashboard.Services;

using System.Collections.Generic;
using TextMateSharp.Themes;

// ─────────────────────────────────────────────────────────────────────────────
// KScriptTheme — wraps the built-in LightPlus/DarkPlus theme and appends colour
// rules for the KScript grammar's scopes (source.ks). The built-in themes only
// carry a handful of token rules (regexp quantifiers, labels, escapes), so
// without this every KS element would fall back to the plain default colour.
// Colours follow the VS Light+/Dark+ palette for familiar scopes.
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class KScriptTheme : IRawTheme
{
    private readonly IRawTheme _base;
    private readonly List<IRawThemeSetting> _tokenColors;

    public KScriptTheme(IRawTheme baseTheme, bool isDark)
    {
        _base = baseTheme;
        _tokenColors = new List<IRawThemeSetting>(baseTheme.GetTokenColors());
        _tokenColors.AddRange(KsRules(isDark));
    }

    public string GetName() => _base.GetName();
    public string GetInclude() => _base.GetInclude();
    public ICollection<IRawThemeSetting> GetSettings() => _base.GetSettings();
    public ICollection<IRawThemeSetting> GetTokenColors() => _tokenColors;
    public ICollection<KeyValuePair<string, object>> GetGuiColors() => _base.GetGuiColors();

    private static IEnumerable<IRawThemeSetting> KsRules(bool isDark) =>
    [
        // Keyword control flow: if/else/switch/forEach/while/break/continue/const/var/as/default
        new KsThemeSetting("keyword.control.ks", isDark ? "#C586C0" : "#AF00DB"),
        // Storage types: int/float/double/bool/string/char/dynamic
        new KsThemeSetting("storage.type.ks", isDark ? "#569CD6" : "#0000FF"),
        // Builtin / helper function names (Compare, Print, PluginCall, CharCodeAt, ...)
        new KsThemeSetting("support.function.ks", isDark ? "#DCDCAA" : "#795E26"),
        // Pipe operator `>` and assignment `=`
        new KsThemeSetting("keyword.operator.pipe.ks", isDark ? "#D4D4D4" : "#267F99"),
        new KsThemeSetting("keyword.operator.assignment.ks", isDark ? "#D4D4D4" : "#000000"),
        // Variables / constants identifiers
        new KsThemeSetting("variable.other.ks", isDark ? "#9CDCFE" : "#001080"),
        // Placeholder `_`
        new KsThemeSetting("variable.parameter.placeholder.ks", isDark ? "#9CDCFE" : "#001080"),
        // Literal constants true/false/null
        new KsThemeSetting("constant.language.ks", isDark ? "#569CD6" : "#0000FF"),
        // Numbers
        new KsThemeSetting("constant.numeric.ks", isDark ? "#B5CEA8" : "#098658"),
        // Strings / chars
        new KsThemeSetting("string.quoted.double.ks", isDark ? "#CE9178" : "#A31515"),
        new KsThemeSetting("string.quoted.single.ks", isDark ? "#CE9178" : "#A31515"),
        // Comments
        new KsThemeSetting("comment.line.double-slash.ks", isDark ? "#6A9955" : "#008000"),
        // Punctuation (braces/parens/commas/colons)
        new KsThemeSetting("punctuation.definition.ks", isDark ? "#D4D4D4" : "#000000"),
    ];
}

/// <summary>A single token-colour rule: one scope selector + foreground colour.</summary>
internal sealed class KsThemeSetting : IRawThemeSetting, IThemeSetting
{
    private readonly string _scope;
    private readonly string _foreground;

    public KsThemeSetting(string scope, string foreground)
    {
        _scope = scope;
        _foreground = foreground;
    }

    public string GetName() => "";
    public object GetScope() => _scope;
    public IThemeSetting GetSetting() => this;
    public object GetFontStyle() => FontStyle.None;
    public string GetBackground() => "";
    public string GetForeground() => _foreground;
}
