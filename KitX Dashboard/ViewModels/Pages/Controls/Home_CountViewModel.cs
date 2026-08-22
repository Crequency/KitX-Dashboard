using System;
using System.Collections.Generic;
using System.Linq;
using KitX.Core.Contract.Configuration;
using KitX.Core.Contract.Event;
using KitX.Core.Statistics;
using KitX.Dashboard;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using ReactiveUI;

namespace KitX.Dashboard.ViewModels.Pages.Controls;

internal class Home_CountViewModel : ViewModelBase
{
    private readonly IConfigService _configService;
    private readonly IEventService _eventService;

    public Home_CountViewModel(IConfigService configService, IEventService eventService)
    {
        _configService = configService;
        _eventService = eventService;

        RecoveryUseCount();

        InitEvents();

        NoCount_TipHeight = Use_Series.Length == 0 ? 200 : 0;
    }

    public override void InitCommands() { }

    public sealed override void InitEvents()
    {
        _useStatisticsChangedHandler = (s, e) => RecoveryUseCount();
        _eventService.Subscribe(EventNames.UseStatisticsChanged, _useStatisticsChangedHandler);
    }

    /// <summary>Named handler so <see cref="Dispose"/> can unsubscribe it (D11).</summary>
    private EventHandler<EventArgs>? _useStatisticsChangedHandler;

    /// <summary>Unsubscribes event handlers to avoid leaks when the page is unloaded.</summary>
    public void Dispose()
    {
        if (_useStatisticsChangedHandler is not null)
            _eventService.Unsubscribe(EventNames.UseStatisticsChanged, _useStatisticsChangedHandler);
        _useStatisticsChangedHandler = null;
    }

    internal void RecoveryUseCount()
    {
        // D10 note: IStatisticsService.GetUsageStatistics() exists but returns DateTime
        // keys + a date range filter, while the chart consumes the raw "yyyy.MM.dd"
        // string keys (X labels). Kept on the static accessor to preserve label format.
        var use = StatisticsManager.UseStatistics;

        Use_XAxes = [new Axis { Labels = use?.Keys.ToList() }];

        Use_Series =
        [
            new LineSeries<double>
            {
                Values = use?.Values.ToArray(),
                Fill = null,
                XToolTipLabelFormatter = x => $"{use?.Keys.ToArray()[(int)x.Coordinate.SecondaryValue]}: {x.Coordinate.PrimaryValue} h",
            },
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
        get => _configService.AppConfig.Pages.Home.UseAreaExpanded;
        set
        {
            _configService.AppConfig.Pages.Home.UseAreaExpanded = value;

            this.RaisePropertyChanged(nameof(UseAreaExpanded));

            _configService.SaveAll();
        }
    }

    private ISeries[] useSeries = [new LineSeries<double> { Values = new double[] { 2, 1, 3, 5, 3, 4, 6 }, Fill = null }];

    public ISeries[] Use_Series
    {
        get => useSeries;
        set => this.RaiseAndSetIfChanged(ref useSeries, value);
    }

    private List<Axis> use_xAxes = [new Axis { Labeler = Labelers.Default }];

    public List<Axis> Use_XAxes
    {
        get => use_xAxes;
        set => this.RaiseAndSetIfChanged(ref use_xAxes, value);
    }

    private List<Axis> use_yAxes = [new Axis { Labeler = (value) => $"{value} h" }];

    public List<Axis> Use_YAxes
    {
        get => use_yAxes;
        set => this.RaiseAndSetIfChanged(ref use_yAxes, value);
    }
}
