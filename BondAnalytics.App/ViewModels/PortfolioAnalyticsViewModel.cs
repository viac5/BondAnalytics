using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Annotations;
using OxyPlot.Series;
using App.Localization;
using System;
using System.Collections.Generic;
using System.Linq;

namespace App.ViewModels
{
    public class PortfolioAnalyticsViewModel
    {
        public PlotModel PlotModel { get; }

        public PortfolioAnalyticsViewModel(
            IReadOnlyCollection<PortfolioItemViewModel> items,
            string chartKey,
            LocalizationManager localization)
        {
            PlotModel = new PlotModel
            {
                Title = items.Count > 20 && chartKey != "Chart.Allocation"
                    ? $"{localization.Get(chartKey)} · 20"
                    : localization.Get(chartKey),
                Background = OxyColors.White,
                PlotAreaBackground = OxyColors.White,
                TextColor = OxyColor.FromRgb(43, 57, 52),
                PlotAreaBorderColor = OxyColor.FromRgb(220, 227, 222)
            };

            if (chartKey == "Chart.Allocation")
            {
                AddPortfolioStructure(items, localization);
                return;
            }

            Func<PortfolioItemViewModel, decimal>? valueSelector = chartKey switch
            {
                "Chart.Value" => item => item.TotalValue,
                "Chart.Profit" => item => item.Profit,
                "Chart.Yield" => item => item.CurrentYield,
                "Chart.Nkd" => item => item.TotalAccruedInterest,
                "Chart.AnnualCoupons" => item => item.Coupon * item.CouponsPerYear * item.Quantity,
                _ => null
            };

            if (valueSelector == null)
            {
                AddEmptyState(localization.Get("Chart.Prompt"));
                return;
            }

            var chartItems = items
                .Where(item => chartKey != "Chart.Yield" || item.CurrentYield > 0)
                .OrderByDescending(item => Math.Abs(valueSelector(item)))
                .Take(20)
                .Reverse()
                .ToList();

            if (chartItems.Count == 0)
            {
                AddEmptyState(localization.Get("Chart.Empty"));
                return;
            }

            var isProfitChart = chartKey == "Chart.Profit";
            var isYieldChart = chartKey == "Chart.Yield";
            var categories = new CategoryAxis
            {
                Position = AxisPosition.Left,
                TickStyle = TickStyle.None,
                TextColor = OxyColor.FromRgb(83, 97, 92),
                AxislineColor = OxyColor.FromRgb(220, 227, 222),
                GapWidth = 0.35
            };
            var values = new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = isYieldChart ? localization.Get("Chart.Yield") : localization.Get("Common.CurrencyRub"),
                StringFormat = isYieldChart ? "0.0%" : "#,##0",
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromRgb(232, 237, 233),
                MinorGridlineStyle = LineStyle.None,
                AxislineColor = OxyColor.FromRgb(220, 227, 222),
                MinimumPadding = 0.08,
                MaximumPadding = 0.12
            };
            PlotModel.Axes.Add(categories);
            PlotModel.Axes.Add(values);

            var series = new BarSeries
            {
                BarWidth = 0.62,
                StrokeThickness = 0
            };

            foreach (var item in chartItems)
            {
                var value = valueSelector(item);
                categories.Labels.Add(item.Name);
                series.Items.Add(new BarItem
                {
                    Value = (double)value,
                    Color = isProfitChart
                        ? value < 0 ? OxyColor.FromRgb(186, 70, 62) : OxyColor.FromRgb(42, 126, 94)
                        : isYieldChart
                            ? OxyColor.FromRgb(57, 125, 157)
                            : chartKey == "Chart.Nkd"
                                ? OxyColor.FromRgb(205, 139, 61)
                                : OxyColor.FromRgb(42, 126, 94)
                });
            }

            PlotModel.Series.Add(series);
        }

        private void AddPortfolioStructure(IReadOnlyCollection<PortfolioItemViewModel> items, LocalizationManager localization)
        {
            var ofzValue = items.Where(item => item.Group == "ОФЗ").Sum(item => item.TotalValue);
            var corporateValue = items.Where(item => item.Group == "Корпоративные").Sum(item => item.TotalValue);
            var otherValue = items.Where(item => item.Group == "Фонды и прочее").Sum(item => item.TotalValue);
            var series = new PieSeries
            {
                Stroke = OxyColors.White,
                StrokeThickness = 2,
                InsideLabelPosition = 0.72,
                InsideLabelFormat = "{2:0}%",
                OutsideLabelFormat = "{1}: {0:N0}",
                TextColor = OxyColor.FromRgb(43, 57, 52),
                FontSize = 12
            };

            if (ofzValue > 0)
                series.Slices.Add(new PieSlice(localization.Get("Filter.Government"), (double)ofzValue) { Fill = OxyColor.FromRgb(42, 126, 94) });
            if (corporateValue > 0)
                series.Slices.Add(new PieSlice(localization.Get("Filter.Corporate"), (double)corporateValue) { Fill = OxyColor.FromRgb(57, 125, 157) });
            if (otherValue > 0)
                series.Slices.Add(new PieSlice(localization.Get("Filter.Other"), (double)otherValue) { Fill = OxyColor.FromRgb(197, 138, 66) });

            if (series.Slices.Count == 0)
            {
                AddEmptyState(localization.Get("Common.NoData"));
                return;
            }

            PlotModel.Series.Add(series);
        }

        private void AddEmptyState(string message)
        {
            PlotModel.Annotations.Add(new TextAnnotation
            {
                Text = message,
                TextColor = OxyColor.FromRgb(104, 119, 115),
                Stroke = OxyColors.Transparent,
                TextPosition = new DataPoint(0.5, 0.5),
                TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center,
                TextVerticalAlignment = OxyPlot.VerticalAlignment.Middle
            });
        }
    }
}