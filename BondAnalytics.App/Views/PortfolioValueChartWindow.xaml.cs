using App.ViewModels;
using System.Windows;

namespace BondAnalytics.App.Views
{
    public partial class PortfolioValueChartWindow : Window
    {
        public PortfolioValueChartWindow(PortfolioPeriodAnalyticsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}
