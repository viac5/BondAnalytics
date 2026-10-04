using System;

namespace Domain
{
    public enum InvestmentContributionFrequency
    {
        Weekly,
        Biweekly,
        Monthly,
        Quarterly
    }

    public sealed class InvestmentPlan
    {
        public string Name { get; set; } = "Мой план";
        public DateTime StartDate { get; set; } = DateTime.Today;
        public int DurationYears { get; set; } = 10;
        public decimal InitialCapitalRub { get; set; }
        public decimal TargetAmountRub { get; set; } = 10_000_000m;
        public decimal ContributionAmountRub { get; set; } = 50_000m;
        public InvestmentContributionFrequency ContributionFrequency { get; set; } = InvestmentContributionFrequency.Monthly;
        public int ContributionDayOfMonth { get; set; } = 10;
        public decimal AnnualContributionIncreasePercent { get; set; } = 5m;
        public decimal AnnualPriceGrowthPercent { get; set; } = 6m;
        public decimal AnnualCouponYieldPercent { get; set; } = 10m;
        public bool ReinvestCouponIncome { get; set; } = true;
        public decimal CouponTaxPercent { get; set; } = 13m;
        public decimal AnnualFeePercent { get; set; } = 0.5m;
        public decimal AnnualInflationPercent { get; set; } = 5m;
    }
}
