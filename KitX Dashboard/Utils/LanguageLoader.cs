using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Common.BasicHelper.Utils.Extensions;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Event;
using Serilog;

namespace KitX.Dashboard.Utils;

/// <summary>
/// Loads a language resource dictionary from disk.
/// <para>
/// D6 convergence: previously duplicated in App.LoadLanguage and
/// Settings_PersonaliseViewModel.LoadLanguage — both now call <see cref="LoadLanguage"/>.
/// </para>
/// <para>
/// D16: language file names are validated against the whitelist of configured
/// <c>SurpportLanguages</c> keys plus a syntax check (no separators / rooted paths),
/// and the loader falls back to the first configured language on any failure.
/// </para>
/// </summary>
internal static class LanguageLoader
{
    private const string FallbackLanguage = "en-us";

    /// <summary>
    /// Loads the configured language dictionary into the app's merged resources and
    /// publishes <see cref="EventNames.LanguageChanged"/>. Falls back to the first
    /// configured supported language when the selected one is missing or invalid.
    /// </summary>
    internal static void LoadLanguage()
    {
        const string location = $"{nameof(LanguageLoader)}.{nameof(LoadLanguage)}";

        var configService = App.GetService<IConfigService>();
        var config = configService.AppConfig;
        var lang = config.App.AppLanguage;
        var backupLang = config.App.SurpportLanguages.Keys.FirstOrDefault() ?? FallbackLanguage;

        if (Application.Current is null)
            return;

        // D16: only configured language keys are acceptable file names.
        if (!config.App.SurpportLanguages.ContainsKey(lang))
            lang = backupLang;

        App.AppLanguage = lang;

        try
        {
            Application.Current.Resources.MergedDictionaries.Clear();

            Application.Current.Resources.MergedDictionaries.Add(LoadDictionary(lang));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, $"Language File {lang}.axaml not found.");

            Application.Current.Resources.MergedDictionaries.Clear();

            try
            {
                Application.Current.Resources.MergedDictionaries.Add(LoadDictionary(backupLang));

                config.App.AppLanguage = backupLang;
                App.AppLanguage = backupLang;
            }
            catch (Exception e)
            {
                Log.Warning(e, $"Suspected absence of language files on record.");
            }
            finally
            {
                Log.Warning($"No supported language file loaded.");
            }
        }

        try
        {
            var eventService = App.GetService<IEventService>();
            eventService.Publish(EventNames.LanguageChanged, EventArgs.Empty);
        }
        catch (Exception e)
        {
            Log.Warning(e, $"Failed to invoke language changed event.");
        }
    }

    /// <summary>
    /// Loads one language file from the fixed language directory.
    /// The file name is validated before any path construction (D16).
    /// </summary>
    private static ResourceDictionary LoadDictionary(string langName)
    {
        if (!IsValidLanguageFileName(langName))
            throw new ArgumentException($"Invalid language file name: {langName}");

        var path = $"{ConstantTable.LanguageFilePath}/{langName}.axaml".GetFullPath();

        if (!File.Exists(path))
            throw new FileNotFoundException($"Language file not found: {path}");

        return AvaloniaRuntimeXamlLoader.Load(File.ReadAllText(path)) as ResourceDictionary ?? [];
    }

    /// <summary>
    /// Rejects anything that could escape the language directory (D16): path
    /// separators, rooted paths, traversal sequences and invalid file name chars.
    /// </summary>
    private static bool IsValidLanguageFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (name.Contains('/') || name.Contains('\\') || name.Contains(':'))
            return false;

        if (name.Contains(".."))
            return false;

        if (Path.IsPathRooted(name))
            return false;

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return false;

        return true;
    }
}
