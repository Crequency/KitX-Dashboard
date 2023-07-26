using Common.BasicHelper.IO;
using KitX_Dashboard.Managers;
using KitX_Dashboard.Services;
using ReactiveUI;
using System.ComponentModel;
using System.Reactive;
using System.Reflection;
using System.Threading.Tasks;

namespace KitX_Dashboard.ViewModels.Pages.Controls;

internal class Settings_AboutViewModel : ViewModelBase, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    internal int clickCount = 0;

    internal Settings_AboutViewModel()
    {
        InitCommands();
    }

    private void InitCommands()
    {
        AppNameButtonClickedCommand = ReactiveCommand.Create(() => ++clickCount);

        LoadThirdPartyLicenseCommand = ReactiveCommand.Create(async () =>
        {
            var license = await FileHelper.ReadAllAsync(
                Data.GlobalInfo.ThirdPartLicenseFilePath
            );

            ThirdPartyLicenseString = license;
        });
    }

    internal static string VersionText => $"v{Assembly.GetEntryAssembly()?.GetName().Version}";

    internal bool easterEggsFounded = false;

    internal bool EasterEggsFounded
    {
        get => easterEggsFounded;
        set
        {
            easterEggsFounded = value;
            PropertyChanged?.Invoke(
                this,
                new(nameof(EasterEggsFounded))
            );
        }
    }

    private string thirdPartyLicenseString = string.Empty;

    internal string ThirdPartyLicenseString
    {
        get => thirdPartyLicenseString;
        set
        {
            thirdPartyLicenseString = value;
            PropertyChanged?.Invoke(
                this,
                new(nameof(ThirdPartyLicenseString))
            );
        }
    }

    public static bool AboutAreaExpanded
    {
        get => ConfigManager.AppConfig.Pages.Settings.AboutAreaExpanded;
        set
        {
            ConfigManager.AppConfig.Pages.Settings.AboutAreaExpanded = value;
            SaveAppConfigChanges();
        }
    }

    public static bool AuthorsAreaExpanded
    {
        get => ConfigManager.AppConfig.Pages.Settings.AuthorsAreaExpanded;
        set
        {
            ConfigManager.AppConfig.Pages.Settings.AuthorsAreaExpanded = value;
            SaveAppConfigChanges();
        }
    }

    public static bool LinksAreaExpanded
    {
        get => ConfigManager.AppConfig.Pages.Settings.LinksAreaExpanded;
        set
        {
            ConfigManager.AppConfig.Pages.Settings.LinksAreaExpanded = value;
            SaveAppConfigChanges();
        }
    }

    public static bool ThirdPartyLicensesAreaExpanded
    {
        get => ConfigManager.AppConfig.Pages.Settings.ThirdPartyLicensesAreaExpanded;
        set
        {
            ConfigManager.AppConfig.Pages.Settings.ThirdPartyLicensesAreaExpanded = value;
            SaveAppConfigChanges();
        }
    }

    internal ReactiveCommand<Unit, int>? AppNameButtonClickedCommand { get; set; }

    internal ReactiveCommand<Unit, Task>? LoadThirdPartyLicenseCommand { get; set; }
}
