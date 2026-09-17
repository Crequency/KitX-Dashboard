using System;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using KitX.Core.Contract.Plugin;
using KitX.Core.Plugin;
using KitX.Dashboard.ViewModels.Pages;
using Serilog;

namespace KitX.Dashboard.Views.Pages;

public partial class RepoPage : UserControl
{
    private readonly RepoPageViewModel viewModel = App.GetService<RepoPageViewModel>();

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

        // Refresh plugin list when page loads. Uses direct synchronous call
        // instead of ReactiveCommand.Execute() which schedules asynchronously
        // and may not complete before the UI renders.
        Loaded += (_, _) => viewModel.PerformRefresh();

        Unloaded += (_, _) => viewModel.Cleanup();
    }

    private void Drop(object? sender, DragEventArgs e)
    {
        const string location = $"{nameof(RepoPage)}.{nameof(Drop)}";

        var files = e.DataTransfer.TryGetFiles()?.Select(x => x.Path.LocalPath).ToArray();

        if (files is not null && files?.Length > 0)
        {
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var pluginService = App.GetService<IPluginService>();
                    foreach (var file in files!)
                    {
                        await pluginService.ImportPluginAsync(file);
                    }

                    Dispatcher.UIThread.Post(() => viewModel.PerformRefresh());
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"In {location}: {ex.Message}");
                }
            });
        }
    }

    private void DragOver(object? sender, DragEventArgs e)
    {
        // Only allow Copy or Link as Drop Operations.
        e.DragEffects &= (DragDropEffects.Copy | DragDropEffects.Link);

        // Only allow if the dragged data's type is file.
        if (!e.DataTransfer.Formats.Contains(DataFormat.File))
            e.DragEffects = DragDropEffects.None;
    }
}
