using App.ViewModels;
using BondAnalytics.App.Views;
using OxyPlot;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BondAnalytics.App
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private PortfolioValueChartWindow? _portfolioValueChartWindow;

        public MainWindow(MainViewModel vm)
        {
            InitializeComponent();
            _viewModel = vm;
            DataContext = vm;
            vm.PeriodAnalytics.OpenPortfolioChartRequested += OpenPortfolioValueChart;
            Closed += (_, _) =>
            {
                vm.PeriodAnalytics.OpenPortfolioChartRequested -= OpenPortfolioValueChart;
                vm.Dispose();
            };
        }

        private void OpenPortfolioValueChart()
        {
            if (_portfolioValueChartWindow?.IsVisible == true)
            {
                _portfolioValueChartWindow.Activate();
                return;
            }

            _portfolioValueChartWindow = new PortfolioValueChartWindow(_viewModel.PeriodAnalytics)
            {
                Owner = this
            };
            _portfolioValueChartWindow.Closed += (_, _) => _portfolioValueChartWindow = null;
            _portfolioValueChartWindow.Show();
        }

        private void ChartList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            TooltipPanel.Visibility = Visibility.Collapsed;
        }

        private void Plot_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not MainViewModel viewModel ||
                viewModel.CurrentCouponChart == null ||
                Plot.ActualModel is not { DefaultXAxis: not null, DefaultYAxis: not null } model)
            {
                TooltipPanel.Visibility = Visibility.Collapsed;
                return;
            }

            var position = e.GetPosition(Plot);
            var screenPoint = new ScreenPoint(position.X, position.Y);
            if (!model.PlotArea.Contains(screenPoint))
            {
                TooltipPanel.Visibility = Visibility.Collapsed;
                return;
            }

            var dataPoint = model.DefaultXAxis.InverseTransform(screenPoint.X, screenPoint.Y, model.DefaultYAxis);
            var segment = viewModel.CurrentCouponChart.Segments.FirstOrDefault(item =>
                dataPoint.X >= item.X0 && dataPoint.X <= item.X1 &&
                dataPoint.Y >= item.Y0 && dataPoint.Y <= item.Y1);

            if (segment == null)
            {
                TooltipPanel.Visibility = Visibility.Collapsed;
                return;
            }

            TooltipText.Text = $"{segment.Name}\n{segment.Month:MMMM yyyy}\nКупон: {segment.Value:N0} ₽";
            TooltipPanel.Visibility = Visibility.Visible;
            TooltipPanel.Measure(new Size(Plot.ActualWidth, Plot.ActualHeight));

            var left = Math.Clamp(position.X + 12, 8, Math.Max(8, Plot.ActualWidth - TooltipPanel.DesiredSize.Width - 8));
            var top = Math.Clamp(position.Y + 12, 8, Math.Max(8, Plot.ActualHeight - TooltipPanel.DesiredSize.Height - 8));
            TooltipPanel.Margin = new Thickness(left, top, 0, 0);
        }
    }
}