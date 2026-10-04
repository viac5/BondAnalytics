using Domain;
using Grpc.Core;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Annotations;
using OxyPlot.Legends;
using OxyPlot.Series;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace App.ViewModels
{
    public sealed class PortfolioPeriodAnalyticsViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly IPortfolioService _portfolioService;
        private readonly IPortfolioHistoryRepository _historyRepository;
        private readonly ILogger _logger;
        private CancellationTokenSource? _loadCancellation;
        private string _selectedRange = "3 месяца";
        private DateTime? _fromDate = DateTime.Today.AddMonths(-3);
        private DateTime? _toDate = DateTime.Today;
        private string _statusMessage = "Выберите период и загрузите аналитику.";
        private string _depositsText = "—";
        private string _withdrawalsText = "—";
        private string _netCashFlowText = "—";
        private string _periodProfitText = "—";
        private string _periodReturnText = "—";
        private string _depositCountText = "0";
        private string _withdrawalCountText = "0";
        private string _tradeCountText = "0";
        private string _currentPortfolioValueText = "Загрузка...";
        private decimal? _currentPortfolioValueRub;
        private bool _isLoading;

        public ObservableCollection<PortfolioOperationRowViewModel> Operations { get; } = new();
        public IReadOnlyList<string> PeriodOptions { get; } = new[]
        {
            "1 месяц",
            "3 месяца",
            "6 месяцев",
            "Год",
            "10 лет",
            "Свой период"
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action? OpenPortfolioChartRequested;

        public ICommand LoadCommand { get; }
        public ICommand OpenPortfolioChartCommand { get; }
        public PlotModel PortfolioValuePlotModel { get; private set; } = CreateEmptyPlot("Загрузите аналитику за выбранный период");

        public string SelectedRange
        {
            get => _selectedRange;
            set
            {
                if (_selectedRange == value)
                    return;

                _selectedRange = value;
                OnPropertyChanged();
                if (value != "Свой период")
                {
                    SetPresetRange(value);
                    _ = LoadPeriodAsync();
                }
            }
        }

        public DateTime? FromDate
        {
            get => _fromDate;
            set
            {
                if (_fromDate == value)
                    return;
                _fromDate = value;
                OnPropertyChanged();
            }
        }

        public DateTime? ToDate
        {
            get => _toDate;
            set
            {
                if (_toDate == value)
                    return;
                _toDate = value;
                OnPropertyChanged();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetField(ref _statusMessage, value);
        }

        public string DepositsText
        {
            get => _depositsText;
            private set => SetField(ref _depositsText, value);
        }

        public string WithdrawalsText
        {
            get => _withdrawalsText;
            private set => SetField(ref _withdrawalsText, value);
        }

        public string NetCashFlowText
        {
            get => _netCashFlowText;
            private set => SetField(ref _netCashFlowText, value);
        }

        public string PeriodProfitText
        {
            get => _periodProfitText;
            private set => SetField(ref _periodProfitText, value);
        }

        public string PeriodReturnText
        {
            get => _periodReturnText;
            private set => SetField(ref _periodReturnText, value);
        }

        public string DepositCountText
        {
            get => _depositCountText;
            private set => SetField(ref _depositCountText, value);
        }

        public string WithdrawalCountText
        {
            get => _withdrawalCountText;
            private set => SetField(ref _withdrawalCountText, value);
        }

        public string TradeCountText
        {
            get => _tradeCountText;
            private set => SetField(ref _tradeCountText, value);
        }

        public string CurrentPortfolioValueText
        {
            get => _currentPortfolioValueText;
            private set => SetField(ref _currentPortfolioValueText, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set => SetField(ref _isLoading, value);
        }

        public PortfolioPeriodAnalyticsViewModel(
            IPortfolioService portfolioService,
            IPortfolioHistoryRepository historyRepository,
            ILogger logger)
        {
            _portfolioService = portfolioService;
            _historyRepository = historyRepository;
            _logger = logger.ForContext<PortfolioPeriodAnalyticsViewModel>();
            LoadCommand = new RelayCommand(async _ => await LoadPeriodAsync());
            OpenPortfolioChartCommand = new RelayCommand(_ => OpenPortfolioChartRequested?.Invoke());
        }

        public async Task LoadPeriodAsync()
        {
            if (_fromDate == null || _toDate == null || _fromDate.Value.Date > _toDate.Value.Date)
            {
                StatusMessage = "Укажите корректный период: дата начала должна быть не позже даты окончания.";
                return;
            }
            if (_toDate.Value.Date > DateTime.Today)
            {
                StatusMessage = "Дата окончания периода не может быть в будущем.";
                return;
            }

            var previous = _loadCancellation;
            var current = new CancellationTokenSource();
            _loadCancellation = current;
            previous?.Cancel();
            IsLoading = true;
            StatusMessage = "Загружаем операции и историю портфеля...";

            try
            {
                var fromLocal = DateTime.SpecifyKind(_fromDate.Value.Date, DateTimeKind.Local);
                var toLocal = DateTime.SpecifyKind(_toDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Local);
                var from = new DateTimeOffset(fromLocal);
                var to = new DateTimeOffset(toLocal);

                var operationsTask = _portfolioService.GetOperationsAsync(DateTimeOffset.UnixEpoch, to, current.Token);
                var snapshotsTask = _historyRepository.GetSnapshotsAsync(from, to, current.Token);
                await Task.WhenAll(operationsTask, snapshotsTask);
                current.Token.ThrowIfCancellationRequested();

                var operationHistory = await operationsTask;
                var operations = operationHistory
                    .Where(operation => operation.Date >= from && operation.Date <= to)
                    .ToList();
                var snapshots = await snapshotsTask;
                var summary = PortfolioPeriodCalculator.Calculate(operationHistory, snapshots, from, to);
                var realizedTrades = RealizedTradeProfitCalculator.Calculate(operationHistory);
                UpdateOperations(operations, summary, realizedTrades);
                UpdateSummary(summary);
                PortfolioValuePlotModel = CreatePortfolioValuePlot(snapshots, operations, from, to);
                OnPropertyChanged(nameof(PortfolioValuePlotModel));

                var localFrom = from.LocalDateTime.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("ru-RU"));
                var localTo = to.LocalDateTime.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("ru-RU"));
                StatusMessage = $"Период {localFrom}–{localTo}. Снимков стоимости: {snapshots.Count}; операций: {operations.Count}. Результат продажи рассчитан по FIFO, купоны учитываются только для облигаций.";
            }
            catch (OperationCanceledException) when (current.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load period portfolio analytics");
                StatusMessage = GetUserFacingError(ex);
            }
            finally
            {
                if (ReferenceEquals(_loadCancellation, current))
                {
                    _loadCancellation = null;
                    IsLoading = false;
                }
                current.Dispose();
            }
        }

        public void SetCurrentPortfolioValue(decimal valueRub)
        {
            _currentPortfolioValueRub = valueRub;
            CurrentPortfolioValueText = $"{valueRub:N0} ₽";
        }

        public void AddCurrentPortfolioValueDelta(decimal deltaRub)
        {
            if (_currentPortfolioValueRub == null)
                return;
            SetCurrentPortfolioValue(_currentPortfolioValueRub.Value + deltaRub);
        }

        private void SetPresetRange(string range)
        {
            var today = DateTime.Today;
            FromDate = range switch
            {
                "1 месяц" => today.AddMonths(-1),
                "3 месяца" => today.AddMonths(-3),
                "6 месяцев" => today.AddMonths(-6),
                "Год" => today.AddYears(-1),
                "10 лет" => today.AddYears(-10),
                _ => FromDate
            };
            ToDate = today;
        }

        private void UpdateOperations(
            IReadOnlyList<PortfolioOperation> operations,
            PortfolioPeriodSummary summary,
            IReadOnlyDictionary<string, RealizedTradeResult> realizedTrades)
        {
            Operations.Clear();
            foreach (var operation in operations.OrderByDescending(item => item.Date))
            {
                realizedTrades.TryGetValue(operation.Id, out var realizedTrade);
                Operations.Add(new PortfolioOperationRowViewModel(operation, realizedTrade));
            }

            DepositCountText = summary.DepositCount.ToString("N0");
            WithdrawalCountText = summary.WithdrawalCount.ToString("N0");
            TradeCountText = summary.TradeCount.ToString("N0");
            DepositsText = FormatAmounts(summary.Deposits);
            WithdrawalsText = FormatAmounts(summary.Withdrawals);
            NetCashFlowText = FormatAmounts(summary.NetCashFlows);
        }

        private void UpdateSummary(PortfolioPeriodSummary summary)
        {
            if (summary.ReturnUnavailableReason != null)
            {
                PeriodProfitText = summary.ProfitRub.HasValue
                    ? $"{summary.ProfitRub.Value:+#,0.00;-#,0.00;0.00} ₽"
                    : summary.ReturnUnavailableReason;
                PeriodReturnText = "—";
                return;
            }

            PeriodProfitText = summary.ProfitRub.HasValue
                ? $"{summary.ProfitRub.Value:+#,0.00;-#,0.00;0.00} ₽"
                : "—";
            PeriodReturnText = summary.Return.HasValue ? $"{summary.Return.Value:P2}" : "—";
        }

        private static PlotModel CreatePortfolioValuePlot(
            IReadOnlyList<PortfolioSnapshot> snapshots,
            IReadOnlyList<PortfolioOperation> operations,
            DateTimeOffset from,
            DateTimeOffset to)
        {
            var model = new PlotModel
            {
                Title = "Стоимость всего портфеля",
                Background = OxyColors.White,
                PlotAreaBackground = OxyColors.White,
                TextColor = OxyColor.FromRgb(43, 57, 52),
                PlotAreaBorderColor = OxyColor.FromRgb(220, 227, 222)
            };

            var dateAxis = new DateTimeAxis
            {
                Position = AxisPosition.Bottom,
                StringFormat = "dd MMM",
                IntervalType = DateTimeIntervalType.Auto,
                MajorGridlineStyle = LineStyle.None,
                AxislineColor = OxyColor.FromRgb(220, 227, 222)
            };
            var valueAxis = new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Стоимость, ₽",
                StringFormat = "#,##0",
                MinimumPadding = 0.08,
                MaximumPadding = 0.12,
                MajorGridlineStyle = LineStyle.Solid,
                MajorGridlineColor = OxyColor.FromRgb(232, 237, 233),
                AxislineColor = OxyColor.FromRgb(220, 227, 222)
            };
            model.Axes.Add(dateAxis);
            model.Axes.Add(valueAxis);

            var rangeSnapshots = snapshots
                .Where(snapshot => snapshot.CapturedAt >= from && snapshot.CapturedAt <= to)
                .ToList();
            var chartSnapshots = DownsampleSnapshots(rangeSnapshots, 1800);

            if (rangeSnapshots.Count == 0)
            {
                model.Annotations.Add(new TextAnnotation
                {
                    Text = "История стоимости появится после первого успешного подключения к API",
                    TextColor = OxyColor.FromRgb(104, 119, 115),
                    Stroke = OxyColors.Transparent,
                    TextPosition = new DataPoint(0.5, 0.5),
                    TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center,
                    TextVerticalAlignment = OxyPlot.VerticalAlignment.Middle
                });
                return model;
            }

            var valueSeries = new LineSeries
            {
                Title = "Стоимость портфеля",
                Color = OxyColor.FromRgb(35, 116, 91),
                StrokeThickness = 2.5,
                MarkerType = chartSnapshots.Count < 3 ? MarkerType.Circle : MarkerType.None,
                MarkerSize = 4,
                TrackerFormatString = "{2:dd.MM.yyyy HH:mm}\nСтоимость: {4:N2} ₽"
            };
            foreach (var snapshot in chartSnapshots)
            {
                valueSeries.Points.Add(new DataPoint(
                    DateTimeAxis.ToDouble(snapshot.CapturedAt.LocalDateTime),
                    (double)snapshot.TotalValueRub));
            }
            model.Series.Add(valueSeries);

            AddFlowSeries(model, snapshots, operations, PortfolioOperationKind.Deposit, "Пополнения", OxyColor.FromRgb(42, 126, 94), MarkerType.Triangle, from, to);
            AddFlowSeries(model, snapshots, operations, PortfolioOperationKind.Withdrawal, "Выводы", OxyColor.FromRgb(186, 70, 62), MarkerType.Diamond, from, to);

            model.Legends.Add(new Legend
            {
                LegendPlacement = LegendPlacement.Outside,
                LegendPosition = LegendPosition.TopRight,
                LegendOrientation = LegendOrientation.Horizontal
            });
            return model;
        }

        private static List<PortfolioSnapshot> DownsampleSnapshots(
            IReadOnlyList<PortfolioSnapshot> snapshots,
            int maximumPoints)
        {
            if (snapshots.Count <= maximumPoints || maximumPoints < 4)
                return snapshots.ToList();

            var bucketCount = Math.Max(1, (maximumPoints - 2) / 2);
            var result = new List<PortfolioSnapshot>(maximumPoints) { snapshots[0] };
            var interiorCount = snapshots.Count - 2;

            for (var bucket = 0; bucket < bucketCount; bucket++)
            {
                var start = 1 + (bucket * interiorCount / bucketCount);
                var end = 1 + ((bucket + 1) * interiorCount / bucketCount);
                if (start >= end)
                    continue;

                var minimumIndex = start;
                var maximumIndex = start;
                for (var index = start + 1; index < end; index++)
                {
                    if (snapshots[index].TotalValueRub < snapshots[minimumIndex].TotalValueRub)
                        minimumIndex = index;
                    if (snapshots[index].TotalValueRub > snapshots[maximumIndex].TotalValueRub)
                        maximumIndex = index;
                }

                if (minimumIndex < maximumIndex)
                {
                    result.Add(snapshots[minimumIndex]);
                    if (maximumIndex != minimumIndex)
                        result.Add(snapshots[maximumIndex]);
                }
                else
                {
                    result.Add(snapshots[maximumIndex]);
                    if (minimumIndex != maximumIndex)
                        result.Add(snapshots[minimumIndex]);
                }
            }

            result.Add(snapshots[^1]);
            return result;
        }

        private static void AddFlowSeries(
            PlotModel model,
            IReadOnlyList<PortfolioSnapshot> snapshots,
            IReadOnlyList<PortfolioOperation> operations,
            PortfolioOperationKind kind,
            string title,
            OxyColor color,
            MarkerType markerType,
            DateTimeOffset from,
            DateTimeOffset to)
        {
            var flowSeries = new ScatterSeries
            {
                Title = title,
                MarkerType = markerType,
                MarkerFill = color,
                MarkerStroke = OxyColors.White,
                MarkerStrokeThickness = 1,
                MarkerSize = 11,
                TrackerFormatString = "{Tag}"
            };

            var flows = operations
                .Where(item => item.Kind == kind && item.Date >= from && item.Date <= to)
                .ToList();
            foreach (var flow in flows)
            {
                var nearest = FindNearestSnapshot(snapshots, flow.Date);
                if (nearest == null)
                    continue;

                var x = DateTimeAxis.ToDouble(flow.Date.LocalDateTime);
                var y = (double)nearest.TotalValueRub;
                var flowName = kind == PortfolioOperationKind.Deposit ? "Пополнение" : "Вывод";
                flowSeries.Points.Add(new ScatterPoint(
                    x,
                    y,
                    11,
                    (double)flow.Amount,
                    $"{flowName}: {flow.Amount:N2} {GetCurrencySymbol(flow.Currency)}\n{flow.Date:dd.MM.yyyy HH:mm}"));
            }
            model.Series.Add(flowSeries);
        }

        private static PortfolioSnapshot? FindNearestSnapshot(IReadOnlyList<PortfolioSnapshot> snapshots, DateTimeOffset date)
        {
            if (snapshots.Count == 0)
                return null;

            var low = 0;
            var high = snapshots.Count - 1;
            while (low <= high)
            {
                var middle = low + ((high - low) / 2);
                var comparison = snapshots[middle].CapturedAt.CompareTo(date);
                if (comparison == 0)
                    return snapshots[middle];
                if (comparison < 0)
                    low = middle + 1;
                else
                    high = middle - 1;
            }

            if (low >= snapshots.Count)
                return snapshots[^1];
            if (high < 0)
                return snapshots[0];
            return date - snapshots[high].CapturedAt <= snapshots[low].CapturedAt - date
                ? snapshots[high]
                : snapshots[low];
        }

        private static string FormatAmounts(IEnumerable<CurrencyTotal> totals)
        {
            var formatted = totals
                .Select(total => $"{total.Amount:N0} {GetCurrencySymbol(total.Currency)}")
                .ToList();
            return formatted.Count == 0 ? "0 ₽" : string.Join(" · ", formatted);
        }

        private static string NormalizeCurrency(string currency) =>
            currency.Equals("RUR", StringComparison.OrdinalIgnoreCase) ? "RUB" : currency.ToUpperInvariant();

        private static string GetCurrencySymbol(string currency) => NormalizeCurrency(currency) switch
        {
            "RUB" => "₽",
            "USD" => "$",
            "EUR" => "€",
            var value => value
        };

        private static string GetUserFacingError(Exception exception)
        {
            if (exception is RpcException rpcException)
            {
                return rpcException.StatusCode switch
                {
                    StatusCode.Unauthenticated => "API отклонил токен. Проверьте его актуальность.",
                    StatusCode.PermissionDenied => "У токена нет прав на чтение операций.",
                    StatusCode.Unavailable => "Сервис операций API недоступен или нет подключения к интернету.",
                    _ => "Не удалось загрузить операции. Подробности записаны в журнал приложения."
                };
            }

            if (HasUntrustedRootError(exception))
                return "Windows не доверяет корневому сертификату API. Обновите корневые сертификаты Windows.";

            return "Не удалось загрузить аналитику. Проверьте даты периода, подключение к API и журнал приложения.";
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

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private static PlotModel CreateEmptyPlot(string message)
        {
            var model = new PlotModel
            {
                Background = OxyColors.White,
                PlotAreaBackground = OxyColors.White,
                TextColor = OxyColor.FromRgb(104, 119, 115)
            };
            model.Annotations.Add(new TextAnnotation
            {
                Text = message,
                Stroke = OxyColors.Transparent,
                TextPosition = new DataPoint(0.5, 0.5),
                TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center,
                TextVerticalAlignment = OxyPlot.VerticalAlignment.Middle
            });
            return model;
        }

        public void Dispose()
        {
            _loadCancellation?.Cancel();
        }
    }
}
