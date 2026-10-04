namespace Domain;

public static class CouponYieldCalculator
{
    public static decimal CalculateAnnualYield(decimal annualCoupon, decimal currentPrice)
    {
        if (annualCoupon < 0)
            throw new ArgumentOutOfRangeException(nameof(annualCoupon));
        if (currentPrice < 0)
            throw new ArgumentOutOfRangeException(nameof(currentPrice));

        return currentPrice == 0 ? 0 : annualCoupon / currentPrice;
    }

    public static decimal CalculateReinvestedAnnualYield(decimal annualYield, int paymentsPerYear)
    {
        if (annualYield < 0)
            throw new ArgumentOutOfRangeException(nameof(annualYield));
        if (paymentsPerYear < 0)
            throw new ArgumentOutOfRangeException(nameof(paymentsPerYear));
        if (paymentsPerYear == 0 || annualYield == 0)
            return annualYield;

        var periodicYield = annualYield / paymentsPerYear;
        var annualGrowth = 1m;
        for (var payment = 0; payment < paymentsPerYear; payment++)
            annualGrowth *= 1m + periodicYield;

        return annualGrowth - 1m;
    }
}
