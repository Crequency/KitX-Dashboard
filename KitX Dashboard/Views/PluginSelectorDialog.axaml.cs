using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaEdit;
using KitX.Core.Contract.Workflow;
using KitX.Dashboard;

namespace KitX.Dashboard.Views;

public partial class PluginSelectorDialog : Window
{
    public ObservableCollection<PluginSelectableItem> AvailablePlugins { get; } = new();
    public ObservableCollection<PluginSelectableItem> SelectedPlugins { get; } = new();

    public PluginSelectorDialog()
    {
        InitializeComponent();
        LoadPlugins();
        SetupEventHandlers();
    }

    private void LoadPlugins()
    {
        // TODO (后端连通): 从插件服务获取真实插件列表
        // 需要完成:
        // 1. 获取本地已安装插件列表 (IPluginService.GetInstalledPlugins())
        // 2. 获取本地运行中的插件 (IPluginService.GetRunningPlugins())
        // 3. 发现 KitX 网络中远程设备的插件 (IDeviceDiscoveryService.GetRemotePlugins())
        // 4. 根据插件来源设置 Status (LocalRunning/LocalStopped/Remote)
        // 5. 显示远程设备的名称 (DeviceName)
        //
        // 这里是模拟数据，用于演示
        var mockPlugins = new[]
        {
            new PluginSelectableItem { Id = "1", Name = "FileHelper", Description = "文件操作辅助插件", Status = PluginStatus.LocalRunning },
            new PluginSelectableItem { Id = "2", Name = "NetworkScanner", Description = "网络扫描工具", Status = PluginStatus.LocalRunning },
            new PluginSelectableItem { Id = "3", Name = "ImageProcessor", Description = "图片处理工具", Status = PluginStatus.LocalStopped },
            new PluginSelectableItem { Id = "4", Name = "DataSync", Description = "跨设备数据同步", Status = PluginStatus.Remote, DeviceName = "SERVER-2" },
            new PluginSelectableItem { Id = "5", Name = "TaskManager", Description = "任务管理器", Status = PluginStatus.Remote, DeviceName = "LAPTOP-1" },
            new PluginSelectableItem { Id = "6", Name = "NotificationHub", Description = "通知中心", Status = PluginStatus.LocalRunning },
            new PluginSelectableItem { Id = "7", Name = "BackupTool", Description = "备份工具", Status = PluginStatus.LocalStopped },
            new PluginSelectableItem { Id = "8", Name = "ClipboardManager", Description = "剪贴板管理器", Status = PluginStatus.LocalRunning },
        };

        foreach (var plugin in mockPlugins)
        {
            plugin.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(PluginSelectableItem.IsSelected))
                {
                    UpdateSelectedPlugins();
                }
            };
            AvailablePlugins.Add(plugin);
        }

        // 设置数据上下文
        PluginsList.ItemsSource = AvailablePlugins;
        SelectedPluginsList.ItemsSource = SelectedPlugins;
    }

    private void SetupEventHandlers()
    {
        var confirmButton = this.FindControl<Button>("ConfirmButton");
        if (confirmButton != null)
        {
            confirmButton.Click += ConfirmButton_Click;
        }

        var cancelButton = this.FindControl<Button>("CancelButton");
        if (cancelButton != null)
        {
            cancelButton.Click += (s, e) => Close(null);
        }

        var selectAllButton = this.FindControl<Button>("SelectAllButton");
        if (selectAllButton != null)
        {
            selectAllButton.Click += SelectAllButton_Click;
        }

        var clearAllButton = this.FindControl<Button>("ClearAllButton");
        if (clearAllButton != null)
        {
            clearAllButton.Click += ClearAllButton_Click;
        }
    }

    private void UpdateSelectedPlugins()
    {
        SelectedPlugins.Clear();
        foreach (var plugin in AvailablePlugins.Where(p => p.IsSelected))
        {
            SelectedPlugins.Add(plugin);
        }

        var countText = this.FindControl<TextBlock>("SelectedCountText");
        if (countText != null)
        {
            countText.Text = SelectedPlugins.Count.ToString();
        }
    }

    private void ConfirmButton_Click(object? sender, RoutedEventArgs e)
    {
        var selectedPlugins = AvailablePlugins.Where(p => p.IsSelected).ToList();
        Close(selectedPlugins);
    }

    private void SelectAllButton_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var plugin in AvailablePlugins)
        {
            plugin.IsSelected = true;
        }
    }

    private void ClearAllButton_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var plugin in AvailablePlugins)
        {
            plugin.IsSelected = false;
        }
    }
}

public class PluginSelectableItem : INotifyPropertyChanged
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public PluginStatus Status { get; set; }
    public string? DeviceName { get; set; }

    private bool isSelected;
    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected != value)
            {
                isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public enum PluginStatus
{
    LocalRunning,   // 本地运行中
    LocalStopped,  // 本地未运行
    Remote         // 远程设备
}
