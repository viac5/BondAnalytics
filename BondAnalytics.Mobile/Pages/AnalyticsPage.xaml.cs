using BondAnalytics.Mobile.ViewModels;

namespace BondAnalytics.Mobile.Pages;

public partial class AnalyticsPage : ContentPage
{
    private readonly AnalyticsViewModel _viewModel;

    public AnalyticsPage(AnalyticsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_viewModel.Operations.Count == 0 && !_viewModel.IsBusy)
            await _viewModel.LoadAsync();
    }

    private void OnPresetChanged(object? sender, EventArgs e)
    {
        if (sender is Picker { SelectedIndex: >= 0 } picker && picker.SelectedIndex != 3)
            _viewModel.ApplyPreset(picker.SelectedIndex);
    }

    private async void OnLoadClicked(object? sender, EventArgs e) => await _viewModel.LoadAsync();
}
