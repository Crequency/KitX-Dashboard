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

    internal Settings_AboutViewModel(IConfigService configService)
    {
        _configService = configService;

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

    public override void InitEvents() { }

    internal string VersionText => $"v{Assembly.GetEntryAssembly()?.GetName().Version}";

    private string _thirdPartyLicenseString = string.Empty;

    internal string ThirdPartyLicenseString
    {
        get => _thirdPartyLicenseString;
        set => this.RaiseAndSetIfChanged(ref _thirdPartyLicenseString, value);
    }

    public bool AboutAreaExpanded
    {
        get => _configService.AppConfig.Pages.Settings.AboutAreaExpanded;
        set
        {
            _configService.AppConfig.Pages.Settings.AboutAreaExpanded = value;
            _configService.SaveAll();
        }
    }

    public bool AuthorsAreaExpanded
    {
        get => _configService.AppConfig.Pages.Settings.AuthorsAreaExpanded;
        set
        {
            _configService.AppConfig.Pages.Settings.AuthorsAreaExpanded = value;
            _configService.SaveAll();
        }
    }

    public bool LinksAreaExpanded
    {
        get => _configService.AppConfig.Pages.Settings.LinksAreaExpanded;
        set
        {
            _configService.AppConfig.Pages.Settings.LinksAreaExpanded = value;
            _configService.SaveAll();
        }
    }

    public bool ThirdPartyLicensesAreaExpanded
    {
        get => _configService.AppConfig.Pages.Settings.ThirdPartyLicensesAreaExpanded;
        set
        {
            _configService.AppConfig.Pages.Settings.ThirdPartyLicensesAreaExpanded = value;
            _configService.SaveAll();
        }
    }

    internal ReactiveCommand<Unit, Unit>? AppNameButtonClickedCommand { get; set; }

    internal ReactiveCommand<Unit, Task>? LoadThirdPartyLicenseCommand { get; set; }
}
