using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Annotations;
using OxyPlot.Series;
using App.Localization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Media;

namespace App.ViewModels
{
    public class CouponHistogramViewModel
    {
        public PlotModel PlotModel { get; }
        public List<SegmentInfo> Segments { get; } = new List<SegmentInfo>();
        public List<CouponLegendItem> LegendItems { get; } = new();

        public CouponHistogramViewModel(List<PortfolioItemViewModel> items, LocalizationManager localization)
        {
            PlotModel = new PlotModel
            {
                Title = localization.Get("Chart.CouponTitle"),
                Background = OxyColors.White,
                PlotAreaBackground = OxyColors.White,
                TextColor = OxyColor.FromRgb(43, 57, 52),
                PlotAreaBorderColor = OxyColor.FromRgb(220, 227, 222)
            };

            // тикер -> Имя бумаги
            var nameByTicker = items
                .GroupBy(i => i.Ticker)
                .ToDictionary(g => g.Key, g => g.First().Name);

            var monthBuckets = new Dictionary<DateTime, Dictionary<string, decimal>>();

            var horizonEnd = DateTime.Today.AddYears(1);

            foreach (var bond in items)
            {
                if (bond.NextCouponDate == null)
                    continue;

                int couponsPerYear = bond.CouponsPerYear;
                if (couponsPerYear <= 0)
                    continue;

                // защитимся от целочисленного деления, которое может дать 0
                int stepMonths = 12 / couponsPerYear;
                if (stepMonths <= 0)
                    stepMonths = 1;

                var date = bond.NextCouponDate.Value.Date;

                // safety: ограничение числа итераций для предотвращения бесконечного цикла при некорректных данных
                int safety = 0;
                const int MaxIterations = 1000;

                while (date <= horizonEnd)
                {
                    if (++safety > MaxIterations)
                    {
                        // Добавляем диагностическую метку в график и прерываем цикл для этой бумаги
                        PlotModel.Annotations.Add(new TextAnnotation
                        {
                            Text = $"Предупреждение: некорректные данные у бумаги {bond.Ticker}, остановлено добавление купонов.",
                            TextColor = OxyColors.OrangeRed,
                            Stroke = OxyColors.Transparent,
                            FontSize = 12,
                            TextPosition = new DataPoint(0.5, 0.5),
                            TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center,
                            TextVerticalAlignment = OxyPlot.VerticalAlignment.Middle
                        });
                        break;
                    }

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
            {
                PlotModel.Axes.Add(new LinearAxis
                {
                    Position = AxisPosition.Left,
                    Minimum = 0,
                    Maximum = 1,
                    Title = "Выплаты, ₽"
                });

                PlotModel.Axes.Add(new CategoryAxis
                {
                    Position = AxisPosition.Bottom
                });

                PlotModel.Annotations.Add(new TextAnnotation
                {
                          Text = "Нет ожидаемых купонных выплат в ближайшие 12 месяцев",
                          TextColor = OxyColor.FromRgb(104, 119, 115),
                    Stroke = OxyColors.Transparent,
                    FontSize = 14,
                    TextPosition = new DataPoint(0.5, 0.5),
                    TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center,
                    TextVerticalAlignment = OxyPlot.VerticalAlignment.Middle
                });

                return;
            }

            var months = monthBuckets.Keys.OrderBy(d => d).ToList();
            var monthLabels = months.Select(m => m.ToString("MMM yy", CultureInfo.GetCultureInfo("ru-RU"))).ToList();

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
                    var index = (int)Math.Round(value);
                    return index >= 0 && index < monthLabels.Count ? monthLabels[index] : string.Empty;
                }
            });

            PlotModel.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Minimum = 0,
                Maximum = Math.Max(1, (double)(maxMonthSum * 1.15m)),
                Title = "Выплаты, ₽",
                StringFormat = "#,##0",
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromRgb(232, 237, 233)
            });

            var colorMap = new Dictionary<string, OxyColor>();
            int colorIndex = 0;

            OxyColor GetColor(string ticker)
            {
                if (!colorMap.TryGetValue(ticker, out var c))
                {
                    var baseColor = ColorPalette.Colors[colorIndex % ColorPalette.Colors.Count];
                    c = OxyColor.FromAColor(255, baseColor);
                    colorMap[ticker] = c;
                    colorIndex++;
                }
                return c;
            }

            var tickerTotals = tickers
                .Select(ticker => new
                {
                    Ticker = ticker,
                    Total = monthBuckets.Values.Sum(bucket => bucket.GetValueOrDefault(ticker))
                })
                .OrderByDescending(item => item.Total)
                .Take(14);

            foreach (var item in tickerTotals)
            {
                var color = GetColor(item.Ticker);
                LegendItems.Add(new CouponLegendItem(
                    nameByTicker[item.Ticker],
                    new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B))));
            }

            for (int monthIndex = 0; monthIndex < months.Count; monthIndex++)
            {
                var month = months[monthIndex];
                var currentBottom = 0d;
                var totalMonthSum = monthBuckets[month].Values.Sum();

                foreach (var ticker in tickers)
                {
                    var amount = monthBuckets[month].GetValueOrDefault(ticker);
                    if (amount <= 0)
                        continue;

                    PlotModel.Annotations.Add(new RectangleAnnotation
                    {
                        MinimumX = monthIndex - 0.42,
                        MaximumX = monthIndex + 0.42,
                        MinimumY = currentBottom,
                        MaximumY = currentBottom + (double)amount,
                        Fill = GetColor(ticker),
                        Stroke = OxyColors.White,
                        StrokeThickness = 1
                    });

                    Segments.Add(new SegmentInfo
                    {
                        Name = nameByTicker[ticker],
                        Value = amount,
                        Month = month,
                        X0 = monthIndex - 0.45,
                        X1 = monthIndex + 0.45,
                        Y0 = currentBottom,
                        Y1 = currentBottom + (double)amount
                    });
                    currentBottom += (double)amount;
                }

                PlotModel.Annotations.Add(new TextAnnotation
                {
                    Text = $"{totalMonthSum:N0}",
                    TextColor = OxyColor.FromRgb(43, 57, 52),
                    Stroke = OxyColors.Transparent,
                    FontSize = 10,
                    TextPosition = new DataPoint(
                        monthIndex,
                        (double)totalMonthSum + (double)(maxMonthSum * 0.025m)),
                    TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center,
                    TextVerticalAlignment = OxyPlot.VerticalAlignment.Bottom
                });
            }
        }
    }

    public sealed class CouponLegendItem
    {
        public string Name { get; }
        public Brush Color { get; }

        public CouponLegendItem(string name, Brush color)
        {
            Name = name;
            Color = color;
        }
    }
}
