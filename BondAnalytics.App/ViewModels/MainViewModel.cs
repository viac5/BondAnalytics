using Domain;
using App.Localization;
using Grpc.Core;
using OxyPlot;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Tinkoff.InvestApi.V1;

namespace App.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public const string CouponChartKey = "Chart.Coupons";

        private static readonly TimeSpan PortfolioRefreshInterval = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromMinutes(5);

        private readonly IPortfolioService _portfolioService;
        private readonly IPortfolioHistoryRepository _historyRepository;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _workerCancellation = new();
        private readonly SemaphoreSlim _manualRefreshSignal = new(0, 1);
        private CancellationTokenSource? _chartRefreshCancellation;
        private List<PortfolioItemViewModel> _allItems = new();
        private CouponHistogramViewModel? _couponChart;
        private int _selectedFilterIndex;
        private int _selectedChartIndex;
        private int _selectedTabIndex;
        private string _apiStatusText = "Status.Connecting";
        private string _apiStatusDetails = "Status.FirstCheck";
        private string _statusMessage = "Common.Loading";
        private bool _isApiConnected;
        private bool _isDisposed;
        private DateTime? _lastPortfolioUpdate;
        private DateTime? _lastPriceUpdate;

        public ObservableCollection<PortfolioItemViewModel> Portfolio { get; } = new();

        public IReadOnlyList<string> AvailableCharts => Localization.Charts;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ICommand RefreshCommand { get; }
        public ICommand ShowAnalyticsCommand { get; }
        public ICommand ShowInvestmentPlanCommand { get; }
        public ICommand ToggleLanguageCommand { get; }
        public LocalizationManager Localization { get; }

        public PlotModel? PlotModel { get; private set; }
        public CouponHistogramViewModel? CurrentCouponChart => _couponChart;
        public PortfolioPeriodAnalyticsViewModel PeriodAnalytics { get; }
        public InvestmentPlanViewModel InvestmentPlan { get; }

        public decimal TotalPortfolioValue => _allItems.Sum(item => item.TotalValue);
        public decimal TotalNkd => _allItems.Sum(item => item.TotalAccruedInterest);
        public decimal TotalFullValue => _allItems.Sum(item => item.FullValue);
        public decimal TotalProfit => _allItems.Sum(item => item.Profit);
        public int PortfolioCount => _allItems.Count;

        public bool IsApiConnected
        {
            get => _isApiConnected;
            private set
            {
                if (_isApiConnected == value)
                    return;
                _isApiConnected = value;
                OnPropertyChanged();
            }
        }

        public string ApiStatusText
        {
            get => _apiStatusText.StartsWith("Status.") || _apiStatusText.StartsWith("Common.") ? Localization.Get(_apiStatusText) : _apiStatusText;
            private set
            {
                if (_apiStatusText == value)
                    return;
                _apiStatusText = value;
                OnPropertyChanged();
            }
        }

        public string ApiStatusDetails
        {
            get => _apiStatusDetails.StartsWith("Status.") || _apiStatusDetails.StartsWith("Common.") ? Localization.Get(_apiStatusDetails) : _apiStatusDetails;
            private set
            {
                if (_apiStatusDetails == value)
                    return;
                _apiStatusDetails = value;
                OnPropertyChanged();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage.StartsWith("Status.") || _statusMessage.StartsWith("Common.") ? Localization.Get(_statusMessage) : _statusMessage;
            private set
            {
                if (_statusMessage == value)
                    return;
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        public int SelectedFilterIndex
        {
            get => _selectedFilterIndex;
            set
            {
                if (_selectedFilterIndex == value)
                    return;
                _selectedFilterIndex = value;
                OnPropertyChanged();
                ApplyFilter();
            }
        }

        public int SelectedChartIndex
        {
            get => _selectedChartIndex;
            set
            {
                if (_selectedChartIndex == value || value < 0 || value >= LocalizationManager.ChartKeys.Count)
                    return;
                _selectedChartIndex = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedChart));
                OnPropertyChanged(nameof(SelectedChartDescription));
                UpdateChart();
            }
        }

        public string SelectedChart => Localization.Get(LocalizationManager.ChartKeys[_selectedChartIndex]);

        public string SelectedChartDescription => Localization.Get(LocalizationManager.ChartDescriptionKeys[_selectedChartIndex]);

        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (_selectedTabIndex == value)
                    return;
                _selectedTabIndex = value;
                OnPropertyChanged();
                if (value == 1)
                {
                    UpdateChart();
                }
                else if (value == 2)
                    _ = PeriodAnalytics.LoadPeriodAsync();
                else if (value == 3)
                    _ = InvestmentPlan.LoadAsync();
            }
        }

        public string LastUpdateText => _lastPortfolioUpdate.HasValue
            ? $"{Localization.Get("Status.PortfolioUpdated")} {_lastPortfolioUpdate.Value:dd.MM.yyyy HH:mm}"
            : Localization.Get("Status.LoadingPortfolio");

        public string LastPriceText => _lastPriceUpdate.HasValue
            ? $"{Localization.Get("Status.PortfolioUpdated")} {_lastPriceUpdate.Value:HH:mm:ss}"
            : Localization.Get("Status.StreamChecking");

        public MainViewModel(
            IPortfolioService portfolioService,
            IPortfolioHistoryRepository historyRepository,
            PortfolioPeriodAnalyticsViewModel periodAnalytics,
            InvestmentPlanViewModel investmentPlan,
            LocalizationManager localization,
            ILogger logger)
        {
            _portfolioService = portfolioService;
            _historyRepository = historyRepository;
            PeriodAnalytics = periodAnalytics;
            InvestmentPlan = investmentPlan;
            Localization = localization;
            _logger = logger.ForContext<MainViewModel>();

            RefreshCommand = new RelayCommand(_ => RequestRefresh());
            ShowAnalyticsCommand = new RelayCommand(_ => SelectedTabIndex = 2);
            ShowInvestmentPlanCommand = new RelayCommand(_ => SelectedTabIndex = 3);
            Localization.LanguageChanged += OnLanguageChanged;
            ToggleLanguageCommand = new RelayCommand(_ => Localization.ToggleLanguage());

            UpdateChart();
            _ = RunApiWorkerAsync(_workerCancellation.Token);
        }

        private void RequestRefresh()
        {
            if (_isDisposed)
                return;

            StatusMessage = Localization.Get("Status.RefreshRequested");
            if (_manualRefreshSignal.CurrentCount == 0)
                _manualRefreshSignal.Release();
        }

        private async Task RunApiWorkerAsync(CancellationToken cancellationToken)
        {
            var retryDelay = InitialRetryDelay;

            while (!cancellationToken.IsCancellationRequested)
            {
                var connectionStarted = DateTime.UtcNow;
                try
                {
                    await SetConnectionStatusAsync(false, Localization.Get("Status.Connecting"), Localization.Get("Status.LoadingPortfolio"));
                    var instrumentUids = await RefreshPortfolioAsync(cancellationToken);

                    if (instrumentUids.Count == 0)
                    {
                        await SetConnectionStatusAsync(true, Localization.Get("Status.Connected"), Localization.Get("Status.NoPositions"));
                        await WaitForRefreshOrTimeoutAsync(PortfolioRefreshInterval, cancellationToken);
                        retryDelay = InitialRetryDelay;
                        continue;
                    }

                    await SetConnectionStatusAsync(true, Localization.Get("Status.Connected"), Localization.Get("Status.StreamChecking"));
                    var subscriptionsChanged = await MonitorMarketDataAsync(instrumentUids, cancellationToken);
                    if (DateTime.UtcNow - connectionStarted >= TimeSpan.FromMinutes(1))
                        retryDelay = InitialRetryDelay;

                    if (subscriptionsChanged)
                        continue;

                    if (cancellationToken.IsCancellationRequested)
                        break;

                    throw new IOException("Поток рыночных цен завершился. Выполняется повторное подключение.");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "API worker connection attempt failed");
                    var retrySeconds = Math.Max(1, (int)Math.Ceiling(retryDelay.TotalSeconds));
                    var message = GetUserFacingError(ex);
                    await SetConnectionStatusAsync(
                        false,
                        Localization.Get("Status.Disconnected"),
                        $"{message} {Localization.Get("Status.Generic")}");
                    StatusMessage = message;

                    try
                    {
                        await Task.Delay(retryDelay, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, MaximumRetryDelay.TotalSeconds));
                }
            }
        }

        private async Task<IReadOnlyList<string>> RefreshPortfolioAsync(CancellationToken cancellationToken)
        {
            var portfolioData = await _portfolioService.GetPortfolioAsync();
            cancellationToken.ThrowIfCancellationRequested();
            var items = portfolioData.Positions;

            _logger.Information("Portfolio refreshed: {PositionCount} positions", items.Count);
            try
            {
                await _historyRepository.SaveSnapshotAsync(
                    new PortfolioSnapshot(DateTimeOffset.UtcNow, portfolioData.TotalValueRub),
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.Error(ex, "Could not persist portfolio value snapshot");
            }

            await RunOnUiAsync(() =>
            {
                _allItems = items.Select(item => new PortfolioItemViewModel(item)).ToList();
                PeriodAnalytics.SetCurrentPortfolioValue(portfolioData.TotalValueRub);
                InvestmentPlan.SetCurrentPortfolioValue(portfolioData.TotalValueRub);
                _lastPortfolioUpdate = DateTime.Now;
                OnPropertyChanged(nameof(LastUpdateText));
                ApplyFilter();
                UpdateChart();
                StatusMessage = $"Данные портфеля обновлены в {_lastPortfolioUpdate:HH:mm:ss}.";
            });

            await InvestmentPlan.RefreshActualContributionsAsync();

            return items.Select(item => item.Uid).Where(uid => !string.IsNullOrWhiteSpace(uid)).Distinct().ToList();
        }

        private async Task<bool> MonitorMarketDataAsync(IReadOnlyList<string> instrumentUids, CancellationToken cancellationToken)
        {
            using var monitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var monitorToken = monitorCancellation.Token;
            await using var enumerator = _portfolioService
                .SubscribePricesAsync(instrumentUids, monitorToken)
                .GetAsyncEnumerator(monitorToken);
            using var timer = new PeriodicTimer(PortfolioRefreshInterval);

            var nextPriceTask = enumerator.MoveNextAsync().AsTask();
            var periodicRefreshTask = timer.WaitForNextTickAsync(monitorToken).AsTask();
            var manualRefreshTask = _manualRefreshSignal.WaitAsync(monitorToken);

            try
            {
                while (!monitorToken.IsCancellationRequested)
                {
                    var completedTask = await Task.WhenAny(nextPriceTask, periodicRefreshTask, manualRefreshTask);
                    if (completedTask == nextPriceTask)
                    {
                        if (!await nextPriceTask)
                            return false;

                        var message = enumerator.Current;
                        if (message.LastPrice != null)
                            await RunOnUiAsync(() => UpdatePrice(message.LastPrice));

                        nextPriceTask = enumerator.MoveNextAsync().AsTask();
                        continue;
                    }

                    if (completedTask == periodicRefreshTask)
                    {
                        if (!await periodicRefreshTask)
                            return false;
                        periodicRefreshTask = timer.WaitForNextTickAsync(monitorToken).AsTask();
                    }
                    else
                    {
                        await manualRefreshTask;
                        manualRefreshTask = _manualRefreshSignal.WaitAsync(monitorToken);
                    }

                    var refreshedUids = await RefreshPortfolioAsync(monitorToken);
                    await SetConnectionStatusAsync(true, "API подключено", "Портфель обновлён; поток рыночных цен работает.");
                    if (!instrumentUids.SequenceEqual(refreshedUids))
                        return true;
                }

                return false;
            }
            finally
            {
                monitorCancellation.Cancel();
                await ObserveCompletionAsync(nextPriceTask);
                await ObserveCompletionAsync(periodicRefreshTask);
                await ObserveCompletionAsync(manualRefreshTask);
            }
        }

        private async Task WaitForRefreshOrTimeoutAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var delayTask = Task.Delay(timeout, waitCancellation.Token);
            var manualRefreshTask = _manualRefreshSignal.WaitAsync(waitCancellation.Token);
            await Task.WhenAny(delayTask, manualRefreshTask);
            waitCancellation.Cancel();
            await ObserveCompletionAsync(delayTask);
            await ObserveCompletionAsync(manualRefreshTask);
        }

        private static async Task ObserveCompletionAsync(Task task)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static Task RunOnUiAsync(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return Task.CompletedTask;
            }

            return dispatcher.InvokeAsync(action).Task;
        }

        private async Task SetConnectionStatusAsync(bool connected, string status, string details)
        {
            await RunOnUiAsync(() =>
            {
                IsApiConnected = connected;
                ApiStatusText = status;
                ApiStatusDetails = details;
            });
        }

        private void ApplyFilter()
        {
            IEnumerable<PortfolioItemViewModel> items = _allItems;

            if (SelectedFilterIndex == 1)
                items = items.Where(item => item.Group == "ОФЗ");
            else if (SelectedFilterIndex == 2)
                items = items.Where(item => item.Group == "Корпоративные");
            else if (SelectedFilterIndex == 3)
                items = items.Where(item => item.Group == "Фонды и прочее");

            Portfolio.Clear();
            foreach (var item in items)
                Portfolio.Add(item);

            OnPropertyChanged(nameof(TotalPortfolioValue));
            OnPropertyChanged(nameof(TotalNkd));
            OnPropertyChanged(nameof(TotalFullValue));
            OnPropertyChanged(nameof(TotalProfit));
            OnPropertyChanged(nameof(PortfolioCount));
        }

        private void UpdateChart()
        {
            _couponChart = SelectedChartIndex == 0
                ? new CouponHistogramViewModel(_allItems, Localization)
                : null;

            PlotModel = _couponChart?.PlotModel ??
                new PortfolioAnalyticsViewModel(_allItems, LocalizationManager.ChartKeys[SelectedChartIndex], Localization).PlotModel;
            OnPropertyChanged(nameof(PlotModel));
            OnPropertyChanged(nameof(CurrentCouponChart));
        }

        private void UpdatePrice(LastPrice last)
        {
            if (_isDisposed)
                return;

            var item = _allItems.FirstOrDefault(position => position.Uid == last.InstrumentUid);
            if (item == null)
                return;

            var marketPrice = last.Price.Units + last.Price.Nano / 1_000_000_000m;
            if (marketPrice <= 0 || (item.IsBond && item.Nominal <= 0))
                return;

            var previousPrice = item.CurrentPrice;
            item.CurrentPrice = MarketPriceConverter.ToUnitPrice(
                item.InstrumentType,
                marketPrice,
                item.Nominal);
            PeriodAnalytics.AddCurrentPortfolioValueDelta((item.CurrentPrice - previousPrice) * item.Quantity);
            _lastPriceUpdate = DateTime.Now;
            OnPropertyChanged(nameof(LastPriceText));
            OnPropertyChanged(nameof(TotalPortfolioValue));
            OnPropertyChanged(nameof(TotalFullValue));
            OnPropertyChanged(nameof(TotalProfit));
            ScheduleChartUpdate();
        }

        private void ScheduleChartUpdate()
        {
            if (SelectedTabIndex != 1)
                return;

            var previous = _chartRefreshCancellation;
            var current = new CancellationTokenSource();
            _chartRefreshCancellation = current;
            previous?.Cancel();
            _ = RefreshChartAfterDelayAsync(current);
        }

        private async Task RefreshChartAfterDelayAsync(CancellationTokenSource cancellation)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300), cancellation.Token);
                await RunOnUiAsync(() =>
                {
                    if (!cancellation.IsCancellationRequested)
                        UpdateChart();
                });
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            finally
            {
                if (ReferenceEquals(_chartRefreshCancellation, cancellation))
                    _chartRefreshCancellation = null;
                cancellation.Dispose();
            }
        }

        private static string GetUserFacingError(Exception exception)
        {
            if (HasUntrustedRootError(exception))
            {
                return "Windows не доверяет корневому сертификату API. Обновите корневые сертификаты Windows или установите официальный сертификат из доверенного источника.";
            }

            if (exception.Message.Contains("BOND_ANALYTICS_TOKEN", StringComparison.OrdinalIgnoreCase) ||
                exception.Message.Contains("Токен пустой", StringComparison.OrdinalIgnoreCase))
            {
                return "Не найден токен API. Проверьте пользовательскую переменную BOND_ANALYTICS_TOKEN и перезапустите приложение.";
            }

            if (exception.Message.Contains("Нет доступных счетов", StringComparison.OrdinalIgnoreCase))
                return "У API-токена нет доступного брокерского счёта.";

            if (exception is IOException)
                return "Поток рыночных цен прервался. Проверьте подключение к интернету; приложение повторит попытку.";

            if (exception is TimeoutException or TaskCanceledException)
                return "Истекло время ожидания ответа API. Проверьте подключение к интернету; приложение повторит попытку.";

            if (exception is RpcException rpcException)
            {
                return rpcException.StatusCode switch
                {
                    StatusCode.Unauthenticated => "API отклонил токен. Проверьте его актуальность и права чтения.",
                    StatusCode.PermissionDenied => "У токена нет прав на чтение портфеля.",
                    StatusCode.Unavailable => "Сервис API недоступен или нет сетевого подключения.",
                    StatusCode.DeadlineExceeded => "API не ответил вовремя. Проверьте сеть; приложение повторит попытку.",
                    _ => $"Ошибка API ({rpcException.StatusCode}). Подробности записаны в журнал."
                };
            }

            return "Не удалось обновить данные API. Проверьте интернет и журнал приложения; повторная попытка будет выполнена автоматически.";
        }

        private static bool HasUntrustedRootError(Exception exception)
        {
            if (exception.Message.Contains("UntrustedRoot", StringComparison.OrdinalIgnoreCase) ||
                exception.Message.Contains("untrusted root", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return exception.InnerException != null && HasUntrustedRootError(exception.InnerException);
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _workerCancellation.Cancel();
            _chartRefreshCancellation?.Cancel();
            PeriodAnalytics.Dispose();
            InvestmentPlan.Dispose();
            Localization.LanguageChanged -= OnLanguageChanged;
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            OnPropertyChanged(nameof(AvailableCharts));
            OnPropertyChanged(nameof(SelectedChart));
            OnPropertyChanged(nameof(SelectedChartDescription));
            UpdateChart();
        }
    }
}