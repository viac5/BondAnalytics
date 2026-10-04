using BondAnalytics.Mobile.ViewModels;

namespace BondAnalytics.Mobile.Pages;

public partial class ChartsPage : ContentPage
{
    public ChartsPage(AppViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
