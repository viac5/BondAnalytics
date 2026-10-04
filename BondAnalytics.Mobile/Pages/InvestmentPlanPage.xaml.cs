using BondAnalytics.Mobile.ViewModels;

namespace BondAnalytics.Mobile.Pages;

public partial class InvestmentPlanPage : ContentPage
{
    private readonly InvestmentPlanViewModel _viewModel;
    private bool _loaded;

    public InvestmentPlanPage(InvestmentPlanViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded)
            return;
        _loaded = true;
        await _viewModel.LoadAsync();
    }

    private async void OnRecalculateClicked(object? sender, EventArgs e) =>
        await _viewModel.RecalculateAsync();

    private async void OnSaveClicked(object? sender, EventArgs e) =>
        await _viewModel.SaveAsync();
}
