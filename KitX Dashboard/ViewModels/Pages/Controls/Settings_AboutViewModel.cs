using System.Reactive;
using System.Reflection;
using System.Threading.Tasks;
using Common.BasicHelper.IO;
using KitX.Core.Configuration;
using KitX.Core.Contract.Configuration;
using KitX.Dashboard.Views.Pages.Controls;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels.Pages.Controls;

internal class Settings_AboutViewModel : ViewModelBase
{
    private readonly IConfigService _configService;

    internal AppLogo? AppLogo { get; set; }

    internal Settings_AboutViewModel()
    {
        _configService = ConfigService;

        InitCommands();
    }

    public sealed override void InitCommands()
    {
        AppNameButtonClickedCommand = ReactiveCommand.Create(() =>
        {
            if (AppLogo is null)
                return;

            if (AppLogo.IsAnimating)
                AppLogo.StopAnimations();
            else
                AppLogo.InitAnimations();
        });

        LoadThirdPartyLicenseCommand = ReactiveCommand.Create(async () =>
        {
            var license = await FileHelper.ReadAllAsync(ConstantTable.ThirdPartyLicenseFilePath);

            ThirdPartyLicenseString = license;
        });
    }

    public override void InitEvents() => throw new System.NotImplementedException();

    internal static string VersionText => $"v{Assembly.GetEntryAssembly()?.GetName().Version}";

    private string _thirdPartyLicenseString = string.Empty;

    internal string ThirdPartyLicenseString
    {
        get => _thirdPartyLicenseString;
        set => this.RaiseAndSetIfChanged(ref _thirdPartyLicenseString, value);
    }

    public static bool AboutAreaExpanded
    {
        get => App.GetService<IConfigService>().AppConfig.Pages.Settings.AboutAreaExpanded;
        set
        {
            App.GetService<IConfigService>().AppConfig.Pages.Settings.AboutAreaExpanded = value;
            App.GetService<IConfigService>().SaveAll();
        }
    }

    public static bool AuthorsAreaExpanded
    {
        get => App.GetService<IConfigService>().AppConfig.Pages.Settings.AuthorsAreaExpanded;
        set
        {
            App.GetService<IConfigService>().AppConfig.Pages.Settings.AuthorsAreaExpanded = value;
            App.GetService<IConfigService>().SaveAll();
        }
    }

    public static bool LinksAreaExpanded
    {
        get => App.GetService<IConfigService>().AppConfig.Pages.Settings.LinksAreaExpanded;
        set
        {
            App.GetService<IConfigService>().AppConfig.Pages.Settings.LinksAreaExpanded = value;
            App.GetService<IConfigService>().SaveAll();
        }
    }

    public static bool ThirdPartyLicensesAreaExpanded
    {
        get => App.GetService<IConfigService>().AppConfig.Pages.Settings.ThirdPartyLicensesAreaExpanded;
        set
        {
            App.GetService<IConfigService>().AppConfig.Pages.Settings.ThirdPartyLicensesAreaExpanded = value;
            App.GetService<IConfigService>().SaveAll();
        }
    }

    internal ReactiveCommand<Unit, Unit>? AppNameButtonClickedCommand { get; set; }

    internal ReactiveCommand<Unit, Task>? LoadThirdPartyLicenseCommand { get; set; }
}
