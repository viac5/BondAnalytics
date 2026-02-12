using System.Linq;
using System.Windows;
using System.Windows.Input;
using OxyPlot;
using App.ViewModels;

namespace App.Views
{
    public partial class CouponHistogramWindow : Window
    {
        public CouponHistogramWindow()
        {
            InitializeComponent();
        }

        private void Plot_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var vm = DataContext as CouponHistogramViewModel;
            if (vm == null) return;

            var model = Plot.ActualModel;
            if (model == null) return;

            var pos = e.GetPosition(Plot);
            var sp = new ScreenPoint(pos.X, pos.Y);

            if (!model.PlotArea.Contains(sp))
            {
                TooltipPanel.Visibility = Visibility.Collapsed;
                return;
            }

            DataPoint dp = model.DefaultXAxis.InverseTransform(
                sp.X,
                sp.Y,
                model.DefaultYAxis);

            double x = dp.X;
            double y = dp.Y;

            var hit = vm.Segments.FirstOrDefault(s =>
                x >= s.X0 && x <= s.X1 &&
                y >= s.Y0 && y <= s.Y1);

            if (hit != null)
            {
                TooltipText.Text =
                    $"{hit.Name}\n" +
                    $"{hit.Month:MMMM yyyy}\n" +
                    $"Купон: {hit.Value:N0} ₽";

                TooltipPanel.Visibility = Visibility.Visible;
                TooltipPanel.Margin = new Thickness(pos.X + 10, pos.Y + 10, 0, 0);
            }
            else
            {
                TooltipPanel.Visibility = Visibility.Collapsed;
            }
        }
    }
}
