using CommunityToolkit.Mvvm.ComponentModel;
using Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Graphics;
using System.Collections.ObjectModel;
using System.Globalization;

namespace BondAnalytics.Mobile.ViewModels;

public partial class InvestmentPlanViewModel : ObservableObject
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(90);
    private readonly IInvestmentPlanRepository _planRepository;
    private readonly IPortfolioService _portfolioService;
    private readonly AppViewModel _portfolio;
    private readonly ILogger<InvestmentPlanViewModel> _logger;
    private InvestmentPlan? _plan;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private DateTime startDate = DateTime.Today;
    [ObservableProperty] private string durationYearsInput = "10";
    [ObservableProperty] private string initialCapitalInput = "0";
    [ObservableProperty] private string targetAmountInput = "10000000";
    [ObservableProperty] private string contributionAmountInput = "50000";
    [ObservableProperty] private int frequencyIndex = 2;
    [ObservableProperty] private string contributionDayInput = "10";
    [ObservableProperty] private string contributionIncreaseInput = "5";
    [ObservableProperty] private string priceGrowthInput = "6";
    [ObservableProperty] private string couponYieldInput = "10";
    [ObservableProperty] private bool reinvestCoupons = true;
    [ObservableProperty] private string couponTaxInput = "13";
    [ObservableProperty] private string annualFeeInput = "0.5";
    [ObservableProperty] private string inflationInput = "5";
    [ObservableProperty] private string statusMessage = "Настройте параметры и рассчитайте план";
    [ObservableProperty] private string targetDateText = "Цель не достигнута в выбранном горизонте";
    [ObservableProperty] private string nextContributionText = "—";
    [ObservableProperty] private string projectedValueText = "—";
    [ObservableProperty] private string projectedWealthText = "—";
    [ObservableProperty] private string paidCouponsText = "—";
    [ObservableProperty] private string totalContributionsText = "—";
    [ObservableProperty] private string realWealthText = "—";
    [ObservableProperty] private double goalProgress;

    public IReadOnlyList<string> FrequencyOptions { get; } =
        ["Каждую неделю", "Раз в две недели", "Каждый месяц", "Раз в квартал"];
    public ObservableCollection<ChartPoint> ProjectionPoints { get; } = new();
    public ObservableCollection<ChartSeries> ProjectionSeries { get; } = new();
    public ObservableCollection<ContributionPeriodViewModel> ContributionPeriods { get; } = new();
    public double ProjectionChartWidth => Math.Max(640d, ProjectionPoints.Count * 14d);
    public decimal? TargetAmount => _plan?.TargetAmountRub > 0 ? _plan.TargetAmountRub : null;

    public InvestmentPlanViewModel(
        IInvestmentPlanRepository planRepository,
        IPortfolioService portfolioService,
        AppViewModel portfolio,
        ILogger<InvestmentPlanViewModel> logger)
    {
        _planRepository = planRepository;
        _portfolioService = portfolioService;
        _portfolio = portfolio;
        _logger = logger;
    }

    public async Task LoadAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        StatusMessage = "Загружаем сохранённый план";
        using var timeout = new CancellationTokenSource(RequestTimeout);
        try
        {
            _plan = await Task.Run(
                () => _planRepository.GetActivePlanAsync(timeout.Token), timeout.Token) ?? new InvestmentPlan
            {
                InitialCapitalRub = _portfolio.TotalPortfolioValue
            };
            CopyPlanToInputs(_plan);
            IsBusy = false;
            await RecalculateAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load the saved investment plan");
            var cause = ex.GetBaseException();
            StatusMessage = timeout.IsCancellationRequested
                ? $"Загрузка плана превысила {RequestTimeout.TotalSeconds:0} секунд."
                : $"Не удалось загрузить план: {cause.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RecalculateAsync()
    {
        if (IsBusy)
            return;

        if (!TryBuildPlan(out var plan, out var error))
        {
            StatusMessage = error;
            return;
        }

        IsBusy = true;
        using var timeout = new CancellationTokenSource(RequestTimeout);
        try
        {
            StatusMessage = "Загружаем фактические операции";
            var actuals = await GetActualOperationsAsync(plan, timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            StatusMessage = "Рассчитываем сценарий";
            var evaluation = await Task.Run(
                () => InvestmentPlanCalculator.Calculate(plan, actuals, DateTime.Today, timeout.Token),
                timeout.Token);
            _plan = plan;
            ProjectedValueText = FormatMoney(evaluation.FinalPortfolioValueRub);
            ProjectedWealthText = FormatMoney(evaluation.FinalTotalWealthRub);
            PaidCouponsText = FormatMoney(evaluation.FinalPaidCouponsRub);
            TotalContributionsText = FormatMoney(evaluation.TotalContributionsRub);
            RealWealthText = FormatMoney(evaluation.FinalRealWealthRub);
            TargetDateText = evaluation.TargetReachedDate is { } reached
                ? $"Цель будет достигнута · {reached:MMMM yyyy}"
                : "Цель не достигнута в выбранном горизонте";
            NextContributionText = evaluation.NextContributionDate is { } next
                ? $"{next:dd MMM yyyy} · {FormatMoney(evaluation.NextContributionAmountRub)}"
                : "Ближайший взнос не запланирован";
            GoalProgress = plan.TargetAmountRub > 0
                ? Math.Clamp((double)(evaluation.FinalTotalWealthRub / plan.TargetAmountRub), 0d, 1d)
                : 0;

            ProjectionPoints.Clear();
            ProjectionSeries.Clear();
            foreach (var point in evaluation.Points)
                ProjectionPoints.Add(new ChartPoint(point.Date.ToString("MMM yy", CultureInfo.GetCultureInfo("ru-RU")),
                    point.TotalWealthRub));
            ProjectionSeries.Add(new ChartSeries("Стоимость портфеля",
                Color.FromArgb("#398B77"), evaluation.Points.Select(point => point.PortfolioValueRub).ToList()));
            ProjectionSeries.Add(new ChartSeries("Капитал с купонами",
                Color.FromArgb("#4080AE"), evaluation.Points.Select(point => point.TotalWealthRub).ToList()));
            ProjectionSeries.Add(new ChartSeries("Внесено",
                Color.FromArgb("#BD8A36"), evaluation.Points.Select(point => point.ContributionsRub).ToList()));
            OnPropertyChanged(nameof(ProjectionChartWidth));
            OnPropertyChanged(nameof(TargetAmount));
            ContributionPeriods.Clear();
            foreach (var period in evaluation.ContributionPeriods.Take(36))
                ContributionPeriods.Add(new ContributionPeriodViewModel(period));
            StatusMessage = "Прогноз рассчитан по сценарным ставкам и фактическим пополнениям.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Investment plan calculation failed");
            var cause = ex.GetBaseException();
            StatusMessage = timeout.IsCancellationRequested
                ? $"Расчёт превысил {RequestTimeout.TotalSeconds:0} секунд. Попробуйте уменьшить период или проверьте соединение."
                : $"Не удалось рассчитать план: {cause.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SaveAsync()
    {
        if (!TryBuildPlan(out var plan, out var error))
        {
            StatusMessage = error;
            return;
        }

        IsBusy = true;
        using var timeout = new CancellationTokenSource(RequestTimeout);
        try
        {
            await Task.Run(() => _planRepository.SaveActivePlanAsync(plan, timeout.Token), timeout.Token);
            _plan = plan;
            StatusMessage = "План сохранён на этом устройстве.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not save the investment plan");
            var cause = ex.GetBaseException();
            StatusMessage = timeout.IsCancellationRequested
                ? $"Сохранение плана превысило {RequestTimeout.TotalSeconds:0} секунд."
                : $"Не удалось сохранить план: {cause.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task<IReadOnlyList<PortfolioOperation>> GetActualOperationsAsync(
        InvestmentPlan plan,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;
        var from = new DateTimeOffset(plan.StartDate.Date);
        if (from > now)
            return Task.FromResult<IReadOnlyList<PortfolioOperation>>(Array.Empty<PortfolioOperation>());

        return Task.Run(
            () => _portfolioService.GetOperationsAsync(from, now, cancellationToken), cancellationToken);
    }

    private void CopyPlanToInputs(InvestmentPlan plan)
    {
        StartDate = plan.StartDate;
        DurationYearsInput = plan.DurationYears.ToString(CultureInfo.InvariantCulture);
        InitialCapitalInput = plan.InitialCapitalRub.ToString(CultureInfo.InvariantCulture);
        TargetAmountInput = plan.TargetAmountRub.ToString(CultureInfo.InvariantCulture);
        ContributionAmountInput = plan.ContributionAmountRub.ToString(CultureInfo.InvariantCulture);
        FrequencyIndex = (int)plan.ContributionFrequency;
        ContributionDayInput = plan.ContributionDayOfMonth.ToString(CultureInfo.InvariantCulture);
        ContributionIncreaseInput = plan.AnnualContributionIncreasePercent.ToString(CultureInfo.InvariantCulture);
        PriceGrowthInput = plan.AnnualPriceGrowthPercent.ToString(CultureInfo.InvariantCulture);
        CouponYieldInput = plan.AnnualCouponYieldPercent.ToString(CultureInfo.InvariantCulture);
        ReinvestCoupons = plan.ReinvestCouponIncome;
        CouponTaxInput = plan.CouponTaxPercent.ToString(CultureInfo.InvariantCulture);
        AnnualFeeInput = plan.AnnualFeePercent.ToString(CultureInfo.InvariantCulture);
        InflationInput = plan.AnnualInflationPercent.ToString(CultureInfo.InvariantCulture);
    }

    private bool TryBuildPlan(out InvestmentPlan plan, out string error)
    {
        plan = new InvestmentPlan();
        if (!TryDecimal(InitialCapitalInput, out var initial) ||
            !TryDecimal(TargetAmountInput, out var target) ||
            !TryDecimal(ContributionAmountInput, out var contribution) ||
            !TryDecimal(ContributionIncreaseInput, out var contributionIncrease) ||
            !TryDecimal(PriceGrowthInput, out var priceGrowth) ||
            !TryDecimal(CouponYieldInput, out var couponYield) ||
            !TryDecimal(CouponTaxInput, out var tax) ||
            !TryDecimal(AnnualFeeInput, out var fee) ||
            !TryDecimal(InflationInput, out var inflation) ||
            !int.TryParse(DurationYearsInput, out var years) ||
            !int.TryParse(ContributionDayInput, out var day))
        {
            error = "Проверьте числовые значения: используйте цифры и, при необходимости, десятичную запятую.";
            return false;
        }

        if (years is < 1 or > 70)
        {
            error = "Срок плана должен быть от 1 до 70 лет.";
            return false;
        }
        if (day is < 1 or > 31)
        {
            error = "День взноса должен быть от 1 до 31.";
            return false;
        }
        if (initial < 0 || target < 0 || contribution < 0)
        {
            error = "Капитал, цель и размер взноса не могут быть отрицательными.";
            return false;
        }
        if (priceGrowth <= -100 || couponYield < 0 || contributionIncrease < 0 ||
            fee is < 0 or >= 100 || tax is < 0 or > 100 || inflation <= -100)
        {
            error = "Проверьте ставки роста, купонов, комиссии, налога и инфляции.";
            return false;
        }
        try
        {
            _ = StartDate.Date.AddYears(years);
        }
        catch (ArgumentOutOfRangeException)
        {
            error = "Дата начала и срок плана выходят за допустимый календарный диапазон.";
            return false;
        }

        plan = new InvestmentPlan
        {
            StartDate = StartDate.Date,
            DurationYears = years,
            InitialCapitalRub = initial,
            TargetAmountRub = target,
            ContributionAmountRub = contribution,
            ContributionFrequency = (InvestmentContributionFrequency)Math.Clamp(FrequencyIndex, 0, 3),
            ContributionDayOfMonth = day,
            AnnualContributionIncreasePercent = contributionIncrease,
            AnnualPriceGrowthPercent = priceGrowth,
            AnnualCouponYieldPercent = couponYield,
            ReinvestCouponIncome = ReinvestCoupons,
            CouponTaxPercent = tax,
            AnnualFeePercent = fee,
            AnnualInflationPercent = inflation
        };
        error = string.Empty;
        return true;
    }

    private static bool TryDecimal(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value) ||
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    private static string FormatMoney(decimal value) =>
        value.ToString("#,0 ₽", CultureInfo.GetCultureInfo("ru-RU"));
}

public sealed class ContributionPeriodViewModel
{
    public string DateText => $"{Period.StartDate:MMM yyyy} — {Period.EndDate:MMM yyyy}";
    public string PlannedText => $"План {Period.PlannedAmountRub:N0} ₽";
    public string ActualText => $"Факт {Period.ActualAmountRub:N0} ₽";
    public string StateText => Period.IsFuture ? "Впереди" : Period.IsCurrent ? "Текущий период" : "Завершён";
    public InvestmentPlanPeriod Period { get; }

    public ContributionPeriodViewModel(InvestmentPlanPeriod period) => Period = period;
}
