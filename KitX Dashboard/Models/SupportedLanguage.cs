using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KitX.Dashboard.Models;

internal class SupportedLanguage : INotifyPropertyChanged
{
    private string languageName = string.Empty;

    internal string LanguageName
    {
        get => languageName;
        set
        {
            if (languageName != value)
            {
                languageName = value;
                OnPropertyChanged();
            }
        }
    }

    internal string LanguageCode { get; set; } = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
