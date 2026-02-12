using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Annotations;
using OxyPlot.Legends;
using System;
using System.Collections.Generic;
using System.Linq;

namespace App.ViewModels
{
    public class CouponHistogramViewModel
    {
        public PlotModel PlotModel { get; }
        public List<SegmentInfo> Segments { get; } = new List<SegmentInfo>();

        public CouponHistogramViewModel(List<PortfolioItemViewModel> items)
        {
            PlotModel = new PlotModel
            {
                Title = "Накопительная гистограмма купонов"
            };

            PlotModel.Legends.Add(new Legend
            {
                LegendPlacement = LegendPlacement.Outside,
                LegendPosition = LegendPosition.BottomCenter,
                LegendOrientation = LegendOrientation.Horizontal
            });

            // тикер -> Имя бумаги
            var nameByTicker = items
                .GroupBy(i => i.Ticker)
                .ToDictionary(g => g.Key, g => g.First().Name);

            var monthBuckets = new Dictionary<DateTime, Dictionary<string, decimal>>();

            var firstCouponDate = items
                .Where(x => x.NextCouponDate != null)
                .Select(x => x.NextCouponDate.Value.Date)
                .DefaultIfEmpty(DateTime.Today)
                .Min();

            var horizonEnd = firstCouponDate.AddYears(1);

            foreach (var bond in items)
            {
                if (bond.NextCouponDate == null)
                    continue;

                int couponsPerYear = bond.CouponsPerYear;
                if (couponsPerYear <= 0)
                    continue;

                int stepMonths = 12 / couponsPerYear;
                var date = bond.NextCouponDate.Value.Date;

                while (date <= horizonEnd)
                {
                    var monthKey = new DateTime(date.Year, date.Month, 1);

                    if (!monthBuckets.TryGetValue(monthKey, out var dict))
                    {
                        dict = new Dictionary<string, decimal>();
                        monthBuckets[monthKey] = dict;
                    }

                    decimal amount = bond.Coupon * bond.Quantity;

                    if (dict.ContainsKey(bond.Ticker))
                        dict[bond.Ticker] += amount;
                    else
                        dict[bond.Ticker] = amount;

                    date = date.AddMonths(stepMonths);
                }
            }

            if (monthBuckets.Count == 0)
                return;

            var months = monthBuckets.Keys.OrderBy(d => d).ToList();
            var monthLabels = months.Select(m => m.ToString("MMM yyyy")).ToList();

            var tickers = monthBuckets.Values
                .SelectMany(d => d.Keys)
                .Distinct()
                .OrderBy(t => t)
                .ToList();


            var maxMonthSum = monthBuckets.Values
                .Select(bucket => bucket.Values.Sum())
                .DefaultIfEmpty(0m)
                .Max();


            PlotModel.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Minimum = -0.5,
                Maximum = months.Count - 0.5,
                MajorStep = 1,
                MinorStep = 1,
                LabelFormatter = value =>
                {
                    int index = (int)Math.Round(value);
                    return index >= 0 && index < monthLabels.Count
                        ? monthLabels[index]
                        : "";
                }
            });

            PlotModel.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Minimum = 0,
                Maximum = (double)(maxMonthSum * 1.15m),
                Title = "Купоны, ₽"
            });

            var colorMap = new Dictionary<string, OxyColor>();
            int colorIndex = 0;

            OxyColor GetColor(string ticker)
            {
                if (!colorMap.TryGetValue(ticker, out var c))
                {
                    var baseColor = ColorPalette.Colors[colorIndex % ColorPalette.Colors.Count];
                    c = OxyColor.FromAColor(130, baseColor);
                    colorMap[ticker] = c;
                    colorIndex++;
                }
                return c;
            }

            foreach (var ticker in tickers)
            {
                var title = nameByTicker[ticker];

                PlotModel.Series.Add(new OxyPlot.Series.LineSeries
                {
                    Title = title,
                    Color = GetColor(ticker),
                    LineStyle = LineStyle.Solid,
                    StrokeThickness = 6,
                    MarkerType = MarkerType.None
                });
            }

            //рисуем столбцы + сохраняем сегменты

            for (int monthIndex = 0; monthIndex < months.Count; monthIndex++)
            {
                double xCenter = monthIndex;
                double halfWidth = 0.4;
                double x0 = xCenter - halfWidth;
                double x1 = xCenter + halfWidth;

                double currentBottom = 0;

                var bucket = monthBuckets[months[monthIndex]];
                decimal totalMonthSum = bucket.Values.Sum();

                foreach (var ticker in tickers)
                {
                    if (!bucket.TryGetValue(ticker, out var sum) || sum <= 0)
                        continue;

                    var color = GetColor(ticker);
                    var name = nameByTicker[ticker];

                    var rect = new RectangleAnnotation
                    {
                        MinimumX = x0,
                        MaximumX = x1,
                        MinimumY = currentBottom,
                        MaximumY = currentBottom + (double)sum,
                        Fill = color,
                        Stroke = OxyColors.Black,
                        StrokeThickness = 0.5
                    };

                    PlotModel.Annotations.Add(rect);

 
                    Segments.Add(new SegmentInfo
                    {
                        Name = name,
                        Value = sum,
                        Month = months[monthIndex],
                        X0 = x0,
                        X1 = x1,
                        Y0 = currentBottom,
                        Y1 = currentBottom + (double)sum
                    });

                    currentBottom += (double)sum;
                }

                //  Подпись суммы столбца
                PlotModel.Annotations.Add(new TextAnnotation
                {
                    Text = $"{totalMonthSum:N0}",
                    TextColor = OxyColors.Black,
                    Stroke = OxyColors.Transparent,
                    FontSize = 12,
                    TextPosition = new DataPoint(
                        xCenter,
                        (double)totalMonthSum + (double)(maxMonthSum * 0.02m)),
                    TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center,
                    TextVerticalAlignment = OxyPlot.VerticalAlignment.Bottom
                });
            }
        }
    }
}
