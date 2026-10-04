using BondAnalytics.Mobile.ViewModels;
using Microsoft.Maui.Graphics;
using System.Collections.Specialized;

namespace BondAnalytics.Mobile.Controls;

public sealed class PortfolioChartView : GraphicsView
{
    public static readonly BindableProperty PointsProperty = BindableProperty.Create(
        nameof(Points),
        typeof(IList<ChartPoint>),
        typeof(PortfolioChartView),
        propertyChanged: (view, oldValue, newValue) =>
            ((PortfolioChartView)view).OnPointsChanged(
                oldValue as INotifyCollectionChanged,
                newValue as INotifyCollectionChanged));

    public static readonly BindableProperty IsAllocationChartProperty = BindableProperty.Create(
        nameof(IsAllocationChart),
        typeof(bool),
        typeof(PortfolioChartView),
        false,
        propertyChanged: (view, _, _) => ((PortfolioChartView)view).Invalidate());

    public static readonly BindableProperty IsStackedChartProperty = BindableProperty.Create(
        nameof(IsStackedChart),
        typeof(bool),
        typeof(PortfolioChartView),
        false,
        propertyChanged: (view, _, _) => ((PortfolioChartView)view).Invalidate());

    public static readonly BindableProperty SeriesProperty = BindableProperty.Create(
        nameof(Series),
        typeof(IReadOnlyList<ChartSeries>),
        typeof(PortfolioChartView),
        propertyChanged: (view, _, _) => ((PortfolioChartView)view).Invalidate());

    public static readonly BindableProperty TargetValueProperty = BindableProperty.Create(
        nameof(TargetValue),
        typeof(decimal?),
        typeof(PortfolioChartView),
        null,
        propertyChanged: (view, _, _) => ((PortfolioChartView)view).Invalidate());

    private bool _themeSubscribed;

    public bool IsAllocationChart
    {
        get => (bool)GetValue(IsAllocationChartProperty);
        set => SetValue(IsAllocationChartProperty, value);
    }

    public bool IsStackedChart
    {
        get => (bool)GetValue(IsStackedChartProperty);
        set => SetValue(IsStackedChartProperty, value);
    }

    public IReadOnlyList<ChartSeries> Series
    {
        get => (IReadOnlyList<ChartSeries>)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public decimal? TargetValue
    {
        get => (decimal?)GetValue(TargetValueProperty);
        set => SetValue(TargetValueProperty, value);
    }

    public IList<ChartPoint> Points
    {
        get => (IList<ChartPoint>)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public PortfolioChartView()
    {
        Drawable = new ChartDrawable(this);
        HeightRequest = 260;
    }

    protected override void OnParentSet()
    {
        base.OnParentSet();
        if (Parent is not null && !_themeSubscribed)
        {
            ThemeManager.ThemeChanged += OnThemeChanged;
            _themeSubscribed = true;
        }
        else if (Parent is null && _themeSubscribed)
        {
            ThemeManager.ThemeChanged -= OnThemeChanged;
            _themeSubscribed = false;
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Invalidate();

    private void OnPointsChanged(INotifyCollectionChanged? oldPoints, INotifyCollectionChanged? newPoints)
    {
        if (oldPoints is not null)
            oldPoints.CollectionChanged -= OnCollectionChanged;
        if (newPoints is not null)
            newPoints.CollectionChanged += OnCollectionChanged;
        Invalidate();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Invalidate();

    private sealed class ChartDrawable(PortfolioChartView view) : IDrawable
    {
        private static readonly Color[] AllocationColors =
        [
            Color.FromArgb("#398B77"), Color.FromArgb("#4080AE"),
            Color.FromArgb("#BD8A36"), Color.FromArgb("#8562A8")
        ];

        public void Draw(ICanvas canvas, RectF bounds)
        {
            var light = ThemeManager.CurrentTheme == AppTheme.Light;
            var ink = Color.FromArgb(light ? "#233544" : "#DDE8F1");
            var muted = Color.FromArgb(light ? "#687786" : "#8294A8");
            var accent = Color.FromArgb(light ? "#16805D" : "#70D7B8");
            var negative = Color.FromArgb(light ? "#B42318" : "#FF887D");
            var points = view.Series is { Count: > 0 } || view.IsStackedChart
                ? view.Points?.ToList() ?? []
                : view.Points?.TakeLast(18).ToList() ?? [];

            if (points.Count == 0)
            {
                canvas.FontColor = muted;
                canvas.FontSize = 13;
                canvas.DrawString("Обновите портфель, чтобы увидеть данные", 0, 90,
                    bounds.Width, 30, HorizontalAlignment.Center, VerticalAlignment.Center);
                return;
            }

            if (view.IsAllocationChart)
            {
                DrawAllocation(canvas, bounds, points, ink, light);
                return;
            }

            if (view.IsStackedChart)
            {
                DrawStacked(canvas, bounds, points, view.Series ?? Array.Empty<ChartSeries>(), muted);
                return;
            }

            if (view.Series is { Count: > 0 })
            {
                DrawLines(canvas, bounds, points, view.Series, view.TargetValue, muted);
                return;
            }

            DrawBars(canvas, bounds, points, ink, muted, accent, negative, light);
        }

        private static void DrawBars(
            ICanvas canvas,
            RectF bounds,
            IReadOnlyList<ChartPoint> points,
            Color ink,
            Color muted,
            Color accent,
            Color negative,
            bool light)
        {
            const float left = 8f;
            const float top = 18f;
            var bottom = bounds.Height - 35f;
            var plotHeight = bottom - top;
            var maximum = points.Max(point => Math.Abs((double)point.Value));
            if (maximum <= 0)
                maximum = 1;

            DrawGrid(canvas, left, top, bounds.Width - left, plotHeight, muted);
            var slot = (bounds.Width - left * 2) / points.Count;
            var barWidth = Math.Clamp(slot * 0.5f, 5f, 24f);
            for (var i = 0; i < points.Count; i++)
            {
                var point = points[i];
                var amount = Math.Max(3f, (float)(Math.Abs((double)point.Value) / maximum * (plotHeight - 8f)));
                var x = left + slot * i + (slot - barWidth) / 2;
                canvas.FillColor = point.Value < 0 ? negative : accent;
                canvas.FillRoundedRectangle(x, bottom - amount, barWidth, amount, barWidth / 2);
            }

            canvas.FontColor = ink;
            canvas.FontSize = 12;
            canvas.DrawString(Format(points[^1]), left, 0, bounds.Width - left * 2, 20,
                HorizontalAlignment.Right, VerticalAlignment.Center);
            DrawAxisLabels(canvas, points, left, bottom, bounds.Width, muted);
        }

        private static void DrawStacked(
            ICanvas canvas,
            RectF bounds,
            IReadOnlyList<ChartPoint> points,
            IReadOnlyList<ChartSeries> series,
            Color muted)
        {
            const float left = 8f;
            const float top = 12f;
            var bottom = bounds.Height - 33f;
            var plotHeight = bottom - top;
            var maximum = Math.Max(1m, points.Max(point => point.Value));
            DrawGrid(canvas, left, top, bounds.Width - left, plotHeight, muted);
            var slot = (bounds.Width - left * 2) / points.Count;
            var barWidth = Math.Clamp(slot * 0.58f, 6f, 28f);

            for (var pointIndex = 0; pointIndex < points.Count; pointIndex++)
            {
                var x = left + slot * pointIndex + (slot - barWidth) / 2f;
                var currentBottom = bottom;
                foreach (var item in series)
                {
                    if (pointIndex >= item.Values.Count || item.Values[pointIndex] <= 0)
                        continue;

                    var segmentHeight = (float)(item.Values[pointIndex] / maximum * (decimal)(plotHeight - 4f));
                    currentBottom -= Math.Max(1f, segmentHeight);
                    canvas.FillColor = item.Color;
                    canvas.FillRectangle(x, currentBottom, barWidth, Math.Max(1f, segmentHeight));
                }
            }

            DrawAxisLabels(canvas, points, left, bottom, bounds.Width, muted);
        }

        private static void DrawLines(
            ICanvas canvas,
            RectF bounds,
            IReadOnlyList<ChartPoint> points,
            IReadOnlyList<ChartSeries> series,
            decimal? target,
            Color muted)
        {
            const float left = 12f;
            const float top = 12f;
            var bottom = bounds.Height - 34f;
            var plotHeight = bottom - top;
            var values = series.SelectMany(item => item.Values).ToList();
            if (target is { } targetValue)
                values.Add(targetValue);
            var maximum = Math.Max(1m, values.DefaultIfEmpty(0m).Max());
            DrawGrid(canvas, left, top, bounds.Width - left, plotHeight, muted);

            float GetY(decimal value) => bottom - (float)(value / maximum) * plotHeight;
            if (target is { } goal)
            {
                canvas.StrokeColor = Color.FromArgb(ThemeManager.CurrentTheme == AppTheme.Light ? "#B42318" : "#FF887D");
                canvas.StrokeSize = 1.5f;
                canvas.DrawLine(left, GetY(goal), bounds.Width - left, GetY(goal));
            }

            var denominator = Math.Max(1, points.Count - 1);
            foreach (var item in series)
            {
                var count = Math.Min(points.Count, item.Values.Count);
                if (count < 2)
                    continue;

                var path = new PathF();
                for (var index = 0; index < count; index++)
                {
                    var x = left + (bounds.Width - left * 2) * index / denominator;
                    var y = GetY(item.Values[index]);
                    if (index == 0)
                        path.MoveTo(x, y);
                    else
                        path.LineTo(x, y);
                }

                canvas.StrokeColor = item.Color;
                canvas.StrokeSize = 2.5f;
                canvas.DrawPath(path);
            }

            DrawAxisLabels(canvas, points, left, bottom, bounds.Width, muted);
        }

        private static void DrawAllocation(
            ICanvas canvas,
            RectF bounds,
            IReadOnlyList<ChartPoint> points,
            Color ink,
            bool light)
        {
            var total = points.Sum(point => Math.Max(0m, point.Value));
            var centerX = bounds.Center.X;
            var centerY = bounds.Center.Y - 8;
            var radius = Math.Min(bounds.Width * 0.32f, bounds.Height * 0.39f);
            var startAngle = -90d;
            for (var index = 0; index < points.Count; index++)
            {
                var value = Math.Max(0m, points[index].Value);
                if (value == 0 || total == 0)
                    continue;

                var sweep = (double)(value / total) * 360d;
                var path = new PathF();
                path.MoveTo(centerX, centerY);
                for (var step = 0; step <= 48; step++)
                {
                    var angle = (startAngle + sweep * step / 48d) * Math.PI / 180d;
                    path.LineTo(centerX + radius * (float)Math.Cos(angle),
                        centerY + radius * (float)Math.Sin(angle));
                }

                path.Close();
                canvas.FillColor = AllocationColors[index % AllocationColors.Length];
                canvas.FillPath(path);
                startAngle += sweep;
            }

            var holeRadius = radius * 0.59f;
            canvas.FillColor = Color.FromArgb(light ? "#FFFFFF" : "#111E2C");
            canvas.FillEllipse(centerX - holeRadius, centerY - holeRadius, holeRadius * 2, holeRadius * 2);
            canvas.FontColor = ink;
            canvas.FontSize = 16;
            canvas.DrawString(Format(total), centerX - radius, centerY - 12, radius * 2, 24,
                HorizontalAlignment.Center, VerticalAlignment.Center);
        }

        private static void DrawGrid(ICanvas canvas, float left, float top, float right, float height, Color muted)
        {
            canvas.StrokeColor = muted.WithAlpha(0.35f);
            canvas.StrokeSize = 0.6f;
            for (var line = 0; line < 4; line++)
            {
                var y = top + height * line / 3f;
                canvas.DrawLine(left, y, right, y);
            }
        }

        private static void DrawAxisLabels(
            ICanvas canvas,
            IReadOnlyList<ChartPoint> points,
            float left,
            float bottom,
            float width,
            Color muted)
        {
            canvas.FontColor = muted;
            canvas.FontSize = 10;
            var indices = new HashSet<int> { 0, points.Count / 2, points.Count - 1 };
            foreach (var index in indices)
            {
                if (index < 0 || index >= points.Count)
                    continue;

                var x = points.Count == 1 ? left : left + (width - left * 2) * index / (points.Count - 1);
                const float labelWidth = 90f;
                var alignment = index == 0
                    ? HorizontalAlignment.Left
                    : index == points.Count - 1
                        ? HorizontalAlignment.Right
                        : HorizontalAlignment.Center;
                var labelX = alignment switch
                {
                    HorizontalAlignment.Left => x,
                    HorizontalAlignment.Right => x - labelWidth,
                    _ => x - labelWidth / 2f
                };
                canvas.DrawString(points[index].Label, labelX, bottom + 5, labelWidth, 18,
                    alignment, VerticalAlignment.Center);
            }
        }

        private static string Format(ChartPoint point) =>
            point.IsPercent
                ? point.Value.ToString("P1", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"))
                : Format(point.Value);

        private static string Format(decimal value) =>
            value.ToString("#,0.##;−#,0.##", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
    }
}
