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

    public static readonly BindableProperty ShowAllAxisLabelsProperty = BindableProperty.Create(
        nameof(ShowAllAxisLabels),
        typeof(bool),
        typeof(PortfolioChartView),
        false,
        propertyChanged: (view, _, _) => ((PortfolioChartView)view).Invalidate());

    public static readonly BindableProperty IsInteractiveLineChartProperty = BindableProperty.Create(
        nameof(IsInteractiveLineChart),
        typeof(bool),
        typeof(PortfolioChartView),
        false,
        propertyChanged: (view, _, _) => ((PortfolioChartView)view).Invalidate());

    public static readonly BindableProperty ShowAllPointsProperty = BindableProperty.Create(
        nameof(ShowAllPoints),
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
    private readonly ChartDrawable _drawable;
    private ChartPoint? _selectedDetail;
    private PointF _selectedPoint;
    private int _selectedLineIndex = -1;

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

    public bool ShowAllAxisLabels
    {
        get => (bool)GetValue(ShowAllAxisLabelsProperty);
        set => SetValue(ShowAllAxisLabelsProperty, value);
    }

    public bool IsInteractiveLineChart
    {
        get => (bool)GetValue(IsInteractiveLineChartProperty);
        set => SetValue(IsInteractiveLineChartProperty, value);
    }

    public bool ShowAllPoints
    {
        get => (bool)GetValue(ShowAllPointsProperty);
        set => SetValue(ShowAllPointsProperty, value);
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
        _drawable = new ChartDrawable(this);
        Drawable = _drawable;
        EndInteraction += OnEndInteraction;
        HeightRequest = 260;
    }

    private void OnEndInteraction(object? sender, TouchEventArgs e)
    {
        if (!e.IsInsideBounds || e.Touches.Length == 0)
        {
            _selectedDetail = null;
            _selectedLineIndex = -1;
            Invalidate();
            return;
        }

        _selectedPoint = e.Touches[0];
        var bounds = new RectF(0, 0, (float)Width, (float)Height);
        if (IsInteractiveLineChart)
        {
            _selectedDetail = _drawable.FindLineDetailAt(
                _selectedPoint, bounds, out _selectedPoint, out _selectedLineIndex);
        }
        else
        {
            _selectedLineIndex = -1;
            _selectedDetail = _drawable.FindDetailAt(_selectedPoint, bounds);
        }
        Invalidate();
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
        _selectedDetail = null;
        _selectedLineIndex = -1;
        if (oldPoints is not null)
            oldPoints.CollectionChanged -= OnCollectionChanged;
        if (newPoints is not null)
            newPoints.CollectionChanged += OnCollectionChanged;
        Invalidate();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _selectedDetail = null;
        _selectedLineIndex = -1;
        Invalidate();
    }

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
                : view.ShowAllPoints ? view.Points?.ToList() ?? [] : view.Points?.TakeLast(18).ToList() ?? [];

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
            }
            else if (view.IsStackedChart)
            {
                DrawStacked(canvas, bounds, points, view.Series ?? Array.Empty<ChartSeries>(), muted);
            }
            else if (view.Series is { Count: > 0 })
            {
                DrawLines(canvas, bounds, points, view.Series, view.TargetValue, muted, view.ShowAllAxisLabels);
                if (view.IsInteractiveLineChart && view._selectedLineIndex >= 0)
                    DrawSelectedLinePoint(
                        canvas, bounds, points, view.Series, view.TargetValue, view._selectedLineIndex, muted);
            }
            else
            {
                DrawBars(canvas, bounds, points, ink, muted, accent, negative, light);
            }

            if (!view.IsAllocationChart && view._selectedDetail is { } selected)
                DrawSelection(canvas, bounds, selected, view._selectedPoint, view.IsStackedChart, light);
        }

        public ChartPoint? FindDetailAt(PointF touch, RectF bounds)
        {
            if (view.IsAllocationChart || view.Series is { Count: > 0 } && !view.IsStackedChart)
                return null;

            const float left = 8f;
            if (view.IsStackedChart)
            {
                var points = view.Points?.ToList() ?? [];
                var series = view.Series ?? Array.Empty<ChartSeries>();
                const float top = 12f;
                var bottom = bounds.Height - 33f;
                var plotHeight = bottom - top;
                if (points.Count == 0 || touch.Y < top || touch.Y > bottom)
                    return null;

                var slot = (bounds.Width - left * 2) / points.Count;
                var pointIndex = (int)((touch.X - left) / slot);
                if (pointIndex < 0 || pointIndex >= points.Count)
                    return null;
                var barWidth = Math.Clamp(slot * 0.58f, 6f, 28f);
                var barX = left + slot * pointIndex + (slot - barWidth) / 2f;
                if (touch.X < barX || touch.X > barX + barWidth)
                    return null;

                var maximum = Math.Max(1m, points.Max(point => point.Value));
                var currentBottom = bottom;
                foreach (var item in series)
                {
                    if (pointIndex >= item.Values.Count || item.Values[pointIndex] <= 0)
                        continue;

                    var segmentHeight = (float)(item.Values[pointIndex] / maximum * (decimal)(plotHeight - 4f));
                    var segmentBottom = currentBottom;
                    currentBottom -= Math.Max(1f, segmentHeight);
                    if (touch.Y >= currentBottom && touch.Y <= segmentBottom)
                        return pointIndex < (item.Details?.Count ?? 0)
                            ? item.Details![pointIndex]
                            : new ChartPoint(item.Name, item.Values[pointIndex]);
                }
                return null;
            }

            if (view.Series is { Count: > 0 })
                return null;

            var pointsToHitTest = view.ShowAllPoints
                ? view.Points?.ToList() ?? []
                : view.Points?.TakeLast(18).ToList() ?? [];
            const float topForBars = 18f;
            var chartBottom = bounds.Height - 35f;
            var chartHeight = chartBottom - topForBars;
            if (pointsToHitTest.Count == 0 || touch.Y < topForBars || touch.Y > chartBottom)
                return null;

            var barSlot = (bounds.Width - left * 2) / pointsToHitTest.Count;
            var index = (int)((touch.X - left) / barSlot);
            if (index < 0 || index >= pointsToHitTest.Count)
                return null;
            var width = Math.Clamp(barSlot * 0.5f, 5f, 24f);
            var x = left + barSlot * index + (barSlot - width) / 2f;
            if (touch.X < x || touch.X > x + width)
                return null;

            var max = pointsToHitTest.Max(point => Math.Abs((double)point.Value));
            if (max <= 0)
                return null;
            var height = Math.Max(3f,
                (float)(Math.Abs((double)pointsToHitTest[index].Value) / max * (chartHeight - 8f)));
            return touch.Y >= chartBottom - height ? pointsToHitTest[index] : null;
        }

        public ChartPoint? FindLineDetailAt(
            PointF touch,
            RectF bounds,
            out PointF anchor,
            out int selectedIndex)
        {
            anchor = touch;
            selectedIndex = -1;
            var points = view.Points?.ToList() ?? [];
            var series = view.Series ?? Array.Empty<ChartSeries>();
            const float left = 12f;
            const float top = 12f;
            var bottom = bounds.Height - 34f;
            if (points.Count == 0 || series.Count == 0 ||
                touch.X < left || touch.X > bounds.Width - left ||
                touch.Y < top || touch.Y > bottom)
                return null;

            var denominator = Math.Max(1, points.Count - 1);
            selectedIndex = Math.Clamp(
                (int)Math.Round((touch.X - left) / (bounds.Width - left * 2) * denominator),
                0,
                points.Count - 1);

            var values = series.SelectMany(item => item.Values).ToList();
            if (view.TargetValue is { } target)
                values.Add(target);
            var maximum = Math.Max(1m, values.DefaultIfEmpty(0m).Max());
            var nearestDistance = float.MaxValue;
            foreach (var item in series)
            {
                if (selectedIndex >= item.Values.Count)
                    continue;

                var y = bottom - (float)(item.Values[selectedIndex] / maximum) * (bottom - top);
                var distance = Math.Abs(touch.Y - y);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    anchor = new PointF(
                        left + (bounds.Width - left * 2) * selectedIndex / denominator,
                        y);
                }
            }

            return points[selectedIndex];
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
                if (points.Count <= 18)
                {
                    canvas.FontColor = ink;
                    canvas.FontSize = 9;
                    canvas.DrawString(FormatBarLabel(point), x - (slot - barWidth) / 2,
                        Math.Max(0, bottom - amount - 16), slot, 14,
                        HorizontalAlignment.Center, VerticalAlignment.Center);
                }
            }

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

        private static void DrawSelectedLinePoint(
            ICanvas canvas,
            RectF bounds,
            IReadOnlyList<ChartPoint> points,
            IReadOnlyList<ChartSeries> series,
            decimal? target,
            int index,
            Color muted)
        {
            if (index < 0 || index >= points.Count)
                return;

            const float left = 12f;
            const float top = 12f;
            var bottom = bounds.Height - 34f;
            var denominator = Math.Max(1, points.Count - 1);
            var x = left + (bounds.Width - left * 2) * index / denominator;
            var values = series.SelectMany(item => item.Values).ToList();
            if (target is { } targetValue)
                values.Add(targetValue);
            if (values.Count == 0)
                return;

            var maximum = Math.Max(1m, values.Max());
            canvas.StrokeColor = muted.WithAlpha(0.7f);
            canvas.StrokeSize = 1f;
            canvas.DrawLine(x, top, x, bottom);
            foreach (var item in series)
            {
                if (index >= item.Values.Count)
                    continue;

                var y = bottom - (float)(item.Values[index] / maximum) * (bottom - top);
                canvas.FillColor = item.Color;
                canvas.FillCircle(x - 4f, y - 4f, 8f);
                canvas.StrokeColor = Color.FromArgb(ThemeManager.CurrentTheme == AppTheme.Light ? "#FFFFFF" : "#111E2C");
                canvas.StrokeSize = 1.5f;
                canvas.DrawCircle(x, y, 4f);
            }
        }

        private static void DrawLines(
            ICanvas canvas,
            RectF bounds,
            IReadOnlyList<ChartPoint> points,
            IReadOnlyList<ChartSeries> series,
            decimal? target,
            Color muted,
            bool showAllAxisLabels)
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

            DrawAxisLabels(canvas, points, left, bottom, bounds.Width, muted, showAllAxisLabels);
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

        private static void DrawSelection(
            ICanvas canvas,
            RectF bounds,
            ChartPoint point,
            PointF anchor,
            bool isCouponChart,
            bool light)
        {
            const float horizontalPadding = 11f;
            const float lineHeight = 17f;
            var lines = new List<string> { point.Label };
            if (!string.IsNullOrWhiteSpace(point.Ticker))
                lines.Add(point.Ticker);
            if (point.Quantity is { } quantity)
                lines.Add($"Количество: {quantity:N2}");
            if (point.InstrumentValue is { } value)
                lines.Add($"Стоимость: {Format(value)} ₽");
            if (isCouponChart)
                lines.Add($"Купон за месяц: {Format(point.Value)} ₽");
            if (!string.IsNullOrWhiteSpace(point.Detail))
                lines.AddRange(point.Detail.Split('\n', StringSplitOptions.RemoveEmptyEntries));

            var popupWidth = Math.Min(250f, bounds.Width - 16f);
            var popupHeight = 16f + lineHeight * lines.Count;
            var x = anchor.X + 14f + popupWidth > bounds.Width
                ? anchor.X - popupWidth - 14f
                : anchor.X + 14f;
            x = Math.Clamp(x, 8f, bounds.Width - popupWidth - 8f);
            var y = Math.Clamp(anchor.Y - popupHeight - 10f, 8f, bounds.Height - popupHeight - 8f);
            canvas.FillColor = Color.FromArgb(light ? "#FFFFFF" : "#1B293B");
            canvas.FillRoundedRectangle(x, y, popupWidth, popupHeight, 12f);
            canvas.StrokeColor = Color.FromArgb(light ? "#D3DDE5" : "#42546A");
            canvas.StrokeSize = 1f;
            canvas.DrawRoundedRectangle(x, y, popupWidth, popupHeight, 12f);

            var textColor = Color.FromArgb(light ? "#233544" : "#E6EEF5");
            for (var index = 0; index < lines.Count; index++)
            {
                canvas.FontColor = index == 0 ? textColor : Color.FromArgb(light ? "#566979" : "#B9C7D4");
                canvas.FontSize = index == 0 ? 11 : 10;
                canvas.DrawString(lines[index], x + horizontalPadding, y + 7 + index * lineHeight,
                    popupWidth - horizontalPadding * 2, lineHeight,
                    HorizontalAlignment.Left, VerticalAlignment.Center);
            }
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
            Color muted,
            bool showAllLabels = false)
        {
            canvas.FontColor = muted;
            canvas.FontSize = 10;
            var labels = showAllLabels
                ? points.Select((point, index) => new { Text = point.Label, Index = index }).ToList()
                : points.Select((point, index) => (point.Label, index))
                    .GroupBy(item => item.Label, StringComparer.CurrentCulture)
                    .Select(group => new
                    {
                        Text = group.Key,
                        Index = (group.First().index + group.Last().index) / 2
                    })
                    .ToList();
            var selectedLabels = labels.Count <= 3
                ? labels
                : showAllLabels
                    ? labels
                    : [labels[0], labels[labels.Count / 2], labels[^1]];
            var labelWidth = showAllLabels
                ? Math.Max(58f, (width - left * 2) / Math.Max(1, points.Count - 1))
                : 90f;
            foreach (var label in selectedLabels)
            {
                var index = label.Index;
                if (index < 0 || index >= points.Count)
                    continue;

                var x = selectedLabels.Count == 1
                    ? width / 2f
                    : left + (width - left * 2) * index / (points.Count - 1);
                var alignment = labels.Count == 1 || index == 0
                    ? labels.Count == 1 ? HorizontalAlignment.Center : HorizontalAlignment.Left
                    : index == points.Count - 1
                        ? HorizontalAlignment.Right
                        : HorizontalAlignment.Center;
                var labelX = alignment switch
                {
                    HorizontalAlignment.Left => x,
                    HorizontalAlignment.Right => x - labelWidth,
                    _ when alignment == HorizontalAlignment.Center => x - labelWidth / 2f,
                    _ => x
                };
                canvas.DrawString(label.Text, labelX, bottom + 5, labelWidth, 18,
                    alignment, VerticalAlignment.Center);
            }
        }

        private static string FormatBarLabel(ChartPoint point)
        {
            if (point.IsPercent)
                return point.Value.ToString("P0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));

            var absoluteValue = Math.Abs(point.Value);
            return absoluteValue switch
            {
                >= 1_000_000m => $"{point.Value / 1_000_000m:0.#}м",
                >= 10_000m => $"{point.Value / 1_000m:0.#}к",
                >= 1_000m => $"{point.Value / 1_000m:0.#}т",
                _ => $"{point.Value:0}"
            };
        }

        private static string Format(ChartPoint point) =>
            point.IsPercent
                ? point.Value.ToString("P1", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"))
                : Format(point.Value);

        private static string Format(decimal value) =>
            value.ToString("#,0.##;−#,0.##", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
    }
}
