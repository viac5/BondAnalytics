using CommunityToolkit.Mvvm.ComponentModel;
using Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Graphics;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Channels;
using Tinkoff.InvestApi.V1;

namespace BondAnalytics.Mobile.ViewModels;

public partial class AppViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);
    private static readonly string[] FilterLabels = ["Все", "ОФЗ", "Корпоративные", "Фонды и прочее"];
    private static readonly string[] ChartLabels =
        ["Купоны", "Стоимость", "Прибыль", "Доходность", "НКД", "Купоны за год", "Структура"];
    private static readonly string[] SortLabels =
        ["Стоимость ↓", "Название А—Я", "Прибыль ↓", "Доходность ↓"];
    private static readonly Color[] ChartColors =
    [
        Color.FromArgb("#398B77"), Color.FromArgb("#4080AE"), Color.FromArgb("#BD8A36"),
        Color.FromArgb("#8562A8"), Color.FromArgb("#C35D4A"), Color.FromArgb("#51969A"),
        Color.FromArgb("#777FBD"), Color.FromArgb("#A56A8A"), Color.FromArgb("#648C49"),
        Color.FromArgb("#AB7548"), Color.FromArgb("#4D7791"), Color.FromArgb("#D76C68"),
        Color.FromArgb("#46A5C0"), Color.FromArgb("#AD7ED1"), Color.FromArgb("#D18B43"),
        Color.FromArgb("#5875C9"), Color.FromArgb("#C35E9C"), Color.FromArgb("#3B9C8F"),
        Color.FromArgb("#9A9D48"), Color.FromArgb("#6B809C")
    ];
    private readonly IPortfolioService _portfolioService;
    private readonly IPortfolioHistoryRepository _historyRepository;
    private readonly ILogger<AppViewModel> _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _cacheWriteLock = new(1, 1);
    private readonly Dictionary<string, PortfolioPositionViewModel> _positionsByUid = new(StringComparer.Ordinal);
    private CancellationTokenSource? _priceSubscription;
    private string _subscribedUids = string.Empty;
    private bool _initialized;
    private bool _chartsVisible;
    private DateTime _lastChartRefresh = DateTime.MinValue;
    private long _chartGeneration;
    private Task _initialRefreshTask = Task.CompletedTask;
    private decimal? _dailyChangeBaseValue;
    private string? _dailyChangeError;

    private sealed record ChartPosition(
        string Ticker,
        string Name,
        string Group,
        decimal Quantity,
        decimal AveragePrice,
        decimal CurrentPrice,
        decimal CurrentYield,
        decimal Coupon,
        int CouponsPerYear,
        DateTime? NextCouponDate,
        decimal TotalAccruedInterest)
    {
        public decimal TotalValue => Quantity * CurrentPrice;
        public decimal Profit => (CurrentPrice - AveragePrice) * Quantity;
    }

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "Загружаем портфель";
    [ObservableProperty] private string portfolioDataStatusText = "Данные ещё не загружались";
    [ObservableProperty] private int selectedFilterIndex;
    [ObservableProperty] private int selectedSortIndex;
    [ObservableProperty] private int selectedChartIndex;
    [ObservableProperty] private DateTime? lastUpdated;
    [ObservableProperty] private DateTime? lastQuoteUpdated;
    [ObservableProperty] private string realTimeStatusText = "Обновление котировок в реальном времени";

    public ObservableCollection<PortfolioPositionViewModel> Positions { get; } = new();
    public ObservableCollection<PortfolioPositionViewModel> VisiblePositions { get; } = new();
    public ObservableCollection<ChartPoint> ChartPoints { get; } = new();
    public ObservableCollection<ChartSeries> ChartSeries { get; } = new();
    public IReadOnlyList<string> FilterLabelsList => FilterLabels;
    public IReadOnlyList<string> ChartLabelsList => ChartLabels;
    public IReadOnlyList<string> SortLabelsList => SortLabels;
    public bool IsAllocationChart => SelectedChartIndex == 6;
    public bool IsStackedChart => SelectedChartIndex == 0;
    public Color TotalProfitColor => TotalProfit < 0
        ? Color.FromArgb("#D14343")
        : TotalProfit > 0 ? Color.FromArgb("#16805D") : Color.FromArgb("#687786");
    public Color TotalProfitPercentColor => TotalProfitColor;
    public string LastUpdatedText => LastUpdated is null
        ? "Данные ещё не обновлялись"
        : $"Обновлено в {LastUpdated.Value.ToLocalTime():HH:mm}";
    public string LastQuoteUpdatedText => LastQuoteUpdated is null
        ? "Ожидание потока котировок"
        : $"Котировки обновлены в {LastQuoteUpdated.Value.ToLocalTime():HH:mm:ss}";
    public string DailyChangeSummary
    {
        get
        {
            if (_dailyChangeError is not null)
                return _dailyChangeError;
            if (_dailyChangeBaseValue is not { } baseline)
                return "Изменение за день: история ещё накапливается";

            var change = TotalFullValue - baseline;
            var percentage = baseline > 0 ? change / baseline : 0m;
            return $"За день: {change:+#,0;-#,0;0} ₽ ({percentage:+0.0%;-0.0%;0.0%})";
        }
    }
    public Color DailyChangeColor => _dailyChangeBaseValue is null
        ? Color.FromArgb("#687786")
        : TotalFullValue >= _dailyChangeBaseValue
            ? Color.FromArgb("#16805D")
            : Color.FromArgb("#D14343");
    public Task InitialRefreshTask => _initialRefreshTask;
    public decimal TotalPortfolioValue => Positions.Sum(position => position.TotalValue);
    public decimal TotalNkd => Positions.Sum(position => position.TotalAccruedInterest);
    public decimal TotalFullValue => Positions.Sum(position => position.FullValue);
    public decimal TotalProfit => Positions.Sum(position => position.Profit);
    public decimal TotalProfitPercent
    {
        get
        {
            var cost = Positions.Sum(position => position.AveragePrice * position.Quantity);
            return cost > 0 ? TotalProfit / cost : 0;
        }
    }
    public int PositionCount => Positions.Count;
    public decimal WeightedYield
    {
        get
        {
            var yieldedPositions = Positions.Where(position => position.CurrentYield > 0).ToList();
            var value = yieldedPositions.Sum(position => position.TotalValue);
            return value > 0
                ? yieldedPositions.Sum(position => position.TotalValue * position.CurrentYield) / value
                : 0;
        }
    }
    public decimal WeightedReinvestedYield
    {
        get
        {
            var yieldedPositions = Positions.Where(position => position.CurrentYield > 0).ToList();
            var value = yieldedPositions.Sum(position => position.TotalValue);
            return value > 0
                ? yieldedPositions.Sum(position => position.TotalValue * position.ReinvestedYield) / value
                : 0;
        }
    }

    public AppViewModel(
        IPortfolioService portfolioService,
        IPortfolioHistoryRepository historyRepository,
        ILogger<AppViewModel> logger)
    {
        _portfolioService = portfolioService;
        _historyRepository = historyRepository;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
            return;

        _initialized = true;
        await LoadCachedPortfolioAsync();
        _initialRefreshTask = RefreshPortfolioAsync();
        _ = RefreshPeriodicallyAsync(_lifetime.Token);
    }

    private async Task LoadCachedPortfolioAsync()
    {
        try
        {
            var cached = await _historyRepository.GetCachedPortfolioAsync(_lifetime.Token);
            if (cached is null)
                return;

            ApplyPortfolio(cached.Portfolio, cached.CapturedAt.UtcDateTime);
            await RefreshDailyChangeBaselineAsync(_lifetime.Token);
            PortfolioDataStatusText =
                $"Показан локальный кэш от {cached.CapturedAt.ToLocalTime():dd.MM HH:mm} · загружаем свежие данные";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load cached portfolio");
            PortfolioDataStatusText = $"Не удалось прочитать локальный кэш: {ex.GetBaseException().Message}";
        }
    }

    public void SetChartsVisible(bool isVisible)
    {
        _chartsVisible = isVisible;
        if (isVisible)
        {
            RefreshChart();
        }
    }

    public async Task RefreshPortfolioAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        StatusMessage = "Синхронизируем портфель";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            var data = await _portfolioService.GetPortfolioAsync(timeout.Token);
            var capturedAt = DateTimeOffset.UtcNow;
            ApplyPortfolio(data, capturedAt.UtcDateTime);
            PortfolioDataStatusText = $"Данные обновлены · {capturedAt.ToLocalTime():dd.MM HH:mm}";
            StartPriceSubscription();

            var persistenceMessages = new List<string>();
            try
            {
                await _historyRepository.SaveSnapshotAsync(
                    new PortfolioSnapshot(capturedAt, data.TotalValueRub), timeout.Token);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not store a portfolio snapshot");
                persistenceMessages.Add("не удалось сохранить историю стоимости");
            }

            await RefreshDailyChangeBaselineAsync(timeout.Token);

            try
            {
                await _historyRepository.SaveCachedPortfolioAsync(
                    new CachedPortfolioData(data, capturedAt), timeout.Token);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not store the cached portfolio");
                PortfolioDataStatusText = $"Не удалось сохранить локальный кэш: {ex.GetBaseException().Message}";
                persistenceMessages.Add("не удалось сохранить локальный кэш");
            }

            StatusMessage = persistenceMessages.Count == 0
                ? $"Портфель обновлён · {data.Positions.Count} позиций"
                : $"Портфель обновлён · {data.Positions.Count} позиций, {string.Join(" и ", persistenceMessages)}";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (timeout.IsCancellationRequested && !_lifetime.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Portfolio request exceeded the {Timeout} second timeout",
                RequestTimeout.TotalSeconds);
            StatusMessage = $"Запрос занял больше {RequestTimeout.TotalSeconds:0} секунд. Проверьте соединение и попробуйте снова.";
            SetRefreshFailureStatus();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh mobile portfolio");
            StatusMessage = $"Не удалось загрузить портфель: {ex.Message}";
            SetRefreshFailureStatus();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyPortfolio(PortfolioData data, DateTime capturedAt)
    {
        Positions.Clear();
        _positionsByUid.Clear();
        foreach (var item in data.Positions)
        {
            var position = new PortfolioPositionViewModel(item);
            Positions.Add(position);
            if (!string.IsNullOrWhiteSpace(position.Uid))
                _positionsByUid[position.Uid] = position;
        }

        LastUpdated = capturedAt;
        OnPropertyChanged(nameof(LastUpdatedText));
        OnPropertyChanged(nameof(TotalPortfolioValue));
        OnPropertyChanged(nameof(TotalNkd));
        OnPropertyChanged(nameof(TotalFullValue));
        OnPropertyChanged(nameof(DailyChangeSummary));
        OnPropertyChanged(nameof(DailyChangeColor));
        OnPropertyChanged(nameof(TotalProfit));
        OnPropertyChanged(nameof(TotalProfitColor));
        OnPropertyChanged(nameof(TotalProfitPercentColor));
        OnPropertyChanged(nameof(TotalProfitPercent));
        OnPropertyChanged(nameof(WeightedYield));
        OnPropertyChanged(nameof(WeightedReinvestedYield));
        OnPropertyChanged(nameof(PositionCount));
        ApplyFilter();
        if (_chartsVisible)
            RefreshChart();
    }

    private async Task RefreshDailyChangeBaselineAsync(CancellationToken cancellationToken)
    {
        _dailyChangeError = null;
        try
        {
            var todayStart = new DateTimeOffset(DateTime.Today);
            var snapshots = await _historyRepository.GetSnapshotsAsync(
                todayStart,
                DateTimeOffset.UtcNow,
                cancellationToken);
            var previousDay = DateTime.Today.AddDays(-1);
            _dailyChangeBaseValue = snapshots
                .Where(snapshot => snapshot.CapturedAt.ToLocalTime().Date == previousDay)
                .MaxBy(snapshot => snapshot.CapturedAt)
                ?.TotalValueRub;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load the previous-day portfolio value");
            _dailyChangeBaseValue = null;
            _dailyChangeError = "Изменение за день временно недоступно";
        }

        OnPropertyChanged(nameof(DailyChangeSummary));
        OnPropertyChanged(nameof(DailyChangeColor));
    }

    private void SetRefreshFailureStatus()
    {
        if (LastUpdated is { } lastUpdated)
            PortfolioDataStatusText =
                $"Нет связи с API · показаны сохранённые данные от {lastUpdated.ToLocalTime():dd.MM HH:mm}";
    }

    partial void OnSelectedFilterIndexChanged(int value) => ApplyFilter();

    partial void OnSelectedSortIndexChanged(int value) => ApplyFilter();

    partial void OnSelectedChartIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsAllocationChart));
        OnPropertyChanged(nameof(IsStackedChart));
        RefreshChart();
    }

    public void RefreshChart()
    {
        if (!_chartsVisible)
            return;

        var generation = Interlocked.Increment(ref _chartGeneration);
        var chartIndex = SelectedChartIndex;
        var positions = Positions.Select(position => new ChartPosition(
            position.Ticker,
            position.Name,
            position.Group,
            position.Quantity,
            position.AveragePrice,
            position.CurrentPrice,
            position.CurrentYield,
            position.Coupon,
            position.CouponsPerYear,
            position.Item.NextCouponDate,
            position.TotalAccruedInterest)).ToArray();
        _ = RefreshChartAsync(positions, chartIndex, generation);
    }

    private async Task RefreshChartAsync(ChartPosition[] positions, int chartIndex, long generation)
    {
        try
        {
            var result = await Task.Run(() => BuildChart(positions, chartIndex), _lifetime.Token)
                .ConfigureAwait(false);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (generation != _chartGeneration || chartIndex != SelectedChartIndex || !_chartsVisible)
                    return;

                ChartPoints.Clear();
                ChartSeries.Clear();
                foreach (var point in result.Points)
                    ChartPoints.Add(point);
                foreach (var series in result.Series)
                    ChartSeries.Add(series);
                _lastChartRefresh = DateTime.UtcNow;
            });
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not refresh portfolio chart");
            await MainThread.InvokeOnMainThreadAsync(() =>
                StatusMessage = $"Не удалось обновить график: {ex.GetBaseException().Message}");
        }
    }

    private static (IReadOnlyList<ChartPoint> Points, IReadOnlyList<ChartSeries> Series) BuildChart(
        IReadOnlyList<ChartPosition> positions,
        int chartIndex)
    {
        var points = new List<ChartPoint>();
        var series = new List<ChartSeries>();
        if (positions.Count == 0)
            return (points, series);

        if (chartIndex == 0)
        {
            var cutoff = DateTime.Today.AddYears(1);
            var coupons = new SortedDictionary<DateTime, Dictionary<string, decimal>>();
            foreach (var position in positions.Where(position =>
                         position.Coupon > 0 && position.CouponsPerYear > 0 && position.NextCouponDate is not null))
            {
                var date = position.NextCouponDate!.Value.Date;
                var interval = Math.Max(1, 12 / position.CouponsPerYear);
                while (date <= cutoff)
                {
                    if (date >= DateTime.Today)
                    {
                        var month = new DateTime(date.Year, date.Month, 1);
                        if (!coupons.TryGetValue(month, out var instruments))
                            coupons[month] = instruments = new Dictionary<string, decimal>();
                        instruments[position.Ticker] = instruments.GetValueOrDefault(position.Ticker) +
                                                       position.Coupon * position.Quantity;
                    }

                    date = date.AddMonths(interval);
                }
            }

            var couponMonths = coupons.ToList();
            var positionsByTicker = positions
                .GroupBy(position => position.Ticker, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var tickers = coupons.Values
                .SelectMany(month => month.Keys)
                .Distinct(StringComparer.Ordinal)
                .Select(ticker => new
                {
                    Ticker = ticker,
                    Total = coupons.Values.Sum(month => month.GetValueOrDefault(ticker)),
                    CouponsPerYear = positionsByTicker[ticker].CouponsPerYear
                })
                .OrderByDescending(item => item.CouponsPerYear)
                .ThenBy(item => item.Total)
                .ToList();

            for (var index = 0; index < tickers.Count; index++)
            {
                var ticker = tickers[index].Ticker;
                var position = positionsByTicker[ticker];
                var amounts = couponMonths.Select(month => month.Value.GetValueOrDefault(ticker)).ToList();
                var details = couponMonths.Select(month =>
                {
                    var amount = month.Value.GetValueOrDefault(ticker);
                    return amount > 0
                        ? new ChartPoint(position.Name, amount, Ticker: ticker,
                            Quantity: position.Quantity, InstrumentValue: position.TotalValue)
                        : null;
                }).ToList();
                series.Add(new ChartSeries(position.Name, ChartColors[index % ChartColors.Length], amounts, details));
            }

            for (var index = 0; index < couponMonths.Count; index++)
            {
                var month = couponMonths[index];
                points.Add(new ChartPoint(
                    month.Key.ToString("MMM yyyy", CultureInfo.GetCultureInfo("ru-RU")),
                    series.Sum(item => item.Values[index])));
            }
            return (points, series);
        }

        if (chartIndex == 6)
        {
            foreach (var group in positions.GroupBy(position => position.Group))
                points.Add(new ChartPoint(group.Key, group.Sum(position => position.TotalValue)));
            return (points, series);
        }

        var values = positions.Select(position => new ChartPoint(
                position.Name,
                chartIndex switch
                {
                    1 => position.TotalValue,
                    2 => position.Profit,
                    3 => position.CurrentYield,
                    4 => position.TotalAccruedInterest,
                    _ => position.Coupon * position.CouponsPerYear * position.Quantity
                },
                IsPercent: chartIndex == 3,
                Ticker: position.Ticker,
                Quantity: position.Quantity,
                InstrumentValue: position.TotalValue))
            .Where(point => chartIndex != 3 || point.Value > 0)
            .OrderByDescending(point => Math.Abs(point.Value))
            .Take(20)
            .OrderBy(point => point.Value);
        points.AddRange(values);
        return (points, series);
    }

    private void ApplyFilter(bool preserveExistingItems = false)
    {
        var selectedGroup = SelectedFilterIndex == 0 ? null : FilterLabels[SelectedFilterIndex];
        var filtered = Positions.Where(position => selectedGroup is null || position.Group == selectedGroup);
        var ordered = SelectedSortIndex switch
        {
            1 => filtered.OrderBy(position => position.Name, StringComparer.CurrentCultureIgnoreCase),
            2 => filtered.OrderByDescending(position => position.Profit),
            3 => filtered.OrderByDescending(position => position.CurrentYield),
            _ => filtered.OrderByDescending(position => position.FullValue)
        };
        var positions = ordered.ToList();

        if (!preserveExistingItems)
        {
            VisiblePositions.Clear();
            foreach (var position in positions)
                VisiblePositions.Add(position);
            return;
        }

        for (var targetIndex = 0; targetIndex < positions.Count; targetIndex++)
        {
            var position = positions[targetIndex];
            if (targetIndex < VisiblePositions.Count && ReferenceEquals(VisiblePositions[targetIndex], position))
                continue;

            var existingIndex = VisiblePositions.IndexOf(position);
            if (existingIndex >= 0)
                VisiblePositions.Move(existingIndex, targetIndex);
            else
                VisiblePositions.Insert(targetIndex, position);
        }

        while (VisiblePositions.Count > positions.Count)
            VisiblePositions.RemoveAt(VisiblePositions.Count - 1);
    }

    private async Task RefreshPeriodicallyAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
                await RefreshPortfolioAsync();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void StartPriceSubscription()
    {
        var uids = Positions.Select(position => position.Uid).Where(uid => !string.IsNullOrWhiteSpace(uid))
            .Distinct(StringComparer.Ordinal).ToList();
        var uidKey = string.Join("|", uids);
        if (uidKey == _subscribedUids || uids.Count == 0)
            return;

        _priceSubscription?.Cancel();
        _priceSubscription?.Dispose();
        _priceSubscription = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _subscribedUids = uidKey;
        var metadata = Positions.ToDictionary(
            position => position.Uid,
            position => (position.Item.InstrumentType, position.Item.Nominal),
            StringComparer.Ordinal);
        _ = ListenForPricesAsync(uids, metadata, _priceSubscription.Token);
    }

    private async Task ListenForPricesAsync(
        IReadOnlyList<string> uids,
        IReadOnlyDictionary<string, (string InstrumentType, decimal Nominal)> metadata,
        CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<(string Uid, decimal Quote)>(
            new BoundedChannelOptions(256)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            });
        var producer = ReadPriceUpdatesAsync(uids, metadata, channel.Writer, cancellationToken);
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
            while (true)
            {
                var tick = timer.WaitForNextTickAsync(cancellationToken).AsTask();
                if (await Task.WhenAny(tick, producer).ConfigureAwait(false) == producer)
                {
                    await DrainPriceUpdatesAsync(channel.Reader, cancellationToken).ConfigureAwait(false);
                    await producer.ConfigureAwait(false);
                    return;
                }

                if (!await tick.ConfigureAwait(false))
                    return;
                await DrainPriceUpdatesAsync(channel.Reader, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Live market-price subscription failed");
            MainThread.BeginInvokeOnMainThread(() =>
                RealTimeStatusText = $"Поток котировок недоступен: {ex.Message}");
        }
    }

    private async Task ReadPriceUpdatesAsync(
        IReadOnlyList<string> uids,
        IReadOnlyDictionary<string, (string InstrumentType, decimal Nominal)> metadata,
        ChannelWriter<(string Uid, decimal Quote)> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var update in _portfolioService.SubscribePricesAsync(uids, cancellationToken)
                               .ConfigureAwait(false))
            {
                if (update.LastPrice is not { } lastPrice)
                    continue;

                var marketQuote = lastPrice.Price.Units + lastPrice.Price.Nano / 1_000_000_000m;
                if (!metadata.TryGetValue(lastPrice.InstrumentUid, out var instrument))
                    continue;
                try
                {
                    var price = MarketPriceConverter.ToUnitPrice(
                        instrument.InstrumentType, marketQuote, instrument.Nominal);
                    await writer.WriteAsync((lastPrice.InstrumentUid, price), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    _logger.LogError(ex, "Could not convert a live quote for instrument {InstrumentUid}",
                        lastPrice.InstrumentUid);
                    MainThread.BeginInvokeOnMainThread(() =>
                        RealTimeStatusText = "Получена некорректная котировка; показана предыдущая цена.");
                }
            }
            writer.TryComplete();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            writer.TryComplete();
        }
        catch (Exception ex)
        {
            writer.TryComplete(ex);
            throw;
        }
    }

    private async Task DrainPriceUpdatesAsync(
        ChannelReader<(string Uid, decimal Quote)> reader,
        CancellationToken cancellationToken)
    {
        var latestPrices = new Dictionary<string, decimal>(StringComparer.Ordinal);
        while (reader.TryRead(out var update))
            latestPrices[update.Uid] = update.Quote;

        if (latestPrices.Count == 0)
            return;

        await MainThread.InvokeOnMainThreadAsync(() => ApplyLivePrices(latestPrices, cancellationToken));
    }

    private void ApplyLivePrices(IReadOnlyDictionary<string, decimal> prices, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        var hasUpdates = false;
        foreach (var (uid, quote) in prices)
        {
            if (!_positionsByUid.TryGetValue(uid, out var position))
                continue;
            hasUpdates |= position.UpdatePrice(quote);
        }

        if (!hasUpdates)
            return;

        OnPropertyChanged(nameof(TotalPortfolioValue));
        OnPropertyChanged(nameof(TotalNkd));
        OnPropertyChanged(nameof(TotalFullValue));
        OnPropertyChanged(nameof(DailyChangeSummary));
        OnPropertyChanged(nameof(DailyChangeColor));
        OnPropertyChanged(nameof(TotalProfit));
        OnPropertyChanged(nameof(TotalProfitColor));
        OnPropertyChanged(nameof(TotalProfitPercentColor));
        OnPropertyChanged(nameof(TotalProfitPercent));
        OnPropertyChanged(nameof(WeightedYield));
        OnPropertyChanged(nameof(WeightedReinvestedYield));
        if (SelectedSortIndex is 0 or 2 or 3)
            ApplyFilter(preserveExistingItems: true);

        LastQuoteUpdated = DateTime.UtcNow;
        OnPropertyChanged(nameof(LastQuoteUpdatedText));
        _ = PersistCachedPortfolioAsync(cancellationToken);

        if (_chartsVisible && DateTime.UtcNow - _lastChartRefresh >= TimeSpan.FromSeconds(5))
        {
            RefreshChart();
            _lastChartRefresh = DateTime.UtcNow;
        }
        RealTimeStatusText = $"Котировки обновляются · {DateTime.Now:HH:mm}";
    }

    private async Task PersistCachedPortfolioAsync(CancellationToken cancellationToken)
    {
        var items = Positions.Select(position => new PortfolioItem(
            position.Item.Ticker,
            position.Item.Name,
            position.Item.Uid,
            position.Item.InstrumentType,
            position.Item.Lot,
            position.Item.Quantity,
            position.Item.AveragePrice,
            position.CurrentPrice,
            position.Item.AccruedInterestPerBond,
            position.Item.Nominal,
            position.Item.Coupon,
            position.Item.CouponsPerYear,
            position.Item.CurrentYield,
            position.Item.NextCouponDate)).ToList();
        var portfolio = new PortfolioData(items, TotalFullValue);

        try
        {
            await _cacheWriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _historyRepository.SaveCachedPortfolioAsync(
                    new CachedPortfolioData(portfolio, DateTimeOffset.UtcNow), cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _cacheWriteLock.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not persist the latest portfolio prices");
            await MainThread.InvokeOnMainThreadAsync(() =>
                PortfolioDataStatusText = $"Не удалось обновить кэш котировок: {ex.GetBaseException().Message}");
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _priceSubscription?.Cancel();
        _priceSubscription?.Dispose();
        _lifetime.Dispose();
    }
}
