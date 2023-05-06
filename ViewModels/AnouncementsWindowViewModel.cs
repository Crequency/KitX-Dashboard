using Common.BasicHelper.Utils.Extensions;
using FluentAvalonia.UI.Controls;
using KitX_Dashboard.Commands;
using KitX_Dashboard.Data;
using KitX_Dashboard.Managers;
using KitX_Dashboard.Views;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace KitX_Dashboard.ViewModels;

internal class AnouncementsWindowViewModel : ViewModelBase, INotifyPropertyChanged
{
    public AnouncementsWindowViewModel()
    {
        InitCommands();
    }

    private void InitCommands()
    {
        ConfirmReceivedCommand = new(ConfirmReceived);
        ConfirmReceivedAllCommand = new(ConfirmReceivedAll);
    }

    internal static double Window_Width
    {
        get => ConfigManager.AppConfig.Windows.AnnouncementWindow.Window_Width;
        set => ConfigManager.AppConfig.Windows.AnnouncementWindow.Window_Width = value;
    }

    internal static double Window_Height
    {
        get => ConfigManager.AppConfig.Windows.AnnouncementWindow.Window_Height;
        set => ConfigManager.AppConfig.Windows.AnnouncementWindow.Window_Height = value;
    }

    private NavigationViewItem? selectedMenuItem;

    internal NavigationViewItem? SelectedMenuItem
    {
        get => selectedMenuItem;
        set
        {
            selectedMenuItem = value;

            if (SelectedMenuItem is null) return;

            var key = SelectedMenuItem.Content.ToString();

            if (key is null) return;

            Markdown = Sources[key];

            PropertyChanged?.Invoke(
                this,
                new(nameof(SelectedMenuItem))
            );
        }
    }

    private string markdown = string.Empty;

    internal string Markdown
    {
        get => markdown;
        set
        {
            markdown = value;
            PropertyChanged?.Invoke(this, new(nameof(Markdown)));
        }
    }

    internal List<NavigationViewItem> MenuItems { get; set; } = new();

    private Dictionary<string, string> src = new();

    internal Dictionary<string, string> Sources
    {
        get => src;
        set
        {
            src = value;
            MenuItems.Clear();
            foreach (var item in Sources.Reverse())
            {
                MenuItems.Add(new()
                {
                    Content = item.Key
                });
            }
            SelectedMenuItem = MenuItems.First();
        }
    }

    internal AnouncementsWindow? Window { get; set; }

    internal List<string>? Readed { get; set; }

    /// <summary>
    /// 确认收到命令
    /// </summary>
    internal DelegateCommand? ConfirmReceivedCommand { get; set; }

    /// <summary>
    /// 确认收到命令
    /// </summary>
    internal DelegateCommand? ConfirmReceivedAllCommand { get; set; }

    private async void ConfirmReceived(object? _)
    {
        if (SelectedMenuItem is null || Readed is null) return;

        var key = SelectedMenuItem.Content.ToString();

        if (key is null) return;

        if (!Readed.Contains(key))
            Readed.Add(key);

        var ConfigFilePath = GlobalInfo.AnnouncementsJsonPath.GetFullPath();

        var options = new JsonSerializerOptions()
        {
            WriteIndented = true,
            IncludeFields = true,
        };

        await File.WriteAllTextAsync(
            ConfigFilePath,
            JsonSerializer.Serialize(Readed, options)
        );

        var finded = false;

        foreach (var item in MenuItems)
        {
            if (finded)
            {
                SelectedMenuItem = item;
                break;
            }

            if (item == SelectedMenuItem)
                finded = true;
        }
    }

    private async void ConfirmReceivedAll(object? _)
    {
        if (Readed is null) return;

        foreach (var item in MenuItems)
        {
            var key = item.Content.ToString();

            if (key is null) continue;

            if (!Readed.Contains(key))
                Readed.Add(key);
        }

        var ConfigFilePath = GlobalInfo.AnnouncementsJsonPath.GetFullPath();

        var options = new JsonSerializerOptions()
        {
            WriteIndented = true,
            IncludeFields = true,
        };

        await File.WriteAllTextAsync(
            ConfigFilePath,
            JsonSerializer.Serialize(Readed, options)
        );

        Window?.Close();
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
}

//
//        .         .      /\      .:  *       .          .              .
//                  *    .'  `.      .     .     *      .                  .
//   :             .    /      \  _ .________________  .                    .
//        |            `.+-~~-+.'/.' `.^^^^^^^^\~~~~~\.                      .
//  .    -*-   . .       |u--.|  /     \~~~~~~~|~~~~~|
//        |              |   u|.'       `." "  |" " "|                        .
//     :            .    |.u-./ _..---.._ \" " | " " |
//    -*-            *   |    ~-|U U U U|-~____L_____L_                      .
//     :         .   .   |.-u.| |..---..|"//// ////// /\       .            .
//           .  *        |u   | |       |// /// // ///==\     / \          .
//  .          :         |.--u| |..---..|//////~\////====\   /   \       .
//       .               | u  | |       |~~~~/\u |~~|++++| .`+~~~+'  .
//                       |.-|~U~U~|---..|u u|u | |u ||||||   |  U|
//                    /~~~~/-\---.'     |===|  |u|==|++++|   |   |
//           aaa      |===| _ | ||.---..|u u|u | |u ||HH||U~U~U~U~|        aa@@
//      aaa@@@@@@aa   |===|||||_||      |===|_.|u|_.|+HH+|_/_/_/_/aa    a@@@@@@
//  aa@@@@@@@@@@@@@@a |~~|~~~~\---/~-.._|--.---------.~~~`.__ _.@@@@@@a    ~~~~
//    ~~~~~~    ~~~    \_\\ \  \/~ //\  ~,~|  __   | |`.   :||  ~~~~
//                      a\`| `   _//  | / _| || |  | `.'  ,''|     aa@@@@@@@a
//  aaa   aaaa       a@@@@\| \  //'   |  // \`| |  `.'  .' | |  aa@@@@@@@@@@@@@
// @@@@@a@@@@@@a      ~~~~~ \\`//| | \ \//   \`  .-'  .' | '/      ~~~~~~~  ~~
// @S.C.E.S.W.@@@@a          \// |.`  ` ' /~  :-'   .'|  '/~aa
// ~~~~~~ ~~~~~~         a@@@|   \\ |   // .'    .'| |  |@@@@@@a
//                     a@@@@@@@\   | `| ''.'     .' | ' /@@@@@@@@@a       _
//                                                                      _| |_
//
