using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using KitX_Dashboard.ViewModels.Pages;
using System.Threading;
using Serilog;
using System;
using KitX_Dashboard.Services;
using System.Linq;

namespace KitX_Dashboard.Views.Pages
{
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

            AddHandler(DragDrop.DropEvent, Drop);
            AddHandler(DragDrop.DragOverEvent, DragOver);
        }

        private void DragOver(object sender, DragEventArgs e)
        {
            // Only allow Copy or Link as Drop Operations.
            e.DragEffects = e.DragEffects & (DragDropEffects.Copy | DragDropEffects.Link);

            // Only allow if the dragged data contains filenames.
            if (!e.Data.Contains(DataFormats.FileNames))
                e.DragEffects = DragDropEffects.None;
        }

        private void Drop(object sender, DragEventArgs e)
        {
            string[]? files = e.Data.GetFileNames().ToArray();
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
                        Log.Error("In RepoPage.Drop()", ex);
                    }
                }).Start();
            }
        }

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
