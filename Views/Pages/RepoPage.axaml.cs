using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using KitX_Dashboard.Managers;
using KitX_Dashboard.ViewModels.Pages;
using Serilog;
using System;
using System.Linq;
using System.Threading;

namespace KitX_Dashboard.Views.Pages;

public partial class RepoPage : UserControl
{
    private readonly RepoPageViewModel viewModel = new();

    public RepoPage()
    {
        InitializeComponent();

        DataContext = viewModel;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);

#pragma warning disable CS8622 // 参数类型中引用类型的为 Null 性与目标委托不匹配(可能是由于为 Null 性特性)。

        AddHandler(DragDrop.DropEvent, Drop);
        AddHandler(DragDrop.DragOverEvent, DragOver);

#pragma warning restore CS8622 // 参数类型中引用类型的为 Null 性与目标委托不匹配(可能是由于为 Null 性特性)。

    }

    private void Drop(object sender, DragEventArgs e)
    {
        string[]? files = e.Data?.GetFileNames()?.ToArray();
        if (files != null && files?.Length > 0)
        {
            new Thread(() =>
            {
                try
                {
                    PluginsManager.ImportPlugin(files, true);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "In RepoPage.Drop()");
                }
            }).Start();
        }
    }

    private void DragOver(object sender, DragEventArgs e)
    {
        // Only allow Copy or Link as Drop Operations.
        e.DragEffects &= (DragDropEffects.Copy | DragDropEffects.Link);

        // Only allow if the dragged data contains filenames.
        if (!e.Data.Contains(DataFormats.FileNames))
            e.DragEffects = DragDropEffects.None;
    }

}

//
// @@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@
// @@@@@@@@@@@@@@@@@@@@@@@'~~~     ~~~`@@@@@@@@@@@@@@@@@@@@@@@@@
// @@@@@@@@@@@@@@@@@@'                     `@@@@@@@@@@@@@@@@@@@@
// @@@@@@@@@@@@@@@'                           `@@@@@@@@@@@@@@@@@
// @@@@@@@@@@@@@'                               `@@@@@@@@@@@@@@@
// @@@@@@@@@@@'                                   `@@@@@@@@@@@@@
// @@@@@@@@@@'                                     `@@@@@@@@@@@@
// @@@@@@@@@'                                       `@@@@@@@@@@@
// @@@@@@@@@                                         @@@@@@@@@@@
// @@@@@@@@'                      n,                 `@@@@@@@@@@
// @@@@@@@@                     _/ | _                @@@@@@@@@@
// @@@@@@@@                    /'  `'/                @@@@@@@@@@
// @@@@@@@@a                 &lt;~    .'                a@@@@@@@@@@
// @@@@@@@@@                 .'    |                 @@@@@@@@@@@
// @@@@@@@@@a              _/      |                a@@@@@@@@@@@
// @@@@@@@@@@a           _/      `.`.              a@@@@@@@@@@@@
// @@@@@@@@@@@a     ____/ '   \__ | |______       a@@@@@@@@@@@@@
// @@@@@@@@@@@@@a__/___/      /__\ \ \     \___.a@@@@@@@@@@@@@@@
// @@@@@@@@@@@@@/  (___.'\_______)\_|_|        \@@@@@@@@@@@@@@@@
// @@@@@@@@@@@@|\________                       ~~~~~\@@@@@@@@@@
//
