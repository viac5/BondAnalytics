using Domain;
using Infrastructure;
using Xunit;

namespace BondAnalytics.Domain.Tests;

public sealed class PortfolioAnalyticsTests
{
    [Fact]
    public void FundQuoteIsAlreadyUnitPrice()
    {
        var price = MarketPriceConverter.ToUnitPrice("etf", 1_234.56m, 0m);

        Assert.Equal(1_234.56m, price);
    }

    [Fact]
    public void BondQuoteIsConvertedFromPercentOfNominal()
    {
        var price = MarketPriceConverter.ToUnitPrice("bond", 97.25m, 1_000m);

        Assert.Equal(972.5m, price);
    }

    [Fact]
    public void DepositIsRemovedFromProfitAndWeightedByDate()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 1, 11, 0, 0, 0, TimeSpan.Zero);
        var operations = new[]
        {
            Operation("deposit", from.AddDays(5), PortfolioOperationKind.Deposit, 500m)
        };
        var snapshots = new[]
        {
            new PortfolioSnapshot(from, 1_000m),
            new PortfolioSnapshot(to, 1_600m)
        };

        var summary = PortfolioPeriodCalculator.Calculate(operations, snapshots, from, to);

        Assert.Equal(100m, summary.ProfitRub);
        Assert.Equal(0.08m, summary.Return);
    }

    [Fact]
    public void WithdrawalIsAddedBackToProfitAndWeightedByDate()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 1, 11, 0, 0, 0, TimeSpan.Zero);
        var operations = new[]
        {
            Operation("withdrawal", from.AddDays(5), PortfolioOperationKind.Withdrawal, 200m)
        };
        var snapshots = new[]
        {
            new PortfolioSnapshot(from, 1_000m),
            new PortfolioSnapshot(to, 900m)
        };

        var summary = PortfolioPeriodCalculator.Calculate(operations, snapshots, from, to);

        Assert.Equal(100m, summary.ProfitRub);
        Assert.Equal(100m / 900m, summary.Return);
    }

    [Fact]
    public void NonRubExternalFlowDoesNotProduceMisleadingReturn()
    {
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 1, 11, 0, 0, 0, TimeSpan.Zero);
        var operations = new[]
        {
            Operation("deposit-usd", from.AddDays(5), PortfolioOperationKind.Deposit, 100m, "USD")
        };
        var snapshots = new[]
        {
            new PortfolioSnapshot(from, 1_000m),
            new PortfolioSnapshot(to, 1_100m)
        };

        var summary = PortfolioPeriodCalculator.Calculate(operations, snapshots, from, to);

        Assert.Null(summary.Return);
        Assert.NotNull(summary.ReturnUnavailableReason);
    }

    [Fact]
    public async Task SqliteRepositoryReturnsBaselineAndExactValuesInRange()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"BondAnalyticsTests-{Guid.NewGuid():N}");
        var repository = new EfPortfolioRepository(Path.Combine(directory, "history.db"));
        var from = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero);

        try
        {
            await repository.SaveSnapshotAsync(new PortfolioSnapshot(from.AddDays(-1), 123_456.789123m));
            await repository.SaveSnapshotAsync(new PortfolioSnapshot(from.AddDays(5), 234_567.891234m));
            await repository.SaveSnapshotAsync(new PortfolioSnapshot(to.AddDays(1), 345_678.912345m));

            var snapshots = await repository.GetSnapshotsAsync(from, to);
            var plan = new InvestmentPlan { Name = "Цель на квартиру", TargetAmountRub = 15_000_000m };
            await repository.SaveActivePlanAsync(plan);
            var savedPlan = await repository.GetActivePlanAsync();

            Assert.Equal(2, snapshots.Count);
            Assert.Equal(123_456.789123m, snapshots[0].TotalValueRub);
            Assert.Equal(234_567.891234m, snapshots[1].TotalValueRub);
            Assert.Equal("Цель на квартиру", savedPlan?.Name);
            Assert.Equal(15_000_000m, savedPlan?.TargetAmountRub);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BondSalesUseFifoCostAndCouponsWhileSharesWereHeld()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var operations = new[]
        {
            Trade("buy", start, PortfolioTradeDirection.Buy, 10m, 100m, true),
            Coupon("coupon", start.AddDays(5), 0m, 20m),
            Trade("sell-part", start.AddDays(10), PortfolioTradeDirection.Sell, 4m, 110m, true),
            Trade("sell-rest", start.AddDays(15), PortfolioTradeDirection.Sell, 6m, 90m, true)
        };

        var results = RealizedTradeProfitCalculator.Calculate(operations);

        Assert.Equal(40m, results["sell-part"].ProfitByPrice);
        Assert.Equal(8m, results["sell-part"].CouponIncome);
        Assert.Equal(48m, results["sell-part"].ProfitIncludingCoupons);
        Assert.Equal(-60m, results["sell-rest"].ProfitByPrice);
        Assert.Equal(12m, results["sell-rest"].CouponIncome);
        Assert.Equal(-48m, results["sell-rest"].ProfitIncludingCoupons);
    }

    [Fact]
    public void NonBondSaleUsesPriceOnly()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var results = RealizedTradeProfitCalculator.Calculate(new[]
        {
            Trade("buy-fund", start, PortfolioTradeDirection.Buy, 5m, 100m, false),
            Trade("sell-fund", start.AddDays(1), PortfolioTradeDirection.Sell, 5m, 125m, false)
        });

        Assert.Equal(125m, results["sell-fund"].ProfitByPrice);
        Assert.Equal(0m, results["sell-fund"].CouponIncome);
        Assert.Equal(125m, results["sell-fund"].ProfitIncludingCoupons);
    }

    [Fact]
    public void SaleWithoutMatchingPurchaseIsMarkedUnavailable()
    {
        var sale = Trade(
            "external-sale",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            PortfolioTradeDirection.Sell,
            2m,
            120m,
            false);

        var result = RealizedTradeProfitCalculator.Calculate(new[] { sale })["external-sale"];

        Assert.Null(result.ProfitByPrice);
        Assert.Contains("история покупок", result.UnavailableReason);
    }

    [Fact]
    public void ReinvestmentRaisesFinalCapitalAndNonReinvestedCouponsStayAsCash()
    {
        var plan = new InvestmentPlan
        {
            StartDate = new DateTime(2026, 1, 1),
            DurationYears = 2,
            InitialCapitalRub = 100_000m,
            ContributionAmountRub = 0m,
            AnnualPriceGrowthPercent = 0m,
            AnnualCouponYieldPercent = 12m,
            CouponTaxPercent = 0m,
            AnnualFeePercent = 0m,
            AnnualInflationPercent = 0m,
            TargetAmountRub = 0m,
            ReinvestCouponIncome = true
        };
        var reinvested = InvestmentPlanCalculator.Calculate(plan, Array.Empty<PortfolioOperation>(), new DateTime(2026, 1, 1));

        plan.ReinvestCouponIncome = false;
        var paidOut = InvestmentPlanCalculator.Calculate(plan, Array.Empty<PortfolioOperation>(), new DateTime(2026, 1, 1));

        Assert.True(reinvested.FinalPortfolioValueRub > paidOut.FinalPortfolioValueRub);
        Assert.Equal(0m, reinvested.FinalPaidCouponsRub);
        Assert.True(paidOut.FinalPaidCouponsRub > 0m);
    }

    [Fact]
    public void DepositBeforeScheduledDayCountsTowardTheUpcomingPeriod()
    {
        var plan = new InvestmentPlan
        {
            StartDate = new DateTime(2026, 2, 1),
            DurationYears = 1,
            ContributionAmountRub = 1_000m,
            ContributionFrequency = InvestmentContributionFrequency.Monthly,
            ContributionDayOfMonth = 10,
            AnnualPriceGrowthPercent = 0m,
            AnnualCouponYieldPercent = 0m,
            AnnualFeePercent = 0m,
            AnnualInflationPercent = 0m,
            TargetAmountRub = 0m
        };
        var deposit = Operation(
            "early-deposit",
            new DateTimeOffset(2026, 2, 8, 12, 0, 0, TimeSpan.Zero),
            PortfolioOperationKind.Deposit,
            1_250m);

        var evaluation = InvestmentPlanCalculator.Calculate(plan, new[] { deposit }, new DateTime(2026, 2, 9));

        Assert.Equal(new DateTime(2026, 2, 10), evaluation.ContributionPeriods[0].StartDate);
        Assert.Equal(1_250m, evaluation.ContributionPeriods[0].ActualAmountRub);
        Assert.True(evaluation.ContributionPeriods[0].DifferenceRub < 0m);
    }

    private static PortfolioOperation Operation(
        string id,
        DateTimeOffset date,
        PortfolioOperationKind kind,
        decimal amount,
        string currency = "RUB") => new(
            id,
            date,
            kind,
            PortfolioTradeDirection.None,
            kind.ToString(),
            "",
            "",
            false,
            0,
            0,
            amount,
            currency);

    private static PortfolioOperation Trade(
        string id,
        DateTimeOffset date,
        PortfolioTradeDirection direction,
        decimal quantity,
        decimal price,
        bool isBond) => new(
            id,
            date,
            PortfolioOperationKind.Trade,
            direction,
            direction == PortfolioTradeDirection.Buy ? "Покупка" : "Продажа",
            "Тестовый инструмент",
            "test-instrument",
            isBond,
            quantity,
            price,
            quantity * price,
            "RUB");

    private static PortfolioOperation Coupon(string id, DateTimeOffset date, decimal quantity, decimal amount) => new(
        id,
        date,
        PortfolioOperationKind.Income,
        PortfolioTradeDirection.None,
        "Купон",
        "Тестовая облигация",
        "test-instrument",
        true,
        quantity,
        0,
        amount,
        "RUB");
}