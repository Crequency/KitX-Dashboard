using System.Reactive;
using KitX.Dashboard.Views;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels;

internal class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel()
    {
        InitCommands();

        InitEvents();
    }

    public sealed override void InitCommands()
    {
        RefreshGreetingCommand = ReactiveCommand.Create<object?>(mainWindow =>
        {
            var win = mainWindow as MainWindow;

            win?.UpdateGreetingText();
        });
    }

    public sealed override void InitEvents() { }

    internal ReactiveCommand<object?, Unit>? RefreshGreetingCommand { get; set; }
}
