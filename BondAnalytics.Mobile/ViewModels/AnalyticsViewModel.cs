using CommunityToolkit.Mvvm.ComponentModel;
using Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Graphics;
using System.Collections.ObjectModel;
using System.Globalization;

namespace BondAnalytics.Mobile.ViewModels;

public partial class AnalyticsViewModel : ObservableObject
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(90);
    private readonly IPortfolioService _portfolioService;
    private readonly IPortfolioHistoryRepository _historyRepository;
    private readonly ILogger<AnalyticsViewModel> _logger;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private DateTime fromDate = DateTime.Today.AddDays(-30);
    [ObservableProperty] private DateTime toDate = DateTime.Today;
    [ObservableProperty] private string statusMessage = "Выберите период и загрузите аналитику";
    [ObservableProperty] private string depositSummary = "—";
    [ObservableProperty] private string withdrawalSummary = "—";
    [ObservableProperty] private string netFlowSummary = "—";
    [ObservableProperty] private string resultSummary = "Недостаточно данных";
    [ObservableProperty] private string returnSummary = "—";
    [ObservableProperty] private string operationCounts = "—";
    [ObservableProperty] private string currentValueSummary = "—";
    [ObservableProperty] private string returnNote = string.Empty;

    public ObservableCollection<AnalyticsOperationViewModel> Operations { get; } = new();
    public ObservableCollection<ChartPoint> PortfolioHistory { get; } = new();
    public IReadOnlyList<string> Presets { get; } = ["30 дней", "90 дней", "С начала года", "Свой период"];

    public AnalyticsViewModel(
        IPortfolioService portfolioService,
        IPortfolioHistoryRepository historyRepository,
        ILogger<AnalyticsViewModel> logger)
    {
        _portfolioService = portfolioService;
        _historyRepository = historyRepository;
        _logger = logger;
    }

    public void ApplyPreset(int index)
    {
        ToDate = DateTime.Today;
        FromDate = index switch
        {
            1 => DateTime.Today.AddDays(-90),
            2 => new DateTime(DateTime.Today.Year, 1, 1),
            _ => DateTime.Today.AddDays(-30)
        };
    }

    public async Task LoadAsync()
    {
        if (IsBusy)
            return;
        if (FromDate.Date > ToDate.Date)
        {
            StatusMessage = "Начало периода не может быть позже его окончания.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Загружаем операции и историю стоимости";
        using var timeout = new CancellationTokenSource(RequestTimeout);
        try
        {
            var from = new DateTimeOffset(FromDate.Date);
            var to = new DateTimeOffset(ToDate.Date.AddDays(1)).AddTicks(-1);
            var operationsTask = Task.Run(() =>
                _portfolioService.GetOperationsAsync(DateTimeOffset.UnixEpoch, to, timeout.Token), timeout.Token);
            var snapshotsTask = Task.Run(
                () => _historyRepository.GetSnapshotsAsync(from, to, timeout.Token), timeout.Token);
            await Task.WhenAll(operationsTask, snapshotsTask);

            var allOperations = await operationsTask;
            var operationsInPeriod = allOperations
                .Where(operation => operation.Date >= from && operation.Date <= to)
                .OrderByDescending(operation => operation.Date)
                .ToList();
            var snapshots = await snapshotsTask;
            StatusMessage = "Рассчитываем доходность и результат операций";
            var calculations = await Task.Run(() =>
                (Summary: PortfolioPeriodCalculator.Calculate(allOperations, snapshots, from, to),
                    Realized: RealizedTradeProfitCalculator.Calculate(allOperations)),
                timeout.Token);
            var summary = calculations.Summary;
            var realized = calculations.Realized;

            DepositSummary = FormatTotals(summary.Deposits);
            WithdrawalSummary = FormatTotals(summary.Withdrawals);
            NetFlowSummary = FormatTotals(summary.NetCashFlows);
            ResultSummary = summary.ProfitRub is { } profit
                ? $"{profit:+#,0;-#,0;0} ₽"
                : "Недостаточно данных";
            ReturnSummary = summary.Return is { } periodReturn
                ? periodReturn.ToString("P1", CultureInfo.GetCultureInfo("ru-RU"))
                : "—";
            OperationCounts = $"{summary.DepositCount} пополн. · {summary.WithdrawalCount} вывода · {summary.TradeCount} сделок";
            ReturnNote = summary.ReturnUnavailableReason ?? "Доходность рассчитана по снимкам стоимости и денежным потокам.";
            CurrentValueSummary = snapshots.Count == 0
                ? "История ещё накапливается"
                : $"{snapshots[^1].TotalValueRub:N0} ₽";

            Operations.Clear();
            foreach (var operation in operationsInPeriod.Take(250))
            {
                realized.TryGetValue(operation.Id, out var trade);
                Operations.Add(new AnalyticsOperationViewModel(operation, trade));
            }

            PortfolioHistory.Clear();
            foreach (var snapshot in snapshots)
                PortfolioHistory.Add(new ChartPoint(snapshot.CapturedAt.ToLocalTime().ToString("dd.MM"), snapshot.TotalValueRub));

            StatusMessage = $"{operationsInPeriod.Count} операций за выбранный период";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load portfolio period analytics");
            var cause = ex.GetBaseException();
            StatusMessage = timeout.IsCancellationRequested
                ? $"Не удалось завершить расчёт за {RequestTimeout.TotalSeconds:0} секунд. Проверьте соединение и попробуйте ещё раз."
                : $"Не удалось загрузить аналитику: {cause.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string FormatTotals(IReadOnlyList<CurrencyTotal> totals) =>
        totals.Count == 0
            ? "0 ₽"
            : string.Join(" · ", totals.Select(total =>
                $"{total.Amount:N0} {total.Currency}"));
}

public sealed class AnalyticsOperationViewModel
{
    public PortfolioOperation Operation { get; }
    public RealizedTradeResult? Realized { get; }
    public string DateText => Operation.Date.ToLocalTime().ToString("dd MMM yyyy, HH:mm", CultureInfo.GetCultureInfo("ru-RU"));
    public string TypeText => Operation.TypeName;
    public string InstrumentText => string.IsNullOrWhiteSpace(Operation.InstrumentName)
        ? Operation.Kind.ToString()
        : Operation.InstrumentName;
    public string QuantityText => Operation.Quantity > 0 ? $"× {Operation.Quantity:N2}" : string.Empty;
    public string AmountText => $"{Operation.Amount:N2} {Operation.Currency}";
    public string ResultText => Realized?.ProfitIncludingCoupons is { } profit
        ? $"Результат с купонами: {profit:+#,0;-#,0;0} {Operation.Currency}"
        : Realized?.ProfitByPrice is { } priceProfit
            ? $"По цене: {priceProfit:+#,0;-#,0;0} {Operation.Currency}"
            : Realized?.UnavailableReason ?? string.Empty;
    public string IconGlyph => Operation.Kind switch
    {
        PortfolioOperationKind.Deposit => "＋",
        PortfolioOperationKind.Withdrawal => "−",
        PortfolioOperationKind.Trade when Operation.TradeDirection == PortfolioTradeDirection.Buy => "↘",
        PortfolioOperationKind.Trade when Operation.TradeDirection == PortfolioTradeDirection.Sell => "↗",
        PortfolioOperationKind.Income => "↗",
        _ => "·"
    };
    public Color ResultColor => Realized?.ProfitIncludingCoupons is { } totalProfit
        ? ProfitColor(totalProfit)
        : Realized?.ProfitByPrice is { } priceProfit
            ? ProfitColor(priceProfit)
            : Color.FromArgb("#687786");
    public Color AmountColor => Operation.Kind switch
    {
        PortfolioOperationKind.Deposit or PortfolioOperationKind.Income => Color.FromArgb("#16805D"),
        PortfolioOperationKind.Withdrawal => Color.FromArgb("#D14343"),
        PortfolioOperationKind.Trade when Operation.TradeDirection == PortfolioTradeDirection.Buy =>
            Color.FromArgb("#D14343"),
        PortfolioOperationKind.Trade when Operation.TradeDirection == PortfolioTradeDirection.Sell =>
            Color.FromArgb("#16805D"),
        _ => Color.FromArgb("#233544")
    };

    private static Color ProfitColor(decimal profit) => profit < 0
        ? Color.FromArgb("#D14343")
        : profit > 0 ? Color.FromArgb("#16805D") : Color.FromArgb("#687786");

    public AnalyticsOperationViewModel(PortfolioOperation operation, RealizedTradeResult? realized)
    {
        Operation = operation;
        Realized = realized;
    }
}
