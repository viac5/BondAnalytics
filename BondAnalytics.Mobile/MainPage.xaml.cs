namespace BondAnalytics.Mobile;

public partial class MainPage : ContentPage
{
    private readonly ViewModels.AppViewModel _viewModel;

    public MainPage(ViewModels.AppViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_viewModel.Positions.Count == 0 && !_viewModel.IsBusy)
            await _viewModel.RefreshPortfolioAsync();
    }

    private async void OnRefreshClicked(object? sender, EventArgs e) =>
        await _viewModel.RefreshPortfolioAsync();
}
