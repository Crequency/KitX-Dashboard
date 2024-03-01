using System.Collections.Generic;
using System.Linq;
using KitX.Dashboard.Managers;
using KitX.Dashboard.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels.Pages.Controls;

internal class Home_CountViewModel : ViewModelBase
{
    public Home_CountViewModel()
    {
        RecoveryUseCount();

        InitEvents();

        NoCount_TipHeight = Use_Series.Length == 0 ? 200 : 0;
    }

    public override void InitCommands() => throw new System.NotImplementedException();

    public override void InitEvents()
    {
        EventService.UseStatisticsChanged += RecoveryUseCount;
    }

    internal void RecoveryUseCount()
    {
        var use = StatisticsManager.UseStatistics;

        Use_XAxes =
        [
            new Axis
            {
                Labels = use?.Keys.ToList()
            }
        ];

        Use_Series =
        [
            new LineSeries<double>
            {
                Values = use?.Values.ToArray(),
                Fill = null,
                XToolTipLabelFormatter = x => $"{use?.Keys.ToArray()[(int)x.Coordinate.SecondaryValue]}: {x.Coordinate.PrimaryValue} h"
            }
        ];
    }

    private double noCount_TipHeight = 200;

    internal double NoCount_TipHeight
    {
        get => noCount_TipHeight;
        set => this.RaiseAndSetIfChanged(ref noCount_TipHeight, value);
    }

    internal bool UseAreaExpanded
    {
        get => ConfigManager.Instance.AppConfig.Pages.Home.UseAreaExpanded;
        set
        {
            ConfigManager.Instance.AppConfig.Pages.Home.UseAreaExpanded = value;

            this.RaisePropertyChanged(nameof(UseAreaExpanded));

            SaveAppConfigChanges();
        }
    }

    private ISeries[] useSeries =
    [
        new LineSeries<double>
        {
            Values = new double[] { 2, 1, 3, 5, 3, 4, 6 },
            Fill = null
        }
    ];

    public ISeries[] Use_Series
    {
        get => useSeries;
        set => this.RaiseAndSetIfChanged(ref useSeries, value);
    }

    private List<Axis> use_xAxes =
    [
        new Axis
        {
            Labeler = Labelers.Default
        }
    ];

    public List<Axis> Use_XAxes
    {
        get => use_xAxes;
        set => this.RaiseAndSetIfChanged(ref use_xAxes, value);
    }

    private List<Axis> use_yAxes =
    [
        new Axis
        {
            Labeler = (value) => $"{value} h"
        }
    ];

    public List<Axis> Use_YAxes
    {
        get => use_yAxes;
        set => this.RaiseAndSetIfChanged(ref use_yAxes, value);
    }
}
