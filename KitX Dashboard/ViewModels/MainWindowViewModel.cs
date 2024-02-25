using KitX.Dashboard.Configuration;
using KitX.Dashboard.Views;
using ReactiveUI;
using System.Reactive;

namespace KitX.Dashboard.ViewModels;

internal class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel()
    {
        InitCommands();

        InitEvents();
    }

    public override void InitCommands()
    {
        RefreshGreetingCommand = ReactiveCommand.Create<object?>(mainWindow =>
        {
            var win = mainWindow as MainWindow;

            win?.UpdateGreetingText();
        });
    }

    public override void InitEvents()
    {

    }

    internal static double Window_Width
    {
        get => AppConfig.Windows.MainWindow.Size.Width!.Value;
        set => AppConfig.Windows.MainWindow.Size.Width = value;
    }

    internal static double Window_Height
    {
        get => AppConfig.Windows.MainWindow.Size.Height!.Value;
        set => AppConfig.Windows.MainWindow.Size.Height = value;
    }

    internal ReactiveCommand<object?, Unit>? RefreshGreetingCommand { get; set; }
}
