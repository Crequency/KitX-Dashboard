using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using KitX.Dashboard.Converters;
using Xunit;

namespace KitX.Dashboard.Test.Xunit;

/// <summary>
/// Tests for the language resolution used by <see cref="PluginMultiLanguagePropertyConverter"/>.
/// <para>
/// Architecture #2 moved the UI language out of the retired <c>App.AppLanguage</c> static into
/// the resource tree: each <c>{lang}.axaml</c> now carries a <c>CurrentLanguage</c> key, and the
/// converter resolves it via <see cref="PluginMultiLanguagePropertyConverter.CurrentLanguageCode"/>.
/// These tests exercise that resolution directly (isolated hosts) and through <c>Convert</c> on the
/// headless <see cref="Application.Current"/>.
/// </para>
/// </summary>
public class PluginInfoConvertersTests
{
    [AvaloniaFact]
    public void CurrentLanguageCode_NullHost_FallsBackToEnUs()
    {
        Assert.Equal("en-us", PluginMultiLanguagePropertyConverter.CurrentLanguageCode(null));
    }

    [AvaloniaFact]
    public void CurrentLanguageCode_MissingResource_FallsBackToEnUs()
    {
        // A host with no CurrentLanguage resource behaves like a headless run with no app.
        var host = new TextBlock();
        Assert.Equal("en-us", PluginMultiLanguagePropertyConverter.CurrentLanguageCode(host));
    }

    [AvaloniaFact]
    public void CurrentLanguageCode_FoundResource_ReturnsLanguageCode()
    {
        var host = new TextBlock();
        host.Resources["CurrentLanguage"] = "zh-cn";

        Assert.Equal("zh-cn", PluginMultiLanguagePropertyConverter.CurrentLanguageCode(host));
    }

    [AvaloniaFact]
    public void Convert_Reads_Language_From_Active_ResourceDictionary()
    {
        var converter = new PluginMultiLanguagePropertyConverter();
        var dict = new Dictionary<string, string>
        {
            ["zh-cn"] = "你好",
            ["en-us"] = "Hello",
        };

        // With CurrentLanguage in the active app resources the converter picks the zh-cn value.
        Application.Current!.Resources["CurrentLanguage"] = "zh-cn";
        Assert.Equal("你好", converter.Convert(dict, typeof(string), null, CultureInfo.InvariantCulture));

        // Once the resource is absent the converter falls back to en-us.
        Application.Current.Resources.Remove("CurrentLanguage");
        Assert.Equal("Hello", converter.Convert(dict, typeof(string), null, CultureInfo.InvariantCulture));

        // Leave the shared resources clean for the rest of the suite.
        Application.Current.Resources.Remove("CurrentLanguage");
    }
}
