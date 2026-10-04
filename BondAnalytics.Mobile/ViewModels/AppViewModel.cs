using CommunityToolkit.Mvvm.ComponentModel;
using Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Graphics;
using System.Collections.ObjectModel;
using System.Globalization;
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
        Color.FromArgb("#AB7548"), Color.FromArgb("#4D7791")
    ];
    private readonly IPortfolioService _portfolioService;
    private readonly IPortfolioHistoryRepository _historyRepository;
    private readonly ILogger<AppViewModel> _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _priceSubscription;
    private string _subscribedUids = string.Empty;
    private bool _initialized;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "Загружаем портфель";
    [ObservableProperty] private int selectedFilterIndex;
    [ObservableProperty] private int selectedSortIndex;
    [ObservableProperty] private int selectedChartIndex;
    [ObservableProperty] private DateTime? lastUpdated;
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
        await RefreshPortfolioAsync();
        _ = RefreshPeriodicallyAsync(_lifetime.Token);
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
            Positions.Clear();
            foreach (var item in data.Positions)
                Positions.Add(new PortfolioPositionViewModel(item));
            StartPriceSubscription();

            LastUpdated = DateTimeOffset.UtcNow.UtcDateTime;
            OnPropertyChanged(nameof(LastUpdatedText));
            OnPropertyChanged(nameof(TotalPortfolioValue));
            OnPropertyChanged(nameof(TotalNkd));
            OnPropertyChanged(nameof(TotalFullValue));
            OnPropertyChanged(nameof(TotalProfit));
            OnPropertyChanged(nameof(TotalProfitColor));
            OnPropertyChanged(nameof(TotalProfitPercentColor));
            OnPropertyChanged(nameof(TotalProfitPercent));
            OnPropertyChanged(nameof(WeightedYield));
            OnPropertyChanged(nameof(PositionCount));
            ApplyFilter();
            RefreshChart();

            try
            {
                await Task.Run(
                    () => _historyRepository.SaveSnapshotAsync(
                        new PortfolioSnapshot(DateTimeOffset.UtcNow, data.TotalValueRub),
                        timeout.Token),
                    timeout.Token);
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
                var cause = ex.GetBaseException();
                StatusMessage = $"Портфель обновлён, но не удалось сохранить историю стоимости: {cause.GetType().Name}: {cause.Message}";
                return;
            }

            StatusMessage = $"Портфель обновлён · {data.Positions.Count} позиций";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (timeout.IsCancellationRequested && !_lifetime.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Portfolio request exceeded the {Timeout} second timeout",
                RequestTimeout.TotalSeconds);
            StatusMessage = $"Запрос занял больше {RequestTimeout.TotalSeconds:0} секунд. Проверьте соединение и попробуйте снова.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh mobile portfolio");
            StatusMessage = $"Не удалось загрузить портфель: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
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
        ChartPoints.Clear();
        ChartSeries.Clear();
        if (Positions.Count == 0)
            return;

        if (SelectedChartIndex == 0)
        {
            var cutoff = DateTime.Today.AddYears(1);
            var coupons = new SortedDictionary<DateTime, Dictionary<string, decimal>>();
            foreach (var position in Positions.Where(position =>
                         position.Coupon > 0 && position.CouponsPerYear > 0 && position.Item.NextCouponDate is not null))
            {
                var date = position.Item.NextCouponDate!.Value.Date;
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

            var tickers = coupons.Values
                .SelectMany(month => month.Keys)
                .Distinct(StringComparer.Ordinal)
                .Select(ticker => new
                {
                    Ticker = ticker,
                    Total = coupons.Values.Sum(month => month.GetValueOrDefault(ticker))
                })
                .OrderByDescending(item => item.Total)
                .ToList();
            var displayedTickers = tickers.Take(10).Select(item => item.Ticker).ToHashSet(StringComparer.Ordinal);
            var seriesTickers = tickers.Take(10).Select(item => item.Ticker).ToList();
            var namesByTicker = Positions
                .GroupBy(position => position.Ticker, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.Ordinal);
            if (tickers.Count > 10)
                seriesTickers.Add("Другие бумаги");

            for (var index = 0; index < seriesTickers.Count; index++)
            {
                var ticker = seriesTickers[index];
                var amounts = coupons.Select(month => ticker == "Другие бумаги"
                        ? month.Value.Where(instrument => !displayedTickers.Contains(instrument.Key))
                            .Sum(instrument => instrument.Value)
                        : month.Value.GetValueOrDefault(ticker))
                    .ToList();
                var name = ticker == "Другие бумаги" ? ticker : namesByTicker[ticker];
                ChartSeries.Add(new ChartSeries(name, ChartColors[index % ChartColors.Length], amounts));
            }

            var couponMonths = coupons.ToList();
            for (var index = 0; index < couponMonths.Count; index++)
            {
                var month = couponMonths[index];
                ChartPoints.Add(new ChartPoint(
                    month.Key.ToString("MMM", CultureInfo.GetCultureInfo("ru-RU")),
                    ChartSeries.Sum(series => series.Values[index])));
            }
            return;
        }

        if (SelectedChartIndex == 6)
        {
            foreach (var group in Positions.GroupBy(position => position.Group))
                ChartPoints.Add(new ChartPoint(group.Key, group.Sum(position => position.TotalValue)));
            return;
        }

        var values = Positions.Select(position => new ChartPoint(
                position.Ticker,
                SelectedChartIndex switch
                {
                    1 => position.TotalValue,
                    2 => position.Profit,
                    3 => position.CurrentYield,
                    4 => position.TotalAccruedInterest,
                    _ => position.Coupon * position.CouponsPerYear * position.Quantity
                },
                IsPercent: SelectedChartIndex == 3))
            .Where(point => SelectedChartIndex != 3 || point.Value > 0)
            .OrderByDescending(point => Math.Abs(point.Value))
            .Take(20)
            .OrderBy(point => point.Value);
        foreach (var point in values)
            ChartPoints.Add(point);
    }

    private void ApplyFilter()
    {
        VisiblePositions.Clear();
        var selectedGroup = SelectedFilterIndex == 0 ? null : FilterLabels[SelectedFilterIndex];
        var filtered = Positions.Where(position => selectedGroup is null || position.Group == selectedGroup);
        filtered = SelectedSortIndex switch
        {
            1 => filtered.OrderBy(position => position.Name, StringComparer.CurrentCultureIgnoreCase),
            2 => filtered.OrderByDescending(position => position.Profit),
            3 => filtered.OrderByDescending(position => position.CurrentYield),
            _ => filtered.OrderByDescending(position => position.FullValue)
        };
        foreach (var position in filtered)
            VisiblePositions.Add(position);
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
        _ = ListenForPricesAsync(uids, _priceSubscription.Token);
    }

    private async Task ListenForPricesAsync(IReadOnlyList<string> uids, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Run(async () =>
            {
                await foreach (var update in _portfolioService.SubscribePricesAsync(uids, cancellationToken))
                {
                    if (update.LastPrice is null)
                        continue;

                    var lastPrice = update.LastPrice;
                    var quote = lastPrice.Price.Units + lastPrice.Price.Nano / 1_000_000_000m;
                    MainThread.BeginInvokeOnMainThread(() => ApplyLivePrice(lastPrice.InstrumentUid, quote));
                }
            }, cancellationToken);
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

    private void ApplyLivePrice(string uid, decimal quote)
    {
        var position = Positions.FirstOrDefault(item => item.Uid == uid);
        if (position is null)
            return;

        try
        {
            var price = MarketPriceConverter.ToUnitPrice(
                position.Item.InstrumentType,
                quote,
                position.Item.Nominal);
            position.UpdatePrice(price);
            OnPropertyChanged(nameof(TotalPortfolioValue));
            OnPropertyChanged(nameof(TotalNkd));
            OnPropertyChanged(nameof(TotalFullValue));
            OnPropertyChanged(nameof(TotalProfit));
            OnPropertyChanged(nameof(TotalProfitColor));
            OnPropertyChanged(nameof(TotalProfitPercentColor));
            OnPropertyChanged(nameof(TotalProfitPercent));
            OnPropertyChanged(nameof(WeightedYield));
            ApplyFilter();
            RefreshChart();
            RealTimeStatusText = $"Котировки обновляются · {DateTime.Now:HH:mm}";
        }
        catch (ArgumentOutOfRangeException ex)
        {
            _logger.LogError(ex, "Could not convert a live quote for {Ticker}", position.Ticker);
            RealTimeStatusText = $"Нет данных для пересчёта котировки {position.Ticker}.";
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
