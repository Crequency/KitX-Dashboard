using System.Reactive;
using System.Reflection;
using System.Threading.Tasks;
using Common.BasicHelper.IO;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Views.Pages.Controls;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels.Pages.Controls;

internal class Settings_AboutViewModel : ViewModelBase
{
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
        set => this.RaiseAndSetIfChanged(ref thirdPartyLicenseString, value);
    }

    public static bool AboutAreaExpanded
    {
        get => ConfigManager.Instance.AppConfig.Pages.Settings.AboutAreaExpanded;
        set
        {
            ConfigManager.Instance.AppConfig.Pages.Settings.AboutAreaExpanded = value;
            SaveAppConfigChanges();
        }
    }

    public static bool AuthorsAreaExpanded
    {
        get => ConfigManager.Instance.AppConfig.Pages.Settings.AuthorsAreaExpanded;
        set
        {
            ConfigManager.Instance.AppConfig.Pages.Settings.AuthorsAreaExpanded = value;
            SaveAppConfigChanges();
        }
    }

    public static bool LinksAreaExpanded
    {
        get => ConfigManager.Instance.AppConfig.Pages.Settings.LinksAreaExpanded;
        set
        {
            ConfigManager.Instance.AppConfig.Pages.Settings.LinksAreaExpanded = value;
            SaveAppConfigChanges();
        }
    }

    public static bool ThirdPartyLicensesAreaExpanded
    {
        get => ConfigManager.Instance.AppConfig.Pages.Settings.ThirdPartyLicensesAreaExpanded;
        set
        {
            ConfigManager.Instance.AppConfig.Pages.Settings.ThirdPartyLicensesAreaExpanded = value;
            SaveAppConfigChanges();
        }
    }

    internal ReactiveCommand<Unit, Unit>? AppNameButtonClickedCommand { get; set; }

    internal ReactiveCommand<Unit, Task>? LoadThirdPartyLicenseCommand { get; set; }
}
