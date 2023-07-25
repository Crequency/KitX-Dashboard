using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using KitX.Web.Rules;
using KitX_Dashboard.ViewModels.Pages.Controls;

namespace KitX_Dashboard.Views.Pages.Controls;

public partial class WorkshopCard : UserControl
{
    private readonly WorkshopCardViewModel viewModel = new();

    //internal string? IPEndPoint { get; set; }

    public WorkshopCard()
    {
        InitializeComponent();

        DataContext = viewModel;
    }

    public WorkshopCard(WorkshopStruct ws)
    {
        InitializeComponent();

        viewModel.workshopStruct = ws;

        DataContext = viewModel;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
