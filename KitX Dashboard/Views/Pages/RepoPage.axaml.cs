using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using KitX.Dashboard.Managers;
using KitX.Dashboard.ViewModels.Pages;
using Serilog;
using System;
using System.Linq;
using System.Threading;

namespace KitX.Dashboard.Views.Pages;

public partial class RepoPage : UserControl
{
    private readonly RepoPageViewModel viewModel = new();

    public RepoPage()
    {
        InitializeComponent();

        InitHandlers();

        DataContext = viewModel.SetControl(this);
    }

    private void InitHandlers()
    {
        AddHandler(DragDrop.DropEvent, Drop);

        AddHandler(DragDrop.DragOverEvent, DragOver);
    }

    private void Drop(object? sender, DragEventArgs e)
    {
        var location = $"{nameof(RepoPage)}.{nameof(Drop)}";

        var files = e.Data?.GetFiles()?.Select(x => x.Path.LocalPath).ToArray();

        if (files is not null && files?.Length > 0)
        {
            new Thread(() =>
            {
                try
                {
                    PluginsManager.ImportPlugin(files, true);

                    Dispatcher.UIThread.Post(() => viewModel.RefreshPluginsCommand?.Execute());
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"In {location}: {ex.Message}");
                }
            }).Start();
        }
    }

    private void DragOver(object? sender, DragEventArgs e)
    {
        // Only allow Copy or Link as Drop Operations.
        e.DragEffects &= (DragDropEffects.Copy | DragDropEffects.Link);

        // Only allow if the dragged data's type is file.
        if (!e.Data.Contains(DataFormats.Files))
            e.DragEffects = DragDropEffects.None;
    }

}
