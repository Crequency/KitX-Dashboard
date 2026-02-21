using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KitX.Dashboard.Models;

internal class SupportedTheme : INotifyPropertyChanged
{
    private string themeDisplayName = string.Empty;

    internal string ThemeName { get; set; } = string.Empty;

    internal string ThemeDisplayName
    {
        get => themeDisplayName;
        set
        {
            if (themeDisplayName != value)
            {
                themeDisplayName = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
