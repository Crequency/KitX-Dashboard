using System;
using System.Windows.Input;

namespace KitX_Dashboard.Commands;

public class DelegateCommand : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => CanExecuteFunc is null || CanExecuteFunc(parameter);

    public void Execute(object? parameter) => ExecuteAction?.Invoke(parameter);

    public Action<object?>? ExecuteAction { get; set; }

    public Func<object?, bool>? CanExecuteFunc { get; set; }

    public DelegateCommand()
    {
        CanExecuteChanged += (_, _) => { };
    }

    public DelegateCommand(Action<object?> executeAction) => ExecuteAction = executeAction;
}

//
//          _._._                       _._._
//         _|   |_                     _|   |_
//         | ... |_._._._._._._._._._._| ... |
//         | ||| |  o NATIONAL BANK o  | ||| |
//         | """ |  """    """    """  | """ |
//    ())  |[-|-]| [-|-]  [-|-]  [-|-] |[-|-]|  ())
//   (())) |     |---------------------|     | (()))
//  (())())| """ |  """    """    """  | """ |(())())
//  (()))()|[-|-]|  :::   .-"-.   :::  |[-|-]|(()))()
//  ()))(()|     | |~|~|  |_|_|  |~|~| |     |()))(()
//     ||  |_____|_|_|_|__|_|_|__|_|_|_|_____|  ||
//  ~ ~^^ @@@@@@@@@@@@@@/=======\@@@@@@@@@@@@@@ ^^~ ~
//       ^~^~                                ~^~^
// Sher^
// 
//
