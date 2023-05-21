using Avalonia;
using Avalonia.Controls;
using KitX_Dashboard.Services;
using ReactiveUI;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KitX_Dashboard.ViewModels;

public class ViewModelBase : ReactiveObject
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    protected bool RaiseAndSetIfChanged<T>(
        ref T field,
        T value,
        [CallerMemberName] string propertyName = "")
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

        return false;
    }

    protected static string? FetchStringFromResource(
        Application? app,
        string key,
        string prefix = "",
        string suffix = "",
        string seperator = "")
    {
        if (app is null) return null;

        var res_key = $"{prefix}{seperator}{key}{seperator}{suffix}";

        if (app.TryFindResource(res_key, out var found))
        {
            if (found is string text) return text;
            else return null;
        }
        else return null;
    }

    protected static void SaveAppConfigChanges() => EventService.Invoke(
        nameof(EventService.ConfigSettingsChanged)
    );
}
