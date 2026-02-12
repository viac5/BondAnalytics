using System.Collections.Generic;
using System.Windows.Input;
using App.Views;

namespace App.ViewModels
{
    public class ChartsViewModel
    {
        private readonly List<PortfolioItemViewModel> _items;

        public List<string> AvailableCharts { get; } =
            new() { "Накопительная гистограмма купонов" };

        public string SelectedChart { get; set; }

        public ICommand BuildChartCommand { get; }

        public ChartsViewModel(List<PortfolioItemViewModel> items)
        {
            _items = items;
            BuildChartCommand = new RelayCommand(_ => BuildChart());
        }

        private void BuildChart()
        {
            var wnd = new CouponHistogramWindow
            {
                DataContext = new CouponHistogramViewModel(_items)
            };
            wnd.Show();
        }
    }
}
