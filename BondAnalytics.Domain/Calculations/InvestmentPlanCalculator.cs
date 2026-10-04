using System;
using System.Collections.Generic;
using System.Linq;

namespace Domain
{
    public sealed class InvestmentPlanPoint
    {
        public DateTime Date { get; }
        public decimal PortfolioValueRub { get; }
        public decimal PaidCouponsRub { get; }
        public decimal ContributionsRub { get; }
        public decimal TotalWealthRub => PortfolioValueRub + PaidCouponsRub;

        public InvestmentPlanPoint(DateTime date, decimal portfolioValueRub, decimal paidCouponsRub, decimal contributionsRub)
        {
            Date = date;
            PortfolioValueRub = portfolioValueRub;
            PaidCouponsRub = paidCouponsRub;
            ContributionsRub = contributionsRub;
        }
    }

    public sealed class InvestmentPlanPeriod
    {
        public DateTime StartDate { get; }
        public DateTime EndDate { get; }
        public decimal PlannedAmountRub { get; }
        public decimal ActualAmountRub { get; }
        public decimal DifferenceRub => PlannedAmountRub - ActualAmountRub;
        public bool IsFuture { get; }
        public bool IsCurrent { get; }

        public InvestmentPlanPeriod(
            DateTime startDate,
            DateTime endDate,
            decimal plannedAmountRub,
            decimal actualAmountRub,
            bool isFuture,
            bool isCurrent)
        {
            StartDate = startDate;
            EndDate = endDate;
            PlannedAmountRub = plannedAmountRub;
            ActualAmountRub = actualAmountRub;
            IsFuture = isFuture;
            IsCurrent = isCurrent;
        }
    }

    public sealed class InvestmentPlanEvaluation
    {
        public IReadOnlyList<InvestmentPlanPoint> Points { get; }
        public IReadOnlyList<InvestmentPlanPeriod> ContributionPeriods { get; }
        public decimal TotalContributionsRub { get; }
        public decimal FinalPortfolioValueRub { get; }
        public decimal FinalPaidCouponsRub { get; }
        public decimal FinalTotalWealthRub => FinalPortfolioValueRub + FinalPaidCouponsRub;
        public decimal FinalRealWealthRub { get; }
        public DateTime? TargetReachedDate { get; }
        public DateTime? NextContributionDate { get; }
        public decimal NextContributionAmountRub { get; }

        public InvestmentPlanEvaluation(
            IReadOnlyList<InvestmentPlanPoint> points,
            IReadOnlyList<InvestmentPlanPeriod> contributionPeriods,
            decimal totalContributionsRub,
            decimal finalPortfolioValueRub,
            decimal finalPaidCouponsRub,
            decimal finalRealWealthRub,
            DateTime? targetReachedDate,
            DateTime? nextContributionDate,
            decimal nextContributionAmountRub)
        {
            Points = points;
            ContributionPeriods = contributionPeriods;
            TotalContributionsRub = totalContributionsRub;
            FinalPortfolioValueRub = finalPortfolioValueRub;
            FinalPaidCouponsRub = finalPaidCouponsRub;
            FinalRealWealthRub = finalRealWealthRub;
            TargetReachedDate = targetReachedDate;
            NextContributionDate = nextContributionDate;
            NextContributionAmountRub = nextContributionAmountRub;
        }
    }

    public static class InvestmentPlanCalculator
    {
        public static InvestmentPlanEvaluation Calculate(InvestmentPlan plan, IReadOnlyList<PortfolioOperation> actualOperations, DateTime today)
        {
            Validate(plan);
            var startDate = plan.StartDate.Date;
            var endDate = startDate.AddYears(plan.DurationYears).AddDays(-1);
            var contributionPeriods = BuildContributionPeriods(plan, actualOperations, today.Date, endDate);
            var contributionsByDate = contributionPeriods.ToDictionary(period => period.StartDate, period => period.PlannedAmountRub);
            var points = new List<InvestmentPlanPoint>();

            var portfolioValue = plan.InitialCapitalRub;
            var paidCoupons = 0m;
            var totalContributions = 0m;
            var priceDailyRate = DailyRate(plan.AnnualPriceGrowthPercent);
            var couponDailyRate = DailyRate(plan.AnnualCouponYieldPercent);
            var feeDailyRate = DailyRate(-plan.AnnualFeePercent);
            var couponRetention = 1m - plan.CouponTaxPercent / 100m;
            var nextContribution = contributionPeriods.FirstOrDefault(period => period.StartDate >= today.Date);
            var lastRecordedMonth = -1;

            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                if (contributionsByDate.TryGetValue(date, out var contribution))
                {
                    portfolioValue += contribution;
                    totalContributions += contribution;
                }

                portfolioValue *= 1m + priceDailyRate;
                var couponIncome = portfolioValue * couponDailyRate * couponRetention;
                if (plan.ReinvestCouponIncome)
                    portfolioValue += couponIncome;
                else
                    paidCoupons += couponIncome;

                portfolioValue *= 1m + feeDailyRate;

                if (date.Month != lastRecordedMonth || date == endDate)
                {
                    points.Add(new InvestmentPlanPoint(date, portfolioValue, paidCoupons, totalContributions));
                    lastRecordedMonth = date.Month;
                }
            }

            var finalTotalWealth = portfolioValue + paidCoupons;
            var targetDate = plan.TargetAmountRub > 0
                ? points.FirstOrDefault(point => point.TotalWealthRub >= plan.TargetAmountRub)?.Date
                : null;
            var inflationFactor = AnnualFactor(plan.AnnualInflationPercent, plan.DurationYears);
            var finalRealValue = inflationFactor > 0 ? finalTotalWealth / inflationFactor : finalTotalWealth;

            return new InvestmentPlanEvaluation(
                points,
                contributionPeriods,
                totalContributions,
                portfolioValue,
                paidCoupons,
                finalRealValue,
                targetDate,
                nextContribution?.StartDate,
                nextContribution?.PlannedAmountRub ?? 0m);
        }

        private static IReadOnlyList<InvestmentPlanPeriod> BuildContributionPeriods(
            InvestmentPlan plan,
            IReadOnlyList<PortfolioOperation> actualOperations,
            DateTime today,
            DateTime endDate)
        {
            var periods = new List<InvestmentPlanPeriod>();
            var periodStart = GetFirstContributionDate(plan);
            var actualPeriodStart = plan.StartDate.Date;
            var actualDeposits = actualOperations
                .Where(operation => operation.Kind == PortfolioOperationKind.Deposit &&
                                    NormalizeCurrency(operation.Currency) == "RUB")
                .OrderBy(operation => operation.Date)
                .ToList();
            var actualDepositIndex = 0;

            while (periodStart <= endDate)
            {
                var nextPeriodStart = Advance(periodStart, plan);
                var periodEnd = nextPeriodStart.AddDays(-1) > endDate ? endDate : nextPeriodStart.AddDays(-1);
                var plannedAmount = plan.ContributionAmountRub * AnnualFactor(
                    plan.AnnualContributionIncreasePercent,
                    AnniversaryCount(plan.StartDate.Date, periodStart));
                while (actualDepositIndex < actualDeposits.Count &&
                       actualDeposits[actualDepositIndex].Date.LocalDateTime.Date < actualPeriodStart)
                {
                    actualDepositIndex++;
                }

                var actualAmount = 0m;
                while (actualDepositIndex < actualDeposits.Count &&
                       actualDeposits[actualDepositIndex].Date.LocalDateTime.Date <= periodEnd)
                {
                    actualAmount += actualDeposits[actualDepositIndex].Amount;
                    actualDepositIndex++;
                }

                periods.Add(new InvestmentPlanPeriod(
                    periodStart,
                    periodEnd,
                    plannedAmount,
                    actualAmount,
                    periodStart > today,
                    periodStart <= today && periodEnd >= today));
                actualPeriodStart = periodEnd.AddDays(1);
                periodStart = nextPeriodStart;
            }

            return periods;
        }

        private static DateTime GetFirstContributionDate(InvestmentPlan plan)
        {
            var start = plan.StartDate.Date;
            if (plan.ContributionFrequency is InvestmentContributionFrequency.Weekly or InvestmentContributionFrequency.Biweekly)
                return start;

            var day = Math.Clamp(plan.ContributionDayOfMonth, 1, DateTime.DaysInMonth(start.Year, start.Month));
            var contributionDate = new DateTime(start.Year, start.Month, day);
            if (contributionDate < start)
            {
                var nextMonth = start.AddMonths(plan.ContributionFrequency == InvestmentContributionFrequency.Quarterly ? 3 : 1);
                day = Math.Clamp(plan.ContributionDayOfMonth, 1, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month));
                contributionDate = new DateTime(nextMonth.Year, nextMonth.Month, day);
            }

            return contributionDate;
        }

        private static DateTime Advance(DateTime date, InvestmentPlan plan) => plan.ContributionFrequency switch
        {
            InvestmentContributionFrequency.Weekly => date.AddDays(7),
            InvestmentContributionFrequency.Biweekly => date.AddDays(14),
            InvestmentContributionFrequency.Quarterly => AddMonthsOnPlanDay(date, 3, plan.ContributionDayOfMonth),
            _ => AddMonthsOnPlanDay(date, 1, plan.ContributionDayOfMonth)
        };

        private static DateTime AddMonthsOnPlanDay(DateTime date, int months, int dayOfMonth)
        {
            var targetMonth = date.AddMonths(months);
            var day = Math.Clamp(dayOfMonth, 1, DateTime.DaysInMonth(targetMonth.Year, targetMonth.Month));
            return new DateTime(targetMonth.Year, targetMonth.Month, day);
        }

        private static int AnniversaryCount(DateTime startDate, DateTime date)
        {
            var years = date.Year - startDate.Year;
            if (date < startDate.AddYears(years))
                years--;
            return Math.Max(0, years);
        }

        private static decimal DailyRate(decimal annualPercent)
        {
            var annualFactor = 1d + (double)(annualPercent / 100m);
            return (decimal)Math.Pow(annualFactor, 1d / 365d) - 1m;
        }

        private static decimal AnnualFactor(decimal annualPercent, int years)
        {
            var result = 1m;
            for (var year = 0; year < years; year++)
                result *= 1m + annualPercent / 100m;
            return result;
        }

        private static string NormalizeCurrency(string currency) =>
            string.Equals(currency, "RUR", StringComparison.OrdinalIgnoreCase) ? "RUB" : currency.ToUpperInvariant();

        private static void Validate(InvestmentPlan plan)
        {
            if (plan.DurationYears is < 1 or > 70)
                throw new ArgumentOutOfRangeException(nameof(plan.DurationYears), "Горизонт плана должен быть от 1 до 70 лет.");
            if (plan.InitialCapitalRub < 0 || plan.TargetAmountRub < 0 || plan.ContributionAmountRub < 0)
                throw new ArgumentOutOfRangeException(nameof(plan), "Капитал, цель и взнос не могут быть отрицательными.");
            if (plan.AnnualPriceGrowthPercent <= -100 || plan.AnnualCouponYieldPercent < 0 ||
                plan.AnnualContributionIncreasePercent < 0 || plan.AnnualFeePercent is < 0 or >= 100 ||
                plan.CouponTaxPercent is < 0 or > 100 || plan.AnnualInflationPercent <= -100)
            {
                throw new ArgumentOutOfRangeException(nameof(plan), "Проверьте ставки роста, купонов, комиссии, налога и инфляции.");
            }
        }
    }
}
