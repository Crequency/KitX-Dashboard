using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
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

        AddHandler(DragDrop.DropEvent, Drop);

        AddHandler(DragDrop.DragOverEvent, DragOver);

    }

    private void Drop(object? sender, DragEventArgs e)
    {
        var location = $"{nameof(RepoPage)}.{nameof(Drop)}";

        var files = e.Data?.GetFileNames()?.ToArray();

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

        // Only allow if the dragged data contains filenames.
        if (!e.Data.Contains(DataFormats.FileNames))
            e.DragEffects = DragDropEffects.None;
    }

}
