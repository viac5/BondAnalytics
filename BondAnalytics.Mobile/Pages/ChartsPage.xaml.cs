using BondAnalytics.Mobile.ViewModels;

namespace BondAnalytics.Mobile.Pages;

public partial class ChartsPage : ContentPage
{
    private readonly AppViewModel _viewModel;

    public ChartsPage(AppViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.SetChartsVisible(true);
    }

    protected override void OnDisappearing()
    {
        _viewModel.SetChartsVisible(false);
        base.OnDisappearing();
    }
}
