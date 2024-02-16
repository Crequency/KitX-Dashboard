using Common.BasicHelper.IO;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Views.Pages.Controls;
using ReactiveUI;
using System.ComponentModel;
using System.Reactive;
using System.Reflection;
using System.Threading.Tasks;

namespace KitX.Dashboard.ViewModels.Pages.Controls;

internal class Settings_AboutViewModel : ViewModelBase, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    internal AppLogo? AppLogo { get; set; }

    internal Settings_AboutViewModel()
    {
        InitCommands();
    }

    public override void InitCommands()
    {
        AppNameButtonClickedCommand = ReactiveCommand.Create(
            () =>
            {
                if (AppLogo is not null)
                {
                    if (AppLogo.IsAnimating)
                        AppLogo.StopAnimations();
                    else
                        AppLogo.InitAnimations();
                }
            }
        );

        LoadThirdPartyLicenseCommand = ReactiveCommand.Create(async () =>
        {
            var license = await FileHelper.ReadAllAsync(
                ConstantTable.ThirdPartLicenseFilePath
            );

            ThirdPartyLicenseString = license;
        });
    }

    public override void InitEvents() => throw new System.NotImplementedException();

    internal static string VersionText => $"v{Assembly.GetEntryAssembly()?.GetName().Version}";

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
        get => Instances.ConfigManager.AppConfig.Pages.Settings.AboutAreaExpanded;
        set
        {
            Instances.ConfigManager.AppConfig.Pages.Settings.AboutAreaExpanded = value;
            SaveAppConfigChanges();
        }
    }

    public static bool AuthorsAreaExpanded
    {
        get => Instances.ConfigManager.AppConfig.Pages.Settings.AuthorsAreaExpanded;
        set
        {
            Instances.ConfigManager.AppConfig.Pages.Settings.AuthorsAreaExpanded = value;
            SaveAppConfigChanges();
        }
    }

    public static bool LinksAreaExpanded
    {
        get => Instances.ConfigManager.AppConfig.Pages.Settings.LinksAreaExpanded;
        set
        {
            Instances.ConfigManager.AppConfig.Pages.Settings.LinksAreaExpanded = value;
            SaveAppConfigChanges();
        }
    }

    public static bool ThirdPartyLicensesAreaExpanded
    {
        get => Instances.ConfigManager.AppConfig.Pages.Settings.ThirdPartyLicensesAreaExpanded;
        set
        {
            Instances.ConfigManager.AppConfig.Pages.Settings.ThirdPartyLicensesAreaExpanded = value;
            SaveAppConfigChanges();
        }
    }

    internal ReactiveCommand<Unit, Unit>? AppNameButtonClickedCommand { get; set; }

    internal ReactiveCommand<Unit, Task>? LoadThirdPartyLicenseCommand { get; set; }
}
