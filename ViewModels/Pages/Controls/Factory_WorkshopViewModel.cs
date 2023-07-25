using Avalonia.Threading;
using KitX.Web.Rules;
using KitX_Dashboard.Managers;
using KitX_Dashboard.Views.Pages.Controls;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive;
using System.Threading.Tasks;
using System.Text.Json;
using System;

namespace KitX_Dashboard.ViewModels.Pages.Controls;

internal class Factory_WorkshopViewModel:ViewModelBase, INotifyPropertyChanged
{
    public new event PropertyChangedEventHandler? PropertyChanged;

    public Factory_WorkshopViewModel()
    {
        InitCommands();

        InitEvents();
    }

    private void InitCommands()
    {

        AddNewWorkshopCommand = ReactiveCommand.Create(() =>
        {
            WorkshopStruct workshopStruct = new();
            workshopStruct.Name = "Test";
            workshopStruct.Version = "1.0.0";
            workshopStruct.DisplayName = new System.Collections.Generic.Dictionary<string, string>
            {
                {"zh-cn","测试车间" }
            };
            workshopStruct.SimpleDescription = new System.Collections.Generic.Dictionary<string, string>
            {
                {"zh-cn","顾名思义，测试用的" }
            };
            Random random = new Random();
            string[] ws_status = { "运行中", "故障", "待机", "已停用" };
            workshopStruct.WorkshopStatus = ws_status[random.Next(ws_status.Length)];

            Dispatcher.UIThread.Post(() =>
            {

                var card = new WorkshopCard(workshopStruct)
                {
                    //IPEndPoint = workshopStruct.Tags["IPEndPoint"]
                };

                Program.WorkshopCards.Add(card);
            });
        });

        StopAllWorkshopCommand = ReactiveCommand.Create(() =>
        {

        });
    }

    private void InitEvents()
    {
        WorkshopCards.CollectionChanged += (_, _) =>
        {
            NoWorkshop_TipHeight = WorkshopCards.Count == 0 ? 300 : 0;
            WorkshopsCount = WorkshopCards.Count.ToString();
        };
    }

    internal string? SearchingText { get; set; }

    internal string workshopsCount = WorkshopCards.Count.ToString();

    internal string WorkshopsCount
    {
        get => workshopsCount;
        set
        {
            workshopsCount = value;

            PropertyChanged?.Invoke(
                this,
                new(nameof(WorkshopsCount))
            );
        }
    }

    internal double noWorkshop_TipHeight = WorkshopCards.Count == 0 ? 300 : 0;

    internal double NoWorkshop_TipHeight
    {
        get => noWorkshop_TipHeight;
        set
        {
            noWorkshop_TipHeight = value;

            PropertyChanged?.Invoke(
                this,
                new(nameof(NoWorkshop_TipHeight))
            );
        }
    }

    internal static ObservableCollection<WorkshopCard> WorkshopCards => Program.WorkshopCards;

    internal ReactiveCommand<Unit, Unit>? AddNewWorkshopCommand { get; set; }

    internal ReactiveCommand<Unit, Unit>? StopAllWorkshopCommand { get; set; }
}
